// Where:    src/Haven.Android/HavenKeyboardSettings.cs
// What:     SharedPreferences-backed user preferences for the Haven Keyboard IME.
// How:      Read-through properties over a dedicated preference file so the IME
//           service and the settings activity always observe the same live values
//           without cache invalidation.
// Why:      The IME runs as a separate Android component from the main Haven app,
//           so it cannot share Haven's UserPreferencesService; a small isolated
//           preference store keeps the keyboard fully functional offline and
//           decoupled from app startup.
// Maintenance: Add new settings as a property pair here plus a control in
//           HavenKeyboardSettingsActivity. Defaults are deliberate: AI actions OFF,
//           haptics ON, sound OFF.

using Android.Content;

namespace Haven.Android;

/// <summary>One-handed layout anchor.</summary>
internal enum KeyboardOneHandedMode
{
    /// <summary>Centred, full-width layout.</summary>
    Off = 0,

    /// <summary>Shrink toward the left edge.</summary>
    Left = 1,

    /// <summary>Shrink toward the right edge.</summary>
    Right = 2,
}

/// <summary>
/// Live accessor for Haven Keyboard preferences. All values are read from
/// SharedPreferences on access so changes made in the settings activity apply on
/// the next field focus without restarting the IME.
/// </summary>
internal sealed class HavenKeyboardSettings
{
    /// <summary>Preference file name shared by the IME service and settings activity.</summary>
    internal const string PreferenceName = "haven_keyboard";

    private const string KeyAiEnabled = "ai_enabled";
    private const string KeyCloudAiAllowed = "cloud_ai_allowed";
    private const string KeyHapticsEnabled = "haptics_enabled";
    private const string KeySoundEnabled = "sound_enabled";
    private const string KeyNumberRowAlways = "number_row_always";
    private const string KeyHeightScale = "height_scale";
    private const string KeyOneHandedMode = "one_handed_mode";
    private const string KeyThemeMode = "theme_mode";
    private const string KeyLongPressDelayMs = "long_press_delay_ms";

    private const float MinHeightScale = 0.7f;
    private const float MaxHeightScale = 1.4f;
    private const int MinLongPressDelayMs = 120;
    private const int MaxLongPressDelayMs = 800;

    private readonly Context _context;

    /// <summary>Creates a settings accessor bound to the given context.</summary>
    internal HavenKeyboardSettings(Context context)
    {
        _context = context;
    }

    private ISharedPreferences Preferences =>
        _context.GetSharedPreferences(PreferenceName, FileCreationMode.Private)!;

    /// <summary>Master switch for AI text actions in the suggestion strip. Default: false.</summary>
    internal bool AiEnabled
    {
        get => Preferences.GetBoolean(KeyAiEnabled, false);
        set => Preferences.Edit()?.PutBoolean(KeyAiEnabled, value)?.Apply();
    }

    /// <summary>
    /// Reserved consent flag for future cloud-backed executors. It is irrelevant
    /// while <see cref="AiEnabled"/> is false and while only local/model-router
    /// executors are wired; the AI controller documents how it must gate clouds.
    /// </summary>
    internal bool CloudAiAllowed
    {
        get => Preferences.GetBoolean(KeyCloudAiAllowed, true);
        set => Preferences.Edit()?.PutBoolean(KeyCloudAiAllowed, value)?.Apply();
    }

    /// <summary>Haptic tick on key presses. Default: true.</summary>
    internal bool HapticsEnabled
    {
        get => Preferences.GetBoolean(KeyHapticsEnabled, true);
        set => Preferences.Edit()?.PutBoolean(KeyHapticsEnabled, value)?.Apply();
    }

    /// <summary>Audible key click. Default: false.</summary>
    internal bool SoundEnabled
    {
        get => Preferences.GetBoolean(KeySoundEnabled, false);
        set => Preferences.Edit()?.PutBoolean(KeySoundEnabled, value)?.Apply();
    }

    /// <summary>Always show a numeric row above the letter keys. Default: false.</summary>
    internal bool NumberRowAlways
    {
        get => Preferences.GetBoolean(KeyNumberRowAlways, false);
        set => Preferences.Edit()?.PutBoolean(KeyNumberRowAlways, value)?.Apply();
    }

    /// <summary>Keyboard height multiplier clamped to [0.7, 1.4]. Default: 1.0.</summary>
    internal float HeightScale
    {
        get => Math.Clamp(Preferences.GetFloat(KeyHeightScale, 1.0f), MinHeightScale, MaxHeightScale);
        set => Preferences.Edit()?.PutFloat(KeyHeightScale, Math.Clamp(value, MinHeightScale, MaxHeightScale))?.Apply();
    }

    /// <summary>One-handed layout anchor. Default: off.</summary>
    internal KeyboardOneHandedMode OneHandedMode
    {
        get => (KeyboardOneHandedMode)Math.Clamp(Preferences.GetInt(KeyOneHandedMode, 0), 0, 2);
        set => Preferences.Edit()?.PutInt(KeyOneHandedMode, (int)value)?.Apply();
    }

    /// <summary>Keyboard theme selection. Default: follow system.</summary>
    internal KeyboardThemeMode ThemeMode
    {
        get => (KeyboardThemeMode)Math.Clamp(Preferences.GetInt(KeyThemeMode, 0), 0, 2);
        set => Preferences.Edit()?.PutInt(KeyThemeMode, (int)value)?.Apply();
    }

    /// <summary>Initial delay before held-key repeat begins, clamped to [120, 800] ms. Default: 300.</summary>
    internal int LongPressDelayMs
    {
        get => Math.Clamp(Preferences.GetInt(KeyLongPressDelayMs, 300), MinLongPressDelayMs, MaxLongPressDelayMs);
        set => Preferences.Edit()?.PutInt(KeyLongPressDelayMs, Math.Clamp(value, MinLongPressDelayMs, MaxLongPressDelayMs))?.Apply();
    }

    /// <summary>Inclusive lower bound exposed by the settings slider.</summary>
    internal const float HeightScaleMinimum = MinHeightScale;

    /// <summary>Inclusive upper bound exposed by the settings slider.</summary>
    internal const float HeightScaleMaximum = MaxHeightScale;

    /// <summary>Inclusive lower bound (ms) exposed by the settings slider.</summary>
    internal const int LongPressDelayMinimum = MinLongPressDelayMs;

    /// <summary>Inclusive upper bound (ms) exposed by the settings slider.</summary>
    internal const int LongPressDelayMaximum = MaxLongPressDelayMs;
}
