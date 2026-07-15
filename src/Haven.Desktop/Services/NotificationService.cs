using System;
using System.Collections.ObjectModel;
using System.Timers;
using Avalonia.Threading;

namespace Haven.Desktop.Services;

public sealed class NotificationService
{
    private readonly System.Timers.Timer _cleanupTimer;

    public ObservableCollection<ToastNotification> Notifications { get; } = [];

    public NotificationService()
    {
        _cleanupTimer = new System.Timers.Timer(1000);
        _cleanupTimer.Elapsed += (_, _) => Dispatcher.UIThread.Post(CleanupExpired);
        _cleanupTimer.Start();
    }

    public void Show(string title, string message, ToastKind kind = ToastKind.Info, TimeSpan? duration = null)
    {
        var toast = new ToastNotification
        {
            Id = Guid.NewGuid(),
            Title = title,
            Message = message,
            Kind = kind,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow + (duration ?? TimeSpan.FromSeconds(5))
        };
        Dispatcher.UIThread.Post(() => Notifications.Add(toast));
    }

    public void Dismiss(Guid id)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var item = Notifications.FirstOrDefault(n => n.Id == id);
            if (item is not null) Notifications.Remove(item);
        });
    }

    private void CleanupExpired()
    {
        var now = DateTimeOffset.UtcNow;
        Dispatcher.UIThread.Post(() =>
        {
            var expired = Notifications.Where(n => n.ExpiresAt <= now).ToList();
            foreach (var item in expired) Notifications.Remove(item);
        });
    }
}

public sealed class ToastNotification
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public ToastKind Kind { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

public enum ToastKind { Info, Success, Warning, Error }
