namespace Haven.Core;

public enum PresentSlideSizePreset
{
    Widescreen16By9 = 0,
    Standard4By3 = 1,
    Portrait9By16 = 2,
    Custom = 3
}

public enum PresentPlaceholderKind
{
    Title = 0,
    Body = 1,
    Content = 2,
    Image = 3,
    Media = 4
}

public enum PresentBackgroundKind
{
    Theme = 0,
    Solid = 1,
    Image = 2
}

public enum PresentTransitionKind
{
    None = 0,
    Fade = 1,
    Push = 2,
    Wipe = 3,
    Morph = 4
}

public enum PresentAnimationEffect
{
    Appear = 0,
    Fade = 1,
    Fly = 2,
    Scale = 3,
    Emphasis = 4,
    Exit = 5
}

public enum PresentAnimationTrigger
{
    OnClick = 0,
    WithPrevious = 1,
    AfterPrevious = 2
}

public enum PresentEasingKind
{
    Linear = 0,
    EaseIn = 1,
    EaseOut = 2,
    EaseInOut = 3,
    Spring = 4
}

public enum PresentMotionDirection
{
    None = 0,
    Left = 1,
    Right = 2,
    Up = 3,
    Down = 4
}

public enum PresentTextHorizontalAlignment
{
    Left = 0,
    Center = 1,
    Right = 2,
    Justify = 3
}

public enum PresentTextVerticalAlignment
{
    Top = 0,
    Middle = 1,
    Bottom = 2
}

public sealed class PresentSlideSize
{
    public PresentSlideSizePreset Preset { get; set; } = PresentSlideSizePreset.Widescreen16By9;
    public double WidthInches { get; set; } = 13.333333;
    public double HeightInches { get; set; } = 7.5;

    public static PresentSlideSize Widescreen() => new();
    public static PresentSlideSize Standard() => new()
    {
        Preset = PresentSlideSizePreset.Standard4By3,
        WidthInches = 10,
        HeightInches = 7.5
    };
    public static PresentSlideSize Portrait() => new()
    {
        Preset = PresentSlideSizePreset.Portrait9By16,
        WidthInches = 7.5,
        HeightInches = 13.333333
    };

    public void Normalize()
    {
        if (Preset == PresentSlideSizePreset.Widescreen16By9)
        {
            WidthInches = 13.333333;
            HeightInches = 7.5;
            return;
        }
        if (Preset == PresentSlideSizePreset.Standard4By3)
        {
            WidthInches = 10;
            HeightInches = 7.5;
            return;
        }
        if (Preset == PresentSlideSizePreset.Portrait9By16)
        {
            WidthInches = 7.5;
            HeightInches = 13.333333;
            return;
        }

        WidthInches = double.IsFinite(WidthInches) ? Math.Clamp(WidthInches, 1, 100) : 13.333333;
        HeightInches = double.IsFinite(HeightInches) ? Math.Clamp(HeightInches, 1, 100) : 7.5;
    }
}

public sealed class PresentTheme
{
    public string Name { get; set; } = "Haven";
    public string HeadingFontFamily { get; set; } = "Aptos Display";
    public string BodyFontFamily { get; set; } = "Aptos";
    public PresentThemeColors Colors { get; set; } = new();
    public PresentBackground Background { get; set; } = new();

    public void Normalize()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "Haven" : Name.Trim();
        HeadingFontFamily = string.IsNullOrWhiteSpace(HeadingFontFamily) ? "Aptos Display" : HeadingFontFamily.Trim();
        BodyFontFamily = string.IsNullOrWhiteSpace(BodyFontFamily) ? "Aptos" : BodyFontFamily.Trim();
        Colors ??= new PresentThemeColors();
        Background ??= new PresentBackground();
        Colors.Normalize();
        Background.Normalize();
    }
}

