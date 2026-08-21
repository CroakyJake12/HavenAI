using Haven.Core;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Imagine;

internal enum ImagineResizeHandle
{
    None = 0,
    NorthWest = 1,
    NorthEast = 2,
    SouthEast = 3,
    SouthWest = 4
}

internal readonly record struct ImagineSnapResult(ImagineTransform Transform, double? GuideX, double? GuideY);

internal static class ImagineCanvasGeometry
{
    private const double MinimumSize = 12d;

    public static bool Contains(ImagineTransform value, HavenPoint boardPoint)
    {
        var center = Center(value);
        var local = RotatePoint(boardPoint, center, -value.RotationDegrees);
        return local.X >= value.X && local.X <= value.X + value.Width &&
               local.Y >= value.Y && local.Y <= value.Y + value.Height;
    }

    public static bool HitRotateHandle(HavenRect screenRect, double rotationDegrees, HavenPoint absolutePoint)
    {
        var local = RotatePoint(absolutePoint, RectCenter(screenRect), -rotationDegrees);
        return Contains(Inflate(RotateHandle(screenRect), 5), local);
    }

    public static ImagineResizeHandle HitResizeHandle(HavenRect screenRect, double rotationDegrees, HavenPoint absolutePoint)
    {
        var local = RotatePoint(absolutePoint, RectCenter(screenRect), -rotationDegrees);
        var handles = CornerHandles(screenRect);
        for (var index = 0; index < handles.Length; index++)
            if (Contains(Inflate(handles[index], 5), local))
                return (ImagineResizeHandle)(index + 1);
        return ImagineResizeHandle.None;
    }

    public static HavenRect[] CornerHandles(HavenRect rect) =>
    [
        new HavenRect(rect.X - 6, rect.Y - 6, 12, 12),
        new HavenRect(rect.Right - 6, rect.Y - 6, 12, 12),
        new HavenRect(rect.Right - 6, rect.Bottom - 6, 12, 12),
        new HavenRect(rect.X - 6, rect.Bottom - 6, 12, 12)
    ];

    public static HavenRect RotateHandle(HavenRect rect) => new(rect.X + rect.Width / 2 - 7, rect.Y - 32, 14, 14);

    public static ImagineTransform ResizeFromCorner(ImagineTransform original, ImagineResizeHandle handle, HavenPoint boardPoint)
    {
        if (handle == ImagineResizeHandle.None) return original;
        var (dragX, dragY) = HandleSigns(handle);
        var center = Center(original);
        var oppositeOffset = RotateVector(new HavenPoint(-dragX * original.Width / 2, -dragY * original.Height / 2), original.RotationDegrees);
        var fixedCorner = new HavenPoint(center.X + oppositeOffset.X, center.Y + oppositeOffset.Y);
        var pointerVector = new HavenPoint(boardPoint.X - fixedCorner.X, boardPoint.Y - fixedCorner.Y);
        var localVector = RotateVector(pointerVector, -original.RotationDegrees);
        var width = Math.Max(MinimumSize, dragX * localVector.X);
        var height = Math.Max(MinimumSize, dragY * localVector.Y);
        var centerOffset = RotateVector(new HavenPoint(dragX * width / 2, dragY * height / 2), original.RotationDegrees);
        var nextCenter = new HavenPoint(fixedCorner.X + centerOffset.X, fixedCorner.Y + centerOffset.Y);
        return original with { X = nextCenter.X - width / 2, Y = nextCenter.Y - height / 2, Width = width, Height = height };
    }

    public static HavenPoint CornerPoint(ImagineTransform value, ImagineResizeHandle handle)
    {
        var (x, y) = HandleSigns(handle);
        var center = Center(value);
        var offset = RotateVector(new HavenPoint(x * value.Width / 2, y * value.Height / 2), value.RotationDegrees);
        return new HavenPoint(center.X + offset.X, center.Y + offset.Y);
    }

