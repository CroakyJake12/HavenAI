/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/DeveloperTools/ElementPickerWindow.cs in the Desktop composition layer.
 * What: Implements Ctrl+Shift+C click-to-inspect selection with a live bounds highlight.
 * How: A temporary transparent window mirrors the Haven client area and hit-tests the underlying visual tree.
 * Why: Runtime element selection should feel as direct as Chrome's element picker.
 */

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Haven.Desktop.DeveloperTools;

/// <summary>
/// Transparent click-capture window used by Ctrl+Shift+C element selection.
/// </summary>
internal sealed class ElementPickerWindow : Window
{
    private readonly Window _inspectedWindow;
    private readonly Border _highlight;
    private readonly TextBlock _label;
    private Visual? _hovered;

    public ElementPickerWindow(Window inspectedWindow)
    {
        _inspectedWindow = inspectedWindow;
        Title = "Haven element picker";
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        CanResize = false;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        TransparencyBackgroundFallback = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

        _highlight = new Border
        {
            IsHitTestVisible = false,
            BorderBrush = new SolidColorBrush(Color.FromRgb(64, 158, 255)),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromArgb(42, 64, 158, 255)),
            CornerRadius = new CornerRadius(2),
            IsVisible = false
        };
        _label = new TextBlock
        {
            IsHitTestVisible = false,
            Background = new SolidColorBrush(Color.FromRgb(34, 104, 190)),
            Foreground = Brushes.White,
            Padding = new Thickness(7, 4),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            IsVisible = false
        };

        Content = new Canvas
        {
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            Children = { _highlight, _label }
        };

        Opened += OnOpened;
        PointerMoved += OnPointerMoved;
        PointerPressed += OnPointerPressed;
        KeyDown += OnKeyDown;
        _inspectedWindow.PositionChanged += OnOwnerPositionChanged;
        _inspectedWindow.SizeChanged += OnOwnerSizeChanged;
        Closed += OnClosed;
    }

    public event Action<Visual>? ElementPicked;

    private void OnOpened(object? sender, EventArgs e) => SyncToOwner();

    private void OnOwnerPositionChanged(object? sender, PixelPointEventArgs e) => SyncToOwner();

    private void OnOwnerSizeChanged(object? sender, SizeChangedEventArgs e) => SyncToOwner();

    private void SyncToOwner()
    {
        if (!_inspectedWindow.IsVisible) return;
        Position = _inspectedWindow.PointToScreen(new Point(0, 0));
        Width = Math.Max(1, _inspectedWindow.Bounds.Width);
        Height = Math.Max(1, _inspectedWindow.Bounds.Height);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        var visual = _inspectedWindow.GetVisualAt(point, IsInspectable);
        _hovered = visual;
        UpdateHighlight(visual);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_hovered is null) return;
        ElementPicked?.Invoke(_hovered);
        e.Handled = true;
        Close();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }

    private void UpdateHighlight(Visual? visual)
    {
        if (visual is null)
        {
            _highlight.IsVisible = false;
            _label.IsVisible = false;
            return;
        }

        var origin = visual.TranslatePoint(new Point(0, 0), _inspectedWindow);
        if (origin is null)
        {
            _highlight.IsVisible = false;
            _label.IsVisible = false;
            return;
        }

        var bounds = visual.Bounds;
        var x = origin.Value.X;
        var y = origin.Value.Y;
        var width = Math.Max(1, bounds.Width);
        var height = Math.Max(1, bounds.Height);

        Canvas.SetLeft(_highlight, x);
        Canvas.SetTop(_highlight, y);
        _highlight.Width = width;
        _highlight.Height = height;
        _highlight.IsVisible = true;

        _label.Text = DeveloperElementFormatter.BuildSelector(visual) + $"  {width:0.#} × {height:0.#}";
        _label.Measure(Size.Infinity);
        var labelY = y >= 30 ? y - 28 : Math.Min(Height - 28, y + height + 2);
        Canvas.SetLeft(_label, Math.Clamp(x, 0, Math.Max(0, Width - _label.DesiredSize.Width)));
        Canvas.SetTop(_label, Math.Max(0, labelY));
        _label.IsVisible = true;
    }

    private static bool IsInspectable(Visual visual) =>
        visual.IsVisible && (visual is not InputElement input || input.IsHitTestVisible);

    private void OnClosed(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        PointerMoved -= OnPointerMoved;
        PointerPressed -= OnPointerPressed;
        KeyDown -= OnKeyDown;
        _inspectedWindow.PositionChanged -= OnOwnerPositionChanged;
        _inspectedWindow.SizeChanged -= OnOwnerSizeChanged;
        Closed -= OnClosed;
    }
}
