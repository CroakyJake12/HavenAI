/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/VersionedAtomicSettingsStore.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns IVersionedSettingsStore, SettingsExportManifest, VersionedAtomicSettingsStore. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;

namespace Haven.Application;

/// <summary>
/// Defines the i versioned settings store contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IVersionedSettingsStore
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class;
    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class;
    Task RemoveAsync(string key, CancellationToken cancellationToken);
    Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken);
    Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken);
}

/// <summary>
/// Represents settings export manifest and keeps its related state and behavior together.
/// </summary>
public sealed class SettingsExportManifest
{
    /// <summary>
    /// Gets or updates version, the bindable or domain state represented by this property.
    /// </summary>
    public int Version { get; init; } = 1;
    /// <summary>
    /// Gets or updates exported at, the bindable or domain state represented by this property.
    /// </summary>
    public string ExportedAt { get; init; } = DateTimeOffset.UtcNow.ToString("O");
    /// <summary>
    /// Gets or updates settings, the bindable or domain state represented by this property.
    /// </summary>
    public Dictionary<string, string> Settings { get; init; } = new();
}

/// <summary>
/// Represents versioned atomic settings store and keeps its related state and behavior together.
/// </summary>
public sealed class VersionedAtomicSettingsStore : IVersionedSettingsStore
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IAppPaths _paths;
    /// <summary>
    /// Stores lock locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SemaphoreSlim _lock = new(1, 1);
    /// <summary>
    /// Stores settings locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Dictionary<string, string> _settings = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Stores version locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _version;

    public VersionedAtomicSettingsStore(IAppPaths paths)
    {
        _paths = paths;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        if (!_settings.TryGetValue(key, out var json)) return null;
        try { return JsonSerializer.Deserialize<T>(json); }
        catch { return null; }
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _settings[key] = JsonSerializer.Serialize(value);
            _version++;
            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Performs remove async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_settings.Remove(key))
            {
                _version++;
                await PersistAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Performs export async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return new SettingsExportManifest { Version = _version, Settings = new Dictionary<string, string>(_settings) };
    }

    /// <summary>
    /// Performs import async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var errors = new List<string>();
            foreach (var (key, value) in manifest.Settings)
            {
                try
                {
                    _settings[key] = value;
                }
                catch (Exception ex) { errors.Add($"Failed to import '{key}': {ex.Message}"); }
            }
            _version++;
            await PersistAsync(cancellationToken).ConfigureAwait(false);
            var importedSettings = errors.Count == 0 ? new Dictionary<string, string>(_settings) : null;
            return new SettingsImportResult(errors.Count == 0, importedSettings,
                errors.Count == 0 ? $"Imported {manifest.Settings.Count} settings" : $"Imported with {errors.Count} errors");
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Performs ensure loaded async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_settings.Count > 0) return;
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = GetSettingsPath();
            if (File.Exists(path))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                    var loaded = JsonSerializer.Deserialize<SettingsExportManifest>(json);
                    if (loaded is not null)
                    {
                        _settings = new Dictionary<string, string>(loaded.Settings, StringComparer.OrdinalIgnoreCase);
                        _version = loaded.Version;
                    }
                }
                catch
                {
                    var backupPath = path + ".bak";
                    if (File.Exists(backupPath))
                    {
                        var json = await File.ReadAllTextAsync(backupPath, cancellationToken).ConfigureAwait(false);
                        var loaded = JsonSerializer.Deserialize<SettingsExportManifest>(json);
                        if (loaded is not null)
                        {
                            _settings = new Dictionary<string, string>(loaded.Settings, StringComparer.OrdinalIgnoreCase);
                            _version = loaded.Version;
                        }
                    }
                }
            }
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Performs persist async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        var path = GetSettingsPath();
        var dir = Path.GetDirectoryName(path);
        if (dir is not null) Directory.CreateDirectory(dir);

        var manifest = new SettingsExportManifest { Version = _version, Settings = new Dictionary<string, string>(_settings) };
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });

        var tempPath = path + ".tmp";
        var backupPath = path + ".bak";

        await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);

        if (File.Exists(path))
        {
            if (File.Exists(backupPath)) File.Delete(backupPath);
            File.Move(path, backupPath);
        }
        File.Move(tempPath, path);
    }

    /// <summary>
    /// Retrieves settings path for the current operation.
    /// </summary>
    private string GetSettingsPath() => Path.Combine(_paths.DataDirectory, "settings.json");
}
