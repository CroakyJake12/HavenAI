using System.Text.Json;
using System.Text.Json.Serialization;
using Haven.Core;

namespace Haven.Application;

public enum DocumentValidationSeverity { Warning = 0, Error = 1 }
public sealed record DocumentVectorValidationIssue(DocumentValidationSeverity Severity, string Code, string Message);
public sealed record DocumentVectorValidationResult(IReadOnlyList<DocumentVectorValidationIssue> Issues)
{
    public bool IsValid => Issues.All(issue => issue.Severity != DocumentValidationSeverity.Error);
}

public static class DocumentVectorShapeValidator
{
    public const int MaximumPaths = 256;
    public const int MaximumSubpaths = 1024;
    public const int MaximumNodes = 20_000;

    public static DocumentVectorValidationResult Validate(DocumentVectorShape? shape, bool requireEditableGeometry = true)
    {
        var issues = new List<DocumentVectorValidationIssue>();
        if (shape is null) return Invalid("shape.null", "The vector shape is missing.");
        if (shape.SchemaVersion <= 0 || shape.SchemaVersion > DocumentVectorShape.CurrentSchemaVersion)
            Error(issues, "shape.schema", "The vector shape schema version is not supported.");
        if (shape.Id == Guid.Empty) Error(issues, "shape.id", "The vector shape needs a stable identifier.");
        if (shape.ViewBox is null || !FinitePositive(shape.ViewBox.Width) || !FinitePositive(shape.ViewBox.Height)
            || !double.IsFinite(shape.ViewBox.X) || !double.IsFinite(shape.ViewBox.Y))
            Error(issues, "shape.viewBox", "The vector shape view box must contain finite coordinates and positive dimensions.");
        if (shape.Transform is null || !TransformIsFinite(shape.Transform))
            Error(issues, "shape.transform", "The vector shape transform contains invalid numeric values.");
        if (shape.Paths is null || shape.Paths.Count == 0)
            Error(issues, "shape.paths.empty", "An editable vector shape needs at least one path.");
        else if (shape.Paths.Count > MaximumPaths)
            Error(issues, "shape.paths.limit", $"A vector shape may contain at most {MaximumPaths} paths.");

        var subpathCount = 0;
        var nodeCount = 0;
        var pathIds = new HashSet<Guid>();
        foreach (var path in shape.Paths ?? [])
        {
            if (path is null) { Error(issues, "path.null", "A vector path is missing."); continue; }
            if (path.Id == Guid.Empty || !pathIds.Add(path.Id)) Error(issues, "path.id", "Vector path identifiers must be non-empty and unique.");
            if (!double.IsFinite(path.Opacity) || path.Opacity is < 0 or > 1) Error(issues, "path.opacity", "Path opacity must be between 0 and 1.");
            ValidatePaint(path, issues);
            if (path.Subpaths is null || path.Subpaths.Count == 0)
            {
                if (requireEditableGeometry) Error(issues, "path.subpaths.empty", "A path needs at least one editable subpath.");
                continue;
            }

            subpathCount += path.Subpaths.Count;
            var subpathIds = new HashSet<Guid>();
            foreach (var subpath in path.Subpaths)
            {
                if (subpath is null) { Error(issues, "subpath.null", "A vector subpath is missing."); continue; }
                if (subpath.Id == Guid.Empty || !subpathIds.Add(subpath.Id)) Error(issues, "subpath.id", "Subpath identifiers must be non-empty and unique within a path.");
                if (subpath.Nodes is null || subpath.Nodes.Count < 2)
                {
                    if (requireEditableGeometry) Error(issues, "subpath.nodes", "An editable subpath needs at least two nodes.");
                    continue;
                }
                nodeCount += subpath.Nodes.Count;
                var nodeIds = new HashSet<Guid>();
                for (var index = 0; index < subpath.Nodes.Count; index++)
                {
                    var node = subpath.Nodes[index];
                    if (node is null) { Error(issues, "node.null", "A vector node is missing."); continue; }
                    if (node.Id == Guid.Empty || !nodeIds.Add(node.Id)) Error(issues, "node.id", "Node identifiers must be non-empty and unique within a subpath.");
                    if (!double.IsFinite(node.X) || !double.IsFinite(node.Y)) Error(issues, "node.point", "Vector node coordinates must be finite.");
                    if (index == 0) continue;
                    if (node.IncomingSegment == DocumentVectorSegmentKind.Quadratic && !Finite(node.Control1))
                        Error(issues, "node.quadratic.control", "A quadratic segment requires one finite control point.");
                    if (node.IncomingSegment == DocumentVectorSegmentKind.Cubic && (!Finite(node.Control1) || !Finite(node.Control2)))
                        Error(issues, "node.cubic.controls", "A cubic segment requires two finite control points.");
                }
            }
        }
        if (subpathCount > MaximumSubpaths) Error(issues, "shape.subpaths.limit", $"A vector shape may contain at most {MaximumSubpaths} subpaths.");
        if (nodeCount > MaximumNodes) Error(issues, "shape.nodes.limit", $"A vector shape may contain at most {MaximumNodes} nodes.");
        if (shape.ClippingPathId is { } clipping && !pathIds.Contains(clipping)) Error(issues, "shape.clip", "The clipping path must refer to a path in the same shape.");
        foreach (var connector in shape.ConnectorPoints ?? [])
            if (connector is null || !double.IsFinite(connector.X) || !double.IsFinite(connector.Y) || !double.IsFinite(connector.DirectionDegrees))
                Error(issues, "shape.connector", "Connector points must contain finite coordinates and directions.");
        return new DocumentVectorValidationResult(issues);
    }

