using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class ProviderConfigurationStore : IProviderConfigurationStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] SecretLikeTerms = ["secret", "token", "password", "authorization", "api-key", "apikey"];
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ProviderConfigurationStore(IAppPaths paths) => _path = Path.Combine(paths.DataDirectory, "model-providers.json");

    public async Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stored = await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var merged = BuiltInDefaults().ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var configuration in stored) merged[configuration.Id] = Normalize(configuration);
            return merged.Values.OrderByDescending(item => item.IsLocal).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<ProviderConfiguration?> GetAsync(string providerId, CancellationToken cancellationToken) =>
        (await GetAllAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(item => item.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));

    public async Task UpsertAsync(ProviderConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration = Normalize(configuration);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stored = (await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false))
                .Where(item => !item.Id.Equals(configuration.Id, StringComparison.OrdinalIgnoreCase))
                .Append(configuration with { UpdatedAt = DateTimeOffset.UtcNow })
                .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            await SaveUnsafeAsync(stored, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(string providerId, CancellationToken cancellationToken)
    {
        ValidateIdentifier(providerId, nameof(providerId));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stored = (await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false))
                .Where(item => !item.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase)).ToArray();
            await SaveUnsafeAsync(stored, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task<IReadOnlyList<ProviderConfiguration>> LoadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return [];
        try
        {
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<ProviderConfiguration[]>(stream, JsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            var quarantine = _path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
            try { File.Move(_path, quarantine, false); }
            catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException) { }
            return [];
        }
    }

    private async Task SaveUnsafeAsync(IReadOnlyList<ProviderConfiguration> configurations, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, configurations, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(true);
            }
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static ProviderConfiguration Normalize(ProviderConfiguration configuration)
    {
        ValidateIdentifier(configuration.Id, nameof(configuration.Id));
        if (string.IsNullOrWhiteSpace(configuration.DisplayName)) throw new ArgumentException("Provider display name is required.");
        if (!string.IsNullOrWhiteSpace(configuration.Endpoint) &&
            (!Uri.TryCreate(configuration.Endpoint.Trim(), UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https")))
            throw new ArgumentException("Provider endpoint must be an absolute HTTP or HTTPS URL.");
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in configuration.Metadata ?? new Dictionary<string, string>())
        {
            if (SecretLikeTerms.Any(term => pair.Key.Contains(term, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Provider metadata '{pair.Key}' looks secret. Store it through IProviderSecretStore instead.");
            metadata[pair.Key.Trim()] = pair.Value;
        }
        var endpointText = configuration.Endpoint.Trim();
        if (endpointText.Length > 0 && !endpointText.EndsWith("/", StringComparison.Ordinal)) endpointText += "/";
        return configuration with { Id = configuration.Id.Trim().ToLowerInvariant(), DisplayName = configuration.DisplayName.Trim(), Endpoint = endpointText, Metadata = metadata };
    }

    private static IReadOnlyList<ProviderConfiguration> BuiltInDefaults()
    {
        var ollamaEndpoint = Environment.GetEnvironmentVariable("OLLAMA_HOST")?.Trim();
        if (string.IsNullOrWhiteSpace(ollamaEndpoint)) ollamaEndpoint = "http://127.0.0.1:11434/";
        if (!ollamaEndpoint.EndsWith("/", StringComparison.Ordinal)) ollamaEndpoint += "/";
        var now = DateTimeOffset.UtcNow;
        return
        [
            ProviderConfiguration.LocalOllama(ollamaEndpoint),
            new("openai", ModelProviderKind.OpenAI, "OpenAI", "https://api.openai.com/v1/", false, false, false, new Dictionary<string, string>(), now),
            new("anthropic", ModelProviderKind.Anthropic, "Anthropic", "https://api.anthropic.com/v1/", false, false, false, new Dictionary<string, string>(), now),
            new("gemini", ModelProviderKind.Gemini, "Google Gemini", "https://generativelanguage.googleapis.com/v1beta/", false, false, false, new Dictionary<string, string>(), now),
            new("openrouter", ModelProviderKind.OpenRouter, "OpenRouter", "https://openrouter.ai/api/v1/", false, false, false, new Dictionary<string, string>(), now),
            new("openai-compatible", ModelProviderKind.OpenAICompatible, "OpenAI-compatible", string.Empty, false, false, false, new Dictionary<string, string>(), now)
        ];
    }

    private static void ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80 || value.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_' or '.')))
            throw new ArgumentException("Use a provider identifier containing only letters, numbers, dash, underscore, or dot.", parameterName);
    }

    public void Dispose() => _gate.Dispose();
}
