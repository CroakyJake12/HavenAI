using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Haven.Application;

namespace Haven.Desktop.Services;

/// <summary>
/// Owns the topmost Computer Use safety banner and Haven's visual pointer. The
/// banner reflects the same controller that gates and cancels real tool actions.
/// </summary>
public sealed class ComputerUseOverlayCoordinator : IDisposable
{
    private readonly IComputerUseSessionController _controller;
    private readonly Window _banner;
    private readonly Window _cursor;
    private readonly Window _status;
    private readonly TextBlock _detail;
    private readonly TextBlock _actionCount;
    private readonly Button _pause;
    private bool _disposed;

    public ComputerUseOverlayCoordinator(IComputerUseSessionController controller)
    {
        _controller = controller;
        _detail = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Text = "Preparing computer use"
        };
        _actionCount = new TextBlock
        {
            FontSize = 19,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            Text = "Action: 0/30"
        };
        _pause = SafetyButton(
            "Pause",
            new SolidColorBrush(Color.FromArgb(68, 255, 255, 255)),
            Brushes.White);
        _pause.Click += (_, _) => _controller.TogglePause();

        var stop = SafetyButton("Stop", new SolidColorBrush(Color.Parse("#FF1118")), Brushes.White);
        stop.Click += (_, _) => _controller.Stop();

        var labels = new TextBlock
        {
            Text = "Haven is using your Computer",
            FontSize = 34,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };

        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 14,
            Margin = new Thickness(30, 20)
        };
        layout.Children.Add(labels);
        Grid.SetColumn(_pause, 1);
        layout.Children.Add(_pause);
        Grid.SetColumn(stop, 2);
        layout.Children.Add(stop);

        _banner = CreateOverlayWindow();
        _banner.Title = "Haven Computer Use controls";
        _banner.Width = 1400;
        _banner.Height = 136;
        _banner.Content = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#178BFF"), 0),
                    new GradientStop(Color.Parse("#315CF7"), 0.48),
                    new GradientStop(Color.Parse("#6424FF"), 1)
                }
            },
            CornerRadius = new CornerRadius(28),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = 34,
                OffsetY = 14,
                Color = Color.FromArgb(74, 38, 92, 255)
            }),
            Child = layout
        };

        _status = CreateOverlayWindow();
        _status.Title = "Haven Computer Use status";
        _status.Width = 250;
        _status.Height = 102;
        _status.IsHitTestVisible = false;
        _status.Content = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#168DFF"), 0),
                    new GradientStop(Color.Parse("#2577FA"), 1)
                }
            },
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(18, 14),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = 28,
                OffsetY = 10,
                Color = Color.FromArgb(70, 23, 116, 255)
            }),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 4,
                Children = { _actionCount, _detail }
            }
        };

        _cursor = CreateOverlayWindow();
        _cursor.Title = "Haven virtual cursor";
        _cursor.Width = 106;
        _cursor.Height = 110;
        _cursor.IsHitTestVisible = false;
        _cursor.Content = BuildCursor();

        _controller.StateChanged += OnStateChanged;
    }

    private static Window CreateOverlayWindow() => new()
    {
        WindowDecorations = WindowDecorations.None,
        ShowInTaskbar = false,
        ShowActivated = false,
        CanResize = false,
        Topmost = true,
        Background = Brushes.Transparent,
        TransparencyBackgroundFallback = Brushes.Transparent,
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent]
    };

    private static Button SafetyButton(string label, IBrush background, IBrush foreground) => new()
    {
        Content = label,
        MinWidth = 138,
        MinHeight = 68,
        Padding = new Thickness(26, 14),
        CornerRadius = new CornerRadius(34),
        Background = background,
        Foreground = foreground,
        BorderThickness = new Thickness(0),
        FontSize = 18,
        FontWeight = FontWeight.Bold,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private static Control BuildCursor()
    {
        const string cursorGeometry = "M 12,8 L 88,54 Q 94,58 87,64 L 44,96 Q 36,102 33,91 Z";
        var glow = new ShapePath
        {
            Data = Geometry.Parse(cursorGeometry),
            Fill = new SolidColorBrush(Color.FromArgb(55, 113, 171, 255)),
            Stroke = new SolidColorBrush(Color.FromArgb(45, 85, 103, 255)),
            StrokeThickness = 18,
            Effect = new DropShadowEffect
            {
                BlurRadius = 26,
                Color = Color.Parse("#8EADFF"),
                Opacity = 0.8
            }
        };
        var triangle = new ShapePath
        {
            Data = Geometry.Parse(cursorGeometry),
            Fill = new SolidColorBrush(Color.Parse("#838AF4")),
            Stroke = new SolidColorBrush(Color.Parse("#5D42FF")),
            StrokeThickness = 5
        };
        return new Grid
        {
            IsHitTestVisible = false,
            Children = { glow, triangle }
        };
    }

    private void OnStateChanged(object? sender, ComputerUseSessionState state) =>
        Dispatcher.UIThread.Post(() => Apply(state));

    private void Apply(ComputerUseSessionState state)
    {
        if (_disposed)
        {
            return;
        }

        if (!state.IsActive)
        {
            _cursor.Hide();
            _status.Hide();
            _banner.Hide();
            return;
        }

        _detail.Text = state.Action;
        _actionCount.Text = $"Action: {state.ActionNumber}/{state.ActionLimit}";
        _pause.Content = state.IsPaused ? "Resume" : "Pause";
        PositionOverlays();
        if (!_banner.IsVisible)
        {
            _banner.Show();
        }
        if (!_status.IsVisible)
        {
            _status.Show();
        }

        if (state.CursorX is int x && state.CursorY is int y)
        {
            _cursor.Position = new PixelPoint(x - 50, y - 94);
            if (!_cursor.IsVisible)
            {
                _cursor.Show();
            }
        }
        else
        {
            _cursor.Hide();
        }
    }

    private void PositionOverlays()
    {
        var screen = _banner.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var width = Math.Max(640, area.Width - 20);
        _banner.Width = width / screen.Scaling;
        _banner.Position = new PixelPoint(
            area.X + (area.Width - width) / 2,
            area.Y + 10);
        _status.Position = new PixelPoint(
            area.X + 18,
            area.Bottom - (int)Math.Round(_status.Height * screen.Scaling) - 22);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller.StateChanged -= OnStateChanged;
        _cursor.Close();
        _banner.Close();
        _status.Close();
    }
}