    private static void ValidatePaint(DocumentVectorPath path, List<DocumentVectorValidationIssue> issues)
    {
        if (path.Fill is null) Error(issues, "path.fill", "Path fill settings are missing.");
        else if (!double.IsFinite(path.Fill.Opacity) || path.Fill.Opacity is < 0 or > 1) Error(issues, "path.fill.opacity", "Fill opacity must be between 0 and 1.");
        if (path.Stroke is null) Error(issues, "path.stroke", "Path stroke settings are missing.");
        else if (!double.IsFinite(path.Stroke.Width) || path.Stroke.Width < 0 || !double.IsFinite(path.Stroke.Opacity) || path.Stroke.Opacity is < 0 or > 1)
            Error(issues, "path.stroke.values", "Stroke width and opacity must be finite and non-negative.");
    }

    private static bool TransformIsFinite(DocumentVectorTransform value) =>
        double.IsFinite(value.TranslateX) && double.IsFinite(value.TranslateY) && double.IsFinite(value.ScaleX) && double.IsFinite(value.ScaleY)
        && Math.Abs(value.ScaleX) > 0.000001 && Math.Abs(value.ScaleY) > 0.000001 && double.IsFinite(value.RotationDegrees)
        && double.IsFinite(value.OriginX) && double.IsFinite(value.OriginY);
    private static bool FinitePositive(double value) => double.IsFinite(value) && value > 0;
    private static bool Finite(DocumentVectorPoint? point) => point is not null && double.IsFinite(point.X) && double.IsFinite(point.Y);
    private static DocumentVectorValidationResult Invalid(string code, string message) => new([new(DocumentValidationSeverity.Error, code, message)]);
    private static void Error(List<DocumentVectorValidationIssue> issues, string code, string message) => issues.Add(new(DocumentValidationSeverity.Error, code, message));
}

