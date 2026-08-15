using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.HavenUI.Backend;

/// <summary>Single Avalonia rendering/input surface for an entire Haven-owned scene.</summary>
public sealed class HavenSceneControl : Panel, IHavenMeasureContext
{
    private readonly HavenLayoutEngine _layout = new();
    private readonly HavenSceneRenderer _renderer = new();
    private readonly HavenAnimationEngine _animations = new();
    private readonly HavenResourceSet _resources = HavenResourceSet.LoadEmbedded();
    private readonly Dictionary<HavenElement, (string? Transition, string? Animation)> _motionTokens = [];
    private readonly Dictionary<HavenElement, HavenAnimationSnapshot> _motionSnapshots = [];
    private readonly HashSet<HavenElement> _subscriptions = [];
    private readonly IHavenAvaloniaImageResolver _images;
    private readonly IHavenAvaloniaNativeControlResolver _nativeControlResolver;
    private readonly Dictionary<HavenElement, Control> _nativeControls = [];
    private readonly HavenDrawingSurface _drawingSurface;
    private readonly Func<bool> _reduceMotion;
    private readonly TimeProvider _timeProvider;
    private readonly DispatcherTimer _animationTimer;
    private HavenElement? _root;
    private HavenInputRouter? _input;
    private bool _processingMotion;
    private TopLevel? _topLevel;

    public HavenSceneControl() : this(new HavenAvaloniaImageResolver(), new HavenAvaloniaNativeControlResolver()) { }

    public HavenSceneControl(IHavenAvaloniaImageResolver images) : this(images, new HavenAvaloniaNativeControlResolver()) { }

