namespace Haven.Application;

public readonly record struct CanvasLineLayout(
    double Left,
    double Top,
    double Width,
    double Height,
    double RotationDegrees);

/// <summary>Framework-neutral layout math for line segments rendered by Haven Canvas.</summary>
public static class CanvasLineGeometry
{
    public static CanvasLineLayout Compute(double x1, double y1, double x2, double y2, double thickness)
    {
        var safeThickness = double.IsFinite(thickness) ? Math.Max(0.1, thickness) : 1;
        if (!double.IsFinite(x1) || !double.IsFinite(y1) || !double.IsFinite(x2) || !double.IsFinite(y2))
            return new CanvasLineLayout(0, 0, 0, safeThickness, 0);

        var dx = x2 - x1;
        var dy = y2 - y1;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (!double.IsFinite(length) || length < 0.1)
            return new CanvasLineLayout(x1, y1 - safeThickness / 2, 0, safeThickness, 0);

        var midpointX = (x1 + x2) / 2;
        var midpointY = (y1 + y2) / 2;
        return new CanvasLineLayout(
            midpointX - length / 2,
            midpointY - safeThickness / 2,
            length,
            safeThickness,
            Math.Atan2(dy, dx) * 180 / Math.PI);
    }
}
