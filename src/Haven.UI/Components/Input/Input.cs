namespace Haven.UI.Components;

public sealed class Input : HavenElement
{
    public static readonly HavenProperty<string> TextProperty = HavenPropertyRegistry.Register(new HavenProperty<string>("Input.Text", string.Empty));
    public static readonly HavenProperty<string> PlaceholderProperty = HavenPropertyRegistry.Register(new HavenProperty<string>("Input.Placeholder", string.Empty));
    public static readonly HavenProperty<bool> MultilineProperty = HavenPropertyRegistry.Register(new HavenProperty<bool>("Input.Multiline", false));

    public Input()
    {
        Accessibility.Role = HavenAccessibleRole.Input;
        Accessibility.Focusable = true;
        SetValue(HavenProperties.Hover, true, HavenValueSource.Default);
        SetValue(HavenProperties.MinHeight, HavenLength.Px(48), HavenValueSource.Default);
        SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(24)), HavenValueSource.Default);
        SetValue(HavenProperties.Background, "SurfaceRaised", HavenValueSource.Default);
        SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 18px"), HavenValueSource.Default);
    }

    public string Text { get => GetValue(TextProperty); set { SetValue(TextProperty, value ?? string.Empty); Accessibility.AccessibleName = value; } }
    public string Placeholder { get => GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value ?? string.Empty); }
    public bool Multiline { get => GetValue(MultilineProperty); set => SetValue(MultilineProperty, value); }

    public override HavenComponentMetadata Metadata => new(
        "Input",
        "Components/Input/Input.cs",
        ["Input"],
        ["InputFocus"],
        "Haven owns field chrome/state; platform hosts may attach a native editable-text adapter for IME/caret editing.");
}
