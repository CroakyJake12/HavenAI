using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public enum CanvasTool
{
    Select = 0,
    Pen = 1,
    Highlighter = 2,
    Eraser = 3,
    Pan = 4
}

public readonly record struct CanvasPointerSample(
    double ViewportX,
    double ViewportY,
    double Pressure = 0.5,
    double TiltX = 0,
    double TiltY = 0,
    long TimestampMilliseconds = 0);

public static class CanvasDocumentModel
{
    public const string ExperienceMetadataKey = "haven.experience";
    public const string ExperienceMetadataValue = "canvas";
    public const string SchemaMetadataKey = "haven.canvas.schema";
    public const string SchemaVersion = "1";

    private static readonly JsonSerializerOptions CloneJsonOptions = new(JsonSerializerDefaults.Web);

    public static bool IsCanvasDocument(NotesDocument? document) =>
        document is not null
        && document.Metadata.TryGetValue(ExperienceMetadataKey, out var experience)
        && experience.Equals(ExperienceMetadataValue, StringComparison.OrdinalIgnoreCase);

    public static NotesDocument Create(string? title = null)
    {
        var document = NotesDocument.Create(string.IsNullOrWhiteSpace(title) ? "Untitled canvas" : title.Trim());
        document.LayoutMode = NotesLayoutMode.InfiniteCanvas;
        document.Metadata[ExperienceMetadataKey] = ExperienceMetadataValue;
        document.Metadata[SchemaMetadataKey] = SchemaVersion;

        var page = document.Sections[0].Pages[0];
        page.Title = "Board";
        page.Blocks.Clear();
        page.CanvasObjects.Clear();

        var block = NotesBlock.CanvasBlock();
        block.Order = 0;
        block.Canvas!.Infinite = true;
        page.Blocks.Add(block);
        return document;
    }

    public static NotesCanvasData GetBoard(NotesDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!IsCanvasDocument(document))
            throw new InvalidOperationException("This Notes document is not a Haven Canvas board.");

        document.LayoutMode = NotesLayoutMode.InfiniteCanvas;
        document.Metadata[SchemaMetadataKey] = SchemaVersion;
        document.Sections ??= [NotesSection.CreateDefault()];
        if (document.Sections.Count == 0) document.Sections.Add(NotesSection.CreateDefault());

        var section = document.Sections[0];
        section.Pages ??= [NotesPage.CreateDefault()];
        if (section.Pages.Count == 0) section.Pages.Add(NotesPage.CreateDefault());

        var page = section.Pages[0];
        page.Blocks ??= [];
        var block = page.Blocks.FirstOrDefault(candidate => candidate.Kind == NotesBlockKind.Canvas && candidate.Canvas is not null);
        if (block is null)
        {
            block = NotesBlock.CanvasBlock();
            block.Order = page.Blocks.Count == 0 ? 0 : page.Blocks.Max(candidate => candidate.Order) + 1;
            page.Blocks.Add(block);
        }

        var board = block.Canvas ??= new NotesCanvasData();
        board.Objects ??= [];
        board.Strokes ??= [];
        board.GhostLayers ??= [];
        board.Width = ClampFinite(board.Width, 1200, 100, 1_000_000);
        board.Height = ClampFinite(board.Height, 900, 100, 1_000_000);
        board.Zoom = ClampFinite(board.Zoom, 1, 0.05, 8);
        board.OffsetX = Finite(board.OffsetX) ? board.OffsetX : 0;
        board.OffsetY = Finite(board.OffsetY) ? board.OffsetY : 0;

        // Older freeform Notes pages could keep their objects at page scope. A
        // dedicated Canvas document owns one canonical board, so migrate those
        // objects once rather than rendering or validating duplicate IDs.
        page.CanvasObjects ??= [];
        if (page.CanvasObjects.Count > 0)
        {
            var ids = board.Objects.Select(value => value.Id).ToHashSet();
            foreach (var value in page.CanvasObjects)
                if (ids.Add(value.Id)) board.Objects.Add(value);
            page.CanvasObjects.Clear();
        }

