namespace Haven.UI;

[Flags]
public enum HavenResizeEdges
{
    None = 0,
    Left = 1 << 0,
    Top = 1 << 1,
    Right = 1 << 2,
    Bottom = 1 << 3
}

public enum HavenManipulationKind
{
    Move,
    Resize,
    Rotate
}

public readonly record struct HavenManipulationConstraints(
    double MinWidth = 1d,
    double MinHeight = 1d,
    HavenRect? Bounds = null);

/// <summary>
/// Platform-neutral geometry for direct-manipulation previews. A feature keeps one
/// session for the active pointer gesture, renders BoundsAt/RotationAt while moving,
/// and commits only on release. Cancelling the pointer therefore requires no rollback.
/// </summary>
public sealed class HavenDirectManipulationSession
{
    private readonly double _startAngleDegrees;

    private HavenDirectManipulationSession(
        HavenManipulationKind kind,
        HavenRect startBounds,
        HavenPoint startPointer,
        HavenResizeEdges resizeEdges,
        double startRotationDegrees)
    {
        Kind = kind;
        StartBounds = startBounds;
        StartPointer = startPointer;
        ResizeEdges = resizeEdges;
        StartRotationDegrees = startRotationDegrees;
        _startAngleDegrees = AngleDegrees(Center(startBounds), startPointer);
    }

    public HavenManipulationKind Kind { get; }
    public HavenRect StartBounds { get; }
    public HavenPoint StartPointer { get; }
    public HavenResizeEdges ResizeEdges { get; }
    public double StartRotationDegrees { get; }

    public static HavenDirectManipulationSession Move(HavenRect bounds, HavenPoint pointer) =>
        new(HavenManipulationKind.Move, bounds, pointer, HavenResizeEdges.None, 0d);

    public static HavenDirectManipulationSession Resize(HavenRect bounds, HavenPoint pointer, HavenResizeEdges edges)
    {
        if (edges == HavenResizeEdges.None)
            throw new ArgumentOutOfRangeException(nameof(edges), "A resize session needs at least one resize edge.");
        return new HavenDirectManipulationSession(HavenManipulationKind.Resize, bounds, pointer, edges, 0d);
    }

    public static HavenDirectManipulationSession Rotate(HavenRect bounds, HavenPoint pointer, double startRotationDegrees = 0d) =>
        new(HavenManipulationKind.Rotate, bounds, pointer, HavenResizeEdges.None, startRotationDegrees);

    public HavenRect BoundsAt(HavenPoint pointer, HavenManipulationConstraints constraints = default)
    {
        var minWidth = Math.Max(0d, constraints.MinWidth);
        var minHeight = Math.Max(0d, constraints.MinHeight);
        var dx = pointer.X - StartPointer.X;
        var dy = pointer.Y - StartPointer.Y;

        if (Kind == HavenManipulationKind.Move)
            return ClampMove(new HavenRect(StartBounds.X + dx, StartBounds.Y + dy, StartBounds.Width, StartBounds.Height), constraints.Bounds);
        if (Kind != HavenManipulationKind.Resize) return StartBounds;

        var left = StartBounds.X;
        var top = StartBounds.Y;
        var right = StartBounds.Right;
        var bottom = StartBounds.Bottom;

        if (ResizeEdges.HasFlag(HavenResizeEdges.Left)) left += dx;
        if (ResizeEdges.HasFlag(HavenResizeEdges.Right)) right += dx;
        if (ResizeEdges.HasFlag(HavenResizeEdges.Top)) top += dy;
        if (ResizeEdges.HasFlag(HavenResizeEdges.Bottom)) bottom += dy;

        if (ResizeEdges.HasFlag(HavenResizeEdges.Left)) left = Math.Min(left, right - minWidth);
        else right = Math.Max(right, left + minWidth);
        if (ResizeEdges.HasFlag(HavenResizeEdges.Top)) top = Math.Min(top, bottom - minHeight);
        else bottom = Math.Max(bottom, top + minHeight);

        if (constraints.Bounds is { } area)
        {
            if (ResizeEdges.HasFlag(HavenResizeEdges.Left)) left = Math.Max(area.X, left);
            if (ResizeEdges.HasFlag(HavenResizeEdges.Right)) right = Math.Min(area.Right, right);
            if (ResizeEdges.HasFlag(HavenResizeEdges.Top)) top = Math.Max(area.Y, top);
            if (ResizeEdges.HasFlag(HavenResizeEdges.Bottom)) bottom = Math.Min(area.Bottom, bottom);

            if (right - left < minWidth)
            {
                if (ResizeEdges.HasFlag(HavenResizeEdges.Left)) left = right - minWidth;
                else right = left + minWidth;
            }
            if (bottom - top < minHeight)
            {
                if (ResizeEdges.HasFlag(HavenResizeEdges.Top)) top = bottom - minHeight;
                else bottom = top + minHeight;
            }
        }

        return new HavenRect(left, top, Math.Max(0d, right - left), Math.Max(0d, bottom - top));
    }

    public double RotationAt(HavenPoint pointer)
    {
        if (Kind != HavenManipulationKind.Rotate) return StartRotationDegrees;
        var delta = NormalizeDegrees(AngleDegrees(Center(StartBounds), pointer) - _startAngleDegrees);
        return StartRotationDegrees + delta;
    }

    private static HavenRect ClampMove(HavenRect value, HavenRect? constraint)
    {
        if (constraint is not { } area) return value;
        var maxX = Math.Max(area.X, area.Right - value.Width);
        var maxY = Math.Max(area.Y, area.Bottom - value.Height);
        return value with
        {
            X = Math.Clamp(value.X, area.X, maxX),
            Y = Math.Clamp(value.Y, area.Y, maxY)
        };
    }

    private static HavenPoint Center(HavenRect bounds) =>
        new(bounds.X + bounds.Width / 2d, bounds.Y + bounds.Height / 2d);

    private static double AngleDegrees(HavenPoint center, HavenPoint pointer) =>
        Math.Atan2(pointer.Y - center.Y, pointer.X - center.X) * 180d / Math.PI;

    private static double NormalizeDegrees(double value)
    {
        while (value <= -180d) value += 360d;
        while (value > 180d) value -= 360d;
        return value;
    }
}
