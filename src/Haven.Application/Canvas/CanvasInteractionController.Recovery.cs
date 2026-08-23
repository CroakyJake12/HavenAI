using Haven.Core;

namespace Haven.Application;

public enum CanvasEraserMode { Snap, Chunk }

public sealed partial class CanvasInteractionController
{
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
            .Select(value => value.Id).ToArray();
        if (additive) SetSelection(SelectionIds().Concat(hits)); else SetSelection(hits);
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

    private static bool PointInPolygon((double X, double Y) point, IReadOnlyList<(double X, double Y)> polygon)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var j = i == 0 ? polygon.Count - 1 : i - 1;
            var a = polygon[i]; var b = polygon[j];
            if ((a.Y > point.Y) == (b.Y > point.Y)) continue;
            var denominator = b.Y - a.Y;
            if (Math.Abs(denominator) < 1e-12) continue;
            if (point.X < (b.X - a.X) * (point.Y - a.Y) / denominator + a.X) inside = !inside;
        }
        return inside;
    }

    private static IReadOnlyList<NotesInkStroke> SplitStrokeOutsideRadius(NotesInkStroke stroke, double x, double y, double radius)
    {
        var radiusSquared = radius * radius;
        var result = new List<NotesInkStroke>();
        var current = new List<NotesInkPoint>();
        void Flush()
        {
            if (current.Count >= 2) result.Add(new NotesInkStroke
            {
                Tool = stroke.Tool, Colour = stroke.Colour, BaseWidth = stroke.BaseWidth, Opacity = stroke.Opacity,
                IsGhost = stroke.IsGhost, GhostLayerId = stroke.GhostLayerId, RecognitionText = stroke.RecognitionText,
                RecognitionConfidence = stroke.RecognitionConfidence, Points = current.Select(ClonePoint).ToList()
            });
            current = [];
        }
        foreach (var point in stroke.Points)
        {
            var dx = point.X - x; var dy = point.Y - y;
            if (dx * dx + dy * dy <= radiusSquared) Flush(); else current.Add(point);
        }
        Flush();
        return result;
    }

    private static NotesInkPoint ClonePoint(NotesInkPoint point) => new()
    {
        X = point.X, Y = point.Y, Pressure = point.Pressure, TiltX = point.TiltX, TiltY = point.TiltY, TimestampMilliseconds = point.TimestampMilliseconds
    };
}
