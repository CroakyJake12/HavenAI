using System.Text.Json;
using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>
/// Stores privacy choices outside conversational content. Defaults fail closed:
/// background learning and model-improvement sharing are disabled until enabled.
/// </summary>
public sealed class PrivacyPreferenceStore : IPrivacyPreferenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PrivacyPreferences _current;

    public PrivacyPreferenceStore(IAppPaths paths)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        _path = Path.Combine(paths.DataDirectory, "privacy-preferences.json");
        _current = Load(_path);
    }

    public PrivacyPreferences Current => Volatile.Read(ref _current);

    public async Task UpdateAsync(PrivacyPreferences preferences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var normalized = preferences with { UpdatedAt = DateTimeOffset.UtcNow };
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(
                    temporary,
                    JsonSerializer.Serialize(normalized, JsonOptions),
                    cancellationToken).ConfigureAwait(false);
                File.Move(temporary, _path, overwrite: true);
                Volatile.Write(ref _current, normalized);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static PrivacyPreferences Load(string path)
    {
        if (!File.Exists(path)) return PrivacyPreferences.Default;
        try
        {
            return JsonSerializer.Deserialize<PrivacyPreferences>(File.ReadAllText(path), JsonOptions)
                ?? PrivacyPreferences.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return PrivacyPreferences.Default;
        }
    }
}