        return board;
    }

    public static NotesCanvasData CloneBoard(NotesCanvasData board)
    {
        ArgumentNullException.ThrowIfNull(board);
        var json = JsonSerializer.Serialize(board, CloneJsonOptions);
        return JsonSerializer.Deserialize<NotesCanvasData>(json, CloneJsonOptions)
               ?? throw new InvalidDataException("Canvas state could not be cloned.");
    }

    public static void ReplaceBoard(NotesDocument document, NotesCanvasData replacement)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(replacement);
        var current = GetBoard(document);
        var block = document.Sections[0].Pages[0].Blocks
            .First(candidate => candidate.Kind == NotesBlockKind.Canvas && ReferenceEquals(candidate.Canvas, current));
        block.Canvas = replacement;
    }

    public static NotesCanvasObject CreateConnector(NotesCanvasObject from, NotesCanvasObject to, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (from.Id == to.Id)
            throw new InvalidOperationException("A canvas object cannot connect to itself.");

        var startX = from.X + from.Width / 2d;
        var startY = from.Y + from.Height / 2d;
        var endX = to.X + to.Width / 2d;
        var endY = to.Y + to.Height / 2d;
        return new NotesCanvasObject
        {
            Kind = NotesCanvasObjectKind.Connector,
            FromObjectId = from.Id,
            ToObjectId = to.Id,
            Text = label ?? string.Empty,
            X = Math.Min(startX, endX),
            Y = Math.Min(startY, endY),
            Width = Math.Max(8, Math.Abs(endX - startX)),
            Height = Math.Max(8, Math.Abs(endY - startY)),
            ZIndex = Math.Max(from.ZIndex, to.ZIndex) + 1
        };
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static double ClampFinite(double value, double fallback, double minimum, double maximum) =>
        Finite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}

public sealed class CanvasHistory
{
    private const int DefaultLimit = 30;
    private readonly int _limit;
    private readonly List<NotesCanvasData> _undo = [];
    private readonly List<NotesCanvasData> _redo = [];

    public CanvasHistory(int limit = DefaultLimit) => _limit = Math.Clamp(limit, 1, 100);

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    public void Capture(NotesCanvasData board)
    {
        ArgumentNullException.ThrowIfNull(board);
        _undo.Add(CanvasDocumentModel.CloneBoard(board));
        if (_undo.Count > _limit) _undo.RemoveAt(0);
        _redo.Clear();
    }

    public NotesCanvasData? Undo(NotesCanvasData current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (_undo.Count == 0) return null;
        _redo.Add(CanvasDocumentModel.CloneBoard(current));
        var index = _undo.Count - 1;
        var restored = _undo[index];
        _undo.RemoveAt(index);
        return CanvasDocumentModel.CloneBoard(restored);
    }

    public NotesCanvasData? Redo(NotesCanvasData current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (_redo.Count == 0) return null;
        _undo.Add(CanvasDocumentModel.CloneBoard(current));
        var index = _redo.Count - 1;
        var restored = _redo[index];
        _redo.RemoveAt(index);
        return CanvasDocumentModel.CloneBoard(restored);
    }
}

public sealed class CanvasInteractionController
{
    private NotesInkStroke? _activeStroke;
    private Guid? _dragObjectId;
    private CanvasPointerSample _lastViewport;
    private double _dragOffsetX;
    private double _dragOffsetY;
    private bool _gestureCaptured;
    private long _strokeStartedAt;

    public CanvasInteractionController(NotesCanvasData board)
    {
        Board = board ?? throw new ArgumentNullException(nameof(board));
        NormalizeBoard();
    }

    public NotesCanvasData Board { get; private set; }
    public CanvasHistory History { get; } = new();
    public CanvasTool Tool { get; set; } = CanvasTool.Select;
    public Guid? SelectedObjectId { get; private set; }
    public string PenColour { get; set; } = "#FF2F80ED";
    public double PenWidth { get; set; } = 3;
    public double GridSize { get; set; }

    public NotesCanvasObject? SelectedObject =>
        SelectedObjectId is { } id ? Board.Objects.FirstOrDefault(value => value.Id == id) : null;

    public void ReplaceBoard(NotesCanvasData board, bool clearHistory = true)
    {
        Board = board ?? throw new ArgumentNullException(nameof(board));
        NormalizeBoard();
        _activeStroke = null;
        _dragObjectId = null;
        _gestureCaptured = false;
        if (SelectedObjectId is { } selected && Board.Objects.All(value => value.Id != selected))
            SelectedObjectId = null;
        if (clearHistory) History.Clear();
    }

    public void SelectObject(Guid? id) =>
        SelectedObjectId = id is { } value && Board.Objects.Any(candidate => candidate.Id == value) ? value : null;

