using Haven.Application.QuickSettings;

namespace Haven.Core.Tests;

public sealed class QuickSettingsSliceTests
{
    [Fact]
    public void Slice_is_disabled_by_default_and_ignores_toggles()
    {
        var state = QuickSettingsSlice.Toggle(QuickSettingsSlice.Default, QuickSettingKey.ReduceMotion);

        Assert.False(QuickSettingsSlice.IsEnabledByDefault);
        Assert.Equal(QuickSettingsState.Disabled, state);
    }

    [Fact]
    public void Enabled_slice_toggles_local_reduce_motion_state()
    {
        var enabled = QuickSettingsSlice.SetEnabled(QuickSettingsSlice.Default, true);

        var toggled = QuickSettingsSlice.Toggle(enabled, QuickSettingKey.ReduceMotion);

        Assert.True(toggled.IsEnabled);
        Assert.True(toggled.ReduceMotion);
    }
}
