using System.Text.Json;

namespace Haven.Application;

public interface IVersionedSettingsStore
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class;
    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class;
    Task RemoveAsync(string key, CancellationToken cancellationToken);
    Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken);
    Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken);
}

public sealed class SettingsExportManifest
{
    public int Version { get; init; } = 1;
    public string ExportedAt { get; init; } = DateTimeOffset.UtcNow.ToString("O");
    public Dictionary<string, string> Settings { get; init; } = new();
}

public sealed class VersionedAtomicSettingsStore : IVersionedSettingsStore
{
    private readonly IAppPaths _paths;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<string, string> _settings = new(StringComparer.OrdinalIgnoreCase);
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

    public async Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return new SettingsExportManifest { Version = _version, Settings = new Dictionary<string, string>(_settings) };
    }

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

    private string GetSettingsPath() => Path.Combine(_paths.DataDirectory, "settings.json");
}
