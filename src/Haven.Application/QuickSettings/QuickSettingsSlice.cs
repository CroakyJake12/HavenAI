namespace Haven.Application.QuickSettings;

public enum QuickSettingKey
{
    ReduceMotion = 0
}

public sealed record QuickSettingsState(bool IsEnabled, bool ReduceMotion)
{
    public static QuickSettingsState Disabled { get; } = new(false, false);
}

/// <summary>
/// Local, opt-in Quick Settings state. It has no shell, privilege, or startup dependency.
/// </summary>
public static class QuickSettingsSlice
{
    public const string Key = "quick-settings";
    public const bool IsEnabledByDefault = false;

    public static QuickSettingsState Default => QuickSettingsState.Disabled;

    public static QuickSettingsState SetEnabled(QuickSettingsState state, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state with { IsEnabled = enabled };
    }

    public static QuickSettingsState Toggle(QuickSettingsState state, QuickSettingKey setting)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.IsEnabled)
            return state;

        return setting switch
        {
            QuickSettingKey.ReduceMotion => state with { ReduceMotion = !state.ReduceMotion },
            _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, "Unknown Quick Setting.")
        };
    }
}
