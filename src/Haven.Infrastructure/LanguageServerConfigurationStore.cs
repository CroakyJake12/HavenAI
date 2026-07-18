/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/LanguageServerConfigurationStore.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns LanguageServerConfigurationStore. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents language server configuration store and keeps its related state and behavior together.
/// </summary>
public sealed class LanguageServerConfigurationStore(IAppPaths paths) : ILanguageServerConfigurationStore
{
    /// <summary>
    /// Stores json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    /// <summary>
    /// Stores gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Retrieves all async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<LanguageServerDefinition>> GetAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = ConfigurationPath;
            if (!File.Exists(file))
            {
                var defaults = BuiltInDefaults();
                await WriteUnsafeAsync(defaults, cancellationToken).ConfigureAwait(false);
                return defaults;
            }

            try
            {
                await using var stream = File.OpenRead(file);
                var values = await JsonSerializer.DeserializeAsync<List<LanguageServerDefinition>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
                return Normalize(values ?? []);
            }
            catch (JsonException exception)
            {
                var quarantine = file + ".corrupt." + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss") + ".json";
                File.Move(file, quarantine, true);
                var defaults = BuiltInDefaults();
                await WriteUnsafeAsync(defaults, cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException($"Language-server settings were corrupt and moved to '{quarantine}'. Disabled defaults were restored.", exception);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs find for path async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<LanguageServerDefinition?> FindForPathAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension)) return null;
        return (await GetAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(item => item.IsEnabled)
            .FirstOrDefault(item => item.Extensions.Any(value => NormalizeExtension(value).Equals(extension, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Performs upsert async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task UpsertAsync(LanguageServerDefinition definition, CancellationToken cancellationToken)
    {
        Validate(definition);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var index = existing.FindIndex(item => item.Id.Equals(definition.Id, StringComparison.OrdinalIgnoreCase));
            var normalized = NormalizeDefinition(definition);
            if (index >= 0) existing[index] = normalized;
            else existing.Add(normalized);
            await WriteUnsafeAsync(existing, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs delete async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            existing.RemoveAll(item => item.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));
            await WriteUnsafeAsync(existing, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Gets or updates configuration path, the bindable or domain state represented by this property.
    /// </summary>
    private string ConfigurationPath => Path.Combine(paths.DataDirectory, "language-servers.json");

    /// <summary>
    /// Performs read unsafe async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<List<LanguageServerDefinition>> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(ConfigurationPath)) return BuiltInDefaults().ToList();
        await using var stream = File.OpenRead(ConfigurationPath);
        return Normalize(await JsonSerializer.DeserializeAsync<List<LanguageServerDefinition>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false) ?? []).ToList();
    }

    /// <summary>
    /// Performs write unsafe async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task WriteUnsafeAsync(IReadOnlyList<LanguageServerDefinition> definitions, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        var temporary = ConfigurationPath + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16_384, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, Normalize(definitions), JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, ConfigurationPath, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Performs the normalize step owned by this component.
    /// </summary>
    private static IReadOnlyList<LanguageServerDefinition> Normalize(IEnumerable<LanguageServerDefinition> definitions) =>
        definitions.Select(NormalizeDefinition)
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>
    /// Performs the normalize definition step owned by this component.
    /// </summary>
    private static LanguageServerDefinition NormalizeDefinition(LanguageServerDefinition value) => value with
    {
        Id = value.Id.Trim().ToLowerInvariant(),
        DisplayName = value.DisplayName.Trim(),
        Command = value.Command.Trim(),
        Arguments = value.Arguments.Trim(),
        LanguageId = value.LanguageId.Trim(),
        Extensions = value.Extensions.Select(NormalizeExtension).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray(),
        RequestTimeoutSeconds = Math.Clamp(value.RequestTimeoutSeconds, 2, 120),
        InitializationOptionsJson = NormalizeJson(value.InitializationOptionsJson)
    };

    /// <summary>
    /// Performs the normalize extension step owned by this component.
    /// </summary>
    private static string NormalizeExtension(string extension)
    {
        var trimmed = extension.Trim();
        return trimmed.StartsWith('.') ? trimmed : "." + trimmed;
    }

    /// <summary>
    /// Performs the normalize json step owned by this component.
    /// </summary>
    private static string NormalizeJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "{}";
        using var document = JsonDocument.Parse(value);
        return JsonSerializer.Serialize(document.RootElement, JsonOptions);
    }

    /// <summary>
    /// Validates this member before it crosses the next trust or persistence boundary.
    /// </summary>
    private static void Validate(LanguageServerDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.Id) || definition.Id.Length > 80) throw new ArgumentException("A short language-server identifier is required.", nameof(definition));
        if (string.IsNullOrWhiteSpace(definition.DisplayName) || definition.DisplayName.Length > 160) throw new ArgumentException("A display name is required.", nameof(definition));
        if (string.IsNullOrWhiteSpace(definition.Command) || definition.Command.Length > 1_000) throw new ArgumentException("A language-server command is required.", nameof(definition));
        if (string.IsNullOrWhiteSpace(definition.LanguageId) || definition.LanguageId.Length > 80) throw new ArgumentException("A language identifier is required.", nameof(definition));
        if (definition.Extensions.Count == 0 || definition.Extensions.Any(item => string.IsNullOrWhiteSpace(item) || item.Length > 32))
            throw new ArgumentException("At least one valid file extension is required.", nameof(definition));
        _ = NormalizeJson(definition.InitializationOptionsJson);
    }

    /// <summary>
    /// Performs the built in defaults step owned by this component.
    /// </summary>
    private static IReadOnlyList<LanguageServerDefinition> BuiltInDefaults() =>
    [
        new("csharp-ls", "C# Language Server", "csharp-ls", string.Empty, "csharp", [".cs"], false, 25),
        new("typescript-language-server", "TypeScript Language Server", "typescript-language-server", "--stdio", "typescript", [".ts", ".tsx", ".js", ".jsx"], false, 25),
        new("pylsp", "Python Language Server", "pylsp", string.Empty, "python", [".py"], false, 25),
        new("rust-analyzer", "Rust Analyzer", "rust-analyzer", string.Empty, "rust", [".rs"], false, 30)
    ];
}