    public HavenSceneControl(
        IHavenAvaloniaImageResolver images,
        IHavenAvaloniaNativeControlResolver nativeControlResolver,
        Func<bool>? reduceMotion = null,
        TimeProvider? timeProvider = null)
    {
        _images = images ?? throw new ArgumentNullException(nameof(images));
        _nativeControlResolver = nativeControlResolver ?? throw new ArgumentNullException(nameof(nativeControlResolver));
        _reduceMotion = reduceMotion ?? (() => Haven.Desktop.Services.MotionPreferencesService.Current.ReduceAnimations);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _drawingSurface = new HavenDrawingSurface(this);
        Children.Add(_drawingSurface);
        Focusable = true;
        ClipToBounds = true;
        _animationTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => AdvanceAnimationFrame());
        _animationTimer.Stop();
        AttachedToVisualTree += (_, _) => AttachTopLevelPointerObserver();
        DetachedFromVisualTree += (_, _) =>
        {
            DetachTopLevelPointerObserver();
            _animationTimer.Stop();
        };
    }

    public HavenPlatform Platform { get; set; } = OperatingSystem.IsAndroid() ? HavenPlatform.Android : HavenPlatform.Windows;
    public HavenRenderSurfaceMetrics SurfaceMetrics { get; private set; } = new(HavenSize.Zero, 1d, HavenPlatform.Unknown);
    public bool HasActiveAnimations => _animations.HasActiveAnimations;
    public event Action<Input>? InputSubmitted;
    public event Action? PointerPressedOutside;

    public bool FocusElement(HavenElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (_root is null || !_root.DescendantsAndSelf().Contains(element) || !element.Accessibility.Focusable) return false;
        _input?.Focus(element);
        return Focus();
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
            if (_input is not null) _input.InputSubmitted += input => InputSubmitted?.Invoke(input);
            _motionTokens.Clear();
            _motionSnapshots.Clear();
            _animations.StopAll();
            RefreshNativeControls();
            if (_root is not null)
            {
                _resources.ApplyClasses(_root);
                RefreshSubscriptions();
                CaptureMotionState(_root, true);
            }
            InvalidateMeasure();
            InvalidateScene();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_root is null) return default;
        var width = double.IsInfinity(availableSize.Width) ? Math.Max(0, Bounds.Width) : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? Math.Max(0, Bounds.Height) : availableSize.Height;
        UpdateSurfaceMetrics(width, height);
        _layout.Layout(_root, SurfaceMetrics.Viewport, Platform, this);
        return new Size(_root.DesiredSize.Width, _root.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        UpdateSurfaceMetrics(finalSize.Width, finalSize.Height);
        if (_root is not null) _layout.Layout(_root, SurfaceMetrics.Viewport, Platform, this);
        _drawingSurface.Arrange(new Rect(finalSize));
        ArrangeNativeControls();
        return finalSize;
    }

    public HavenSize MeasureLeaf(HavenElement element, HavenSize available)
    {
        if (_nativeControls.TryGetValue(element, out var nativeControl))
        {
            nativeControl.Measure(new Size(available.Width, available.Height));
            return new HavenSize(
                Math.Min(available.Width, nativeControl.DesiredSize.Width),
                Math.Min(available.Height, nativeControl.DesiredSize.Height));
        }
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

    private void RenderScene(DrawingContext context)
    {
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

    private void AttachTopLevelPointerObserver()
    {
        DetachTopLevelPointerObserver();
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(PointerPressedEvent, OnTopLevelPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void DetachTopLevelPointerObserver()
    {
        if (_topLevel is null) return;
        _topLevel.RemoveHandler(PointerPressedEvent, OnTopLevelPointerPressed);
        _topLevel = null;
    }

    private void OnTopLevelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual visual && (ReferenceEquals(visual, this) || visual.GetVisualAncestors().Contains(this))) return;
        NotifyPointerPressedOutside();
    }

    internal void NotifyPointerPressedOutside() => PointerPressedOutside?.Invoke();

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (IsNativeControlSource(e.Source)) return;
        var p = e.GetPosition(this);
        _input?.PointerMoved(new HavenPoint(p.X, p.Y));
        InvalidateScene();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (IsNativeControlSource(e.Source)) return;
        var p = e.GetPosition(this);
        _input?.PointerPressed(
            new HavenPoint(p.X, p.Y),
            e.Pointer.Type == PointerType.Touch ? HavenPointerKind.Touch : e.Pointer.Type == PointerType.Pen ? HavenPointerKind.Pen : HavenPointerKind.Mouse);
        Focus();
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateScene();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (IsNativeControlSource(e.Source)) return;
        var p = e.GetPosition(this);
        _input?.PointerReleased(new HavenPoint(p.X, p.Y));
        e.Pointer.Capture(null);
        e.Handled = true;
        InvalidateScene();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (IsNativeControlSource(e.Source)) return;
        var p = e.GetPosition(this);
        if (_input?.Scroll(new HavenPoint(p.X, p.Y), -e.Delta.X * 48d, -e.Delta.Y * 48d) == true)
        {
            e.Handled = true;
            InvalidateMeasure();
            InvalidateScene();
        }
    }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (_input?.KeyDown(MapKey(e.Key)) == true) { e.Handled = true; InvalidateScene(); } }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (_input?.KeyUp(MapKey(e.Key)) == true) { e.Handled = true; InvalidateScene(); } }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (_input?.TextInput(e.Text) != true) return;
        e.Handled = true;
        InvalidateMeasure();
        InvalidateScene();
    }

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
            case HavenTextCommand text:
            {
                var formatted = CreateText(text.Layout.Text, text.Layout.FontFamily, text.Layout.FontSize, text.Layout.FontWeight, text.Layout.MaxWidth, Resolve(text.Brush));
                var y = text.Layout.CenterVertically
                    ? text.Rect.Y + Math.Max(0, (text.Rect.Height - formatted.Height) / 2d)
                    : text.Rect.Y;
                context.DrawText(formatted, new Point(text.Rect.X, y));
                break;
            }
            case HavenCaretCommand caret:
            {
                if (caret.Rect.Width <= 0 || caret.Rect.Height <= 0) break;
                var brush = Resolve(caret.Brush);
                var prefix = CreateText(caret.PrefixLayout.Text, caret.PrefixLayout.FontFamily, caret.PrefixLayout.FontSize, caret.PrefixLayout.FontWeight, caret.Rect.Width, brush);
                var sample = CreateText("Mg", caret.PrefixLayout.FontFamily, caret.PrefixLayout.FontSize, caret.PrefixLayout.FontWeight, caret.Rect.Width, brush);
                var x = Math.Min(caret.Rect.Right - 1, caret.Rect.X + prefix.Width);
                var height = Math.Min(caret.Rect.Height, Math.Max(caret.PrefixLayout.FontSize, sample.Height));
                var y = caret.Rect.Y + Math.Max(0, (caret.Rect.Height - height) / 2d);
                context.DrawLine(new Pen(brush, 1.5d), new Point(x, y), new Point(x, y + height));
                break;
            }
            case HavenLineCommand line: context.DrawLine(new Pen(Resolve(line.Pen.Brush), line.Pen.Thickness), new Point(line.Start.X, line.Start.Y), new Point(line.End.X, line.End.Y)); break;
            case HavenEllipseCommand ellipse: context.DrawEllipse(Resolve(ellipse.Brush), ellipse.Pen is null ? null : new Pen(Resolve(ellipse.Pen.Brush), ellipse.Pen.Thickness), new Point(ellipse.Rect.X + ellipse.Rect.Width / 2, ellipse.Rect.Y + ellipse.Rect.Height / 2), ellipse.Rect.Width / 2, ellipse.Rect.Height / 2); break;
            case HavenGeometryCommand geometry: context.DrawGeometry(geometry.Fill is null ? null : Resolve(geometry.Fill), geometry.Stroke is null ? null : new Pen(Resolve(geometry.Stroke.Brush), geometry.Stroke.Thickness), CreateGeometry(geometry.Geometry, geometry.Rect)); break;
            case HavenIconCommand icon: context.DrawGeometry(null, new Pen(Resolve(icon.Brush), Math.Max(1.5d, Math.Min(icon.Rect.Width, icon.Rect.Height) / 12d)), CreateGeometry(HavenIconCatalog.Resolve(icon.Key), icon.Rect, true)); break;
            case HavenImageCommand image: DrawImage(context, image); break;
            case HavenShadowCommand shadow: DrawEffect(context, shadow.Rect, shadow.Radius, shadow.Shadow.Brush, shadow.Shadow.OffsetX, shadow.Shadow.OffsetY, shadow.Shadow.Blur, shadow.Shadow.Spread); break;
            case HavenGlowCommand glow:
                DrawEffect(context, glow.Rect, glow.Radius, glow.Glow.Brush, 0, 0, glow.Glow.Blur, 0);
                break;
        }
    }

    private void DrawImage(DrawingContext context, HavenImageCommand command)
    {
        var target = Rect(command.Rect);
        if (!_images.TryResolve(command.Image.Source, out var image) || image is null)
        {
            var border = new Pen(HavenAvaloniaThemeResolver.Resolve("Border"), 1.5d);
            context.DrawRectangle(HavenAvaloniaThemeResolver.Resolve("SurfaceRaised"), border, target, 8, 8, default);
            context.DrawLine(border, target.TopLeft + new Vector(8, 8), target.BottomRight - new Vector(8, 8));
            context.DrawLine(border, target.TopRight + new Vector(-8, 8), target.BottomLeft + new Vector(8, -8));
            return;
        }

        var destination = ImageDestination(target, image.Size, command.Layout);
        if (command.Layout is HavenImageLayout.Cover or HavenImageLayout.None)
        {
            using var clip = context.PushClip(target);
            context.DrawImage(image, destination);
            return;
        }
        context.DrawImage(image, destination);
    }

    private static Avalonia.Rect ImageDestination(Avalonia.Rect target, Size source, HavenImageLayout layout)
    {
        if (layout == HavenImageLayout.Fill || source.Width <= 0 || source.Height <= 0) return target;
        if (layout == HavenImageLayout.None)
            return new Avalonia.Rect(target.X + (target.Width - source.Width) / 2d, target.Y + (target.Height - source.Height) / 2d, source.Width, source.Height);
        var scale = layout == HavenImageLayout.Cover ? Math.Max(target.Width / source.Width, target.Height / source.Height) : Math.Min(target.Width / source.Width, target.Height / source.Height);
        var width = source.Width * scale;
        var height = source.Height * scale;
        return new Avalonia.Rect(target.X + (target.Width - width) / 2d, target.Y + (target.Height - height) / 2d, width, height);
    }

    private static void DrawEffect(DrawingContext context, HavenRect bounds, double radius, HavenBrush brush, double offsetX, double offsetY, double blur, double spread)
    {
        var resolved = Resolve(brush);
        var color = HavenAvaloniaThemeResolver.EffectColor(resolved, "Haven shadow/glow brush");
        var shadows = new BoxShadows(new BoxShadow { OffsetX = offsetX, OffsetY = offsetY, Blur = Math.Max(0, blur), Spread = spread, Color = color });
        context.DrawRectangle(Brushes.Transparent, null, Rect(bounds), radius, radius, shadows);
    }

    private static StreamGeometry CreateGeometry(HavenGeometry source, HavenRect target, bool preserveAspect = false)
    {
        var geometry = new StreamGeometry();
        using var writer = geometry.Open();
        writer.SetFillRule(source.Path.FillRule == HavenFillRule.NonZero ? FillRule.NonZero : FillRule.EvenOdd);
        foreach (var figure in source.Path.Figures)
        {
            writer.BeginFigure(MapPoint(figure.Start, source.ViewBox, target, preserveAspect), true);
            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case HavenLineSegment line: writer.LineTo(MapPoint(line.End, source.ViewBox, target, preserveAspect), true); break;
                    case HavenQuadraticBezierSegment quadratic: writer.QuadraticBezierTo(MapPoint(quadratic.Control, source.ViewBox, target, preserveAspect), MapPoint(quadratic.End, source.ViewBox, target, preserveAspect), true); break;
                    case HavenCubicBezierSegment cubic: writer.CubicBezierTo(MapPoint(cubic.Control1, source.ViewBox, target, preserveAspect), MapPoint(cubic.Control2, source.ViewBox, target, preserveAspect), MapPoint(cubic.End, source.ViewBox, target, preserveAspect), true); break;
                    case HavenArcSegment arc: writer.ArcTo(MapPoint(arc.End, source.ViewBox, target, preserveAspect), MapSize(arc.Radius, source.ViewBox, target, preserveAspect), arc.RotationDegrees, arc.IsLargeArc, arc.SweepDirection == HavenSweepDirection.Clockwise ? SweepDirection.Clockwise : SweepDirection.CounterClockwise, true); break;
                }
            }
            writer.EndFigure(figure.Closed);
        }
        return geometry;
    }

    private static Point MapPoint(HavenPoint point, HavenRect? viewBox, HavenRect target, bool preserveAspect)
    {
        if (viewBox is not { Width: > 0, Height: > 0 } source) return new Point(point.X, point.Y);
        var scaleX = target.Width / source.Width;
        var scaleY = target.Height / source.Height;
        if (preserveAspect) scaleX = scaleY = Math.Min(scaleX, scaleY);
        var contentWidth = source.Width * scaleX;
        var contentHeight = source.Height * scaleY;
        return new Point(target.X + (target.Width - contentWidth) / 2d + (point.X - source.X) * scaleX, target.Y + (target.Height - contentHeight) / 2d + (point.Y - source.Y) * scaleY);
    }

    private static Size MapSize(HavenSize size, HavenRect? viewBox, HavenRect target, bool preserveAspect)
    {
        if (viewBox is not { Width: > 0, Height: > 0 } source) return new Size(size.Width, size.Height);
        var scaleX = target.Width / source.Width;
        var scaleY = target.Height / source.Height;
        if (preserveAspect) scaleX = scaleY = Math.Min(scaleX, scaleY);
        return new Size(size.Width * scaleX, size.Height * scaleY);
    }

    private static IBrush Resolve(HavenBrush brush) => brush switch
    {
        HavenTokenBrush token => HavenAvaloniaThemeResolver.Resolve(token.Token),
        HavenSolidBrush solid => new SolidColorBrush(Color.FromArgb(solid.A, solid.R, solid.G, solid.B)),
        _ => throw new InvalidOperationException($"Unsupported Haven brush '{brush.GetType().Name}'.")
    };

    private static Avalonia.Rect Rect(HavenRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
    private void UpdateSurfaceMetrics(double width, double height) => SurfaceMetrics = new(new HavenSize(Math.Max(0, width), Math.Max(0, height)), Math.Max(.01d, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d), Platform);
    private static FormattedText CreateText(string text, HavenElement element, double maxWidth) => CreateText(text, element.GetValue(HavenProperties.FontFamily), element.GetValue(HavenProperties.FontSize), element.GetValue(HavenProperties.FontWeight), maxWidth, HavenAvaloniaThemeResolver.Resolve(element.GetValue(HavenProperties.Foreground)));
    private static FormattedText CreateText(string text, string family, double size, int weight, double maxWidth, IBrush foreground)
    {
        var fontFamily = family.Equals("Montserrat", StringComparison.OrdinalIgnoreCase) ? new FontFamily("avares://Haven/Assets/Fonts/MontserratStatic#Montserrat") : new FontFamily(family);
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(fontFamily, FontStyle.Normal, Weight(weight), FontStretch.Normal), size, foreground) { MaxTextWidth = Math.Max(1, maxWidth) };
    }
    private static FontWeight Weight(int weight) => weight switch { >= 800 => FontWeight.ExtraBold, >= 700 => FontWeight.Bold, >= 600 => FontWeight.SemiBold, >= 500 => FontWeight.Medium, _ => FontWeight.Normal };
    private static HavenKey MapKey(Key key) => key switch { Key.Enter => HavenKey.Enter, Key.Space => HavenKey.Space, Key.Escape => HavenKey.Escape, Key.Tab => HavenKey.Tab, Key.Left => HavenKey.Left, Key.Right => HavenKey.Right, Key.Up => HavenKey.Up, Key.Down => HavenKey.Down, Key.Home => HavenKey.Home, Key.End => HavenKey.End, Key.Back => HavenKey.Backspace, Key.Delete => HavenKey.Delete, _ => HavenKey.Unknown };
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
        if (_processingMotion)
        {
            InvalidateMeasure();
            InvalidateScene();
            return;
        }
        if (_root is not null)
        {
            _resources.ApplyClasses(_root);
            RefreshSubscriptions();
            RefreshNativeControls();
            CaptureMotionState(_root, false);
        }
        InvalidateMeasure();
        InvalidateScene();
    }

    private void RefreshNativeControls()
    {
        var current = _root?.DescendantsAndSelf().Where(IsNativeElement).ToHashSet() ?? [];
        foreach (var removed in _nativeControls.Keys.Where(element => !current.Contains(element)).ToArray())
        {
            Children.Remove(_nativeControls[removed]);
            _nativeControls.Remove(removed);
        }
        foreach (var element in current.Where(element => !_nativeControls.ContainsKey(element)))
        {
            if (!_nativeControlResolver.TryCreate(element, out var control) || control is null) continue;
            _nativeControls.Add(element, control);
            Children.Add(control);
        }
    }

    private void ArrangeNativeControls()
    {
        foreach (var (element, control) in _nativeControls)
        {
            control.IsVisible = element.IsIncluded
                && element.GetValue(HavenProperties.Visibility) == HavenVisibility.Visible;
            if (control.IsVisible) control.Arrange(Rect(element.Bounds));
        }
    }

    private bool IsNativeControlSource(object? source)
    {
        if (source is not Visual visual) return false;
        return _nativeControls.Values.Any(control => ReferenceEquals(visual, control) || visual.GetVisualAncestors().Contains(control));
    }

    private static bool IsNativeElement(HavenElement element) => element is Video or Web;

    private void InvalidateScene()
    {
        _drawingSurface.InvalidateVisual();
        InvalidateVisual();
    }

    private void CaptureMotionState(HavenElement root, bool initial)
    {
        var now = _timeProvider.GetUtcNow();
        _animations.MotionPolicy = new HavenMotionPolicy(_reduceMotion());
        var currentElements = root.DescendantsAndSelf().ToHashSet();
        foreach (var removed in _motionTokens.Keys.Where(element => !currentElements.Contains(element)).ToArray())
        {
            _animations.Stop(removed);
            _motionTokens.Remove(removed);
            _motionSnapshots.Remove(removed);
        }

        foreach (var element in root.DescendantsAndSelf())
        {
            var next = (Transition: element.GetValue(HavenProperties.Transition), Animation: element.GetValue(HavenProperties.Animation));
            var known = _motionTokens.TryGetValue(element, out var previous);
            _motionTokens[element] = next;
            var startedKeyframes = false;

            if (!string.IsNullOrWhiteSpace(next.Animation) && (!known || !string.Equals(next.Animation, previous.Animation, StringComparison.Ordinal)))
            {
                if (!_resources.TryResolveAnimation(next.Animation, out var animation) || animation is null)
                    throw new KeyNotFoundException($"Animation '{next.Animation}' was not found in UserAnimations.hui or SystemAnimations.hui.");
                ProcessMotion(() => _animations.Start(element, animation, now));
                startedKeyframes = true;
            }

            if (string.IsNullOrWhiteSpace(next.Transition))
            {
                _motionSnapshots.Remove(element);
                continue;
            }
            if (!_resources.TryResolveTransition(next.Transition, out var transition) || transition is null)
                throw new KeyNotFoundException($"Transition '{next.Transition}' was not found in UserAnimations.hui or SystemAnimations.hui.");

            var target = _animations.Capture(element, transition.Properties, includeAnimationValues: false);
            if (!initial && !startedKeyframes && _motionSnapshots.TryGetValue(element, out var previousTarget))
            {
                var fromValues = new Dictionary<HavenProperty, object?>();
                foreach (var property in target.Values.Keys)
                {
                    fromValues[property] = _animations.HasActiveAnimation(element)
                        ? element.GetValue(property)
                        : previousTarget.Values.GetValueOrDefault(property, element.GetValue(property));
                }
                ProcessMotion(() => _animations.StartTransition(element, transition, new HavenAnimationSnapshot(fromValues), target, now));
            }
            _motionSnapshots[element] = target;
        }

        if (_animations.HasActiveAnimations && !_animationTimer.IsEnabled) _animationTimer.Start();
    }

    public bool AdvanceAnimationFrame()
    {
        _animations.MotionPolicy = new HavenMotionPolicy(_reduceMotion());
        var active = false;
        ProcessMotion(() => active = _animations.Tick(_timeProvider.GetUtcNow()));
        InvalidateScene();
        if (!active) _animationTimer.Stop();
        return active;
    }

    private void ProcessMotion(Action action)
    {
        _processingMotion = true;
        try { action(); }
        finally { _processingMotion = false; }
    }

    private sealed class HavenDrawingSurface(HavenSceneControl owner) : Control
    {
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            owner.RenderScene(context);
        }
    }
}
