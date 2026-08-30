using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public enum CanvasEraserMode
{
    Snap = 0,
    Chunk = 1
}

public sealed partial class CanvasInteractionController
{
    private static readonly JsonSerializerOptions CanvasStyleJson = new(JsonSerializerDefaults.Web);
    private bool _eraseGestureCaptured;

    public CanvasEraserMode EraserMode { get; set; } = CanvasEraserMode.Snap;
    public double PenOpacity { get; set; } = 1;
    public string PenEffect { get; set; } = "Pressure";

    public void BeginEraseGesture() => _eraseGestureCaptured = false;
    public void EndEraseGesture() => _eraseGestureCaptured = false;

    public bool ReleaseInteraction()
    {
        var changed = _activeStroke is not null || _eraseGestureCaptured;
        _activeStroke = null;
        _dragObjectId = null;
        _gestureCaptured = false;
        _eraseGestureCaptured = false;
        return changed;
    }

    public IReadOnlyCollection<Guid> SelectViewportPolygon(IReadOnlyList<CanvasPointerSample> samples, bool additive = false)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count < 3)
        {
            if (!additive) ClearSelection();
            return SelectedObjectIds;
        }

        var polygon = samples.Select(ToCanvas).ToArray();
        var hits = Board.Objects
            .Where(value => value.Kind != NotesCanvasObjectKind.Connector)
            .Where(value =>
            {
                var bounds = RotatedBounds(value);
                return PointInPolygon((bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2), polygon)
                    || PointInPolygon((bounds.X, bounds.Y), polygon)
                    || PointInPolygon((bounds.Right, bounds.Y), polygon)
                    || PointInPolygon((bounds.Right, bounds.Bottom), polygon)
                    || PointInPolygon((bounds.X, bounds.Bottom), polygon);
            })
            .Select(value => value.Id)
            .ToArray();

        if (additive) SetSelection(SelectionIds().Concat(hits));
        else SetSelection(hits);
        return SelectedObjectIds;
    }

    public bool EraseAtViewport(double viewportX, double viewportY)
    {
        var point = ToCanvas(new CanvasPointerSample(viewportX, viewportY));
        var radius = 16 / Math.Max(Board.Zoom, 0.05);
        var stroke = FindStroke(point.X, point.Y, radius);
        if (stroke is null) return false;

        if (!_eraseGestureCaptured)
        {
            History.Capture(Board);
            _eraseGestureCaptured = true;
        }

        if (EraserMode == CanvasEraserMode.Snap)
        {
            Board.Strokes.Remove(stroke);
            return true;
        }

        var segments = SplitStrokeOutsideRadius(stroke, point.X, point.Y, radius);
        var index = Board.Strokes.IndexOf(stroke);
        Board.Strokes.RemoveAt(index);
        foreach (var segment in segments) Board.Strokes.Insert(index++, segment);
        return true;
    }

    public bool SetSelectionLocked(bool locked)
    {
        var selected = SelectedObjects.Where(value => value.Locked != locked).ToArray();
        if (selected.Length == 0) return false;
        History.Capture(Board);
        foreach (var value in selected) value.Locked = locked;
        return true;
    }

    public bool SetSelectedStyleString(string key, string value) =>
        SetSelectedStyleValue(key, JsonSerializer.SerializeToElement(value ?? string.Empty, CanvasStyleJson));

    public bool SetSelectedStyleNumber(string key, double value) =>
        double.IsFinite(value) && SetSelectedStyleValue(key, JsonSerializer.SerializeToElement(value, CanvasStyleJson));

    public bool SetSelectedStyleBoolean(string key, bool value) =>
        SetSelectedStyleValue(key, JsonSerializer.SerializeToElement(value, CanvasStyleJson));

    public string? ReadSelectedStyleString(string key)
    {
        if (SelectedObject is not { } value || string.IsNullOrWhiteSpace(key)) return null;
        var style = ReadStyle(value.StyleJson);
        if (!style.TryGetValue(key, out var element)) return null;
        return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
    }

    public int ReadSelectedStyleInt(string key, int fallback)
    {
        if (SelectedObject is not { } value || string.IsNullOrWhiteSpace(key)) return fallback;
        var style = ReadStyle(value.StyleJson);
        return style.TryGetValue(key, out var element) && element.TryGetInt32(out var number) ? number : fallback;
    }

    public bool AdjustSelectedTable(int rowDelta, int columnDelta)
    {
        if (SelectedObject is not { Locked: false } value) return false;
        var style = ReadStyle(value.StyleJson);
        if (!StyleString(style, "kind").Equals("table", StringComparison.OrdinalIgnoreCase)) return false;
        var rows = Math.Clamp(StyleInt(style, "rows", 3) + rowDelta, 1, 100);
        var columns = Math.Clamp(StyleInt(style, "columns", 3) + columnDelta, 1, 100);
        History.Capture(Board);
        style["rows"] = JsonSerializer.SerializeToElement(rows, CanvasStyleJson);
        style["columns"] = JsonSerializer.SerializeToElement(columns, CanvasStyleJson);
        value.StyleJson = JsonSerializer.Serialize(style, CanvasStyleJson);
        return true;
    }

    public NotesCanvasObject AddRichObject(
        string kind,
        double x,
        double y,
        double width,
        double height,
        string? text = null,
        IReadOnlyDictionary<string, object?>? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        var style = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["kind"] = kind };
        if (options is not null)
            foreach (var pair in options)
                style[pair.Key] = pair.Value;

        var objectKind = kind.Equals("image", StringComparison.OrdinalIgnoreCase) ? NotesCanvasObjectKind.Image
            : kind.Equals("frame", StringComparison.OrdinalIgnoreCase) ? NotesCanvasObjectKind.Frame
            : kind.Equals("text", StringComparison.OrdinalIgnoreCase) || kind.Equals("sticky", StringComparison.OrdinalIgnoreCase)
                ? NotesCanvasObjectKind.Text
                : NotesCanvasObjectKind.Shape;

        return AddObjectAt(objectKind, x, y, width, height, text, JsonSerializer.Serialize(style, CanvasStyleJson));
    }

    public bool FitSelectionOrBoard(double viewportWidth, double viewportHeight, double padding = 64)
    {
        if (viewportWidth <= 1 || viewportHeight <= 1) return false;
        var bounds = SelectionBounds() ?? ContentBounds();
        if (bounds is null)
        {
            ResetView();
            return true;
        }

        var usableWidth = Math.Max(1, viewportWidth - padding * 2);
        var usableHeight = Math.Max(1, viewportHeight - padding * 2);
        var zoom = Math.Clamp(
            Math.Min(
                usableWidth / Math.Max(1, bounds.Value.Width),
                usableHeight / Math.Max(1, bounds.Value.Height)),
            0.05,
            8);
        Board.Zoom = zoom;
        Board.OffsetX = (viewportWidth - bounds.Value.Width * zoom) / 2 - bounds.Value.X * zoom;
        Board.OffsetY = (viewportHeight - bounds.Value.Height * zoom) / 2 - bounds.Value.Y * zoom;
        return true;
    }

    public bool ZoomBy(double factor, double viewportWidth, double viewportHeight)
    {
        if (!double.IsFinite(factor) || factor <= 0) return false;
        var center = ViewportToCanvas(viewportWidth / 2, viewportHeight / 2);
        var next = Math.Clamp(Board.Zoom * factor, 0.05, 8);
        if (Math.Abs(next - Board.Zoom) < .0001) return false;
        Board.Zoom = next;
        Board.OffsetX = viewportWidth / 2 - center.X * next;
        Board.OffsetY = viewportHeight / 2 - center.Y * next;
        return true;
    }

    private bool SetSelectedStyleValue(string key, JsonElement value)
    {
        if (SelectedObject is not { Locked: false } selected || string.IsNullOrWhiteSpace(key)) return false;
        var style = ReadStyle(selected.StyleJson);
        if (style.TryGetValue(key, out var current) && current.ToString() == value.ToString()) return false;
        History.Capture(Board);
        style[key] = value;
        selected.StyleJson = JsonSerializer.Serialize(style, CanvasStyleJson);
        return true;
    }

    private CanvasBounds? ContentBounds()
    {
        var values = Board.Objects.Where(value => value.Kind != NotesCanvasObjectKind.Connector).Select(RotatedBounds).ToArray();
        if (values.Length == 0) return null;
        var left = values.Min(value => value.X);
        var top = values.Min(value => value.Y);
        var right = values.Max(value => value.Right);
        var bottom = values.Max(value => value.Bottom);
        return new CanvasBounds(left, top, right - left, bottom - top);
    }

    private static Dictionary<string, JsonElement> ReadStyle(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, CanvasStyleJson)
                ?? new Dictionary<string, JsonElement>();
            return new Dictionary<string, JsonElement>(values, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string StyleString(IReadOnlyDictionary<string, JsonElement> style, string key) =>
        style.TryGetValue(key, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString()
            : string.Empty;

    private static int StyleInt(IReadOnlyDictionary<string, JsonElement> style, string key, int fallback) =>
        style.TryGetValue(key, out var value) && value.TryGetInt32(out var number) ? number : fallback;

    private static bool PointInPolygon((double X, double Y) point, IReadOnlyList<(double X, double Y)> polygon)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var j = i == 0 ? polygon.Count - 1 : i - 1;
            var a = polygon[i];
            var b = polygon[j];
            if ((a.Y > point.Y) == (b.Y > point.Y)) continue;
            var denominator = b.Y - a.Y;
            if (Math.Abs(denominator) < 1e-12) continue;
            var intersection = (b.X - a.X) * (point.Y - a.Y) / denominator + a.X;
            if (point.X < intersection) inside = !inside;
        }
        return inside;
    }

    private static IReadOnlyList<NotesInkStroke> SplitStrokeOutsideRadius(
        NotesInkStroke stroke,
        double x,
        double y,
        double radius)
    {
        var radiusSquared = radius * radius;
        var result = new List<NotesInkStroke>();
        var current = new List<NotesInkPoint>();

        void Flush()
        {
            if (current.Count >= 2)
            {
                result.Add(new NotesInkStroke
                {
                    Tool = stroke.Tool,
                    Colour = stroke.Colour,
                    BaseWidth = stroke.BaseWidth,
                    Opacity = stroke.Opacity,
                    IsGhost = stroke.IsGhost,
                    GhostLayerId = stroke.GhostLayerId,
                    RecognitionText = stroke.RecognitionText,
                    RecognitionConfidence = stroke.RecognitionConfidence,
                    Points = current.Select(ClonePoint).ToList()
                });
            }
            current = [];
        }

        foreach (var point in stroke.Points)
        {
            var dx = point.X - x;
            var dy = point.Y - y;
            if (dx * dx + dy * dy <= radiusSquared) Flush();
            else current.Add(point);
        }

        Flush();
        return result;
    }

    private static NotesInkPoint ClonePoint(NotesInkPoint point) => new()
    {
        X = point.X,
        Y = point.Y,
        Pressure = point.Pressure,
        TiltX = point.TiltX,
        TiltY = point.TiltY,
        TimestampMilliseconds = point.TimestampMilliseconds
    };
}
