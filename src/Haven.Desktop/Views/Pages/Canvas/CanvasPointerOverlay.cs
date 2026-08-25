using Haven.Application;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Canvas;

internal enum CanvasPointerMode
{
    Normal,
    Pan,
    Lasso,
    LaserPointer,
    LaserLasso
}

internal sealed class CanvasPointerOverlay : HavenElement, IHavenDrawCommandSource, IHavenPointerInputTarget
{
    private CanvasInteractionController _controller;
    private readonly List<HavenPoint> _points = [];
    private bool _drawing;

    public CanvasPointerOverlay(CanvasInteractionController controller)
    {
        _controller = controller;
        Name = "Canvas.PointerOverlay";
        Accessibility.Role = HavenAccessibleRole.Group;
        Accessibility.AccessibleName = "Canvas pointer mode overlay";
        SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SetValue(HavenProperties.Height, HavenLength.Percent(100));
        SetValue(HavenProperties.Background, "Transparent");
        SetValue(HavenProperties.ZIndex, 40);
        SetMode(CanvasPointerMode.Normal);
    }

    public event EventHandler? SelectionChanged;
    public CanvasPointerMode Mode { get; private set; }

    public void SetController(CanvasInteractionController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _drawing = false;
        _points.Clear();
        Invalidate();
    }

    public void SetMode(CanvasPointerMode mode)
    {
        if (Mode == mode)
        {
            SetValue(HavenProperties.Visibility,
                mode is CanvasPointerMode.Normal or CanvasPointerMode.Pan ? HavenVisibility.Collapsed : HavenVisibility.Visible);
            Invalidate();
            return;
        }
        Mode = mode;
        _drawing = false;
        if (mode != CanvasPointerMode.LaserLasso) _points.Clear();
        SetValue(HavenProperties.Visibility,
            mode is CanvasPointerMode.Normal or CanvasPointerMode.Pan ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        Invalidate();
    }

    public void ClearLaser()
    {
        _points.Clear();
        _drawing = false;
        Invalidate();
    }

    public bool PointerPressed(HavenPointerInput input)
    {
        if (Mode is CanvasPointerMode.Normal or CanvasPointerMode.Pan) return false;
        _points.Clear();
        _points.Add(input.LocalPosition);
        _drawing = true;
        Invalidate();
        return true;
    }

    public bool PointerMoved(HavenPointerInput input)
    {
        if (!_drawing) return false;
        if (_points.Count == 0 || DistanceSquared(_points[^1], input.LocalPosition) >= 4)
            _points.Add(input.LocalPosition);
        Invalidate();
        return true;
    }

    public bool PointerReleased(HavenPointerInput input)
    {
        if (!_drawing) return false;
        if (_points.Count == 0 || DistanceSquared(_points[^1], input.LocalPosition) >= 1)
            _points.Add(input.LocalPosition);
        _drawing = false;

        if (Mode is CanvasPointerMode.Lasso or CanvasPointerMode.LaserLasso)
        {
            var samples = _points.Select(point => new CanvasPointerSample(point.X, point.Y)).ToArray();
            _controller.SelectViewportPolygon(samples, input.Modifiers.HasFlag(HavenKeyModifiers.Shift));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        if (Mode is CanvasPointerMode.Lasso or CanvasPointerMode.LaserPointer)
            _points.Clear();

        Invalidate();
        return true;
    }

    public void Draw(HavenDrawingContext context, double opacity)
    {
        if (_points.Count < 2) return;
        var laser = Mode is CanvasPointerMode.LaserPointer or CanvasPointerMode.LaserLasso;
        var brush = laser ? new HavenSolidBrush(235, 255, 68, 98) : new HavenSolidBrush(220, 57, 110, 220);
        var pen = new HavenPen(brush, laser ? 3.5 : 1.8);
        for (var index = 1; index < _points.Count; index++)
            context.Add(new HavenLineCommand(_points[index - 1], _points[index], pen, opacity));

        if ((Mode is CanvasPointerMode.Lasso or CanvasPointerMode.LaserLasso) && _points.Count > 2)
            context.Add(new HavenLineCommand(_points[^1], _points[0], pen, opacity));
    }

    private static double DistanceSquared(HavenPoint a, HavenPoint b)
    {
        var x = a.X - b.X;
        var y = a.Y - b.Y;
        return x * x + y * y;
    }
}
