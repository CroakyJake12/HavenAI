namespace Haven.UI.Components;

public enum TextLevel
{
    H1,
    H2,
    H3,
    H4,
    Paragraph,
    Caption,
    Code
}

public sealed record TextVisualDefaults(double FontSize, int FontWeight, string FontFamily, string Foreground);

public static class TextDefaults
{
    public const string SystemClass = "Text";

    public static TextVisualDefaults For(TextLevel level) => level switch
    {
        TextLevel.H1 => new(36, 800, "Montserrat", "TextPrimary"),
        TextLevel.H2 => new(30, 800, "Montserrat", "TextPrimary"),
        TextLevel.H3 => new(24, 700, "Montserrat", "TextPrimary"),
        TextLevel.H4 => new(19, 700, "Montserrat", "TextPrimary"),
        TextLevel.Caption => new(12, 600, "Montserrat", "TextSecondary"),
        TextLevel.Code => new(14, 500, "Code", "TextPrimary"),
        _ => new(15, 600, "Montserrat", "TextPrimary")
    };
}

/// <summary>Canonical Haven text primitive; Level changes semantics/design, not component type.</summary>
public sealed class Text : HavenElement
{
    public static readonly HavenProperty<string> ContentProperty =
        HavenPropertyRegistry.Register(new HavenProperty<string>("Text.Content", string.Empty));
    public static readonly HavenProperty<TextLevel> LevelProperty =
        HavenPropertyRegistry.Register(new HavenProperty<TextLevel>("Text.Level", TextLevel.Paragraph));

    public Text()
    {
        Accessibility.Role = HavenAccessibleRole.Text;
        ApplyDefaults();
    }

    public Text(string content) : this() => Content = content;

    public string Content
    {
        get => GetValue(ContentProperty);
        set
        {
            SetValue(ContentProperty, value ?? string.Empty);
            Accessibility.AccessibleName = value;
        }
    }

    public TextLevel Level
    {
        get => GetValue(LevelProperty);
        set
        {
            SetValue(LevelProperty, value);
            ApplyDefaults();
        }
    }

    public override HavenComponentMetadata Metadata => new(
        "Text",
        "Components/Text/Text.cs",
        [TextDefaults.SystemClass],
        [],
        "Text levels and typography defaults are defined beside the component.");

    private void ApplyDefaults()
    {
        var defaults = TextDefaults.For(Level);
        SetValue(HavenProperties.FontSize, defaults.FontSize, HavenValueSource.Default);
        SetValue(HavenProperties.FontWeight, defaults.FontWeight, HavenValueSource.Default);
        SetValue(HavenProperties.FontFamily, defaults.FontFamily, HavenValueSource.Default);
        SetValue(HavenProperties.Foreground, defaults.Foreground, HavenValueSource.Default);
        SetValue(HavenProperties.Hover, false, HavenValueSource.Default);
    }
}
