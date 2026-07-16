using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace Haven.Desktop.Services;

public sealed class NotificationService : IDisposable
{
    private readonly System.Timers.Timer _cleanupTimer;
    private int _disposed;

    public ObservableCollection<ToastNotification> Notifications { get; } = [];

    public NotificationService()
    {
        _cleanupTimer = new System.Timers.Timer(1000)
        {
            AutoReset = true,
            Enabled = true
        };
        _cleanupTimer.Elapsed += OnCleanupElapsed;
        _cleanupTimer.Start();
    }

    public void Show(string title, string message, ToastKind kind = ToastKind.Info, TimeSpan? duration = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        var toast = new ToastNotification
        {
            Id = Guid.NewGuid(),
            Title = title,
            Message = message,
            Kind = kind,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow + (duration ?? TimeSpan.FromSeconds(5))
        };
        Dispatcher.UIThread.Post(() =>
        {
            if (Volatile.Read(ref _disposed) == 0) Notifications.Add(toast);
        });
    }

    public void Dismiss(Guid id)
    {
        if (Volatile.Read(ref _disposed) == 1) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (Volatile.Read(ref _disposed) == 1) return;
            var item = Notifications.FirstOrDefault(notification => notification.Id == id);
            if (item is not null) Notifications.Remove(item);
        });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _cleanupTimer.Stop();
        _cleanupTimer.Elapsed -= OnCleanupElapsed;
        _cleanupTimer.Dispose();
        Dispatcher.UIThread.Post(Notifications.Clear);
    }

    private void OnCleanupElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (Volatile.Read(ref _disposed) == 1) return;
        Dispatcher.UIThread.Post(CleanupExpired);
    }

    private void CleanupExpired()
    {
        if (Volatile.Read(ref _disposed) == 1) return;
        var now = DateTimeOffset.UtcNow;
        var expired = Notifications.Where(notification => notification.ExpiresAt <= now).ToList();
        foreach (var item in expired) Notifications.Remove(item);
    }
}

public sealed class ToastNotification
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public ToastKind Kind { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

public enum ToastKind { Info, Success, Warning, Error }