public static class DocumentVectorShapes
{
    private static readonly JsonSerializerOptions CloneOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public static DocumentVectorShape CreateEditableStarter(string? name = null)
    {
        var shape = new DocumentVectorShape
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Custom shape" : name.Trim(),
            AccessibilityDescription = "Editable custom vector shape",
            Paths =
            [
                new DocumentVectorPath
                {
                    Fill = new DocumentVectorFill { Kind = DocumentVectorFillKind.Solid, Color = "#FFE9EEF8" },
                    Stroke = new DocumentVectorStroke { Enabled = true, Color = "#FF384860", Width = 2 },
                    Subpaths =
                    [
                        new DocumentVectorSubpath
                        {
                            Closed = true,
                            Nodes =
                            [
                                new DocumentVectorNode { X = 8, Y = 15 },
                                new DocumentVectorNode { X = 92, Y = 15, IncomingSegment = DocumentVectorSegmentKind.Cubic, Control1 = new DocumentVectorPoint(30, 0), Control2 = new DocumentVectorPoint(70, 0) },
                                new DocumentVectorNode { X = 92, Y = 85, IncomingSegment = DocumentVectorSegmentKind.Line },
                                new DocumentVectorNode { X = 8, Y = 85, IncomingSegment = DocumentVectorSegmentKind.Cubic, Control1 = new DocumentVectorPoint(70, 100), Control2 = new DocumentVectorPoint(30, 100) }
                            ]
                        }
                    ]
                }
            ],
            ConnectorPoints =
            [
                new DocumentVectorConnectorPoint { Name = "Left", X = 0, Y = 50, DirectionDegrees = 180 },
                new DocumentVectorConnectorPoint { Name = "Right", X = 100, Y = 50, DirectionDegrees = 0 }
            ]
        };
        shape.Normalize();
        return shape;
    }

    public static DocumentVectorShape PrepareAiShape(DocumentVectorShape candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        EnforceComplexity(candidate);
        var copy = Clone(candidate);
        copy.SourceKind = DocumentShapeSourceKind.Ai;
        copy.Normalize();
        var result = DocumentVectorShapeValidator.Validate(copy);
        if (!result.IsValid)
            throw new InvalidDataException("AI-generated vector geometry was rejected: " + string.Join("; ", result.Issues.Where(issue => issue.Severity == DocumentValidationSeverity.Error).Select(issue => issue.Message)));
        return copy;
    }

    public static DocumentVectorShape CloneForInsertion(DocumentVectorShape source, Guid? gallerySourceId = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var copy = Clone(source);
        copy.Normalize();
        copy.GallerySourceId = gallerySourceId ?? source.GallerySourceId;
        RegenerateIds(copy);
        var result = DocumentVectorShapeValidator.Validate(copy);
        if (!result.IsValid) throw new InvalidDataException("The custom shape cannot be inserted because its geometry is invalid.");
        return copy;
    }

    public static DocumentVectorPoint TransformPoint(DocumentVectorShape shape, DocumentVectorPoint point)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(point);
        var transform = shape.Transform ?? new DocumentVectorTransform();
        var view = shape.ViewBox ?? new DocumentVectorViewBox();
        var originX = view.X + view.Width * transform.OriginX;
        var originY = view.Y + view.Height * transform.OriginY;
        var x = (point.X - originX) * transform.ScaleX;
        var y = (point.Y - originY) * transform.ScaleY;
        var radians = transform.RotationDegrees * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new DocumentVectorPoint(
            x * cos - y * sin + originX + transform.TranslateX,
            x * sin + y * cos + originY + transform.TranslateY);
    }

    public static DocumentVectorPoint InverseTransformPoint(DocumentVectorShape shape, DocumentVectorPoint point)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(point);
        var transform = shape.Transform ?? new DocumentVectorTransform();
        var view = shape.ViewBox ?? new DocumentVectorViewBox();
        if (!double.IsFinite(transform.ScaleX) || !double.IsFinite(transform.ScaleY)
            || Math.Abs(transform.ScaleX) < 0.000001 || Math.Abs(transform.ScaleY) < 0.000001)
            throw new InvalidDataException("The vector transform cannot be inverted because its scale is invalid.");
        var originX = view.X + view.Width * transform.OriginX;
        var originY = view.Y + view.Height * transform.OriginY;
        var x = point.X - originX - transform.TranslateX;
        var y = point.Y - originY - transform.TranslateY;
        var radians = transform.RotationDegrees * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var unrotatedX = x * cos + y * sin;
        var unrotatedY = -x * sin + y * cos;
        return new DocumentVectorPoint(
            originX + unrotatedX / transform.ScaleX,
            originY + unrotatedY / transform.ScaleY);
    }

    public static DocumentVectorShape Clone(DocumentVectorShape source)
    {
        var json = JsonSerializer.Serialize(source, CloneOptions);
        return JsonSerializer.Deserialize<DocumentVectorShape>(json, CloneOptions)
            ?? throw new InvalidDataException("The vector shape could not be cloned.");
    }

    private static void EnforceComplexity(DocumentVectorShape candidate)
    {
        if ((candidate.Paths?.Count ?? 0) > DocumentVectorShapeValidator.MaximumPaths) throw new InvalidDataException("AI vector geometry contains too many paths.");
        var subpaths = candidate.Paths?.Sum(path => path?.Subpaths?.Count ?? 0) ?? 0;
        if (subpaths > DocumentVectorShapeValidator.MaximumSubpaths) throw new InvalidDataException("AI vector geometry contains too many subpaths.");
        var nodes = candidate.Paths?.Sum(path => path?.Subpaths?.Sum(subpath => subpath?.Nodes?.Count ?? 0) ?? 0) ?? 0;
        if (nodes > DocumentVectorShapeValidator.MaximumNodes) throw new InvalidDataException("AI vector geometry contains too many nodes.");
    }

    private static void RegenerateIds(DocumentVectorShape copy)
    {
        copy.Id = Guid.NewGuid();
        var oldToNewPaths = new Dictionary<Guid, Guid>();
        foreach (var path in copy.Paths)
        {
            var old = path.Id; path.Id = Guid.NewGuid(); oldToNewPaths[old] = path.Id;
            foreach (var subpath in path.Subpaths)
            {
                subpath.Id = Guid.NewGuid();
                foreach (var node in subpath.Nodes) node.Id = Guid.NewGuid();
            }
        }
        if (copy.ClippingPathId is { } clip && oldToNewPaths.TryGetValue(clip, out var replacement)) copy.ClippingPathId = replacement;
        else if (copy.ClippingPathId is not null) copy.ClippingPathId = null;
        foreach (var point in copy.ConnectorPoints) point.Id = Guid.NewGuid();
    }
}

