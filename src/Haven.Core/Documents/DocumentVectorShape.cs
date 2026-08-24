namespace Haven.Core;

public enum DocumentVectorFillRule { EvenOdd = 0, NonZero = 1 }
public enum DocumentVectorSegmentKind { Line = 0, Quadratic = 1, Cubic = 2 }
public enum DocumentVectorNodeKind { Corner = 0, Smooth = 1, Symmetric = 2 }
public enum DocumentVectorFillKind { None = 0, Solid = 1 }
public enum DocumentVectorLineJoin { Miter = 0, Round = 1, Bevel = 2 }
public enum DocumentVectorLineCap { Butt = 0, Round = 1, Square = 2 }
public enum DocumentShapeSourceKind { Manual = 0, Ai = 1, BuiltIn = 2, Workspace = 3, Plugin = 4, Imported = 5 }

/// <summary>Persistent, editor-neutral custom vector shape shared by Write, Present, Data and Canvas.</summary>
public sealed class DocumentVectorShape
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Custom shape";
    public DocumentShapeSourceKind SourceKind { get; set; } = DocumentShapeSourceKind.Manual;
    public Guid? GallerySourceId { get; set; }
    public DocumentVectorViewBox ViewBox { get; set; } = new();
    public DocumentVectorTransform Transform { get; set; } = new();
    public List<DocumentVectorPath> Paths { get; set; } = [];
    public Guid? ClippingPathId { get; set; }
    public List<DocumentVectorConnectorPoint> ConnectorPoints { get; set; } = [];
    public string AccessibilityDescription { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);

    public void Normalize()
    {
        SchemaVersion = CurrentSchemaVersion;
        if (Id == Guid.Empty) Id = Guid.NewGuid();
        Name = string.IsNullOrWhiteSpace(Name) ? "Custom shape" : Name.Trim();
        AccessibilityDescription ??= string.Empty;
        ViewBox ??= new DocumentVectorViewBox();
        Transform ??= new DocumentVectorTransform();
        Paths ??= [];
        ConnectorPoints ??= [];
        Metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
        ViewBox.Normalize();
        Transform.Normalize();

        var pathIds = new HashSet<Guid>();
        foreach (var path in Paths)
        {
            path.Normalize();
            if (path.Id == Guid.Empty || !pathIds.Add(path.Id))
            {
                path.Id = Guid.NewGuid();
                pathIds.Add(path.Id);
            }
        }
        if (ClippingPathId is { } clip && !pathIds.Contains(clip)) ClippingPathId = null;

        var connectorIds = new HashSet<Guid>();
        foreach (var point in ConnectorPoints)
        {
            point.Normalize();
            if (point.Id == Guid.Empty || !connectorIds.Add(point.Id))
            {
                point.Id = Guid.NewGuid();
                connectorIds.Add(point.Id);
            }
        }
    }
}

public sealed class DocumentVectorViewBox
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 100;
    public double Height { get; set; } = 100;

    public void Normalize()
    {
        X = Finite(X, 0);
        Y = Finite(Y, 0);
        Width = PositiveFinite(Width, 100);
        Height = PositiveFinite(Height, 100);
    }

    internal static double Finite(double value, double fallback) => double.IsFinite(value) ? value : fallback;
    internal static double PositiveFinite(double value, double fallback) => double.IsFinite(value) && value > 0 ? value : fallback;
}

public sealed class DocumentVectorTransform
{
    public double TranslateX { get; set; }
    public double TranslateY { get; set; }
    public double ScaleX { get; set; } = 1;
    public double ScaleY { get; set; } = 1;
    public double RotationDegrees { get; set; }
    public double OriginX { get; set; } = 0.5;
    public double OriginY { get; set; } = 0.5;

    public void Normalize()
    {
        TranslateX = DocumentVectorViewBox.Finite(TranslateX, 0);
        TranslateY = DocumentVectorViewBox.Finite(TranslateY, 0);
        ScaleX = ClampScale(ScaleX);
        ScaleY = ClampScale(ScaleY);
        RotationDegrees = NormalizeDegrees(DocumentVectorViewBox.Finite(RotationDegrees, 0));
        OriginX = Math.Clamp(DocumentVectorViewBox.Finite(OriginX, 0.5), -1000, 1000);
        OriginY = Math.Clamp(DocumentVectorViewBox.Finite(OriginY, 0.5), -1000, 1000);
    }

    private static double ClampScale(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) < 0.000001) return 1;
        return Math.Clamp(value, -1000, 1000);
    }

    private static double NormalizeDegrees(double value)
    {
        var result = value % 360;
        if (result > 180) result -= 360;
        if (result <= -180) result += 360;
        return result;
    }
}

public sealed class DocumentVectorPath
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DocumentVectorFillRule FillRule { get; set; } = DocumentVectorFillRule.EvenOdd;
    public DocumentVectorFill Fill { get; set; } = new();
    public DocumentVectorStroke Stroke { get; set; } = new();
    public double Opacity { get; set; } = 1;
    public List<DocumentVectorSubpath> Subpaths { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);

    public void Normalize()
    {
        if (Id == Guid.Empty) Id = Guid.NewGuid();
        Fill ??= new DocumentVectorFill();
        Stroke ??= new DocumentVectorStroke();
        Subpaths ??= [];
        Metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
        Fill.Normalize();
        Stroke.Normalize();
        Opacity = Math.Clamp(DocumentVectorViewBox.Finite(Opacity, 1), 0, 1);
        var ids = new HashSet<Guid>();
        foreach (var subpath in Subpaths)
        {
            subpath.Normalize();
            if (subpath.Id == Guid.Empty || !ids.Add(subpath.Id))
            {
                subpath.Id = Guid.NewGuid();
                ids.Add(subpath.Id);
            }
        }
    }
}

