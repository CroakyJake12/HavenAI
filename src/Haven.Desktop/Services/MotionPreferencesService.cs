/*
 * FILE DOCUMENTATION
 * Where: src/Haven.OldHaven/Services/MotionPreferencesService.cs, in the Desktop services layer, adapting application behavior to Windows and Avalonia concerns.
 * What: This file owns MotionPreferencesService, MotionPreferences. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;

namespace Haven.Desktop.Services;

/// <summary>
/// Stores visual-motion preferences independently from model and workspace settings so the
/// shell can honour reduced motion before any page view model has finished initialising.
/// </summary>
public sealed class MotionPreferencesService
{
    /// <summary>
    /// Stores json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    /// Stores lazy current locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly Lazy<MotionPreferencesService> LazyCurrent = new(() => new MotionPreferencesService());
    /// <summary>
    /// Stores gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly object _gate = new();
    /// <summary>
    /// Stores path locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _path;
    /// <summary>
    /// Stores preferences locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private MotionPreferences _preferences;

    private MotionPreferencesService()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(localData, "Haven");
        _path = Path.Combine(directory, "ui-preferences.json");
        _preferences = Load();
    }

    /// <summary>
    /// Gets or updates current, the bindable or domain state represented by this property.
    /// </summary>
    public static MotionPreferencesService Current => LazyCurrent.Value;

    public bool ReduceAnimations
    {
        get
        {
            lock (_gate)
                return _preferences.ReduceAnimations;
        }
    }

    /// <summary>
    /// Stores changed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Performs the set reduce animations step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the load step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the save step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Represents motion preferences and keeps its related state and behavior together.
    /// </summary>
    private sealed record MotionPreferences
    {
        /// <summary>
        /// Gets or updates reduce animations, the bindable or domain state represented by this property.
        /// </summary>
        public bool ReduceAnimations { get; init; }
    }
}