public enum DocumentOperationOrigin { User = 0, Ai = 1, Import = 2, System = 3 }
public sealed record DocumentOperationMetadata(Guid Id, string Name, DocumentOperationOrigin Origin, DateTimeOffset CreatedAt, string? Actor = null);

/// <summary>Shared atomic snapshot history used by document-suite native editors.</summary>
public sealed class DocumentMutationHistory<T> where T : class
{
    private readonly Func<T, T> _clone;
    private readonly int _limit;
    private readonly List<Entry> _undo = [];
    private readonly List<Entry> _redo = [];

    public DocumentMutationHistory(T state, Func<T, T> clone, int limit = 100)
    {
        Current = state ?? throw new ArgumentNullException(nameof(state));
        _clone = clone ?? throw new ArgumentNullException(nameof(clone));
        _limit = Math.Clamp(limit, 1, 500);
    }

    public T Current { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public DocumentOperationMetadata? LastOperation { get; private set; }
    public event EventHandler? Changed;

    public void Apply(string name, DocumentOperationOrigin origin, Action<T> mutation, string? actor = null)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var before = _clone(Current);
        var working = _clone(Current);
        mutation(working);
        var after = _clone(working);
        Current = working;
        var metadata = new DocumentOperationMetadata(Guid.NewGuid(), string.IsNullOrWhiteSpace(name) ? "Document edit" : name.Trim(), origin, DateTimeOffset.UtcNow, actor);
        _undo.Add(new Entry(metadata, before, after));
        if (_undo.Count > _limit) _undo.RemoveAt(0);
        _redo.Clear();
        LastOperation = metadata;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var entry = Pop(_undo);
        _redo.Add(entry);
        Current = _clone(entry.Before);
        LastOperation = new DocumentOperationMetadata(Guid.NewGuid(), "Undo " + entry.Metadata.Name, DocumentOperationOrigin.System, DateTimeOffset.UtcNow);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        var entry = Pop(_redo);
        _undo.Add(entry);
        Current = _clone(entry.After);
        LastOperation = new DocumentOperationMetadata(Guid.NewGuid(), "Redo " + entry.Metadata.Name, DocumentOperationOrigin.System, DateTimeOffset.UtcNow);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private static Entry Pop(List<Entry> values)
    {
        var index = values.Count - 1; var result = values[index]; values.RemoveAt(index); return result;
    }

    private sealed record Entry(DocumentOperationMetadata Metadata, T Before, T After);
}

public sealed class DocumentVectorShapeEditor
{
    private readonly DocumentMutationHistory<DocumentVectorShape> _history;

