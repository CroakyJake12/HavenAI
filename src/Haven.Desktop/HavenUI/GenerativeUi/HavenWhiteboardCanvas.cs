using System.Globalization;
using Haven.UI;

namespace Haven.Desktop.HavenUI.GenerativeUi;

internal sealed class HavenWhiteboardCanvas : HavenElement, IHavenDrawCommandSource, IHavenPointerInputTarget, IHavenScrollInputTarget
{
    private const int MaximumPointsPerStroke = 700;
    private readonly HavenWhiteboardSession _session;
    private readonly Func<string> _textProvider;
    private readonly Action<HavenWhiteboardElement?> _selectionChanged;
    private List<HavenWhiteboardInkPoint>? _pending;
    private HavenPoint? _shapeStart;
    private HavenPoint _shapeCurrent;
    private HavenPoint _pointerStart;
    private HavenPoint _lastBoardPoint;
    private HavenWhiteboardElement? _movingOriginal;
    private bool _panning;

    public HavenWhiteboardCanvas(
        HavenWhiteboardSession session,
        Func<string> textProvider,
        Action<HavenWhiteboardElement?> selectionChanged)
    {
        _session = session;
        _textProvider = textProvider;
        _selectionChanged = selectionChanged;
        Accessibility.Role = HavenAccessibleRole.Image;
        Accessibility.Focusable = true;
        Accessibility.AccessibleName = "Interactive whiteboard canvas";
        SetValue(HavenProperties.Background, "Transparent");
        SetValue(HavenProperties.BorderColor, "Border");
        SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        SetValue(HavenProperties.Clip, true);
    }

    public bool PointerPressed(HavenPointerInput input)
    {
        _pointerStart = input.LocalPosition;
        _lastBoardPoint = _session.ToBoard(input.LocalPosition);

        switch (_session.Tool)
        {
            case HavenWhiteboardTool.Select:
                _session.SelectAt(_lastBoardPoint);
                _movingOriginal = _session.SelectedElement();
                _selectionChanged(_movingOriginal);
                break;
            case HavenWhiteboardTool.Eraser:
                _session.EraseAt(_lastBoardPoint);
                _selectionChanged(_session.SelectedElement());
                break;
            case HavenWhiteboardTool.Text:
                _session.CommitText(_lastBoardPoint, _textProvider());
                _session.Tool = HavenWhiteboardTool.Select;
                _selectionChanged(_session.SelectedElement());
                break;
            case HavenWhiteboardTool.Rectangle:
            case HavenWhiteboardTool.Ellipse:
            case HavenWhiteboardTool.Line:
                _shapeStart = _lastBoardPoint;
                _shapeCurrent = _lastBoardPoint;
                break;
            case HavenWhiteboardTool.Pan:
                _panning = true;
                break;
            default:
                _pending = [new HavenWhiteboardInkPoint(_lastBoardPoint.X, _lastBoardPoint.Y)];
                break;
        }

        Invalidate();
        return true;
    }

    public bool PointerMoved(HavenPointerInput input)
    {
        var boardPoint = _session.ToBoard(input.LocalPosition);

        if (_pending is not null)
        {
            if (_pending.Count < MaximumPointsPerStroke && DistanceSquared(_pending[^1].Position, boardPoint) >= 1.5)
                _pending.Add(new HavenWhiteboardInkPoint(boardPoint.X, boardPoint.Y));
        }
        else if (_shapeStart is not null)
        {
            _shapeCurrent = boardPoint;
        }
        else if (_movingOriginal is not null)
        {
            _session.PreviewMove(
                _movingOriginal,
                boardPoint.X - _lastBoardPoint.X,
                boardPoint.Y - _lastBoardPoint.Y);
        }
        else if (_panning)
        {
            _session.PanBy(
                input.LocalPosition.X - _pointerStart.X,
                input.LocalPosition.Y - _pointerStart.Y);
            _pointerStart = input.LocalPosition;
        }
        else if (_session.Tool == HavenWhiteboardTool.Eraser)
        {
            _session.EraseAt(boardPoint);
        }

        Invalidate();
        return true;
    }

