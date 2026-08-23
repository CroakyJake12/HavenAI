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
using Haven.Application;
using Haven.Core;

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
    private readonly HavenNotificationService? _backend;
    private readonly Dictionary<Guid, ToastNotification> _local = [];
    private readonly CancellationTokenSource _lifetime = new();

    /// <summary>
    /// Gets or updates notifications, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ToastNotification> Notifications { get; } = [];

    public NotificationService() : this(null)
    {
    }

    public NotificationService(HavenNotificationService? backend)
    {
        _backend = backend;
        _cleanupTimer = new System.Timers.Timer(1000)
        {
            AutoReset = true,
            Enabled = true
        };
        _cleanupTimer.Elapsed += OnCleanupElapsed;
        _cleanupTimer.Start();
        if (_backend is not null)
        {
            _backend.Changed += OnBackendChanged;
            _ = RefreshBackendAsync();
        }
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
            if (Volatile.Read(ref _disposed) == 0)
            {
                _local[toast.Id] = toast;
                Rebuild(Notifications.Where(item => !item.IsLocal).ToArray());
            }
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
            _local.Remove(id);
            if (item is not null) Notifications.Remove(item);
        });
        if (_backend is not null) _ = _backend.DismissAsync(id, _lifetime.Token);
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
        if (_backend is not null) _backend.Changed -= OnBackendChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
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
        foreach (var id in _local.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray()) _local.Remove(id);
        foreach (var item in Notifications.Where(notification => notification.ExpiresAt <= now).ToList()) Notifications.Remove(item);
    }

    private void OnBackendChanged(object? sender, Haven.Core.HavenNotification notification) => _ = RefreshBackendAsync();

    private async Task RefreshBackendAsync()
    {
        if (_backend is null || Volatile.Read(ref _disposed) == 1) return;
        try
        {
            var recent = await _backend.GetRecentAsync(100, false, _lifetime.Token).ConfigureAwait(false);
            var mapped = _backend.Live.Concat(recent)
                .Where(item => !item.IsDismissed)
                .OrderByDescending(item => item.IsLive)
                .ThenByDescending(item => item.UpdatedAt)
                .Select(Map)
                .ToArray();
            Dispatcher.UIThread.Post(() => Rebuild(mapped));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch { /* The bell may retain its last complete state if persistence is temporarily unavailable. */ }
    }

    private void Rebuild(IReadOnlyList<ToastNotification> backend)
    {
        if (Volatile.Read(ref _disposed) == 1) return;
        Notifications.Clear();
        foreach (var item in backend.Concat(_local.Values).OrderByDescending(item => item.IsLive).ThenByDescending(item => item.CreatedAt)) Notifications.Add(item);
    }

    private static ToastNotification Map(Haven.Core.HavenNotification value) => new()
    {
        Id = value.Id,
        Title = value.Title,
        Message = value.Message,
        Kind = value.Priority switch
        {
            HavenNotificationPriority.Success => ToastKind.Success,
            HavenNotificationPriority.Warning or HavenNotificationPriority.AttentionRequired => ToastKind.Warning,
            HavenNotificationPriority.Error => ToastKind.Error,
            _ => ToastKind.Info
        },
        CreatedAt = value.UpdatedAt,
        ExpiresAt = DateTimeOffset.MaxValue,
        IsLive = value.IsLive,
        IsRead = value.IsRead,
        RequiresAttention = value.RequiresAttention,
        SourceName = value.SourceName,
        Target = value.Target,
        IsLocal = false
    };
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
    public bool IsLive { get; init; }
    public bool IsRead { get; init; }
    public bool RequiresAttention { get; init; }
    public string SourceName { get; init; } = "Haven";
    public HavenNavigationTarget? Target { get; init; }
    public bool IsLocal { get; init; } = true;
}

/// <summary>
/// Lists the supported toast kind values used to make state explicit and type-safe.
/// </summary>
public enum ToastKind { Info, Success, Warning, Error }
