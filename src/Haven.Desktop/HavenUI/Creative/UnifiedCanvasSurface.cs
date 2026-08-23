using System.Globalization;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.UI;

namespace Haven.Desktop.HavenUI.Creative;

internal enum UnifiedCanvasTool
{
    Select, Pen, Highlighter, Eraser, Pan, Lasso, LaserPointer, LaserLasso, Text, Rectangle, Ellipse, Line, Frame, Connector
}

internal enum UnifiedCanvasHandle { None, NorthWest, NorthEast, SouthWest, SouthEast, Rotate }

/// <summary>Shared retained canvas presentation used by standalone Canvas and embedded whiteboards.</summary>
internal sealed class UnifiedCanvasSurface : HavenElement, IHavenDrawCommandSource, IHavenPointerInputTarget, IHavenScrollInputTarget, IHavenKeyboardInputTarget, IHavenTextInputTarget
{
    private const double HandleRadius = 7;
    private readonly Func<string> _textProvider;
    private CanvasInteractionController _controller;
    private UnifiedCanvasTool _tool = UnifiedCanvasTool.Select;
    private HavenPoint _pointerStart;
    private HavenPoint _pointerCurrent;
    private bool _marquee;
    private bool _moving;
    private bool _drawingObject;
    private UnifiedCanvasHandle _handle;
    private Dictionary<Guid, ObjectGeometry> _previewOriginals = [];
    private Guid? _connectorSource;
    private readonly List<HavenPoint> _pointerPath = [];
    private bool _pointerPathDrawing;

    public UnifiedCanvasSurface(CanvasInteractionController controller, Func<string>? textProvider = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _textProvider = textProvider ?? (() => "Text");
        Name = "Canvas.Unified.Surface";
        Accessibility.Role = HavenAccessibleRole.Group;
        Accessibility.Focusable = true;
        Accessibility.AccessibleName = "Editable canvas";
        Accessibility.Description = "Select, draw, pan, zoom and directly transform canvas objects.";
        SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SetValue(HavenProperties.Height, HavenLength.Percent(100));
        SetValue(HavenProperties.MinHeight, HavenLength.Px(520));
        SetValue(HavenProperties.Background, "SurfaceRaised");
        SetValue(HavenProperties.BorderColor, "Border");
        SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        SetValue(HavenProperties.Clip, true);
    }

    public event EventHandler? Changed;
    public event EventHandler? SelectionChanged;
    public CanvasInteractionController Controller => _controller;
    public UnifiedCanvasTool Tool => _tool;
    public double Zoom => _controller.Board.Zoom;
    public bool ShowGrid { get; set; } = true;
    public bool ShowGuides { get; set; } = true;
    public void RefreshSurface() => Invalidate();

    public bool ReleaseInputState()
    {
        if (_moving || _handle != UnifiedCanvasHandle.None) RestorePreviewOriginals();
        _connectorSource = null;
        _pointerPathDrawing = false;
        _pointerPath.Clear();
        ResetGesture();
        var committedMutation = _controller.ReleaseInteraction();
        Invalidate();
        return committedMutation;
    }

    public void SetController(CanvasInteractionController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _pointerPathDrawing = false;
        _pointerPath.Clear();
        ResetGesture();
        Invalidate();
    }

    public void SetTool(UnifiedCanvasTool tool)
    {
        if (_tool != tool)
        {
            _pointerPathDrawing = false;
            _pointerPath.Clear();
        }
        _tool = tool;
        _controller.Tool = tool switch
        {
            UnifiedCanvasTool.Pen => CanvasTool.Pen,
            UnifiedCanvasTool.Highlighter => CanvasTool.Highlighter,
            UnifiedCanvasTool.Eraser => CanvasTool.Eraser,
            UnifiedCanvasTool.Pan => CanvasTool.Pan,
            _ => CanvasTool.Select
        };
        _connectorSource = null;
        ResetGesture();
        Invalidate();
    }

    public bool PointerPressed(HavenPointerInput input)
    {
        _pointerStart = _pointerCurrent = input.LocalPosition;
        ResetGesture(keepPointer: true);
        switch (_tool)
        {
            case UnifiedCanvasTool.Pen:
            case UnifiedCanvasTool.Highlighter:
            case UnifiedCanvasTool.Eraser:
            case UnifiedCanvasTool.Pan:
                _controller.Begin(Sample(input));
                Invalidate();
                return true;
            case UnifiedCanvasTool.Lasso:
            case UnifiedCanvasTool.LaserPointer:
            case UnifiedCanvasTool.LaserLasso:
                _pointerPath.Clear();
                _pointerPath.Add(input.LocalPosition);
                _pointerPathDrawing = true;
                Invalidate();
                return true;
            case UnifiedCanvasTool.Text:
                AddObjectAtPointer(NotesCanvasObjectKind.Text, 220, 72, _textProvider(), "{\"shape\":\"text\"}");
                SetTool(UnifiedCanvasTool.Select);
                return true;
            case UnifiedCanvasTool.Rectangle:
            case UnifiedCanvasTool.Ellipse:
            case UnifiedCanvasTool.Line:
            case UnifiedCanvasTool.Frame:
                _drawingObject = true;
                Invalidate();
                return true;
            case UnifiedCanvasTool.Connector:
                HandleConnectorClick(input);
                return true;
            default:
                return BeginSelection(input);
        }
    }