    public bool PointerReleased(HavenPointerInput input)
    {
        if (_pending is not null)
        {
            var point = _session.ToBoard(input.LocalPosition);
            if (_pending.Count < MaximumPointsPerStroke && DistanceSquared(_pending[^1].Position, point) >= .5)
                _pending.Add(new HavenWhiteboardInkPoint(point.X, point.Y));
            _session.CommitStroke(_pending.ToArray());
        }
        else if (_shapeStart is { } start)
        {
            var kind = _session.Tool switch
            {
                HavenWhiteboardTool.Rectangle => HavenWhiteboardElementKind.Rectangle,
                HavenWhiteboardTool.Ellipse => HavenWhiteboardElementKind.Ellipse,
                _ => HavenWhiteboardElementKind.Line
            };
            _session.CommitShape(kind, start, _shapeCurrent);
        }
        else if (_movingOriginal is not null)
        {
            _session.CommitPreview(_movingOriginal);
        }

        _pending = null;
        _shapeStart = null;
        _movingOriginal = null;
        _panning = false;
        _selectionChanged(_session.SelectedElement());
        Invalidate();
        return true;
    }

    public bool PointerWheel(HavenPoint localPosition, double deltaX, double deltaY)
    {
        if (Math.Abs(deltaY) < .001) return false;
        _session.ZoomAt(localPosition, deltaY < 0 ? 1.1 : 0.9);
        return true;
    }

    public void Draw(HavenDrawingContext context, double opacity)
    {
        if (Bounds.Width <= 1 || Bounds.Height <= 1) return;
        context.Add(new HavenFillRoundedRectCommand(Bounds, new HavenSolidBrush(255, 255, 255, 255), 12, opacity));

        if (_session.ShowGrid) DrawGrid(context, opacity);
        foreach (var element in _session.Elements) DrawElement(context, element, opacity);
        DrawPreview(context, opacity);
        if (_session.SelectedElement() is { } selected) DrawSelection(context, selected, opacity);
    }

    private void DrawGrid(HavenDrawingContext context, double opacity)
    {
        var step = 40 * _session.Zoom;
        if (step < 8) return;
        var gridPen = new HavenPen(new HavenSolidBrush(30, 60, 70, 85), 1);
        var offsetX = Mod(_session.Offset.X, step);
        var offsetY = Mod(_session.Offset.Y, step);
        for (var x = Bounds.X + offsetX; x <= Bounds.Right; x += step)
            context.Add(new HavenLineCommand(new HavenPoint(x, Bounds.Y), new HavenPoint(x, Bounds.Bottom), gridPen, opacity));
        for (var y = Bounds.Y + offsetY; y <= Bounds.Bottom; y += step)
            context.Add(new HavenLineCommand(new HavenPoint(Bounds.X, y), new HavenPoint(Bounds.Right, y), gridPen, opacity));
    }

    private void DrawElement(HavenDrawingContext context, HavenWhiteboardElement element, double opacity)
    {
        var brush = Brush(element.IsEraser ? "#FFFFFF" : element.Color, element.Opacity);
        var thickness = Math.Max(1, element.Thickness * _session.Zoom);

        if (element.Effect == HavenWhiteboardPenEffect.Glow && !element.IsEraser)
        {
            var glowBrush = Brush(element.Color, Math.Min(0.24, element.Opacity));
            DrawElementCore(context, element, glowBrush, thickness * 3.2, opacity);
        }

        DrawElementCore(context, element, brush, thickness, opacity);
    }

    private void DrawElementCore(
        HavenDrawingContext context,
        HavenWhiteboardElement element,
        HavenBrush brush,
        double thickness,
        double opacity)
    {
        var boardBounds = HavenWhiteboardSession.BoundsOf(element);
        var bounds = ToScreen(boardBounds);

        switch (element.Kind)
        {
            case HavenWhiteboardElementKind.Stroke:
                if (element.Effect == HavenWhiteboardPenEffect.Dotted)
                {
                    foreach (var point in element.Points)
                    {
                        var p = ToScreen(point.Position);
                        var diameter = Math.Max(2, thickness);
                        context.Add(new HavenEllipseCommand(
                            new HavenRect(p.X - diameter / 2, p.Y - diameter / 2, diameter, diameter),
                            brush, null, opacity));
                    }
                    break;
                }

                for (var index = 1; index < element.Points.Count; index++)
                {
                    var pressure = (element.Points[index - 1].Pressure + element.Points[index].Pressure) / 2;
                    var width = thickness * (0.45 + pressure * 0.8);
                    context.Add(new HavenLineCommand(
                        ToScreen(element.Points[index - 1].Position),
                        ToScreen(element.Points[index].Position),
                        new HavenPen(brush, width), opacity));
                }
                break;

            case HavenWhiteboardElementKind.Rectangle:
                context.Add(new HavenFillRoundedRectCommand(bounds, WithAlpha(brush, 0.10), 8, opacity));
                context.Add(new HavenStrokeRoundedRectCommand(bounds, new HavenPen(brush, thickness), 8, opacity));
                break;

            case HavenWhiteboardElementKind.Ellipse:
                context.Add(new HavenEllipseCommand(bounds, WithAlpha(brush, 0), new HavenPen(brush, thickness), opacity));
                break;

            case HavenWhiteboardElementKind.Line:
                if (element.Points.Count > 1)
                    context.Add(new HavenLineCommand(
                        ToScreen(element.Points[0].Position),
                        ToScreen(element.Points[1].Position),
                        new HavenPen(brush, thickness), opacity));
                break;

            case HavenWhiteboardElementKind.Text:
                context.Add(new HavenTextCommand(
                    bounds,
                    new HavenTextLayout(element.Text, "Segoe UI", Math.Max(16, element.Thickness * 3) * _session.Zoom, 600, bounds.Width),
                    brush, opacity));
                break;

            case HavenWhiteboardElementKind.Image:
                context.Add(new HavenImageCommand(bounds, new HavenImage(element.Text), HavenImageLayout.Contain, opacity));
                break;
        }
    }