    public bool Begin(CanvasPointerSample sample)
    {
        _lastViewport = sample;
        _gestureCaptured = false;
        var point = ToCanvas(sample);

        switch (Tool)
        {
            case CanvasTool.Select:
            {
                var hit = HitTest(point.X, point.Y);
                SelectObject(hit?.Id);
                if (hit is null || hit.Locked || hit.Kind == NotesCanvasObjectKind.Connector)
                {
                    _dragObjectId = null;
                    return false;
                }

                _dragObjectId = hit.Id;
                _dragOffsetX = point.X - hit.X;
                _dragOffsetY = point.Y - hit.Y;
                return false;
            }

            case CanvasTool.Pen:
            case CanvasTool.Highlighter:
                History.Capture(Board);
                _gestureCaptured = true;
                _strokeStartedAt = sample.TimestampMilliseconds;
                _activeStroke = new NotesInkStroke
                {
                    Tool = Tool == CanvasTool.Highlighter ? "highlighter" : "pen",
                    Colour = PenColour,
                    BaseWidth = Tool == CanvasTool.Highlighter ? Math.Max(8, PenWidth * 3) : Math.Max(0.5, PenWidth),
                    Opacity = Tool == CanvasTool.Highlighter ? 0.32 : 1,
                    Points = [ToInkPoint(sample, point)]
                };
                Board.Strokes.Add(_activeStroke);
                return true;

            case CanvasTool.Eraser:
            {
                var stroke = FindStroke(point.X, point.Y, 16 / Math.Max(Board.Zoom, 0.05));
                if (stroke is null) return false;
                History.Capture(Board);
                Board.Strokes.Remove(stroke);
                return true;
            }

            case CanvasTool.Pan:
                return false;

            default:
                return false;
        }
    }

    public bool Move(CanvasPointerSample sample)
    {
        var changed = false;
        var point = ToCanvas(sample);

        if (Tool == CanvasTool.Select && _dragObjectId is { } objectId
            && Board.Objects.FirstOrDefault(value => value.Id == objectId) is { Locked: false } value)
        {
            var x = point.X - _dragOffsetX;
            var y = point.Y - _dragOffsetY;
            if (Math.Abs(value.X - x) > 0.01 || Math.Abs(value.Y - y) > 0.01)
            {
                CaptureGestureOnce();
                NotesCanvasOperations.Move(value, x, y, GridSize);
                changed = true;
            }
        }
        else if (Tool is CanvasTool.Pen or CanvasTool.Highlighter && _activeStroke is not null)
        {
            var last = _activeStroke.Points[^1];
            var dx = last.X - point.X;
            var dy = last.Y - point.Y;
            if (dx * dx + dy * dy >= 0.35)
            {
                _activeStroke.Points.Add(ToInkPoint(sample, point));
                changed = true;
            }
        }
        else if (Tool == CanvasTool.Pan)
        {
            var dx = sample.ViewportX - _lastViewport.ViewportX;
            var dy = sample.ViewportY - _lastViewport.ViewportY;
            if (Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01)
            {
                Board.OffsetX += dx;
                Board.OffsetY += dy;
                changed = true;
            }
        }

        _lastViewport = sample;
        return changed;
    }

    public bool End(CanvasPointerSample sample)
    {
        var changed = false;
        if (_activeStroke is not null)
        {
            var point = ToCanvas(sample);
            var last = _activeStroke.Points[^1];
            var dx = last.X - point.X;
            var dy = last.Y - point.Y;
            if (dx * dx + dy * dy >= 0.35)
            {
                _activeStroke.Points.Add(ToInkPoint(sample, point));
                changed = true;
            }
        }

        _activeStroke = null;
        _dragObjectId = null;
        _gestureCaptured = false;
        return changed;
    }

    public NotesCanvasObject AddObject(NotesCanvasObjectKind kind, string? text = null)
    {
        History.Capture(Board);
        var index = Board.Objects.Count;
        var value = new NotesCanvasObject
        {
            Kind = kind,
            Text = text ?? (kind == NotesCanvasObjectKind.Text ? "Canvas note" : kind.ToString()),
            X = 80 + (index % 8) * 28,
            Y = 80 + (index % 6) * 28,
            Width = kind == NotesCanvasObjectKind.Frame ? 420 : kind == NotesCanvasObjectKind.Shape ? 180 : 220,
            Height = kind == NotesCanvasObjectKind.Frame ? 260 : kind == NotesCanvasObjectKind.Shape ? 140 : 120,
            ZIndex = Board.Objects.Count == 0 ? 0 : Board.Objects.Max(candidate => candidate.ZIndex) + 1
        };
        Board.Objects.Add(value);
        SelectedObjectId = value.Id;
        return value;
    }