    public DocumentVectorShapeEditor(DocumentVectorShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        shape.Normalize();
        var validation = DocumentVectorShapeValidator.Validate(shape);
        if (!validation.IsValid) throw new InvalidDataException("The vector shape cannot be edited because its geometry is invalid.");
        _history = new DocumentMutationHistory<DocumentVectorShape>(shape, DocumentVectorShapes.Clone);
    }

    public DocumentVectorShape Shape => _history.Current;
    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
    public DocumentOperationMetadata? LastOperation => _history.LastOperation;

    public void SetName(string? name, DocumentOperationOrigin origin = DocumentOperationOrigin.User) =>
        Edit("Rename vector shape", origin, shape => shape.Name = string.IsNullOrWhiteSpace(name) ? "Custom shape" : name.Trim());

    public void SetAccessibilityDescription(string? description, DocumentOperationOrigin origin = DocumentOperationOrigin.User) =>
        Edit("Edit vector accessibility description", origin, shape => shape.AccessibilityDescription = description?.Trim() ?? string.Empty);

    public void SetPathStyle(Guid pathId, string? fillColor, string? strokeColor, double strokeWidth, DocumentOperationOrigin origin = DocumentOperationOrigin.User) =>
        Edit("Style vector path", origin, shape =>
        {
            var path = shape.Paths.FirstOrDefault(value => value.Id == pathId) ?? throw new ArgumentOutOfRangeException(nameof(pathId));
            if (path.Fill.Kind != DocumentVectorFillKind.None && !string.IsNullOrWhiteSpace(fillColor)) path.Fill.Color = fillColor.Trim();
            if (!string.IsNullOrWhiteSpace(strokeColor)) path.Stroke.Color = strokeColor.Trim();
            path.Stroke.Width = Finite(strokeWidth);
        });

    public void MoveNode(Guid nodeId, double x, double y, DocumentOperationOrigin origin = DocumentOperationOrigin.User) =>
        Edit("Move vector node", origin, shape => { var node = RequireNode(shape, nodeId); node.X = Finite(x); node.Y = Finite(y); });

    public void SetNodeSegment(Guid nodeId, DocumentVectorSegmentKind kind, DocumentVectorPoint? control1 = null, DocumentVectorPoint? control2 = null, DocumentOperationOrigin origin = DocumentOperationOrigin.User) =>
        Edit("Edit vector segment", origin, shape => { var node = RequireNode(shape, nodeId); node.IncomingSegment = kind; node.Control1 = control1; node.Control2 = control2; });