public sealed class DocumentVectorSubpath
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Closed { get; set; }
    public List<DocumentVectorNode> Nodes { get; set; } = [];

    public void Normalize()
    {
        if (Id == Guid.Empty) Id = Guid.NewGuid();
        Nodes ??= [];
        var ids = new HashSet<Guid>();
        for (var index = 0; index < Nodes.Count; index++)
        {
            var node = Nodes[index] ?? new DocumentVectorNode();
            Nodes[index] = node;
            node.Normalize();
            if (node.Id == Guid.Empty || !ids.Add(node.Id))
            {
                node.Id = Guid.NewGuid();
                ids.Add(node.Id);
            }
            if (index == 0)
            {
                node.IncomingSegment = DocumentVectorSegmentKind.Line;
                node.Control1 = null;
                node.Control2 = null;
                continue;
            }
            var previous = Nodes[index - 1];
            if (node.IncomingSegment == DocumentVectorSegmentKind.Quadratic && node.Control1 is null)
                node.Control1 = DocumentVectorPoint.Lerp(previous.Point, node.Point, 0.5);
            if (node.IncomingSegment == DocumentVectorSegmentKind.Cubic)
            {
                node.Control1 ??= DocumentVectorPoint.Lerp(previous.Point, node.Point, 1d / 3d);
                node.Control2 ??= DocumentVectorPoint.Lerp(previous.Point, node.Point, 2d / 3d);
            }
            if (node.IncomingSegment == DocumentVectorSegmentKind.Line)
            {
                node.Control1 = null;
                node.Control2 = null;
            }
        }
        if (Nodes.Count < 2) Closed = false;
    }
}

public sealed class DocumentVectorNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public double X { get; set; }
    public double Y { get; set; }
    public DocumentVectorNodeKind Kind { get; set; }
    /// <summary>Segment from the previous node to this node.</summary>
    public DocumentVectorSegmentKind IncomingSegment { get; set; } = DocumentVectorSegmentKind.Line;
    /// <summary>Quadratic control, or first cubic control.</summary>
    public DocumentVectorPoint? Control1 { get; set; }
    /// <summary>Second cubic control.</summary>
    public DocumentVectorPoint? Control2 { get; set; }

    public DocumentVectorPoint Point => new(X, Y);

    public void Normalize()
    {
        if (Id == Guid.Empty) Id = Guid.NewGuid();
        X = DocumentVectorViewBox.Finite(X, 0);
        Y = DocumentVectorViewBox.Finite(Y, 0);
        Control1 = Control1?.Normalized();
        Control2 = Control2?.Normalized();
        if (IncomingSegment == DocumentVectorSegmentKind.Quadratic) Control2 = null;
    }
}

public sealed record DocumentVectorPoint(double X, double Y)
{
    public DocumentVectorPoint Normalized() => new(DocumentVectorViewBox.Finite(X, 0), DocumentVectorViewBox.Finite(Y, 0));
    public static DocumentVectorPoint Lerp(DocumentVectorPoint left, DocumentVectorPoint right, double amount) =>
        new(left.X + (right.X - left.X) * amount, left.Y + (right.Y - left.Y) * amount);
}

public sealed class DocumentVectorFill
{
    public DocumentVectorFillKind Kind { get; set; } = DocumentVectorFillKind.Solid;
    public string Color { get; set; } = "#FFFFFFFF";
    public double Opacity { get; set; } = 1;

    public void Normalize()
    {
        Color = string.IsNullOrWhiteSpace(Color) ? "#FFFFFFFF" : Color.Trim();
        Opacity = Math.Clamp(DocumentVectorViewBox.Finite(Opacity, 1), 0, 1);
    }
}

public sealed class DocumentVectorStroke
{
    public bool Enabled { get; set; } = true;
    public string Color { get; set; } = "#FF202020";
    public double Width { get; set; } = 1;
    public double Opacity { get; set; } = 1;
    public DocumentVectorLineJoin Join { get; set; } = DocumentVectorLineJoin.Round;
    public DocumentVectorLineCap Cap { get; set; } = DocumentVectorLineCap.Round;

    public void Normalize()
    {
        Color = string.IsNullOrWhiteSpace(Color) ? "#FF202020" : Color.Trim();
        Width = Math.Clamp(DocumentVectorViewBox.Finite(Width, 1), 0, 10000);
        Opacity = Math.Clamp(DocumentVectorViewBox.Finite(Opacity, 1), 0, 1);
    }
}

public sealed class DocumentVectorConnectorPoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double DirectionDegrees { get; set; }

    public void Normalize()
    {
        if (Id == Guid.Empty) Id = Guid.NewGuid();
        Name ??= string.Empty;
        X = DocumentVectorViewBox.Finite(X, 0);
        Y = DocumentVectorViewBox.Finite(Y, 0);
        DirectionDegrees = DocumentVectorViewBox.Finite(DirectionDegrees, 0) % 360;
    }
}