public sealed class PresentThemeColors
{
    public string Background { get; set; } = "#FFFFFF";
    public string Foreground { get; set; } = "#1F1F1F";
    public string Accent1 { get; set; } = "#E65F42";
    public string Accent2 { get; set; } = "#4F7CAC";
    public string Accent3 { get; set; } = "#6A9A6B";
    public string Accent4 { get; set; } = "#8C6FB3";
    public string Accent5 { get; set; } = "#C28A3D";
    public string Accent6 { get; set; } = "#4F9A96";

    public void Normalize()
    {
        Background = NormalizeColor(Background, "#FFFFFF");
        Foreground = NormalizeColor(Foreground, "#1F1F1F");
        Accent1 = NormalizeColor(Accent1, "#E65F42");
        Accent2 = NormalizeColor(Accent2, "#4F7CAC");
        Accent3 = NormalizeColor(Accent3, "#6A9A6B");
        Accent4 = NormalizeColor(Accent4, "#8C6FB3");
        Accent5 = NormalizeColor(Accent5, "#C28A3D");
        Accent6 = NormalizeColor(Accent6, "#4F9A96");
    }

    private static string NormalizeColor(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

public sealed class PresentBackground
{
    public PresentBackgroundKind Kind { get; set; } = PresentBackgroundKind.Theme;
    public string Color { get; set; } = string.Empty;
    public string AssetId { get; set; } = string.Empty;

    public void Normalize()
    {
        Color ??= string.Empty;
        AssetId ??= string.Empty;
    }
}

public sealed class PresentLayoutDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Blank";
    public List<PresentLayoutPlaceholder> Placeholders { get; set; } = [];

    public static PresentLayoutDefinition CreateTitleAndContent() => new()
    {
        Name = "Title and content",
        Placeholders =
        [
            new PresentLayoutPlaceholder
            {
                Kind = PresentPlaceholderKind.Title, Role = "title",
                X = 0.06, Y = 0.055, Width = 0.88, Height = 0.16
            },
            new PresentLayoutPlaceholder
            {
                Kind = PresentPlaceholderKind.Body, Role = PresentElementRoles.Body,
                X = 0.08, Y = 0.25, Width = 0.84, Height = 0.58
            }
        ]
    };

    public static PresentLayoutDefinition CreateBlank() => new() { Name = "Blank" };

    public void Normalize()
    {
        if (Id == Guid.Empty) Id = Guid.NewGuid();
        Name = string.IsNullOrWhiteSpace(Name) ? "Layout" : Name.Trim();
        Placeholders ??= [];
        foreach (var placeholder in Placeholders) placeholder?.Normalize();
    }
}

public sealed class PresentLayoutPlaceholder
{
    public PresentPlaceholderKind Kind { get; set; } = PresentPlaceholderKind.Content;
    public string Role { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 1;
    public double Height { get; set; } = 1;

    public void Normalize()
    {
        Role ??= string.Empty;
        X = ClampUnit(X);
        Y = ClampUnit(Y);
        Width = ClampSize(Width);
        Height = ClampSize(Height);
    }

    private static double ClampUnit(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    private static double ClampSize(double value) => double.IsFinite(value) ? Math.Clamp(value, 0.01, 1) : 0.1;
}

public sealed class PresentSection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Section";

    public void Normalize()
    {
        if (Id == Guid.Empty) Id = Guid.NewGuid();
        Name = string.IsNullOrWhiteSpace(Name) ? "Section" : Name.Trim();
    }
}

public sealed class PresentTransition
{
    public PresentTransitionKind Kind { get; set; }
    public double DurationSeconds { get; set; }
    public PresentEasingKind Easing { get; set; } = PresentEasingKind.EaseInOut;
    public PresentMotionDirection Direction { get; set; }

