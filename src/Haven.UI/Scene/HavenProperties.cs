namespace Haven.UI;

public enum HavenHorizontalAlignment { Stretch, Start, Center, End }
public enum HavenVerticalAlignment { Stretch, Start, Center, End }
public enum HavenOverflow { Visible, Clip, Scroll }
public enum HavenVisibility { Visible, Hidden, Collapsed }
public enum HavenPointerEvents { Auto, None }
public enum HavenCursor { Default, Pointer, Text, Grab, Grabbing, Crosshair }

/// <summary>Common properties shared by Haven components when semantics match.</summary>
public static class HavenProperties
{
    private static HavenProperty<T> Property<T>(string name, T value) =>
        HavenPropertyRegistry.Register(new HavenProperty<T>(name, value));

    public static readonly HavenProperty<string?> Name = Property<string?>(nameof(Name), null);
    public static readonly HavenProperty<string> Group = Property(nameof(Group), string.Empty);
    public static readonly HavenProperty<string> Class = Property(nameof(Class), string.Empty);

    public static readonly HavenProperty<HavenLength> Width = Property(nameof(Width), HavenLength.Auto);
    public static readonly HavenProperty<HavenLength> Height = Property(nameof(Height), HavenLength.Auto);
    public static readonly HavenProperty<HavenLength> MinWidth = Property(nameof(MinWidth), HavenLength.Px(0));
    public static readonly HavenProperty<HavenLength> MinHeight = Property(nameof(MinHeight), HavenLength.Px(0));
    public static readonly HavenProperty<HavenLength> MaxWidth = Property(nameof(MaxWidth), HavenLength.Auto);
    public static readonly HavenProperty<HavenLength> MaxHeight = Property(nameof(MaxHeight), HavenLength.Auto);
    public static readonly HavenProperty<double?> AspectRatio = Property<double?>(nameof(AspectRatio), null);

    public static readonly HavenProperty<HavenThickness> Margin = Property(nameof(Margin), HavenThickness.Zero);
    public static readonly HavenProperty<HavenThickness> Padding = Property(nameof(Padding), HavenThickness.Zero);
    public static readonly HavenProperty<HavenLength> Gap = Property(nameof(Gap), HavenLength.Px(0));
    public static readonly HavenProperty<HavenHorizontalAlignment> HorizontalAlignment = Property(nameof(HorizontalAlignment), HavenHorizontalAlignment.Stretch);
    public static readonly HavenProperty<HavenVerticalAlignment> VerticalAlignment = Property(nameof(VerticalAlignment), HavenVerticalAlignment.Stretch);
    public static readonly HavenProperty<int> Row = Property(nameof(Row), 0);
    public static readonly HavenProperty<int> Column = Property(nameof(Column), 0);
    public static readonly HavenProperty<int> RowSpan = Property(nameof(RowSpan), 1);
    public static readonly HavenProperty<int> ColumnSpan = Property(nameof(ColumnSpan), 1);
    public static readonly HavenProperty<HavenLength> Left = Property(nameof(Left), HavenLength.Px(0));
    public static readonly HavenProperty<HavenLength> Top = Property(nameof(Top), HavenLength.Px(0));

    public static readonly HavenProperty<string> Background = Property(nameof(Background), "Transparent");
    public static readonly HavenProperty<string> Foreground = Property(nameof(Foreground), "TextPrimary");
    public static readonly HavenProperty<string> Accent = Property(nameof(Accent), "Accent");
    public static readonly HavenProperty<double> Opacity = Property(nameof(Opacity), 1d);
    public static readonly HavenProperty<HavenLength> BorderWidth = Property(nameof(BorderWidth), HavenLength.Px(0));
    public static readonly HavenProperty<string> BorderColor = Property(nameof(BorderColor), "Border");
    public static readonly HavenProperty<HavenCornerRadius> Radius = Property(nameof(Radius), HavenCornerRadius.Uniform(HavenLength.Px(0)));
    public static readonly HavenProperty<string> Shadow = Property(nameof(Shadow), "None");
    public static readonly HavenProperty<string> Glow = Property(nameof(Glow), "None");
    public static readonly HavenProperty<double> BackdropBlur = Property(nameof(BackdropBlur), 0d);

    public static readonly HavenProperty<double> Scale = Property(nameof(Scale), 1d);
    public static readonly HavenProperty<double> Rotation = Property(nameof(Rotation), 0d);
    public static readonly HavenProperty<HavenLength> TranslationX = Property(nameof(TranslationX), HavenLength.Px(0));
    public static readonly HavenProperty<HavenLength> TranslationY = Property(nameof(TranslationY), HavenLength.Px(0));
    public static readonly HavenProperty<string> TransformOrigin = Property(nameof(TransformOrigin), "Center");

    public static readonly HavenProperty<bool> Clip = Property(nameof(Clip), false);
    public static readonly HavenProperty<HavenOverflow> Overflow = Property(nameof(Overflow), HavenOverflow.Visible);
    public static readonly HavenProperty<int> ZIndex = Property(nameof(ZIndex), 0);
    public static readonly HavenProperty<HavenVisibility> Visibility = Property(nameof(Visibility), HavenVisibility.Visible);

    public static readonly HavenProperty<bool> Enabled = Property(nameof(Enabled), true);
    public static readonly HavenProperty<bool?> Hover = Property<bool?>(nameof(Hover), null);
    public static readonly HavenProperty<HavenPointerEvents> PointerEvents = Property(nameof(PointerEvents), HavenPointerEvents.Auto);
    public static readonly HavenProperty<HavenCursor> Cursor = Property(nameof(Cursor), HavenCursor.Default);
    public static readonly HavenProperty<bool> Responsive = Property(nameof(Responsive), true);

    public static readonly HavenProperty<string?> Animation = Property<string?>(nameof(Animation), null);
    public static readonly HavenProperty<string?> Transition = Property<string?>(nameof(Transition), null);
    public static readonly HavenProperty<string> FontFamily = Property(nameof(FontFamily), "Montserrat");
    public static readonly HavenProperty<double> FontSize = Property(nameof(FontSize), 15d);
    public static readonly HavenProperty<int> FontWeight = Property(nameof(FontWeight), 600);
}