    private void DrawPreview(HavenDrawingContext context, double opacity)
    {
        if (_pending is { Count: > 1 })
        {
            DrawElement(context, new HavenWhiteboardElement(
                "preview", HavenWhiteboardElementKind.Stroke, _session.Color, _session.Thickness,
                _session.Tool == HavenWhiteboardTool.Highlighter ? 0.34 : 1, _session.Effect, false, false, string.Empty, _pending), opacity);
            return;
        }

        if (_shapeStart is not { } start) return;
        var kind = _session.Tool switch
        {
            HavenWhiteboardTool.Rectangle => HavenWhiteboardElementKind.Rectangle,
            HavenWhiteboardTool.Ellipse => HavenWhiteboardElementKind.Ellipse,
            _ => HavenWhiteboardElementKind.Line
        };
        DrawElement(context, new HavenWhiteboardElement(
            "preview", kind, _session.Color, _session.Thickness, 1, _session.Effect, false, false, string.Empty,
            [new HavenWhiteboardInkPoint(start.X, start.Y), new HavenWhiteboardInkPoint(_shapeCurrent.X, _shapeCurrent.Y)]), opacity);
    }

    private void DrawSelection(HavenDrawingContext context, HavenWhiteboardElement element, double opacity)
    {
        var board = HavenWhiteboardSession.BoundsOf(element);
        var pad = 8 / _session.Zoom;
        var expanded = new HavenRect(board.X - pad, board.Y - pad, board.Width + pad * 2, board.Height + pad * 2);
        var bounds = ToScreen(expanded);
        var colour = element.AgentGenerated ? "#8E24AA" : "#1E88E5";
        context.Add(new HavenStrokeRoundedRectCommand(
            bounds, new HavenPen(Brush(colour, 1), Math.Max(1, 2 * _session.Zoom)), 5, opacity));
        if (element.AgentGenerated)
        {
            context.Add(new HavenTextCommand(
                new HavenRect(bounds.X, Math.Max(Bounds.Y, bounds.Y - 18), Math.Max(48, bounds.Width), 16),
                new HavenTextLayout("Haven", "Segoe UI", 11, 600, bounds.Width),
                Brush("#8E24AA", 1), opacity));
        }
    }

    private HavenPoint ToScreen(HavenPoint board) => new(
        Bounds.X + _session.Offset.X + board.X * _session.Zoom,
        Bounds.Y + _session.Offset.Y + board.Y * _session.Zoom);

    private HavenRect ToScreen(HavenRect board) => new(
        Bounds.X + _session.Offset.X + board.X * _session.Zoom,
        Bounds.Y + _session.Offset.Y + board.Y * _session.Zoom,
        board.Width * _session.Zoom,
        board.Height * _session.Zoom);

    private static HavenBrush Brush(string colour, double opacity)
    {
        if (!HavenWhiteboardSession.TryNormaliseColor(colour, out var normalized)) normalized = "#111111";
        var r = byte.Parse(normalized.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = byte.Parse(normalized.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = byte.Parse(normalized.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new HavenSolidBrush((byte)Math.Clamp(opacity * 255, 0, 255), r, g, b);
    }

    private static HavenBrush WithAlpha(HavenBrush brush, double multiplier) => brush switch
    {
        HavenSolidBrush solid => solid with { A = (byte)Math.Clamp(solid.A * multiplier, 0, 255) },
        _ => brush
    };

    private static double Mod(double value, double modulus)
    {
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static double DistanceSquared(HavenPoint first, HavenPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return dx * dx + dy * dy;
    }
}
