using System.Text.Json;

namespace Haven.Desktop.Services;

/// <summary>
/// Stores visual-motion preferences independently from model and workspace settings so the
/// shell can honour reduced motion before any page view model has finished initialising.
/// </summary>
public sealed class MotionPreferencesService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly Lazy<MotionPreferencesService> LazyCurrent = new(() => new MotionPreferencesService());
    private readonly object _gate = new();
    private readonly string _path;
    private MotionPreferences _preferences;

    private MotionPreferencesService()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(localData, "Haven");
        _path = Path.Combine(directory, "ui-preferences.json");
        _preferences = Load();
    }

    public static MotionPreferencesService Current => LazyCurrent.Value;

    public bool ReduceAnimations
    {
        get
        {
            lock (_gate)
                return _preferences.ReduceAnimations;
        }
    }

    public event EventHandler? Changed;

    public void SetReduceAnimations(bool value)
    {
        lock (_gate)
        {
            if (_preferences.ReduceAnimations == value) return;
            _preferences = _preferences with { ReduceAnimations = value };
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private MotionPreferences Load()
    {
        try
        {
            if (!File.Exists(_path)) return new MotionPreferences();
            return JsonSerializer.Deserialize<MotionPreferences>(File.ReadAllText(_path), JsonOptions)
                   ?? new MotionPreferences();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new MotionPreferences();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_preferences, JsonOptions));
            File.Move(temporary, _path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine("[Haven motion preferences] " + exception.Message);
        }
    }

    private sealed record MotionPreferences
    {
        public bool ReduceAnimations { get; init; }
    }
}