    public NotesCanvasObject AddCustomShape(DocumentVectorShape shape, Guid? gallerySourceId = null)
    {
        ArgumentNullException.ThrowIfNull(shape);
        History.Capture(Board);
        var index = Board.Objects.Count;
        var inserted = DocumentVectorShapes.CloneForInsertion(shape, gallerySourceId);
        var value = new NotesCanvasObject
        {
            Kind = NotesCanvasObjectKind.Shape,
            Text = inserted.Name,
            VectorShape = inserted,
            X = 80 + (index % 8) * 28,
            Y = 80 + (index % 6) * 28,
            Width = 220,
            Height = 170,
            ZIndex = Board.Objects.Count == 0 ? 0 : Board.Objects.Max(candidate => candidate.ZIndex) + 1
        };
        Board.Objects.Add(value);
        SelectedObjectId = value.Id;
        return value;
    }

    public bool UpdateSelectedCustomShape(Action<DocumentVectorShapeEditor> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (SelectedObject is not { Locked: false, VectorShape: { } shape } value) return false;
        var vectorEditor = new DocumentVectorShapeEditor(DocumentVectorShapes.Clone(shape));
        edit(vectorEditor);
        History.Capture(Board);
        value.VectorShape = DocumentVectorShapes.Clone(vectorEditor.Shape);
        value.Text = value.VectorShape.Name;
        return true;
    }

    public bool DeleteSelected()
    {
        if (SelectedObject is not { Locked: false } selected) return false;
        History.Capture(Board);
        Board.Objects.RemoveAll(value =>
            value.Id == selected.Id
            || value.FromObjectId == selected.Id
            || value.ToObjectId == selected.Id);
        SelectedObjectId = null;
        return true;
    }

    public bool UpdateSelectedText(string? text)
    {
        if (SelectedObject is not { Locked: false } value) return false;
        var next = text ?? string.Empty;
        if (value.Text == next) return false;
        History.Capture(Board);
        value.Text = next;
        return true;
    }

    public bool MoveSelected(double x, double y)
    {
        if (SelectedObject is not { Locked: false } value) return false;
        x = Finite(x, value.X);
        y = Finite(y, value.Y);
        if (Math.Abs(value.X - x) < 0.001 && Math.Abs(value.Y - y) < 0.001) return false;
        History.Capture(Board);
        NotesCanvasOperations.Move(value, x, y, GridSize);
        return true;
    }

    public bool ResizeSelected(double width, double height)
    {
        if (SelectedObject is not { Locked: false } value) return false;
        width = Finite(width, value.Width);
        height = Finite(height, value.Height);
        if (Math.Abs(value.Width - width) < 0.001 && Math.Abs(value.Height - height) < 0.001) return false;
        History.Capture(Board);
        NotesCanvasOperations.Resize(value, width, height, GridSize);
        return true;
    }

    public bool RotateSelected(double degrees)
    {
        if (SelectedObject is not { Locked: false } value) return false;
        degrees = Finite(degrees, value.Rotation);
        var normalized = ((degrees % 360) + 360) % 360;
        if (Math.Abs(value.Rotation - normalized) < 0.001) return false;
        History.Capture(Board);
        NotesCanvasOperations.Rotate(value, degrees);
        return true;
    }

    public bool SetSelectedLocked(bool locked)
    {
        if (SelectedObject is not { } value || value.Locked == locked) return false;
        History.Capture(Board);
        value.Locked = locked;
        return true;
    }

    public bool BringSelectedToFront()
    {
        if (SelectedObject is not { Locked: false } value) return false;
        var top = Board.Objects.Count == 0 ? 0 : Board.Objects.Max(candidate => candidate.ZIndex);
        if (value.ZIndex == top) return false;
        History.Capture(Board);
        value.ZIndex = top + 1;
        return true;
    }

    public bool SendSelectedToBack()
    {
        if (SelectedObject is not { Locked: false } value) return false;
        var bottom = Board.Objects.Count == 0 ? 0 : Board.Objects.Min(candidate => candidate.ZIndex);
        if (value.ZIndex == bottom) return false;
        History.Capture(Board);
        value.ZIndex = bottom - 1;
        return true;
    }

