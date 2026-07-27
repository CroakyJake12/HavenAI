/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Controls/NotesInkCanvasControl.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns NotesInkCanvasControl. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Haven.Core;

namespace Haven.Desktop.Controls;

/// <summary>
/// Represents notes ink canvas control and keeps its related state and behavior together.
/// </summary>
public sealed class NotesInkCanvasControl : Control
{
    /// <summary>
    /// Stores active points locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly List<NotesInkPoint> _activePoints = [];
    /// <summary>
    /// Stores drawing locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _drawing;
    /// <summary>
    /// Stores stroke start locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private long _strokeStart;

    public NotesInkCanvasControl()
    {
        Focusable = true;
        ClipToBounds = true;
        MinHeight = 320;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
        PointerWheelChanged += OnPointerWheelChanged;
    }

    /// <summary>
    /// Reports whether canvas data is true for the current state.
    /// </summary>
    public NotesCanvasData? CanvasData { get; set; }
    /// <summary>
    /// Gets or updates tool, the bindable or domain state represented by this property.
    /// </summary>
    public string Tool { get; set; } = "pen";
    /// <summary>
    /// Gets or updates colour, the bindable or domain state represented by this property.
    /// </summary>
    public string Colour { get; set; } = "#FF2F80ED";
    /// <summary>
    /// Gets or updates stroke width, the bindable or domain state represented by this property.
    /// </summary>
    public double StrokeWidth { get; set; } = 2.5;
    /// <summary>
    /// Gets or updates opacity, the bindable or domain state represented by this property.
    /// </summary>
    public new double Opacity { get; set; } = 1;
    /// <summary>
    /// Gets or updates active ghost layer id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid? ActiveGhostLayerId { get; set; }
    /// <summary>
    /// Stores stroke completed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler<NotesInkStroke>? StrokeCompleted;
    /// <summary>
    /// Stores stroke erased locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler<Guid>? StrokeErased;
    /// <summary>
    /// Stores view changed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler? ViewChanged;

