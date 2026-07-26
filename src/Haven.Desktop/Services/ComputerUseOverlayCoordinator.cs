using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
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
    private readonly TextBlock _detail;
    private readonly Button _pause;
    private bool _disposed;

    public ComputerUseOverlayCoordinator(IComputerUseSessionController controller)
    {
        _controller = controller;
        _detail = new TextBlock
        {
            FontSize = 13,
            Opacity = 0.66,
            Text = "Preparing computer use"
        };
        _pause = SafetyButton("Pause", new SolidColorBrush(Color.Parse("#FFF8FF")), Brushes.Black);
        _pause.Click += (_, _) => _controller.TogglePause();

        var stop = SafetyButton("Stop", new SolidColorBrush(Color.Parse("#FF1118")), Brushes.White);
        stop.Click += (_, _) => _controller.Stop();

        var labels = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = "Haven is using your computer",
                    FontSize = 25,
                    FontWeight = FontWeight.Bold
                },
                _detail
            }
        };

        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 14,
            Margin = new Thickness(28, 18)
        };
        layout.Children.Add(labels);
        Grid.SetColumn(_pause, 1);
        layout.Children.Add(_pause);
        Grid.SetColumn(stop, 2);
        layout.Children.Add(stop);

        _banner = CreateOverlayWindow();
        _banner.Title = "Haven Computer Use controls";
        _banner.Width = 1120;
        _banner.Height = 112;
        _banner.Content = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#FFFFF7")),
            BorderBrush = new SolidColorBrush(Color.Parse("#FFF08B")),
            BorderThickness = new Thickness(4),
            CornerRadius = new CornerRadius(42),
            Padding = new Thickness(3),
            Child = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#FFF0FD")),
                BorderBrush = new SolidColorBrush(Color.Parse("#FFD3FC")),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(36),
                Child = layout
            }
        };

        _cursor = CreateOverlayWindow();
        _cursor.Title = "Haven virtual cursor";
        _cursor.Width = 76;
        _cursor.Height = 78;
        _cursor.IsHitTestVisible = false;
        _cursor.Content = new Image
        {
            Source = new Bitmap(AssetLoader.Open(
                new Uri("avares://Haven/Assets/haven-virtual-cursor.png"))),
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false
        };

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
        MinWidth = 116,
        MinHeight = 62,
        Padding = new Thickness(24, 14),
        CornerRadius = new CornerRadius(20),
        Background = background,
        Foreground = foreground,
        BorderThickness = new Thickness(0),
        FontSize = 18,
        FontWeight = FontWeight.Bold,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };

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
            _banner.Hide();
            return;
        }

        _detail.Text = state.Action;
        _pause.Content = state.IsPaused ? "Resume" : "Pause";
        PositionBanner();
        if (!_banner.IsVisible)
        {
            _banner.Show();
        }

        if (state.CursorX is int x && state.CursorY is int y)
        {
            _cursor.Position = new PixelPoint(x - 38, y - 68);
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

    private void PositionBanner()
    {
        var screen = _banner.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var width = Math.Min(1120, Math.Max(520, area.Width - 64));
        _banner.Width = width / screen.Scaling;
        _banner.Position = new PixelPoint(
            area.X + (area.Width - width) / 2,
            area.Y + 18);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller.StateChanged -= OnStateChanged;
        if (_cursor.IsVisible)
        {
            _cursor.Close();
        }

        if (_banner.IsVisible)
        {
            _banner.Close();
        }
    }
}