    public void Normalize()
    {
        if (!double.IsFinite(DurationSeconds)) DurationSeconds = 0;
        DurationSeconds = Kind == PresentTransitionKind.None
            ? 0
            : Math.Clamp(DurationSeconds <= 0 ? 0.35 : DurationSeconds, 0.05, 30);
    }
}

public sealed class PresentAnimationCue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TargetElementId { get; set; }
    public PresentAnimationEffect Effect { get; set; } = PresentAnimationEffect.Appear;
    public PresentAnimationTrigger Trigger { get; set; } = PresentAnimationTrigger.OnClick;
    public int Order { get; set; }
    public double DelaySeconds { get; set; }
    public double DurationSeconds { get; set; } = 0.35;
    public PresentEasingKind Easing { get; set; } = PresentEasingKind.EaseInOut;
    public PresentMotionDirection Direction { get; set; }

    public void Normalize(int order)
    {
        if (Id == Guid.Empty) Id = Guid.NewGuid();
        Order = order;
        DelaySeconds = double.IsFinite(DelaySeconds) ? Math.Clamp(DelaySeconds, 0, 120) : 0;
        DurationSeconds = double.IsFinite(DurationSeconds)
            ? Math.Clamp(DurationSeconds <= 0 ? 0.35 : DurationSeconds, 0.01, 120)
            : 0.35;
    }
}

public sealed class PresentElementStyle
{
    public string FillColor { get; set; } = string.Empty;
    public string StrokeColor { get; set; } = string.Empty;
    public double StrokeWidth { get; set; }
    public double CornerRadius { get; set; }
    public bool Shadow { get; set; }

    public void Normalize()
    {
        FillColor ??= string.Empty;
        StrokeColor ??= string.Empty;
        StrokeWidth = double.IsFinite(StrokeWidth) ? Math.Clamp(StrokeWidth, 0, 100) : 0;
        CornerRadius = double.IsFinite(CornerRadius) ? Math.Clamp(CornerRadius, 0, 1) : 0;
    }
}

public sealed class PresentTextStyle
{
    public string FontFamily { get; set; } = string.Empty;
    public double FontSizePoints { get; set; } = 22;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public string Color { get; set; } = string.Empty;
    public PresentTextHorizontalAlignment HorizontalAlignment { get; set; }
    public PresentTextVerticalAlignment VerticalAlignment { get; set; }

    public void Normalize()
    {
        FontFamily ??= string.Empty;
        Color ??= string.Empty;
        FontSizePoints = double.IsFinite(FontSizePoints)
            ? Math.Clamp(FontSizePoints <= 0 ? 22 : FontSizePoints, 1, 400)
            : 22;
    }
}

public sealed class PresentMediaSettings
{
    public string MimeType { get; set; } = string.Empty;
    public bool AutoPlay { get; set; }
    public bool Loop { get; set; }
    public double StartSeconds { get; set; }
    public double? EndSeconds { get; set; }

    public void Normalize()
    {
        MimeType ??= string.Empty;
        StartSeconds = double.IsFinite(StartSeconds) ? Math.Max(0, StartSeconds) : 0;
        if (EndSeconds is { } end)
            EndSeconds = double.IsFinite(end) ? Math.Max(StartSeconds, end) : null;
    }
}

public sealed record PresentObjectNode(PresentElement Element, IReadOnlyList<PresentObjectNode> Children);

public static class PresentObjectTree
{
    public static IReadOnlyList<PresentObjectNode> Build(PresentSlide slide)
    {
        ArgumentNullException.ThrowIfNull(slide);
        return BuildChildren(slide.Elements ?? [], null, new HashSet<Guid>());
    }

    private static IReadOnlyList<PresentObjectNode> BuildChildren(
        IReadOnlyList<PresentElement> elements, Guid? parentId, HashSet<Guid> ancestry)
    {
        var result = new List<PresentObjectNode>();
        foreach (var element in elements.Where(item => item.ParentGroupId == parentId).OrderBy(item => item.Order))
        {
            if (!ancestry.Add(element.Id)) continue;
            var children = BuildChildren(elements, element.Id, ancestry);
            ancestry.Remove(element.Id);
            result.Add(new PresentObjectNode(element, children));
        }
        return result;
    }
}