    /// <summary>
    /// Performs the render step owned by this component.
    /// </summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)), null, Bounds);
        var canvas = CanvasData;
        if (canvas is null) return;
        var scale = Math.Clamp(canvas.Zoom, 0.05, 100);
        var transform = Matrix.CreateTranslation(canvas.OffsetX, canvas.OffsetY) * Matrix.CreateScale(scale, scale);
        using (context.PushTransform(transform))
        {
            DrawGrid(context, canvas);
            foreach (var canvasObject in canvas.Objects.OrderBy(item => item.ZIndex)) DrawObject(context, canvasObject);
            foreach (var stroke in canvas.Strokes) DrawStroke(context, canvas, stroke);
            if (_activePoints.Count > 1)
            {
                var preview = new NotesInkStroke
                {
                    Colour = Colour,
                    BaseWidth = StrokeWidth,
                    Opacity = Opacity,
                    GhostLayerId = ActiveGhostLayerId,
                    IsGhost = ActiveGhostLayerId is not null,
                    Points = _activePoints.ToList()
                };
                DrawStroke(context, canvas, preview, ignoreGhostVisibility: true);
            }
            foreach (var layer in canvas.GhostLayers.Where(layer => !layer.IsRevealed))
            foreach (var mask in layer.Masks)
            {
                var fill = new SolidColorBrush(Color.FromArgb(225, 36, 40, 48));
                var border = new Pen(new SolidColorBrush(Color.FromArgb(180, 255, 193, 7)), 1.5);
                context.DrawRectangle(fill, border, new Rect(mask.X, mask.Y, mask.Width, mask.Height), 5, 5);
            }
        }
    }

    /// <summary>
    /// Handles the pointer pressed event raised by the UI or runtime.
    /// </summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (CanvasData is null) return;
        var point = e.GetCurrentPoint(this);
        var canvasPoint = ToCanvas(point.Position);
        if (Tool.Equals("eraser", StringComparison.OrdinalIgnoreCase))
        {
            var nearest = FindStroke(canvasPoint, 14 / Math.Max(CanvasData.Zoom, 0.05));
            if (nearest is not null) StrokeErased?.Invoke(this, nearest.Id);
            e.Handled = true;
            return;
        }
        _activePoints.Clear();
        _strokeStart = Environment.TickCount64;
        _activePoints.Add(ToInkPoint(point, canvasPoint));
        _drawing = true;
        e.Pointer.Capture(this);
        Focus();
        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>
    /// Handles the pointer moved event raised by the UI or runtime.
    /// </summary>
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_drawing || CanvasData is null) return;
        var point = e.GetCurrentPoint(this);
        var position = ToCanvas(point.Position);
        if (_activePoints.Count > 0)
        {
            var previous = _activePoints[^1];
            var deltaX = previous.X - position.X;
            var deltaY = previous.Y - position.Y;
            if (deltaX * deltaX + deltaY * deltaY < 0.35) return;
        }
        _activePoints.Add(ToInkPoint(point, position));
        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>
    /// Handles the pointer released event raised by the UI or runtime.
    /// </summary>
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_drawing) return;
        e.Pointer.Capture(null);
        CompleteStroke();
        e.Handled = true;
    }

    /// <summary>
    /// Handles the pointer capture lost event raised by the UI or runtime.
    /// </summary>
    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => CompleteStroke();

    /// <summary>
    /// Performs the complete stroke step owned by this component.
    /// </summary>
    private void CompleteStroke()
    {
        if (!_drawing) return;
        _drawing = false;
        if (_activePoints.Count > 0)
        {
            var stroke = new NotesInkStroke
            {
                Tool = Tool,
                Colour = Colour,
                BaseWidth = StrokeWidth,
                Opacity = Opacity,
                IsGhost = ActiveGhostLayerId is not null,
                GhostLayerId = ActiveGhostLayerId,
                Points = _activePoints.ToList()
            };
            StrokeCompleted?.Invoke(this, stroke);
        }
        _activePoints.Clear();
        InvalidateVisual();
    }

    /// <summary>
    /// Handles the pointer wheel changed event raised by the UI or runtime.
    /// </summary>
    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (CanvasData is null) return;
        CanvasData.Zoom = Math.Clamp(CanvasData.Zoom * (e.Delta.Y > 0 ? 1.1 : 0.9), 0.05, 100);
        ViewChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>
    /// Performs the to canvas step owned by this component.
    /// </summary>
    private Point ToCanvas(Point position)
    {
        var canvas = CanvasData!;
        var scale = Math.Max(canvas.Zoom, 0.05);
        return new Point((position.X - canvas.OffsetX) / scale, (position.Y - canvas.OffsetY) / scale);
    }

    /// <summary>
    /// Performs the to ink point step owned by this component.
    /// </summary>
    private NotesInkPoint ToInkPoint(PointerPoint pointer, Point point)
    {
        var properties = pointer.Properties;
        return new NotesInkPoint
        {
            X = point.X,
            Y = point.Y,
            Pressure = Math.Clamp(ReadNumber(properties, "Pressure", 0.5), 0, 1),
            TiltX = Math.Clamp(ReadNumber(properties, "XTilt", 0), -90, 90),
            TiltY = Math.Clamp(ReadNumber(properties, "YTilt", 0), -90, 90),
            TimestampMilliseconds = Environment.TickCount64 - _strokeStart
        };
    }

    /// <summary>
    /// Performs the find stroke step owned by this component.
    /// </summary>
    private NotesInkStroke? FindStroke(Point point, double radius)
    {
        var canvas = CanvasData!;
        var radiusSquared = radius * radius;
        return canvas.Strokes.LastOrDefault(stroke => stroke.Points.Any(candidate =>
        {
            var deltaX = candidate.X - point.X;
            var deltaY = candidate.Y - point.Y;
            return deltaX * deltaX + deltaY * deltaY <= radiusSquared;
        }));
    }

    /// <summary>
    /// Performs the draw grid step owned by this component.
    /// </summary>
    private static void DrawGrid(DrawingContext context, NotesCanvasData canvas)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)), 1 / Math.Max(canvas.Zoom, 0.05));
        const int step = 40;
        var width = canvas.Infinite ? 10_000 : canvas.Width;
        var height = canvas.Infinite ? 10_000 : canvas.Height;
        for (var x = 0; x <= width; x += step) context.DrawLine(pen, new Point(x, 0), new Point(x, height));
        for (var y = 0; y <= height; y += step) context.DrawLine(pen, new Point(0, y), new Point(width, y));
    }

    /// <summary>
    /// Performs the draw object step owned by this component.
    /// </summary>
    private static void DrawObject(DrawingContext context, NotesCanvasObject value)
    {
        var fill = new SolidColorBrush(value.Locked ? Color.FromArgb(70, 160, 160, 160) : Color.FromArgb(70, 47, 128, 237));
        var pen = new Pen(new SolidColorBrush(value.Locked ? Color.FromArgb(180, 170, 170, 170) : Color.FromArgb(220, 47, 128, 237)), 1.5);
        context.DrawRectangle(fill, pen, new Rect(value.X, value.Y, value.Width, value.Height), 7, 7);
        if (value.FromObjectId is not null && value.ToObjectId is not null)
            context.DrawLine(pen, new Point(value.X, value.Y), new Point(value.X + value.Width, value.Y + value.Height));
    }

    /// <summary>
    /// Performs the draw stroke step owned by this component.
    /// </summary>
    private static void DrawStroke(DrawingContext context, NotesCanvasData canvas, NotesInkStroke stroke, bool ignoreGhostVisibility = false)
    {
        if (!ignoreGhostVisibility && stroke.IsGhost && stroke.GhostLayerId is { } layerId)
        {
            var layer = canvas.GhostLayers.FirstOrDefault(item => item.Id == layerId);
            if (layer is not null && !layer.IsRevealed) return;
        }
        if (stroke.Points.Count == 0) return;
        var colour = Color.Parse(stroke.Colour);
        colour = Color.FromArgb((byte)Math.Clamp(stroke.Opacity * 255, 0, 255), colour.R, colour.G, colour.B);
        var brush = new SolidColorBrush(colour);
        for (var index = 1; index < stroke.Points.Count; index++)
        {
            var first = stroke.Points[index - 1];
            var second = stroke.Points[index];
            var pressure = Math.Max(0.08, (first.Pressure + second.Pressure) / 2);
            var width = stroke.BaseWidth * (0.35 + pressure * 0.9);
            context.DrawLine(new Pen(brush, width, lineCap: PenLineCap.Round), new Point(first.X, first.Y), new Point(second.X, second.Y));
        }
        if (stroke.Points.Count == 1)
        {
            var point = stroke.Points[0];
            context.DrawEllipse(brush, null, new Point(point.X, point.Y), stroke.BaseWidth / 2, stroke.BaseWidth / 2);
        }
    }

    /// <summary>
    /// Performs the read number step owned by this component.
    /// </summary>
    private static double ReadNumber(object properties, string name, double fallback)
    {
        try
        {
            var value = properties.GetType().GetProperty(name)?.GetValue(properties);
            return value is null ? fallback : Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or System.Reflection.TargetInvocationException)
        {
            return fallback;
        }
    }
}