    public bool PointerMoved(HavenPointerInput input)
    {
        _pointerCurrent = input.LocalPosition;
        if (_pointerPathDrawing)
        {
            if (_pointerPath.Count == 0 || DistanceSquared(_pointerPath[^1], input.LocalPosition) >= 4)
                _pointerPath.Add(input.LocalPosition);
            Invalidate();
            return true;
        }
        if (_tool is UnifiedCanvasTool.Pen or UnifiedCanvasTool.Highlighter or UnifiedCanvasTool.Eraser or UnifiedCanvasTool.Pan)
        {
            var changed = _controller.Move(Sample(input));
            if (changed) Invalidate();
            return true;
        }
        if (_moving) PreviewMove();
        else if (_handle != UnifiedCanvasHandle.None) PreviewTransform();
        else if (_marquee || _drawingObject) Invalidate();
        return _moving || _handle != UnifiedCanvasHandle.None || _marquee || _drawingObject;
    }

    public bool PointerReleased(HavenPointerInput input)
    {
        _pointerCurrent = input.LocalPosition;
        if (_pointerPathDrawing)
        {
            if (_pointerPath.Count == 0 || DistanceSquared(_pointerPath[^1], input.LocalPosition) >= 1)
                _pointerPath.Add(input.LocalPosition);
            _pointerPathDrawing = false;
            if ((_tool is UnifiedCanvasTool.Lasso or UnifiedCanvasTool.LaserLasso) && _pointerPath.Count >= 3)
            {
                var samples = _pointerPath.Select(point => new CanvasPointerSample(point.X, point.Y)).ToArray();
                _controller.SelectViewportPolygon(samples, input.Modifiers.HasFlag(HavenKeyModifiers.Shift));
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            if (_tool is UnifiedCanvasTool.Lasso or UnifiedCanvasTool.LaserPointer) _pointerPath.Clear();
            Invalidate();
            return true;
        }
        if (_tool is UnifiedCanvasTool.Pen or UnifiedCanvasTool.Highlighter or UnifiedCanvasTool.Eraser or UnifiedCanvasTool.Pan)
        {
            var changed = _controller.End(Sample(input));
            Changed?.Invoke(this, EventArgs.Empty);
            Invalidate();
            return changed || true;
        }
        if (_drawingObject) CommitCreatedObject();
        else if (_moving) CommitMove();
        else if (_handle != UnifiedCanvasHandle.None) CommitTransform();
        else if (_marquee)
        {
            var rect = Normalize(_pointerStart, _pointerCurrent);
            _controller.SelectViewportRectangle(rect.X, rect.Y, rect.Width, rect.Height, input.Modifiers.HasFlag(HavenKeyModifiers.Shift));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        ResetGesture();
        Invalidate();
        return true;
    }

    public bool PointerWheel(HavenPoint localPosition, double deltaX, double deltaY)
    {
        if (Math.Abs(deltaY) < .001) return false;
        var boardBefore = _controller.ViewportToCanvas(localPosition.X, localPosition.Y);
        var next = Math.Clamp(_controller.Board.Zoom * (deltaY < 0 ? 1.12 : .89), .1, 6);
        _controller.SetZoom(next);
        _controller.Board.OffsetX = localPosition.X - boardBefore.X * next;
        _controller.Board.OffsetY = localPosition.Y - boardBefore.Y * next;
        Changed?.Invoke(this, EventArgs.Empty);
        Invalidate();
        return true;
    }

    public bool TextInput(string? text) => false;

    bool IHavenKeyboardInputTarget.KeyDown(HavenKeyInput input) => KeyDown(input.Key, new HavenInputModifiers(
        Shift: input.Shift,
        Control: input.Control,
        Alt: input.Alt,
        Meta: input.Meta));

    bool IHavenKeyboardInputTarget.KeyUp(HavenKeyInput input) => KeyUp(input.Key);

    public bool KeyDown(HavenKey key, HavenInputModifiers modifiers)
    {
        if (modifiers.Control)
        {
            if (key == HavenKey.C) return _controller.CopySelection();
            if (key == HavenKey.V) { var changed = _controller.PasteSelection(); AfterMutation(changed); return changed; }
            if (key == HavenKey.Z) { var changed = _controller.Undo(); AfterMutation(changed); return changed; }
            if (key == HavenKey.Y) { var changed = _controller.Redo(); AfterMutation(changed); return changed; }
            if (key == HavenKey.A) { _controller.SetSelection(_controller.Board.Objects.Where(value => value.Kind != NotesCanvasObjectKind.Connector).Select(value => value.Id)); SelectionChanged?.Invoke(this, EventArgs.Empty); Invalidate(); return true; }
        }
        switch (key)
        {
            case HavenKey.Delete:
            case HavenKey.Backspace:
                { var changed = _controller.DeleteSelection(); AfterMutation(changed); return changed; }
            case HavenKey.Left: return Nudge(-1, 0, modifiers.Shift);
            case HavenKey.Right: return Nudge(1, 0, modifiers.Shift);
            case HavenKey.Up: return Nudge(0, -1, modifiers.Shift);
            case HavenKey.Down: return Nudge(0, 1, modifiers.Shift);
            case HavenKey.Escape: _controller.ClearSelection(); _connectorSource = null; _pointerPathDrawing = false; _pointerPath.Clear(); SelectionChanged?.Invoke(this, EventArgs.Empty); Invalidate(); return true;
        }
        return false;
    }

    public bool KeyUp(HavenKey key) => key is HavenKey.Left or HavenKey.Right or HavenKey.Up or HavenKey.Down or HavenKey.Delete or HavenKey.Backspace or HavenKey.Escape or HavenKey.A or HavenKey.C or HavenKey.V or HavenKey.Z or HavenKey.Y;

    public void Draw(HavenDrawingContext context, double opacity)
    {
        if (Bounds.Width <= 1 || Bounds.Height <= 1) return;
        context.Add(new HavenFillRoundedRectCommand(Bounds, new HavenSolidBrush(255, 252, 253, 255), 14, opacity));
        if (ShowGrid) DrawGrid(context, opacity);
        foreach (var stroke in _controller.Board.Strokes.Where(value => CanvasGhostVisibility.IsStrokeVisible(_controller.Board, value))) DrawStroke(context, stroke, opacity);
        var visible = _controller.Board.Objects.Where(value => CanvasGhostVisibility.IsObjectVisible(_controller.Board, value)).ToDictionary(value => value.Id);
        foreach (var value in visible.Values.OrderBy(value => value.ZIndex)) DrawObject(context, value, visible, opacity);
        DrawCreationPreview(context, opacity);
        DrawSelection(context, opacity);
        DrawMarquee(context, opacity);
        DrawPointerGesture(context, opacity);
        DrawSmartGuides(context, opacity);
    }

    private bool BeginSelection(HavenPointerInput input)
    {
        if (HitHandle(input.LocalPosition) is { } handle)
        {
            _handle = handle;
            CapturePreviewOriginals();
            return true;
        }
        var hit = _controller.HitObjectAtViewport(input.LocalPosition.X, input.LocalPosition.Y);
        if (hit is null)
        {
            if (!input.Modifiers.HasFlag(HavenKeyModifiers.Shift)) _controller.ClearSelection();
            _marquee = true;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
            return true;
        }
        if (input.Modifiers.HasFlag(HavenKeyModifiers.Shift)) _controller.ToggleSelection(hit.Id);
        else if (!_controller.SelectedObjectIds.Contains(hit.Id)) _controller.SetSelection([hit.Id]);
        if (!hit.Locked) { _moving = true; CapturePreviewOriginals(); }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
        return true;
    }

    private void CapturePreviewOriginals() => _previewOriginals = _controller.SelectedObjects.ToDictionary(value => value.Id, value => new ObjectGeometry(value.X, value.Y, value.Width, value.Height, value.Rotation));

    private void PreviewMove()
    {
        var start = _controller.ViewportToCanvas(_pointerStart.X, _pointerStart.Y);
        var current = _controller.ViewportToCanvas(_pointerCurrent.X, _pointerCurrent.Y);
        var dx = current.X - start.X; var dy = current.Y - start.Y;
        foreach (var value in _controller.SelectedObjects)
            if (_previewOriginals.TryGetValue(value.Id, out var original)) { value.X = Snap(original.X + dx); value.Y = Snap(original.Y + dy); }
        Invalidate();
    }

    private void CommitMove()
    {
        if (_previewOriginals.Count == 0) return;
        var first = _controller.SelectedObjects.FirstOrDefault();
        if (first is null || !_previewOriginals.TryGetValue(first.Id, out var original)) return;
        var dx = first.X - original.X; var dy = first.Y - original.Y;
        RestorePreviewOriginals();
        var changed = _controller.TranslateSelection(dx, dy, snap: true);
        AfterMutation(changed);
    }

    private void PreviewTransform()
    {
        RestorePreviewOriginals();
        if (_controller.SelectionBounds() is not { } bounds) return;
        var start = _controller.ViewportToCanvas(_pointerStart.X, _pointerStart.Y);
        var current = _controller.ViewportToCanvas(_pointerCurrent.X, _pointerCurrent.Y);
        if (_handle == UnifiedCanvasHandle.Rotate)
        {
            var cx = bounds.X + bounds.Width / 2; var cy = bounds.Y + bounds.Height / 2;
            var a = Math.Atan2(start.Y - cy, start.X - cx); var b = Math.Atan2(current.Y - cy, current.X - cx);
            var degrees = (b - a) * 180 / Math.PI;
            foreach (var value in _controller.SelectedObjects) if (_previewOriginals.TryGetValue(value.Id, out var original)) value.Rotation = NormalizeDegrees(original.Rotation + degrees);
            Invalidate();
            return;
        }
        var dx = current.X - start.X; var dy = current.Y - start.Y;
        var transform = ResizeDelta(_handle, dx, dy);
        PreviewScale(bounds, transform);
        Invalidate();
    }

    private void CommitTransform()
    {
        if (_previewOriginals.Count == 0) return;
        var currentBounds = _controller.SelectionBounds();
        if (currentBounds is null) return;
        var previewRot = _controller.SelectedObjects.FirstOrDefault()?.Rotation ?? 0;
        RestorePreviewOriginals();
        var originalBounds = _controller.SelectionBounds();
        if (originalBounds is null) return;
        var firstOriginalRotation = _controller.SelectedObjects.FirstOrDefault()?.Rotation ?? 0;
        var deltaRotation = _handle == UnifiedCanvasHandle.Rotate ? NormalizeSigned(previewRot - firstOriginalRotation) : 0;
        var changed = _controller.TransformSelection(currentBounds.Value.X - originalBounds.Value.X, currentBounds.Value.Y - originalBounds.Value.Y, currentBounds.Value.Width - originalBounds.Value.Width, currentBounds.Value.Height - originalBounds.Value.Height, deltaRotation);
        AfterMutation(changed);
    }

    private void PreviewScale(CanvasBounds bounds, (double X, double Y, double Width, double Height) delta)
    {
        var newWidth = Math.Max(8, bounds.Width + delta.Width); var newHeight = Math.Max(8, bounds.Height + delta.Height);
        var newX = bounds.X + delta.X; var newY = bounds.Y + delta.Y;
        var sx = bounds.Width <= .001 ? 1 : newWidth / bounds.Width; var sy = bounds.Height <= .001 ? 1 : newHeight / bounds.Height;
        foreach (var value in _controller.SelectedObjects)
        {
            if (!_previewOriginals.TryGetValue(value.Id, out var original)) continue;
            value.X = newX + (original.X - bounds.X) * sx; value.Y = newY + (original.Y - bounds.Y) * sy;
            value.Width = Math.Max(8, original.Width * sx); value.Height = Math.Max(8, original.Height * sy);
        }
    }

    private void RestorePreviewOriginals()
    {
        foreach (var value in _controller.SelectedObjects)
            if (_previewOriginals.TryGetValue(value.Id, out var original)) { value.X = original.X; value.Y = original.Y; value.Width = original.Width; value.Height = original.Height; value.Rotation = original.Rotation; }
    }

    private void CommitCreatedObject()
    {
        var rect = NormalizeCanvas(_pointerStart, _pointerCurrent);
        if (rect.Width < 5 && rect.Height < 5) rect = new CanvasBounds(rect.X, rect.Y, _tool == UnifiedCanvasTool.Frame ? 420 : 180, _tool == UnifiedCanvasTool.Line ? 2 : _tool == UnifiedCanvasTool.Frame ? 260 : 120);
        var kind = _tool == UnifiedCanvasTool.Frame ? NotesCanvasObjectKind.Frame : NotesCanvasObjectKind.Shape;
        var shape = _tool switch { UnifiedCanvasTool.Ellipse => "ellipse", UnifiedCanvasTool.Line => "line", UnifiedCanvasTool.Frame => "frame", _ => "rectangle" };
        _controller.AddObjectAt(kind, rect.X, rect.Y, rect.Width, rect.Height, shape == "frame" ? "Frame" : string.Empty, JsonSerializer.Serialize(new { shape }));
        SetTool(UnifiedCanvasTool.Select);
        AfterMutation(true);
    }

    private void AddObjectAtPointer(NotesCanvasObjectKind kind, double width, double height, string text, string styleJson)
    {
        var point = _controller.ViewportToCanvas(_pointerStart.X, _pointerStart.Y);
        _controller.AddObjectAt(kind, point.X, point.Y, width, height, text, styleJson);
        SelectionChanged?.Invoke(this, EventArgs.Empty); Changed?.Invoke(this, EventArgs.Empty); Invalidate();
    }

    public void AddImage(string path, double width = 420, double height = 300)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var center = _controller.ViewportToCanvas(Bounds.Width / 2, Bounds.Height / 2);
        _controller.AddObjectAt(NotesCanvasObjectKind.Image, center.X - width / 2, center.Y - height / 2, width, height, path, JsonSerializer.Serialize(new { source = path }));
        AfterMutation(true);
    }

    private void HandleConnectorClick(HavenPointerInput input)
    {
        var hit = _controller.HitObjectAtViewport(input.LocalPosition.X, input.LocalPosition.Y);
        if (hit is null) { _connectorSource = null; Invalidate(); return; }
        if (_connectorSource is null) { _connectorSource = hit.Id; _controller.SetSelection([hit.Id]); SelectionChanged?.Invoke(this, EventArgs.Empty); Invalidate(); return; }
        if (_connectorSource.Value != hit.Id) { _controller.Connect(_connectorSource.Value, hit.Id); AfterMutation(true); }
        _connectorSource = null; SetTool(UnifiedCanvasTool.Select);
    }

    private void DrawGrid(HavenDrawingContext context, double opacity)
    {
        var stepBoard = _controller.GridSize > 0 ? _controller.GridSize : 40;
        var step = stepBoard * _controller.Board.Zoom;
        if (step < 8) return;
        var pen = new HavenPen(new HavenSolidBrush(26, 70, 80, 95), 1);
        var offsetX = Mod(_controller.Board.OffsetX, step); var offsetY = Mod(_controller.Board.OffsetY, step);
        for (var x = Bounds.X + offsetX; x <= Bounds.Right; x += step) context.Add(new HavenLineCommand(new HavenPoint(x, Bounds.Y), new HavenPoint(x, Bounds.Bottom), pen, opacity));
        for (var y = Bounds.Y + offsetY; y <= Bounds.Bottom; y += step) context.Add(new HavenLineCommand(new HavenPoint(Bounds.X, y), new HavenPoint(Bounds.Right, y), pen, opacity));
    }

    private void DrawStroke(HavenDrawingContext context, NotesInkStroke stroke, double opacity)
    {
        var colour = ParseColour(stroke.Colour, stroke.Tool.Equals("highlighter", StringComparison.OrdinalIgnoreCase) ? .32 : stroke.Opacity);
        for (var index = 1; index < stroke.Points.Count; index++)
        {
            var a = ToScreen(stroke.Points[index - 1].X, stroke.Points[index - 1].Y); var b = ToScreen(stroke.Points[index].X, stroke.Points[index].Y);
            var pressure = (stroke.Points[index - 1].Pressure + stroke.Points[index].Pressure) / 2;
            context.Add(new HavenLineCommand(a, b, new HavenPen(colour, Math.Max(1, stroke.BaseWidth * _controller.Board.Zoom * (.45 + pressure * .8))), opacity));
        }
    }

    private void DrawObject(HavenDrawingContext context, NotesCanvasObject value, IReadOnlyDictionary<Guid, NotesCanvasObject> visible, double opacity)
    {
        if (value.Kind == NotesCanvasObjectKind.Connector)
        {
            if (value.FromObjectId is { } fromId && value.ToObjectId is { } toId && visible.TryGetValue(fromId, out var from) && visible.TryGetValue(toId, out var to))
            {
                var a = ToScreen(from.X + from.Width / 2, from.Y + from.Height / 2); var b = ToScreen(to.X + to.Width / 2, to.Y + to.Height / 2);
                context.Add(new HavenLineCommand(a, b, new HavenPen(new HavenSolidBrush(255, 75, 88, 108), Math.Max(1.5, 2 * _controller.Board.Zoom)), opacity));
            }
            return;
        }
        var rect = ToScreen(value.X, value.Y, value.Width, value.Height);
        if (Math.Abs(value.Rotation) > .001) context.Add(new HavenPushTransformCommand(rect, new HavenTransform(RotationDegrees: value.Rotation), new HavenPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2)));
        var shape = ReadShape(value.StyleJson);
        switch (value.Kind)
        {
            case NotesCanvasObjectKind.Text:
                context.Add(new HavenTextCommand(new HavenRect(rect.X + 6, rect.Y + 5, Math.Max(1, rect.Width - 12), Math.Max(1, rect.Height - 10)), new HavenTextLayout(value.Text, "Montserrat", Math.Max(12, 16 * _controller.Board.Zoom), 500, Math.Max(1, rect.Width - 12)), new HavenSolidBrush(255, 32, 39, 50), opacity));
                break;
            case NotesCanvasObjectKind.Image:
                context.Add(new HavenFillRoundedRectCommand(rect, new HavenSolidBrush(255, 245, 247, 250), 7, opacity));
                if (!string.IsNullOrWhiteSpace(value.Text)) context.Add(new HavenImageCommand(rect, new HavenImage(value.Text), HavenImageLayout.Contain, opacity));
                break;
            case NotesCanvasObjectKind.Frame:
                context.Add(new HavenStrokeRoundedRectCommand(rect, new HavenPen(new HavenSolidBrush(180, 72, 88, 110), 2), 10, opacity));
                if (!string.IsNullOrWhiteSpace(value.Text)) context.Add(new HavenTextCommand(new HavenRect(rect.X + 8, rect.Y + 6, rect.Width - 16, 24), new HavenTextLayout(value.Text, "Montserrat", 12, 700, rect.Width - 16), new HavenSolidBrush(255, 70, 79, 93), opacity));
                break;
            case NotesCanvasObjectKind.Shape when value.VectorShape is not null:
                context.Add(new HavenFillRoundedRectCommand(rect, new HavenSolidBrush(30, 57, 110, 220), 7, opacity));
                context.Add(new HavenStrokeRoundedRectCommand(rect, new HavenPen(new HavenSolidBrush(255, 57, 110, 220), 2), 7, opacity));
                break;
            case NotesCanvasObjectKind.Shape when shape == "ellipse":
                context.Add(new HavenEllipseCommand(rect, new HavenSolidBrush(24, 57, 110, 220), new HavenPen(new HavenSolidBrush(255, 57, 110, 220), 2), opacity));
                break;
            case NotesCanvasObjectKind.Shape when shape == "line":
                context.Add(new HavenLineCommand(new HavenPoint(rect.X, rect.Y), new HavenPoint(rect.Right, rect.Bottom), new HavenPen(new HavenSolidBrush(255, 57, 110, 220), 2.5), opacity));
                break;
            default:
                context.Add(new HavenFillRoundedRectCommand(rect, new HavenSolidBrush(28, 57, 110, 220), 8, opacity));
                context.Add(new HavenStrokeRoundedRectCommand(rect, new HavenPen(new HavenSolidBrush(255, 57, 110, 220), 2), 8, opacity));
                if (!string.IsNullOrWhiteSpace(value.Text)) context.Add(new HavenTextCommand(rect, new HavenTextLayout(value.Text, "Montserrat", 13, 600, rect.Width, true), new HavenSolidBrush(255, 35, 45, 60), opacity));
                break;
        }
        if (Math.Abs(value.Rotation) > .001) context.Add(new HavenPopTransformCommand(rect));
    }