    public NotesCanvasObject? Connect(Guid sourceId, Guid targetId, string? label = null)
    {
        var source = Board.Objects.FirstOrDefault(value => value.Id == sourceId);
        var target = Board.Objects.FirstOrDefault(value => value.Id == targetId);
        if (source is null || target is null || source.Id == target.Id) return null;
        History.Capture(Board);
        var connector = CanvasDocumentModel.CreateConnector(source, target, label);
        connector.ZIndex = Board.Objects.Count == 0 ? 0 : Board.Objects.Max(value => value.ZIndex) + 1;
        Board.Objects.Add(connector);
        SelectedObjectId = connector.Id;
        return connector;
    }

    public Guid? Group(Guid firstId, Guid secondId)
    {
        var values = Board.Objects.Where(value => value.Id == firstId || value.Id == secondId).DistinctBy(value => value.Id).ToArray();
        if (values.Length != 2 || values.Any(value => value.Locked)) return null;
        History.Capture(Board);
        var group = NotesCanvasOperations.Group(values);
        SelectedObjectId = secondId;
        return group;
    }

    public bool UngroupSelected()
    {
        if (SelectedObject?.GroupId is not { } groupId) return false;
        var values = Board.Objects.Where(value => value.GroupId == groupId).ToArray();
        if (values.Length == 0 || values.Any(value => value.Locked)) return false;
        History.Capture(Board);
        NotesCanvasOperations.Ungroup(values);
        return true;
    }

    public bool Undo()
    {
        var restored = History.Undo(Board);
        if (restored is null) return false;
        ReplaceBoard(restored, clearHistory: false);
        return true;
    }

    public bool Redo()
    {
        var restored = History.Redo(Board);
        if (restored is null) return false;
        ReplaceBoard(restored, clearHistory: false);
        return true;
    }

    public void SetZoom(double zoom) => Board.Zoom = Math.Clamp(Finite(zoom, Board.Zoom), 0.05, 8);
    public void SetInfinite(bool infinite) => Board.Infinite = infinite;
    public void ResetView() { Board.Zoom = 1; Board.OffsetX = 24; Board.OffsetY = 24; }

    private void CaptureGestureOnce()
    {
        if (_gestureCaptured) return;
        History.Capture(Board);
        _gestureCaptured = true;
    }

    private NotesCanvasObject? HitTest(double x, double y) =>
        Board.Objects
            .Where(value => value.Kind != NotesCanvasObjectKind.Connector)
            .OrderByDescending(value => value.ZIndex)
            .FirstOrDefault(value => x >= value.X && x <= value.X + value.Width && y >= value.Y && y <= value.Y + value.Height);

    private NotesInkStroke? FindStroke(double x, double y, double radius)
    {
        var radiusSquared = radius * radius;
        return Board.Strokes.LastOrDefault(stroke => stroke.Points.Any(point =>
        {
            var dx = point.X - x;
            var dy = point.Y - y;
            return dx * dx + dy * dy <= radiusSquared;
        }));
    }

    private (double X, double Y) ToCanvas(CanvasPointerSample sample)
    {
        var zoom = Math.Max(Board.Zoom, 0.05);
        return ((sample.ViewportX - Board.OffsetX) / zoom, (sample.ViewportY - Board.OffsetY) / zoom);
    }

    private NotesInkPoint ToInkPoint(CanvasPointerSample sample, (double X, double Y) point) => new()
    {
        X = point.X,
        Y = point.Y,
        Pressure = Math.Clamp(Finite(sample.Pressure, 0.5), 0, 1),
        TiltX = Math.Clamp(Finite(sample.TiltX, 0), -90, 90),
        TiltY = Math.Clamp(Finite(sample.TiltY, 0), -90, 90),
        TimestampMilliseconds = Math.Max(0, sample.TimestampMilliseconds - _strokeStartedAt)
    };

    private void NormalizeBoard()
    {
        Board.Objects ??= [];
        Board.Strokes ??= [];
        Board.GhostLayers ??= [];
        Board.Zoom = Math.Clamp(Finite(Board.Zoom, 1), 0.05, 8);
        Board.Width = Math.Clamp(Finite(Board.Width, 1200), 100, 1_000_000);
        Board.Height = Math.Clamp(Finite(Board.Height, 900), 100, 1_000_000);
        Board.OffsetX = Finite(Board.OffsetX, 0);
        Board.OffsetY = Finite(Board.OffsetY, 0);
    }

    private static double Finite(double value, double fallback) =>
        double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;
}
