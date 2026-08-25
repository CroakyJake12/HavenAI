namespace Haven.Core;

public enum PresentElementKind
{
    Text = 0,
    Image = 1,
    Shape = 2,
    GenUi = 3,
    Media = 4,
    Group = 5,
    Table = 6,
    Chart = 7
}

public sealed class PresentDocument
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Untitled presentation";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int Version { get; set; }
    public PresentSlideSize SlideSize { get; set; } = PresentSlideSize.Widescreen();
    public PresentTheme Theme { get; set; } = new();
    public List<PresentLayoutDefinition> Layouts { get; set; } = [];
    public List<PresentSection> Sections { get; set; } = [];
    public List<PresentSlide> Slides { get; set; } = [];
    public PresentRecoveryState Recovery { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);

    public static PresentDocument Create(string? title = null)
    {
        var document = new PresentDocument
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled presentation" : title.Trim()
        };
        var layout = PresentLayoutDefinition.CreateTitleAndContent();
        document.Layouts.Add(layout);
        document.Layouts.Add(PresentLayoutDefinition.CreateBlank());
        var slide = PresentSlide.Create(0);
        slide.LayoutId = layout.Id;
        document.Slides.Add(slide);
        return document;
    }

    public void Normalize()
    {
        SchemaVersion = CurrentSchemaVersion;
        if (Id == Guid.Empty) Id = Guid.NewGuid();
        Title = string.IsNullOrWhiteSpace(Title) ? "Untitled presentation" : Title.Trim();
        SlideSize ??= PresentSlideSize.Widescreen();
        Theme ??= new PresentTheme();
        Layouts ??= [];
        Sections ??= [];
        Slides ??= [];
        Recovery ??= new PresentRecoveryState();
        Metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
        SlideSize.Normalize();
        Theme.Normalize();

        if (Layouts.Count == 0)
        {
            Layouts.Add(PresentLayoutDefinition.CreateTitleAndContent());
            Layouts.Add(PresentLayoutDefinition.CreateBlank());
        }
        foreach (var layout in Layouts) layout?.Normalize();
        foreach (var section in Sections) section?.Normalize();

        if (Slides.Count == 0) Slides.Add(PresentSlide.Create(0));
        var layoutIds = Layouts.Where(item => item is not null).Select(item => item.Id).ToHashSet();
        var sectionIds = Sections.Where(item => item is not null).Select(item => item.Id).ToHashSet();
        var fallbackLayout = Layouts.First(item => item is not null).Id;
        for (var index = 0; index < Slides.Count; index++)
        {
            Slides[index] ??= PresentSlide.Create(index);
            Slides[index].Normalize(index);
            if (Slides[index].LayoutId is not { } layoutId || !layoutIds.Contains(layoutId))
                Slides[index].LayoutId = fallbackLayout;
            if (Slides[index].SectionId is { } sectionId && !sectionIds.Contains(sectionId))
                Slides[index].SectionId = null;
        }
    }
}

public sealed class PresentSlide
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Order { get; set; }
    public Guid? LayoutId { get; set; }
    public Guid? SectionId { get; set; }
    public string Title { get; set; } = "Untitled slide";
    public string SpeakerNotes { get; set; } = string.Empty;
    public PresentBackground Background { get; set; } = new();
    public PresentTransition Transition { get; set; } = new();
    public List<PresentAnimationCue> Animations { get; set; } = [];
    public bool Hidden { get; set; }
    public List<PresentElement> Elements { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);

    public static PresentSlide Create(int order)
    {
        var slide = new PresentSlide { Order = order };
        slide.Elements.Add(PresentElement.CreateBodyText());
        return slide;
    }

    public PresentElement GetOrCreateBodyText()
    {
        Elements ??= [];
        var body = Elements
            .OrderBy(element => element.Order)
            .FirstOrDefault(element => element.Kind == PresentElementKind.Text
                && string.Equals(element.Role, PresentElementRoles.Body, StringComparison.OrdinalIgnoreCase));
        if (body is not null) return body;

        body = PresentElement.CreateBodyText();
        body.Order = Elements.Count == 0 ? 0 : Elements.Max(element => element.Order) + 1;
        Elements.Add(body);
        return body;
    }

    public void Normalize(int order)
    {
        if (Id == Guid.Empty) Id = Guid.NewGuid();
        Order = order;
        Title ??= string.Empty;
        SpeakerNotes ??= string.Empty;
        Background ??= new PresentBackground();
        Transition ??= new PresentTransition();
        Animations ??= [];
        Elements ??= [];
        Metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
        Background.Normalize();
        Transition.Normalize();

        var elementIds = new HashSet<Guid>();
        for (var index = 0; index < Elements.Count; index++)
        {
            Elements[index] ??= new PresentElement();
            if (Elements[index].Id == Guid.Empty || !elementIds.Add(Elements[index].Id))
            {
                Elements[index].Id = Guid.NewGuid();
                elementIds.Add(Elements[index].Id);
            }
            Elements[index].Normalize(index);
        }

        var groups = Elements.Where(element => element.Kind == PresentElementKind.Group)
            .ToDictionary(element => element.Id);
        foreach (var element in Elements)
        {
            if (element.ParentGroupId is not { } parentId) continue;
            if (!groups.ContainsKey(parentId) || parentId == element.Id || HasParentCycle(element, groups))
                element.ParentGroupId = null;
        }

        Animations = Animations
            .Where(cue => cue is not null && elementIds.Contains(cue.TargetElementId))
            .OrderBy(cue => cue.Order)
            .ToList();
        for (var index = 0; index < Animations.Count; index++) Animations[index].Normalize(index);
        _ = GetOrCreateBodyText();
    }

    private static bool HasParentCycle(PresentElement element, IReadOnlyDictionary<Guid, PresentElement> groups)
    {
        var seen = new HashSet<Guid> { element.Id };
        var parent = element.ParentGroupId;
        while (parent is { } parentId && groups.TryGetValue(parentId, out var group))
        {
            if (!seen.Add(parentId)) return true;
            parent = group.ParentGroupId;
        }
        return false;
    }
}

