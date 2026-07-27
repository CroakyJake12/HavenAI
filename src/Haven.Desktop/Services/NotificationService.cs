/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Services/NotificationService.cs, in the Desktop services layer, adapting application behavior to Windows and Avalonia concerns.
 * What: This file owns NotificationService, ToastNotification, ToastKind. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace Haven.Desktop.Services;

/// <summary>
/// Represents notification service and keeps its related state and behavior together.
/// </summary>
public sealed class NotificationService : IDisposable
{
    /// <summary>
    /// Stores cleanup timer locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly System.Timers.Timer _cleanupTimer;
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _disposed;

    /// <summary>
    /// Gets or updates notifications, the bindable or domain state represented by this property.
    /// </summary>
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

    /// <summary>
    /// Performs the show step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the dismiss step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _cleanupTimer.Stop();
        _cleanupTimer.Elapsed -= OnCleanupElapsed;
        _cleanupTimer.Dispose();
        Dispatcher.UIThread.Post(Notifications.Clear);
    }

    /// <summary>
    /// Handles the cleanup elapsed event raised by the UI or runtime.
    /// </summary>
    private void OnCleanupElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (Volatile.Read(ref _disposed) == 1) return;
        Dispatcher.UIThread.Post(CleanupExpired);
    }

    /// <summary>
    /// Performs the cleanup expired step owned by this component.
    /// </summary>
    private void CleanupExpired()
    {
        if (Volatile.Read(ref _disposed) == 1) return;
        var now = DateTimeOffset.UtcNow;
        var expired = Notifications.Where(notification => notification.ExpiresAt <= now).ToList();
        foreach (var item in expired) Notifications.Remove(item);
    }
}

/// <summary>
/// Represents toast notification and keeps its related state and behavior together.
/// </summary>
public sealed class ToastNotification
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; init; }
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title { get; init; } = string.Empty;
    /// <summary>
    /// Gets or updates message, the bindable or domain state represented by this property.
    /// </summary>
    public string Message { get; init; } = string.Empty;
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public ToastKind Kind { get; init; }
    /// <summary>
    /// Creates d at with the invariants required by its callers.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }
    /// <summary>
    /// Gets or updates expires at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// Lists the supported toast kind values used to make state explicit and type-safe.
/// </summary>
public enum ToastKind { Info, Success, Warning, Error }