    private void DrawSelection(HavenDrawingContext context, double opacity)
    {
        var selected = _controller.SelectedObjects;
        foreach (var value in selected)
        {
            var rect = ToScreen(value.X, value.Y, value.Width, value.Height);
            if (Math.Abs(value.Rotation) > .001) context.Add(new HavenPushTransformCommand(rect, new HavenTransform(RotationDegrees: value.Rotation), new HavenPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2)));
            context.Add(new HavenStrokeRoundedRectCommand(rect, new HavenPen(new HavenSolidBrush(255, 57, 110, 220), 2), 3, opacity));
            if (Math.Abs(value.Rotation) > .001) context.Add(new HavenPopTransformCommand(rect));
        }
        if (selected.Count != 1 || selected[0].Locked) return;
        var handles = HandlePoints(selected[0]);
        foreach (var point in handles.Where(pair => pair.Handle != UnifiedCanvasHandle.Rotate)) context.Add(new HavenEllipseCommand(new HavenRect(point.Point.X - HandleRadius, point.Point.Y - HandleRadius, HandleRadius * 2, HandleRadius * 2), new HavenSolidBrush(255, 255, 255, 255), new HavenPen(new HavenSolidBrush(255, 57, 110, 220), 2), opacity));
        var rotate = handles.First(pair => pair.Handle == UnifiedCanvasHandle.Rotate);
        var top = handles.First(pair => pair.Handle == UnifiedCanvasHandle.NorthWest);
        context.Add(new HavenLineCommand(new HavenPoint((top.Point.X + handles.First(pair => pair.Handle == UnifiedCanvasHandle.NorthEast).Point.X) / 2, top.Point.Y), rotate.Point, new HavenPen(new HavenSolidBrush(255, 57, 110, 220), 1.5), opacity));
        context.Add(new HavenEllipseCommand(new HavenRect(rotate.Point.X - HandleRadius, rotate.Point.Y - HandleRadius, HandleRadius * 2, HandleRadius * 2), new HavenSolidBrush(255, 255, 255, 255), new HavenPen(new HavenSolidBrush(255, 57, 110, 220), 2), opacity));
    }

    private void DrawMarquee(HavenDrawingContext context, double opacity)
    {
        if (!_marquee) return; var rect = Normalize(_pointerStart, _pointerCurrent);
        context.Add(new HavenFillRoundedRectCommand(rect, new HavenSolidBrush(28, 57, 110, 220), 2, opacity));
        context.Add(new HavenStrokeRoundedRectCommand(rect, new HavenPen(new HavenSolidBrush(210, 57, 110, 220), 1.5), 2, opacity));
    }

    private void DrawPointerGesture(HavenDrawingContext context, double opacity)
    {
        if (_pointerPath.Count < 2) return;
        var laser = _tool is UnifiedCanvasTool.LaserPointer or UnifiedCanvasTool.LaserLasso;
        var brush = laser ? new HavenSolidBrush(235, 255, 68, 98) : new HavenSolidBrush(220, 57, 110, 220);
        var pen = new HavenPen(brush, laser ? 3.5 : 1.8);
        for (var index = 1; index < _pointerPath.Count; index++)
            context.Add(new HavenLineCommand(_pointerPath[index - 1], _pointerPath[index], pen, opacity));
        if ((_tool is UnifiedCanvasTool.Lasso or UnifiedCanvasTool.LaserLasso) && _pointerPath.Count > 2)
            context.Add(new HavenLineCommand(_pointerPath[^1], _pointerPath[0], pen, opacity));
    }

    private void DrawCreationPreview(HavenDrawingContext context, double opacity)
    {
        if (!_drawingObject) return; var rect = Normalize(_pointerStart, _pointerCurrent);
        context.Add(new HavenStrokeRoundedRectCommand(rect, new HavenPen(new HavenSolidBrush(210, 57, 110, 220), 1.5), 5, opacity));
    }

    private void DrawSmartGuides(HavenDrawingContext context, double opacity)
    {
        if (!ShowGuides || !_moving || _controller.SelectedObjects.Count == 0) return;
        var selectedBounds = _controller.SelectionBounds(); if (selectedBounds is null) return;
        const double tolerance = 5; var pen = new HavenPen(new HavenSolidBrush(220, 230, 70, 120), 1);
        foreach (var other in _controller.Board.Objects.Where(value => !_controller.SelectedObjectIds.Contains(value.Id) && value.Kind != NotesCanvasObjectKind.Connector))
        {
            var otherCenterX = other.X + other.Width / 2; var selectedCenterX = selectedBounds.Value.X + selectedBounds.Value.Width / 2;
            if (Math.Abs(otherCenterX - selectedCenterX) <= tolerance) { var x = ToScreen(otherCenterX, 0).X; context.Add(new HavenLineCommand(new HavenPoint(x, Bounds.Y), new HavenPoint(x, Bounds.Bottom), pen, opacity)); }
            var otherCenterY = other.Y + other.Height / 2; var selectedCenterY = selectedBounds.Value.Y + selectedBounds.Value.Height / 2;
            if (Math.Abs(otherCenterY - selectedCenterY) <= tolerance) { var y = ToScreen(0, otherCenterY).Y; context.Add(new HavenLineCommand(new HavenPoint(Bounds.X, y), new HavenPoint(Bounds.Right, y), pen, opacity)); }
        }
    }

    private UnifiedCanvasHandle? HitHandle(HavenPoint local)
    {
        if (_controller.SelectedObjects.Count != 1 || _controller.SelectedObjects[0].Locked) return null;
        foreach (var pair in HandlePoints(_controller.SelectedObjects[0])) if (DistanceSquared(local, pair.Point) <= HandleRadius * HandleRadius * 2.4) return pair.Handle;
        return null;
    }

    private IReadOnlyList<(UnifiedCanvasHandle Handle, HavenPoint Point)> HandlePoints(NotesCanvasObject value)
    {
        var rect = ToScreen(value.X, value.Y, value.Width, value.Height); var center = new HavenPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        HavenPoint Rotate(HavenPoint p)
        {
            if (Math.Abs(value.Rotation) < .001) return p; var radians = value.Rotation * Math.PI / 180; var cos = Math.Cos(radians); var sin = Math.Sin(radians); var dx = p.X - center.X; var dy = p.Y - center.Y; return new HavenPoint(dx * cos - dy * sin + center.X, dx * sin + dy * cos + center.Y);
        }
        return [
            (UnifiedCanvasHandle.NorthWest, Rotate(new HavenPoint(rect.X, rect.Y))), (UnifiedCanvasHandle.NorthEast, Rotate(new HavenPoint(rect.Right, rect.Y))),
            (UnifiedCanvasHandle.SouthWest, Rotate(new HavenPoint(rect.X, rect.Bottom))), (UnifiedCanvasHandle.SouthEast, Rotate(new HavenPoint(rect.Right, rect.Bottom))),
            (UnifiedCanvasHandle.Rotate, Rotate(new HavenPoint(center.X, rect.Y - 28)))
        ];
    }

    private bool Nudge(double x, double y, bool large)
    {
        var amount = large ? 10 : 1; var changed = _controller.TranslateSelection(x * amount, y * amount, snap: false); AfterMutation(changed); return changed;
    }

    private void AfterMutation(bool changed)
    {
        if (!changed) return; Changed?.Invoke(this, EventArgs.Empty); SelectionChanged?.Invoke(this, EventArgs.Empty); Invalidate();
    }

    private void ResetGesture(bool keepPointer = false)
    {
        _marquee = false; _moving = false; _drawingObject = false; _handle = UnifiedCanvasHandle.None; _previewOriginals.Clear();
        if (!keepPointer) _pointerStart = _pointerCurrent = default;
    }

    private void SetToolAfterCreate() => SetTool(UnifiedCanvasTool.Select);
    private CanvasPointerSample Sample(HavenPointerInput input) => new(input.LocalPosition.X, input.LocalPosition.Y, .5, 0, 0, Environment.TickCount64);
    private double Snap(double value) => _controller.GridSize > 0 ? Math.Round(value / _controller.GridSize) * _controller.GridSize : value;
    private CanvasBounds NormalizeCanvas(HavenPoint a, HavenPoint b) { var first = _controller.ViewportToCanvas(a.X, a.Y); var second = _controller.ViewportToCanvas(b.X, b.Y); return new CanvasBounds(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y), Math.Abs(first.X - second.X), Math.Abs(first.Y - second.Y)); }
    private HavenRect Normalize(HavenPoint a, HavenPoint b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    private HavenPoint ToScreen(double x, double y) { var point = _controller.CanvasToViewport(x, y); return new HavenPoint(Bounds.X + point.X, Bounds.Y + point.Y); }
    private HavenRect ToScreen(double x, double y, double width, double height) { var topLeft = ToScreen(x, y); return new HavenRect(topLeft.X, topLeft.Y, width * _controller.Board.Zoom, height * _controller.Board.Zoom); }
    private static (double X, double Y, double Width, double Height) ResizeDelta(UnifiedCanvasHandle handle, double dx, double dy) => handle switch { UnifiedCanvasHandle.NorthWest => (dx, dy, -dx, -dy), UnifiedCanvasHandle.NorthEast => (0, dy, dx, -dy), UnifiedCanvasHandle.SouthWest => (dx, 0, -dx, dy), UnifiedCanvasHandle.SouthEast => (0, 0, dx, dy), _ => default };
    private static double NormalizeDegrees(double value) => ((value % 360) + 360) % 360;
    private static double NormalizeSigned(double value) { value %= 360; if (value > 180) value -= 360; if (value < -180) value += 360; return value; }
    private static double DistanceSquared(HavenPoint a, HavenPoint b) { var x = a.X - b.X; var y = a.Y - b.Y; return x * x + y * y; }
    private static double Mod(double value, double modulus) { var result = value % modulus; return result < 0 ? result + modulus : result; }
    private static HavenBrush ParseColour(string? value, double opacity = 1) { var text = string.IsNullOrWhiteSpace(value) ? "#2F80ED" : value.Trim(); text = text.StartsWith('#') ? text[1..] : text; uint packed = 0x2F80ED; if (text.Length is 6 or 8) uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out packed); byte a, r, g, b; if (text.Length == 8) { a = (byte)(packed >> 24); r = (byte)(packed >> 16); g = (byte)(packed >> 8); b = (byte)packed; } else { a = 255; r = (byte)(packed >> 16); g = (byte)(packed >> 8); b = (byte)packed; } a = (byte)Math.Clamp(a * opacity, 0, 255); return new HavenSolidBrush(a, r, g, b); }
    private static string ReadShape(string? json) { if (string.IsNullOrWhiteSpace(json)) return "rectangle"; try { using var document = JsonDocument.Parse(json); return document.RootElement.TryGetProperty("shape", out var value) ? value.GetString() ?? "rectangle" : "rectangle"; } catch (JsonException) { return "rectangle"; } }
    private readonly record struct ObjectGeometry(double X, double Y, double Width, double Height, double Rotation);
}
