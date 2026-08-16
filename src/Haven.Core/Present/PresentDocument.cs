namespace Haven.Core;

public enum PresentElementKind
{
    Text = 0,
    Image = 1,
    Shape = 2,
    GenUi = 3
}

public sealed class PresentDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Untitled presentation";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int Version { get; set; }
    public List<PresentSlide> Slides { get; set; } = [];
    public PresentRecoveryState Recovery { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);

    public static PresentDocument Create(string? title = null)
    {
        var document = new PresentDocument
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled presentation" : title.Trim()
        };
        document.Slides.Add(PresentSlide.Create(0));
        return document;
    }

    public void Normalize()
    {
        SchemaVersion = CurrentSchemaVersion;
        Title = string.IsNullOrWhiteSpace(Title) ? "Untitled presentation" : Title.Trim();
        Slides ??= [];
        Recovery ??= new PresentRecoveryState();
        Metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
        if (Slides.Count == 0)
            Slides.Add(PresentSlide.Create(0));

        for (var index = 0; index < Slides.Count; index++)
        {
            Slides[index] ??= PresentSlide.Create(index);
            Slides[index].Normalize(index);
        }
    }
}

public sealed class PresentSlide
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Order { get; set; }
    public string Title { get; set; } = "Untitled slide";
    public string SpeakerNotes { get; set; } = string.Empty;
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
        if (body is not null)
            return body;

        body = PresentElement.CreateBodyText();
        body.Order = Elements.Count == 0 ? 0 : Elements.Max(element => element.Order) + 1;
        Elements.Add(body);
        return body;
    }

    public void Normalize(int order)
    {
        Order = order;
        Title ??= string.Empty;
        SpeakerNotes ??= string.Empty;
        Elements ??= [];
        Metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < Elements.Count; index++)
        {
            Elements[index] ??= new PresentElement();
            Elements[index].Normalize(index);
        }
        _ = GetOrCreateBodyText();
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
    public string Role { get; set; } = string.Empty;
    public double X { get; set; } = 0.08;
    public double Y { get; set; } = 0.25;
    public double Width { get; set; } = 0.84;
    public double Height { get; set; } = 0.58;
    public string Text { get; set; } = string.Empty;
    public string AssetId { get; set; } = string.Empty;
    public string AlternativeText { get; set; } = string.Empty;
    public string ShapeType { get; set; } = string.Empty;
    public string GenUiMarkup { get; set; } = string.Empty;
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

    public void Normalize(int order)
    {
        Order = order;
        Role ??= string.Empty;
        Text ??= string.Empty;
        AssetId ??= string.Empty;
        AlternativeText ??= string.Empty;
        ShapeType ??= string.Empty;
        GenUiMarkup ??= string.Empty;
        Properties ??= new Dictionary<string, string>(StringComparer.Ordinal);
        X = ClampUnit(X);
        Y = ClampUnit(Y);
        Width = ClampSize(Width);
        Height = ClampSize(Height);
    }

    private static double ClampUnit(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private static double ClampSize(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0.01, 1) : 0.1;
}

public sealed class PresentRecoveryState
{
    public bool RecoveredFromBackup { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset? RecoveredAt { get; set; }
}