public static class PresentElementRoles
{
    public const string Body = "body";
}

public sealed class PresentElement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PresentElementKind Kind { get; set; } = PresentElementKind.Text;
    public int Order { get; set; }
    public Guid? ParentGroupId { get; set; }
    public string Role { get; set; } = string.Empty;
    public double X { get; set; } = 0.08;
    public double Y { get; set; } = 0.25;
    public double Width { get; set; } = 0.84;
    public double Height { get; set; } = 0.58;
    public double RotationDegrees { get; set; }
    public double Opacity { get; set; } = 1;
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public string Text { get; set; } = string.Empty;
    public string AssetId { get; set; } = string.Empty;
    public string AlternativeText { get; set; } = string.Empty;
    public string ShapeType { get; set; } = string.Empty;
    public DocumentVectorShape? VectorShape { get; set; }
    public string GenUiMarkup { get; set; } = string.Empty;
    public PresentElementStyle Style { get; set; } = new();
    public PresentTextStyle TextStyle { get; set; } = new();
    public PresentMediaSettings Media { get; set; } = new();
    public Dictionary<string, string> Properties { get; set; } = new(StringComparer.Ordinal);

    public static PresentElement CreateBodyText() => new()
    {
        Kind = PresentElementKind.Text,
        Role = PresentElementRoles.Body,
        X = 0.08,
        Y = 0.25,
        Width = 0.84,
        Height = 0.58
    };

    public static PresentElement CreateGroup(IEnumerable<PresentElement> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        var materialized = children.Where(child => child is not null).Distinct().ToArray();
        if (materialized.Length < 2) throw new ArgumentException("A group needs at least two elements.", nameof(children));
        var group = new PresentElement
        {
            Kind = PresentElementKind.Group,
            X = materialized.Min(child => child.X),
            Y = materialized.Min(child => child.Y),
            Width = Math.Max(0.01, materialized.Max(child => child.X + child.Width) - materialized.Min(child => child.X)),
            Height = Math.Max(0.01, materialized.Max(child => child.Y + child.Height) - materialized.Min(child => child.Y))
        };
        foreach (var child in materialized) child.ParentGroupId = group.Id;
        return group;
    }

    public void Normalize(int order)
    {
        if (Id == Guid.Empty) Id = Guid.NewGuid();
        Order = order;
        Role ??= string.Empty;
        Text ??= string.Empty;
        AssetId ??= string.Empty;
        AlternativeText ??= string.Empty;
        ShapeType ??= string.Empty;
        VectorShape?.Normalize();
        GenUiMarkup ??= string.Empty;
        Style ??= new PresentElementStyle();
        TextStyle ??= new PresentTextStyle();
        Media ??= new PresentMediaSettings();
        Properties ??= new Dictionary<string, string>(StringComparer.Ordinal);
        X = ClampUnit(X);
        Y = ClampUnit(Y);
        Width = ClampSize(Width);
        Height = ClampSize(Height);
        RotationDegrees = double.IsFinite(RotationDegrees) ? NormalizeDegrees(RotationDegrees) : 0;
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0, 1) : 1;
        Style.Normalize();
        TextStyle.Normalize();
        Media.Normalize();
    }

    private static double ClampUnit(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private static double ClampSize(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0.01, 1) : 0.1;

    private static double NormalizeDegrees(double value)
    {
        var normalized = value % 360;
        if (normalized > 180) normalized -= 360;
        if (normalized <= -180) normalized += 360;
        return normalized;
    }
}

public sealed class PresentRecoveryState
{
    public bool RecoveredFromBackup { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset? RecoveredAt { get; set; }
}
