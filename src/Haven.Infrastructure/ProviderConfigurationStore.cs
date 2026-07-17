using System.Net;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class ProviderConfigurationStore : IProviderConfigurationStore, IDisposable
{
    private const int MaximumProviders = 64;
    private const int MaximumMetadataEntries = 64;
    private const int MaximumMetadataKeyLength = 80;
    private const int MaximumMetadataValueLength = 4096;
    private const int MaximumDisplayNameLength = 120;
    private const int MaximumEndpointLength = 2048;
    private const long MaximumStoreBytes = 1024L * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] SecretLikeTerms =
        ["secret", "token", "password", "authorization", "apikey", "credential", "bearer", "accesskey"];
    private readonly string _path;
    private readonly string _backupPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposed;

    public ProviderConfigurationStore(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _path = Path.Combine(paths.DataDirectory, "model-providers.json");
        _backupPath = _path + ".bak";
    }

    public async Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var stored = await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var merged = BuiltInDefaults().ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var configuration in stored) merged[configuration.Id] = configuration;
            return merged.Values
                .OrderByDescending(item => item.IsLocal)
                .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<ProviderConfiguration?> GetAsync(string providerId, CancellationToken cancellationToken)
    {
        ValidateIdentifier(providerId, nameof(providerId));
        return (await GetAllAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => item.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task UpsertAsync(ProviderConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ThrowIfDisposed();
        configuration = Normalize(configuration);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var stored = (await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false))
                .Where(item => !item.Id.Equals(configuration.Id, StringComparison.OrdinalIgnoreCase))
                .Append(configuration with { UpdatedAt = DateTimeOffset.UtcNow })
                .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (stored.Length > MaximumProviders)
                throw new InvalidOperationException($"Haven supports at most {MaximumProviders} provider configurations.");
            await SaveUnsafeAsync(stored, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(string providerId, CancellationToken cancellationToken)
    {
        ValidateIdentifier(providerId, nameof(providerId));
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var existing = await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var stored = existing
                .Where(item => !item.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (stored.Length == existing.Count) return;
            await SaveUnsafeAsync(stored, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task<IReadOnlyList<ProviderConfiguration>> LoadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (await TryLoadAsync(_path, cancellationToken).ConfigureAwait(false) is { } primary)
            return primary;

        QuarantineInvalidPrimary();
        if (await TryLoadAsync(_backupPath, cancellationToken).ConfigureAwait(false) is { } backup)
            return backup;
        return [];
    }

    private static async Task<IReadOnlyList<ProviderConfiguration>?> ReadAndNormalizeAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > MaximumStoreBytes)
            throw new InvalidDataException("The provider configuration store exceeds its safety limit.");

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var parsed = await JsonSerializer.DeserializeAsync<ProviderConfiguration[]>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false) ?? [];
        if (parsed.Length > MaximumProviders)
            throw new InvalidDataException($"The provider configuration store contains more than {MaximumProviders} entries.");

        return parsed
            .Select(Normalize)
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.UpdatedAt).First())
            .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<IReadOnlyList<ProviderConfiguration>?> TryLoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ReadAndNormalizeAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or JsonException
                                   or InvalidDataException
                                   or ArgumentException
                                   or NotSupportedException)
        {
            return null;
        }
    }

    private void QuarantineInvalidPrimary()
    {
        if (!File.Exists(_path)) return;
        var quarantine = _path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" + Guid.NewGuid().ToString("N");
        try { File.Move(_path, quarantine, false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private async Task SaveUnsafeAsync(
        IReadOnlyList<ProviderConfiguration> configurations,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, configurations, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(_path)) File.Replace(temporary, _path, _backupPath, true);
            else File.Move(temporary, _path, false);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static ProviderConfiguration Normalize(ProviderConfiguration configuration)
    {
        ValidateIdentifier(configuration.Id, nameof(configuration.Id));
        if (!Enum.IsDefined(configuration.Kind))
            throw new ArgumentOutOfRangeException(nameof(configuration.Kind), "The provider kind is not supported.");

        var displayName = configuration.DisplayName?.Trim() ?? string.Empty;
        if (displayName.Length == 0 || displayName.Length > MaximumDisplayNameLength)
            throw new ArgumentException($"Provider display name must contain 1 to {MaximumDisplayNameLength} characters.");

        var endpointText = configuration.Endpoint?.Trim() ?? string.Empty;
        Uri? endpoint = null;
        if (endpointText.Length > 0)
        {
            if (endpointText.Length > MaximumEndpointLength
                || !Uri.TryCreate(endpointText, UriKind.Absolute, out endpoint)
                || endpoint.Scheme is not ("http" or "https")
                || string.IsNullOrWhiteSpace(endpoint.Host))
            {
                throw new ArgumentException("Provider endpoint must be an absolute HTTP or HTTPS URL.");
            }
            if (!string.IsNullOrEmpty(endpoint.UserInfo)
                || !string.IsNullOrEmpty(endpoint.Query)
                || !string.IsNullOrEmpty(endpoint.Fragment))
            {
                throw new ArgumentException(
                    "Provider endpoints cannot contain credentials, query strings, or fragments. Store secrets in Windows Credential Manager.");
            }
            if (endpoint.Scheme == Uri.UriSchemeHttp && !IsLocalNetworkHost(endpoint.Host))
                throw new ArgumentException("Plain HTTP provider endpoints are allowed only for loopback or private-network hosts.");
            endpointText = endpoint.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/";
        }

        if (configuration.IsEnabled && configuration.Kind == ModelProviderKind.OpenAICompatible && endpoint is null)
            throw new ArgumentException("An enabled OpenAI-compatible provider requires an endpoint.");

        var metadataSource = configuration.Metadata ?? new Dictionary<string, string>();
        if (metadataSource.Count > MaximumMetadataEntries)
            throw new ArgumentException($"Provider metadata supports at most {MaximumMetadataEntries} entries.");
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in metadataSource)
        {
            var key = pair.Key?.Trim() ?? string.Empty;
            var value = pair.Value ?? string.Empty;
            if (key.Length == 0
                || key.Length > MaximumMetadataKeyLength
                || key.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_' or '.')))
            {
                throw new ArgumentException(
                    $"Provider metadata keys must contain 1 to {MaximumMetadataKeyLength} letters, numbers, dash, underscore, or dot characters.");
            }
            if (value.Length > MaximumMetadataValueLength)
                throw new ArgumentException($"Provider metadata '{key}' exceeds {MaximumMetadataValueLength} characters.");

            var canonicalKey = new string(key.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
            if (SecretLikeTerms.Any(term => canonicalKey.Contains(term, StringComparison.Ordinal)))
                throw new ArgumentException($"Provider metadata '{key}' looks secret. Store it through IProviderSecretStore instead.");
            if (!metadata.TryAdd(key, value))
                throw new ArgumentException($"Provider metadata contains duplicate key '{key}'.");
        }

        var isLocal = configuration.Kind switch
        {
            ModelProviderKind.Ollama => true,
            ModelProviderKind.OpenAICompatible => configuration.IsLocal
                                                  && endpoint is not null
                                                  && IsLocalNetworkHost(endpoint.Host),
            _ => false
        };

        return configuration with
        {
            Id = configuration.Id.Trim().ToLowerInvariant(),
            DisplayName = displayName,
            Endpoint = endpointText,
            IsLocal = isLocal,
            Metadata = metadata
        };
    }

    private static bool IsLocalNetworkHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var address)) return false;
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return true;
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            return bytes[0] == 10
                   || bytes[0] == 127
                   || bytes[0] == 169 && bytes[1] == 254
                   || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
                   || bytes[0] == 192 && bytes[1] == 168;
        }
        return bytes.Length == 16 && (bytes[0] & 0xFE) == 0xFC;
    }

    private static IReadOnlyList<ProviderConfiguration> BuiltInDefaults()
    {
        var ollamaEndpoint = Environment.GetEnvironmentVariable("OLLAMA_HOST")?.Trim();
        if (string.IsNullOrWhiteSpace(ollamaEndpoint)) ollamaEndpoint = "http://127.0.0.1:11434/";
        var now = DateTimeOffset.UtcNow;
        ProviderConfiguration ollama;
        try
        {
            ollama = Normalize(ProviderConfiguration.LocalOllama(ollamaEndpoint));
        }
        catch (ArgumentException)
        {
            ollama = Normalize(ProviderConfiguration.LocalOllama("http://127.0.0.1:11434/"));
        }

        return
        [
            ollama,
            Normalize(new("openai", ModelProviderKind.OpenAI, "OpenAI", "https://api.openai.com/v1/", false, false, false, new Dictionary<string, string>(), now)),
            Normalize(new("anthropic", ModelProviderKind.Anthropic, "Anthropic", "https://api.anthropic.com/v1/", false, false, false, new Dictionary<string, string>(), now)),
            Normalize(new("gemini", ModelProviderKind.Gemini, "Google Gemini", "https://generativelanguage.googleapis.com/v1beta/", false, false, false, new Dictionary<string, string>(), now)),
            Normalize(new("openrouter", ModelProviderKind.OpenRouter, "OpenRouter", "https://openrouter.ai/api/v1/", false, false, false, new Dictionary<string, string>(), now)),
            Normalize(new("openai-compatible", ModelProviderKind.OpenAICompatible, "OpenAI-compatible", string.Empty, false, false, false, new Dictionary<string, string>(), now))
        ];
    }

    private static void ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 80
            || value.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_' or '.')))
        {
            throw new ArgumentException(
                "Use a provider identifier containing only letters, numbers, dash, underscore, or dot.",
                parameterName);
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        // The store is application-lifetime. Do not dispose the semaphore beneath
        // an in-flight atomic save during coordinated service-provider shutdown.
    }
}
