using Haven.Core;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Present;

internal enum PresentTransformHandle
{
    None = 0,
    NorthWest,
    NorthEast,
    SouthWest,
    SouthEast,
    Rotate
}

internal sealed partial class PresentSlideCanvas
{
    private const double TransformHandleRadius = 7;
    private PresentTransformHandle _activeTransformHandle;
    private bool _marqueeSelecting;

    public event Action<IReadOnlyCollection<Guid>>? SelectionSetRequested;
    public event Action<double, double, double, double, double>? TransformSelectionRequested;

    private void ResetDirectGesture()
    {
        _dragElementId = null;
        _dragVectorHandle = null;
        _activeTransformHandle = PresentTransformHandle.None;
        _marqueeSelecting = false;
    }

    private PresentTransformHandle? HitTransformHandle(HavenPoint local)
    {
        if (_slide is null || _selectedIds.Count != 1) return null;
        var element = _slide.Elements.FirstOrDefault(value => _selectedIds.Contains(value.Id) && value.Visible && !value.Locked && value.Kind != PresentElementKind.Group);
        if (element is null) return null;
        var rect = ElementRect(element, SlideRectLocal(), false);
        var center = Center(rect);
        var handles = HandlePoints(rect, element.RotationDegrees);
        foreach (var pair in handles)
            if (DistanceSquared(local, pair.Point) <= TransformHandleRadius * TransformHandleRadius * 2.25) return pair.Handle;
        return null;
    }

    private DirectTransform BuildDirectTransform(PresentTransformHandle handle, double dx, double dy)
    {
        if (handle == PresentTransformHandle.Rotate && _slide is not null && _selectedIds.Count == 1)
        {
            var element = _slide.Elements.First(value => _selectedIds.Contains(value.Id));
            var center = Center(ElementRect(element, SlideRectLocal(), false));
            var start = Math.Atan2(_pointerStart.Y - center.Y, _pointerStart.X - center.X);
            var current = Math.Atan2(_pointerCurrent.Y - center.Y, _pointerCurrent.X - center.X);
            var degrees = (current - start) * 180d / Math.PI;
            while (degrees > 180) degrees -= 360;
            while (degrees < -180) degrees += 360;
            return new DirectTransform(0, 0, 0, 0, degrees);
        }

        return handle switch
        {
            PresentTransformHandle.NorthWest => new DirectTransform(dx, dy, -dx, -dy, 0),
            PresentTransformHandle.NorthEast => new DirectTransform(0, dy, dx, -dy, 0),
            PresentTransformHandle.SouthWest => new DirectTransform(dx, 0, -dx, dy, 0),
            PresentTransformHandle.SouthEast => new DirectTransform(0, 0, dx, dy, 0),
            _ => default
        };
    }

    private IReadOnlyCollection<Guid> HitElementsInMarquee(HavenPoint start, HavenPoint end)
    {
        if (_slide is null) return Array.Empty<Guid>();
        var marquee = NormalizeRect(start, end);
        var slide = SlideRectLocal();
        return _slide.Elements
            .Where(element => element.Visible && element.Kind != PresentElementKind.Group && Intersects(marquee, RotatedBounds(ElementRect(element, slide, false), element.RotationDegrees)))
            .Select(element => element.Id)
            .ToArray();
    }