    public void MoveControlPoint(Guid nodeId, int controlIndex, double x, double y, DocumentOperationOrigin origin = DocumentOperationOrigin.User)
    {
        if (controlIndex is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(controlIndex));
        Edit("Move vector control point", origin, shape =>
        {
            var node = RequireNode(shape, nodeId);
            if (node.IncomingSegment == DocumentVectorSegmentKind.Line)
                throw new InvalidOperationException("Line segments do not have Bezier control points.");
            if (controlIndex == 2 && node.IncomingSegment != DocumentVectorSegmentKind.Cubic)
                throw new InvalidOperationException("Only cubic Bezier segments have a second control point.");
            var point = new DocumentVectorPoint(Finite(x), Finite(y));
            if (controlIndex == 1) node.Control1 = point; else node.Control2 = point;
        });
    }

    public Guid AddNode(Guid subpathId, DocumentVectorNode node, int? index = null, DocumentOperationOrigin origin = DocumentOperationOrigin.User)
    {
        ArgumentNullException.ThrowIfNull(node);
        var id = node.Id == Guid.Empty ? Guid.NewGuid() : node.Id;
        Edit("Add vector node", origin, shape =>
        {
            var subpath = RequireSubpath(shape, subpathId);
            var copy = new DocumentVectorNode { Id = id, X = Finite(node.X), Y = Finite(node.Y), Kind = node.Kind, IncomingSegment = node.IncomingSegment, Control1 = node.Control1, Control2 = node.Control2 };
            subpath.Nodes.Insert(Math.Clamp(index ?? subpath.Nodes.Count, 0, subpath.Nodes.Count), copy);
        });
        return id;
    }

    public bool DeleteNode(Guid nodeId, DocumentOperationOrigin origin = DocumentOperationOrigin.User)
    {
        var location = FindNode(Shape, nodeId);
        if (location is null || location.Value.Subpath.Nodes.Count <= 2) return false;
        Edit("Delete vector node", origin, shape => FindNode(shape, nodeId)!.Value.Subpath.Nodes.RemoveAt(FindNode(shape, nodeId)!.Value.Index));
        return true;
    }

    public void SetTransform(DocumentVectorTransform transform, DocumentOperationOrigin origin = DocumentOperationOrigin.User)
    {
        ArgumentNullException.ThrowIfNull(transform);
        Edit("Transform vector shape", origin, shape => shape.Transform = new DocumentVectorTransform
        {
            TranslateX = transform.TranslateX, TranslateY = transform.TranslateY, ScaleX = transform.ScaleX, ScaleY = transform.ScaleY,
            RotationDegrees = transform.RotationDegrees, OriginX = transform.OriginX, OriginY = transform.OriginY
        });
    }

    public bool Undo() => _history.Undo();
    public bool Redo() => _history.Redo();

    private void Edit(string name, DocumentOperationOrigin origin, Action<DocumentVectorShape> mutation)
    {
        _history.Apply(name, origin, shape => { mutation(shape); shape.Normalize(); var validation = DocumentVectorShapeValidator.Validate(shape); if (!validation.IsValid) throw new InvalidDataException("The vector edit would create invalid geometry."); });
    }

    private static DocumentVectorNode RequireNode(DocumentVectorShape shape, Guid id) => FindNode(shape, id)?.Node ?? throw new ArgumentOutOfRangeException(nameof(id));
    private static DocumentVectorSubpath RequireSubpath(DocumentVectorShape shape, Guid id) => shape.Paths.SelectMany(path => path.Subpaths).FirstOrDefault(value => value.Id == id) ?? throw new ArgumentOutOfRangeException(nameof(id));
    private static (DocumentVectorSubpath Subpath, DocumentVectorNode Node, int Index)? FindNode(DocumentVectorShape shape, Guid id)
    {
        foreach (var subpath in shape.Paths.SelectMany(path => path.Subpaths))
            for (var index = 0; index < subpath.Nodes.Count; index++) if (subpath.Nodes[index].Id == id) return (subpath, subpath.Nodes[index], index);
        return null;
    }
    private static double Finite(double value) => double.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), "Vector coordinates must be finite.");
}
