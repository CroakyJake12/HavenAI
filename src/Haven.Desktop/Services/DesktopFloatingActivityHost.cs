using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Components;
using Haven.Desktop.HavenUI.Floating;

namespace Haven.Desktop.Services;

public sealed class DesktopFloatingActivityHost(FloatingActivityStateStore stateStore) : IFloatingActivityHost
{
    private readonly Dictionary<Guid, Window> _windows = [];
    private bool _disposed;

    public string Platform => "Windows";
    public bool IsAvailable => OperatingSystem.IsWindows();
    public string? UnavailableReason => IsAvailable ? null : "Detached desktop windows require Windows.";
    public event EventHandler<FloatingActivitySnapshot>? StateChanged;

    public Task<FloatingActivitySnapshot> PresentAsync(
        FloatingActivityDefinition definition,
        IFloatingActivityContent content,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsAvailable) throw new PlatformNotSupportedException(UnavailableReason);

        var activityContent = content.Content as Control
                              ?? new ContentControl { Content = content.Content };
        var window = new Window
        {
            Title = definition.Title,
            Width = 420,
            Height = 280,
            MinWidth = 240,
            MinHeight = 160,
            CanResize = true,
            ShowInTaskbar = false,
            Topmost = definition.AlwaysOnTop,
            WindowDecorations = WindowDecorations.None,
            Background = Brushes.Transparent,
            TransparencyBackgroundFallback = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
        };

        var close = new HavenIconButton
        {
            Width = 34,
            Height = 34,
            IsVisible = definition.IsDismissible,
            Content = new TextBlock
            {
                Text = "×",
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        AutomationProperties.SetName(close, "Close " + definition.Title);
        close.Click += (_, _) => window.Close();

        var dragBar = new HavenToolbar
        {
            Padding = new Thickness(8, 4, 6, 4),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    new TextBlock
                    {
                        Text = definition.Title,
                        FontSize = 13,
                        FontWeight = FontWeight.ExtraBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0)
                    },
                    Column(close, 1)
                }
            }
        };
        AutomationProperties.SetName(dragBar, "Drag " + definition.Title);
        dragBar.PointerPressed += (_, args) =>
        {
            if (!args.GetCurrentPoint(dragBar).Properties.IsLeftButtonPressed
                || args.Source is Control source
                && (source is Button || source.FindAncestorOfType<Button>() is not null)) return;
            window.BeginMoveDrag(args);
            args.Handled = true;
        };

        var surface = new HavenFloatingSurface
        {
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                RowSpacing = 8,
                Children = { dragBar, Row(activityContent, 1) }
            }
        };
        window.Content = surface;

        if (stateStore.Get(definition.Id) is { } previous)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Width = Math.Max(window.MinWidth, previous.Width);
            window.Height = Math.Max(window.MinHeight, previous.Height);
            window.Position = new PixelPoint((int)Math.Round(previous.X), (int)Math.Round(previous.Y));
        }

        void PublishState(FloatingActivityState state)
        {
            var snapshot = new FloatingActivitySnapshot(
                definition.Id,
                state,
                Math.Max(window.MinWidth, window.Width),
                Math.Max(window.MinHeight, window.Height),
                window.Position.X,
                window.Position.Y);
            stateStore.Set(snapshot);
            StateChanged?.Invoke(this, snapshot);
        }

        window.PositionChanged += (_, _) => PublishState(FloatingActivityState.Presented);
        window.SizeChanged += (_, _) => PublishState(FloatingActivityState.Presented);
        window.Closed += (_, _) =>
        {
            _windows.Remove(definition.Id);
            PublishState(FloatingActivityState.Dismissed);
        };
        _windows[definition.Id] = window;
        window.Show();
        PublishState(FloatingActivityState.Presented);
        return Task.FromResult(stateStore.Get(definition.Id)!);
    }

    public Task<FloatingActivitySnapshot> UpdateAsync(FloatingActivitySnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_windows.TryGetValue(snapshot.Id, out var window))
        {
            window.Width = Math.Max(240, snapshot.Width);
            window.Height = Math.Max(160, snapshot.Height);
            window.Position = new PixelPoint((int)Math.Round(snapshot.X), (int)Math.Round(snapshot.Y));
        }
        stateStore.Set(snapshot);
        StateChanged?.Invoke(this, snapshot);
        return Task.FromResult(snapshot);
    }

    public Task DismissAsync(Guid activityId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_windows.Remove(activityId, out var window)) window.Close();
        stateStore.Remove(activityId);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        foreach (var window in _windows.Values.ToArray()) window.Close();
        _windows.Clear();
        return ValueTask.CompletedTask;
    }

    private static T Column<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static T Row<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }
}
