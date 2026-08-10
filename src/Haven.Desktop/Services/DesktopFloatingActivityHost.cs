using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Haven.Application;
using Haven.Core;
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

        var surface = new HavenFloatingSurface { Content = content.Content };
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
            Content = surface
        };
        window.Closed += (_, _) => _windows.Remove(definition.Id);
        _windows[definition.Id] = window;

        var snapshot = new FloatingActivitySnapshot(
            definition.Id,
            FloatingActivityState.Presented,
            window.Width,
            window.Height,
            window.Position.X,
            window.Position.Y);
        stateStore.Set(snapshot);
        StateChanged?.Invoke(this, snapshot);
        window.Show();
        return Task.FromResult(snapshot);
    }

    public Task<FloatingActivitySnapshot> UpdateAsync(FloatingActivitySnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_windows.TryGetValue(snapshot.Id, out var window))
        {
            window.Width = Math.Max(240, snapshot.Width);
            window.Height = Math.Max(160, snapshot.Height);
            stateStore.Set(snapshot);
            StateChanged?.Invoke(this, snapshot);
        }
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
}
