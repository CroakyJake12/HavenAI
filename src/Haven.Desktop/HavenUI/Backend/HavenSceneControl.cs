using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.HavenUI.Backend;

/// <summary>Single Avalonia rendering/input surface for an entire Haven-owned scene.</summary>
public sealed class HavenSceneControl : Control, IHavenMeasureContext
{
    private readonly HavenLayoutEngine _layout = new();
    private readonly HavenSceneRenderer _renderer = new();
    private readonly HavenAnimationEngine _animations = new();
    private readonly HavenResourceSet _resources = HavenResourceSet.LoadEmbedded();
    private readonly Dictionary<HavenElement, (string? Transition, string? Animation)> _motionTokens = [];
    private readonly HashSet<HavenElement> _subscriptions = [];
    private readonly DispatcherTimer _animationTimer;
    private HavenElement? _root;
    private HavenInputRouter? _input;

    public HavenSceneControl()
    {
        Focusable = true;
        ClipToBounds = true;
        _animationTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => TickAnimations());
        _animationTimer.Stop();
        DetachedFromVisualTree += (_, _) => _animationTimer.Stop();
    }

    public HavenElement? Root
    {
        get => _root;
        set
        {
            if (ReferenceEquals(_root, value)) return;
            ClearSubscriptions();
            _root = value;
            _input = value is null ? null : new HavenInputRouter(value);
            _motionTokens.Clear();
            if (_root is not null)
            {
                _resources.ApplyClasses(_root);
                RefreshSubscriptions();
                CaptureMotionTokens(_root, false);
            }
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_root is null) return default;
        var width = double.IsInfinity(availableSize.Width) ? Math.Max(0, Bounds.Width) : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? Math.Max(0, Bounds.Height) : availableSize.Height;
        _layout.Layout(_root, new HavenSize(width, height), HavenPlatform.Windows, this);
        return new Size(_root.DesiredSize.Width, _root.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_root is not null) _layout.Layout(_root, new HavenSize(finalSize.Width, finalSize.Height), HavenPlatform.Windows, this);
        return finalSize;
    }

    public HavenSize MeasureLeaf(HavenElement element, HavenSize available)
    {
        var text = element switch
        {
            Text value => value.Content,
            Haven.UI.Components.Button value => value.Content,
            Input value => string.IsNullOrEmpty(value.Text) ? value.Placeholder : value.Text,
            Select value => value.SelectedItem ?? "Select",
            _ => string.Empty
        };
        if (string.IsNullOrEmpty(text)) return new HavenSize(Math.Min(available.Width, 48), Math.Min(available.Height, 48));
        var formatted = CreateText(text, element, available.Width);
        return new HavenSize(Math.Min(available.Width, formatted.Width + 2), Math.Min(available.Height, formatted.Height + 2));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_root is null) return;
        var drawingScopes = new Stack<IDisposable>();
        try
        {
            foreach (var command in _renderer.Render(_root))
            {
                if (command is HavenPushTransformCommand push)
                {
                    var transform = Matrix.CreateTranslation(-push.Origin.X, -push.Origin.Y)
                                    * Matrix.CreateScale(push.Transform.ScaleX, push.Transform.ScaleY)
                                    * Matrix.CreateRotation(push.Transform.RotationDegrees * Math.PI / 180d)
                                    * Matrix.CreateTranslation(push.Origin.X + push.Transform.TranslateX, push.Origin.Y + push.Transform.TranslateY);
                    drawingScopes.Push(context.PushTransform(transform));
                    continue;
                }
                if (command is HavenPushClipCommand clip)
                {
                    drawingScopes.Push(context.PushClip(Rect(clip.Rect)));
                    continue;
                }
                if (command is HavenPopTransformCommand or HavenPopClipCommand)
                {
                    if (drawingScopes.Count > 0) drawingScopes.Pop().Dispose();
                    continue;
                }
                Draw(context, command);
            }
        }
        finally
        {
            while (drawingScopes.Count > 0) drawingScopes.Pop().Dispose();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); var p = e.GetPosition(this); _input?.PointerMoved(new HavenPoint(p.X, p.Y)); InvalidateVisual(); }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); var p = e.GetPosition(this); _input?.PointerPressed(new HavenPoint(p.X, p.Y), e.Pointer.Type == PointerType.Touch ? HavenPointerKind.Touch : e.Pointer.Type == PointerType.Pen ? HavenPointerKind.Pen : HavenPointerKind.Mouse); Focus(); e.Pointer.Capture(this); e.Handled = true; InvalidateVisual(); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); var p = e.GetPosition(this); _input?.PointerReleased(new HavenPoint(p.X, p.Y)); e.Pointer.Capture(null); e.Handled = true; InvalidateVisual(); }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e) { base.OnPointerWheelChanged(e); var p = e.GetPosition(this); if (_input?.Scroll(new HavenPoint(p.X, p.Y), -e.Delta.X * 48d, -e.Delta.Y * 48d) == true) { e.Handled = true; InvalidateMeasure(); InvalidateVisual(); } }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (_input?.KeyDown(MapKey(e.Key)) == true) { e.Handled = true; InvalidateVisual(); } }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (_input?.KeyUp(MapKey(e.Key)) == true) { e.Handled = true; InvalidateVisual(); } }

    private void Draw(DrawingContext context, HavenDrawCommand command)
    {
        if (command.Opacity < .9999d)
        {
            using var opacity = context.PushOpacity(Math.Clamp(command.Opacity, 0d, 1d));
            DrawCore(context, command);
            return;
        }
        DrawCore(context, command);
    }

    private void DrawCore(DrawingContext context, HavenDrawCommand command)
    {
        switch (command)
        {
            case HavenFillRoundedRectCommand fill: context.DrawRectangle(Resolve(fill.Brush), null, Rect(fill.Rect), fill.Radius, fill.Radius, default); break;
            case HavenStrokeRoundedRectCommand stroke: context.DrawRectangle(null, new Pen(Resolve(stroke.Pen.Brush), stroke.Pen.Thickness), Rect(stroke.Rect), stroke.Radius, stroke.Radius, default); break;
            case HavenTextCommand text: context.DrawText(CreateText(text.Layout.Text, text.Layout.FontFamily, text.Layout.FontSize, text.Layout.FontWeight, text.Layout.MaxWidth, Resolve(text.Brush)), new Point(text.Rect.X, text.Rect.Y)); break;
            case HavenLineCommand line: context.DrawLine(new Pen(Resolve(line.Pen.Brush), line.Pen.Thickness), new Point(line.Start.X, line.Start.Y), new Point(line.End.X, line.End.Y)); break;
            case HavenEllipseCommand ellipse: context.DrawEllipse(Resolve(ellipse.Brush), ellipse.Pen is null ? null : new Pen(Resolve(ellipse.Pen.Brush), ellipse.Pen.Thickness), new Point(ellipse.Rect.X + ellipse.Rect.Width / 2, ellipse.Rect.Y + ellipse.Rect.Height / 2), ellipse.Rect.Width / 2, ellipse.Rect.Height / 2); break;
            case HavenGlowCommand glow:
                var grow = Math.Max(2, glow.Glow.Blur / 3);
                var rect = new Avalonia.Rect(glow.Rect.X - grow, glow.Rect.Y - grow, glow.Rect.Width + grow * 2, glow.Rect.Height + grow * 2);
                context.DrawRectangle(Resolve(glow.Glow.Brush), null, rect, glow.Radius + grow, glow.Radius + grow, default);
                break;
        }
    }

    private static IBrush Resolve(HavenBrush brush) => brush switch
    {
        HavenTokenBrush token => HavenAvaloniaThemeResolver.Resolve(token.Token),
        HavenSolidBrush solid => new SolidColorBrush(Color.FromArgb(solid.A, solid.R, solid.G, solid.B)),
        _ => throw new InvalidOperationException($"Unsupported Haven brush '{brush.GetType().Name}'.")
    };

    private static Avalonia.Rect Rect(HavenRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
    private static FormattedText CreateText(string text, HavenElement element, double maxWidth) => CreateText(text, element.GetValue(HavenProperties.FontFamily), element.GetValue(HavenProperties.FontSize), element.GetValue(HavenProperties.FontWeight), maxWidth, HavenAvaloniaThemeResolver.Resolve(element.GetValue(HavenProperties.Foreground)));
    private static FormattedText CreateText(string text, string family, double size, int weight, double maxWidth, IBrush foreground)
    {
        var fontFamily = family.Equals("Montserrat", StringComparison.OrdinalIgnoreCase) ? new FontFamily("avares://Haven/Assets/Fonts/MontserratStatic#Montserrat") : new FontFamily(family);
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(fontFamily, FontStyle.Normal, Weight(weight), FontStretch.Normal), size, foreground) { MaxTextWidth = Math.Max(1, maxWidth) };
    }
    private static FontWeight Weight(int weight) => weight switch { >= 800 => FontWeight.ExtraBold, >= 700 => FontWeight.Bold, >= 600 => FontWeight.SemiBold, >= 500 => FontWeight.Medium, _ => FontWeight.Normal };
    private static HavenKey MapKey(Key key) => key switch { Key.Enter => HavenKey.Enter, Key.Space => HavenKey.Space, Key.Escape => HavenKey.Escape, Key.Tab => HavenKey.Tab, Key.Left => HavenKey.Left, Key.Right => HavenKey.Right, Key.Up => HavenKey.Up, Key.Down => HavenKey.Down, Key.Home => HavenKey.Home, Key.End => HavenKey.End, _ => HavenKey.Unknown };
    private void RefreshSubscriptions()
    {
        if (_root is null) { ClearSubscriptions(); return; }
        var current = _root.DescendantsAndSelf().ToHashSet();
        foreach (var removed in _subscriptions.Where(element => !current.Contains(element)).ToArray())
        {
            removed.Invalidated -= OnSceneInvalidated;
            _subscriptions.Remove(removed);
        }
        foreach (var added in current.Where(element => !_subscriptions.Contains(element)))
        {
            added.Invalidated += OnSceneInvalidated;
            _subscriptions.Add(added);
        }
    }

    private void ClearSubscriptions()
    {
        foreach (var element in _subscriptions) element.Invalidated -= OnSceneInvalidated;
        _subscriptions.Clear();
    }

    private void OnSceneInvalidated(object? sender, EventArgs e)
    {
        if (_root is not null)
        {
            _resources.ApplyClasses(_root);
            RefreshSubscriptions();
            CaptureMotionTokens(_root, true);
        }
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void CaptureMotionTokens(HavenElement root, bool start)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var element in root.DescendantsAndSelf())
        {
            var next = (Transition: element.GetValue(HavenProperties.Transition), Animation: element.GetValue(HavenProperties.Animation));
            _motionTokens.TryGetValue(element, out var previous);
            _motionTokens[element] = next;
            if (!start) continue;
            var name = next.Animation != previous.Animation && !string.IsNullOrWhiteSpace(next.Animation)
                ? next.Animation
                : next.Transition != previous.Transition && !string.IsNullOrWhiteSpace(next.Transition)
                    ? next.Transition
                    : null;
            if (!_resources.TryResolveAnimation(name, out var definition) || definition is null) continue;
            _animations.Start(element, definition, now);
            if (!_animationTimer.IsEnabled) _animationTimer.Start();
        }
    }

    private void TickAnimations()
    {
        var active = _animations.Tick(DateTimeOffset.UtcNow);
        InvalidateVisual();
        if (!active) _animationTimer.Stop();
    }
}
