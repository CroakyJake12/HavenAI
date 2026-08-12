namespace Haven.UI.Components;

public static class ToggleDefaults
{
    public const string SystemClass = "Toggle";
    public const string Transition = "ToggleChange";
    public static readonly HavenCornerRadius Radius = HavenCornerRadius.Uniform(HavenLength.Px(15));
}

/// <summary>Canonical binary Haven Toggle with Haven-owned activation semantics.</summary>
public sealed class Toggle : HavenElement
{
    public static readonly HavenProperty<bool> CheckedProperty =
        HavenPropertyRegistry.Register(new HavenProperty<bool>("Toggle.Checked", false));

    public event EventHandler? CheckedChanged;

    public Toggle()
    {
        Accessibility.Role = HavenAccessibleRole.CheckBox;
        Accessibility.Focusable = true;
        SetValue(HavenProperties.Width, HavenLength.Px(58), HavenValueSource.Default);
        SetValue(HavenProperties.Height, HavenLength.Px(30), HavenValueSource.Default);
        SetValue(HavenProperties.Radius, ToggleDefaults.Radius, HavenValueSource.Default);
        SetValue(HavenProperties.Background, "AccentSecondary", HavenValueSource.Default);
        SetValue(HavenProperties.Hover, true, HavenValueSource.Default);
    }

    public bool IsChecked
    {
        get => GetValue(CheckedProperty);
        set
        {
            if (value == GetValue(CheckedProperty)) return;
            SetValue(CheckedProperty, value);
            Accessibility.Checked = value;
            SetState(HavenElementState.Checked, value);
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void ToggleValue() => IsChecked = !IsChecked;

    public override HavenComponentMetadata Metadata => new(
        "Toggle",
        "Components/Toggle/Toggle.cs",
        [ToggleDefaults.SystemClass],
        [ToggleDefaults.Transition],
        "Track/thumb defaults, activation and checked-state behavior are defined beside the component.");

    protected override void OnStateChanged()
    {
        ClearValue(HavenProperties.Background, HavenValueSource.State);
        ClearValue(HavenProperties.Glow, HavenValueSource.State);
        ClearValue(HavenProperties.Transition, HavenValueSource.State);
        if (!State.HasFlag(HavenElementState.Checked)) return;
        SetValue(HavenProperties.Background, "Accent", HavenValueSource.State);
        SetValue(HavenProperties.Glow, "AccentGlow", HavenValueSource.State);
        SetValue(HavenProperties.Transition, ToggleDefaults.Transition, HavenValueSource.State);
    }
}