    private void DrawDirectSelection(HavenDrawingContext context, PresentElement element, HavenRect rect, double opacity, bool showHandles)
    {
        var center = Center(rect);
        if (Math.Abs(element.RotationDegrees) > .001)
            context.Add(new HavenPushTransformCommand(rect, new HavenTransform(RotationDegrees: element.RotationDegrees), center));
        context.Add(new HavenStrokeRoundedRectCommand(rect, new HavenPen(new HavenTokenBrush("Accent"), 2), 3, opacity));
        if (showHandles)
        {
            var size = TransformHandleRadius * 2;
            foreach (var point in new[]
            {
                new HavenPoint(rect.X, rect.Y), new HavenPoint(rect.Right, rect.Y),
                new HavenPoint(rect.X, rect.Bottom), new HavenPoint(rect.Right, rect.Bottom)
            })
                context.Add(new HavenEllipseCommand(new HavenRect(point.X - TransformHandleRadius, point.Y - TransformHandleRadius, size, size), new HavenTokenBrush("Surface"), new HavenPen(new HavenTokenBrush("Accent"), 2), opacity));
            var rotate = new HavenPoint(rect.X + rect.Width / 2, rect.Y - 28);
            context.Add(new HavenLineCommand(new HavenPoint(rect.X + rect.Width / 2, rect.Y), rotate, new HavenPen(new HavenTokenBrush("Accent"), 1.5), opacity));
            context.Add(new HavenEllipseCommand(new HavenRect(rotate.X - TransformHandleRadius, rotate.Y - TransformHandleRadius, size, size), new HavenTokenBrush("Surface"), new HavenPen(new HavenTokenBrush("Accent"), 2), opacity));
        }
        if (Math.Abs(element.RotationDegrees) > .001) context.Add(new HavenPopTransformCommand(rect));
    }

    private void DrawMarquee(HavenDrawingContext context, double opacity)
    {
        if (!_marqueeSelecting || DistanceSquared(_pointerStart, _pointerCurrent) < 4) return;
        var rect = NormalizeRect(_pointerStart, _pointerCurrent);
        var absolute = new HavenRect(Bounds.X + rect.X, Bounds.Y + rect.Y, rect.Width, rect.Height);
        context.Add(new HavenFillRoundedRectCommand(absolute, new HavenTokenBrush("AccentSubtle"), 2, opacity * .35));
        context.Add(new HavenStrokeRoundedRectCommand(absolute, new HavenPen(new HavenTokenBrush("Accent"), 1.5), 2, opacity));
    }

    private IEnumerable<(PresentTransformHandle Handle, HavenPoint Point)> HandlePoints(HavenRect rect, double rotation)
    {
        var center = Center(rect);
        yield return (PresentTransformHandle.NorthWest, RotatePoint(new HavenPoint(rect.X, rect.Y), center, rotation));
        yield return (PresentTransformHandle.NorthEast, RotatePoint(new HavenPoint(rect.Right, rect.Y), center, rotation));
        yield return (PresentTransformHandle.SouthWest, RotatePoint(new HavenPoint(rect.X, rect.Bottom), center, rotation));
        yield return (PresentTransformHandle.SouthEast, RotatePoint(new HavenPoint(rect.Right, rect.Bottom), center, rotation));
        yield return (PresentTransformHandle.Rotate, RotatePoint(new HavenPoint(rect.X + rect.Width / 2, rect.Y - 28), center, rotation));
    }

    private static HavenPoint Center(HavenRect rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    private static HavenPoint RotatePoint(HavenPoint point, HavenPoint center, double degrees)
    {
        if (Math.Abs(degrees) < .001) return point;
        var radians = degrees * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var x = point.X - center.X;
        var y = point.Y - center.Y;
        return new HavenPoint(x * cos - y * sin + center.X, x * sin + y * cos + center.Y);
    }

    private static HavenRect RotatedBounds(HavenRect rect, double rotation)
    {
        var center = Center(rect);
        var points = new[]
        {
            RotatePoint(new HavenPoint(rect.X, rect.Y), center, rotation),
            RotatePoint(new HavenPoint(rect.Right, rect.Y), center, rotation),
            RotatePoint(new HavenPoint(rect.X, rect.Bottom), center, rotation),
            RotatePoint(new HavenPoint(rect.Right, rect.Bottom), center, rotation)
        };
        var minX = points.Min(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxX = points.Max(point => point.X);
        var maxY = points.Max(point => point.Y);
        return new HavenRect(minX, minY, maxX - minX, maxY - minY);
    }

    private static HavenRect NormalizeRect(HavenPoint start, HavenPoint end) => new(
        Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));

    private static bool Intersects(HavenRect left, HavenRect right) =>
        left.Right >= right.X && right.Right >= left.X && left.Bottom >= right.Y && right.Bottom >= left.Y;

    private readonly record struct DirectTransform(double DeltaX, double DeltaY, double DeltaWidth, double DeltaHeight, double DeltaRotation);
}