    public static ImagineSnapResult SnapMove(ImagineProject project, Guid objectId, ImagineTransform value, double zoom)
    {
        var tolerance = 8d / Math.Max(.05d, zoom);
        var x = FindSnap(value.X, value.X + value.Width / 2, value.X + value.Width, 0, project.CanvasWidth / 2, project.CanvasWidth, tolerance);
        var y = FindSnap(value.Y, value.Y + value.Height / 2, value.Y + value.Height, 0, project.CanvasHeight / 2, project.CanvasHeight, tolerance);

        foreach (var item in project.Objects)
        {
            if (item.Id == objectId || !item.IsVisible) continue;
            x = BetterSnap(x, value.X, value.X + value.Width / 2, value.X + value.Width, item.Transform.X, item.Transform.X + item.Transform.Width / 2, item.Transform.X + item.Transform.Width, tolerance);
            y = BetterSnap(y, value.Y, value.Y + value.Height / 2, value.Y + value.Height, item.Transform.Y, item.Transform.Y + item.Transform.Height / 2, item.Transform.Y + item.Transform.Height, tolerance);
        }

        return new ImagineSnapResult(value with { X = value.X + x.Offset, Y = value.Y + y.Offset }, x.Guide, y.Guide);
    }

    private static SnapCandidate FindSnap(double a, double b, double c, double t1, double t2, double t3, double tolerance)
    {
        var best = SnapCandidate.None;
        best = Consider(best, a, t1, tolerance); best = Consider(best, a, t2, tolerance); best = Consider(best, a, t3, tolerance);
        best = Consider(best, b, t1, tolerance); best = Consider(best, b, t2, tolerance); best = Consider(best, b, t3, tolerance);
        best = Consider(best, c, t1, tolerance); best = Consider(best, c, t2, tolerance); best = Consider(best, c, t3, tolerance);
        return best;
    }

    private static SnapCandidate BetterSnap(SnapCandidate best, double a, double b, double c, double t1, double t2, double t3, double tolerance)
    {
        best = Consider(best, a, t1, tolerance); best = Consider(best, a, t2, tolerance); best = Consider(best, a, t3, tolerance);
        best = Consider(best, b, t1, tolerance); best = Consider(best, b, t2, tolerance); best = Consider(best, b, t3, tolerance);
        best = Consider(best, c, t1, tolerance); best = Consider(best, c, t2, tolerance); best = Consider(best, c, t3, tolerance);
        return best;
    }

    private static SnapCandidate Consider(SnapCandidate current, double candidate, double target, double tolerance)
    {
        var offset = target - candidate;
        var distance = Math.Abs(offset);
        return distance <= tolerance && distance < current.Distance ? new SnapCandidate(offset, target, distance) : current;
    }

    private static (double X, double Y) HandleSigns(ImagineResizeHandle handle) => handle switch
    {
        ImagineResizeHandle.NorthWest => (-1, -1),
        ImagineResizeHandle.NorthEast => (1, -1),
        ImagineResizeHandle.SouthEast => (1, 1),
        ImagineResizeHandle.SouthWest => (-1, 1),
        _ => (1, 1)
    };

    private static HavenPoint Center(ImagineTransform value) => new(value.X + value.Width / 2, value.Y + value.Height / 2);
    private static HavenPoint RectCenter(HavenRect value) => new(value.X + value.Width / 2, value.Y + value.Height / 2);

    private static HavenPoint RotatePoint(HavenPoint point, HavenPoint center, double degrees)
    {
        var vector = RotateVector(new HavenPoint(point.X - center.X, point.Y - center.Y), degrees);
        return new HavenPoint(center.X + vector.X, center.Y + vector.Y);
    }

    private static HavenPoint RotateVector(HavenPoint value, double degrees)
    {
        var radians = degrees * Math.PI / 180d;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        return new HavenPoint(value.X * cosine - value.Y * sine, value.X * sine + value.Y * cosine);
    }

    private static HavenRect Inflate(HavenRect value, double amount) => new(value.X - amount, value.Y - amount, value.Width + amount * 2, value.Height + amount * 2);
    private static bool Contains(HavenRect value, HavenPoint point) => point.X >= value.X && point.X <= value.Right && point.Y >= value.Y && point.Y <= value.Bottom;

    private readonly record struct SnapCandidate(double Offset, double? Guide, double Distance)
    {
        public static SnapCandidate None => new(0, null, double.PositiveInfinity);
    }
}
