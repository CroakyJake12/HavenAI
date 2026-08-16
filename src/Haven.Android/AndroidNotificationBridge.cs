using System.Collections.Specialized;
using Android.App;
using Android.Content;
using Haven.Desktop.Services;

namespace Haven.Android;

public sealed class AndroidNotificationBridge : IDisposable
{
    private const string ChannelId = "haven.alerts";
    private readonly NotificationService _notifications;
    private readonly NotificationManager? _manager;
    private readonly Context _context;
    private int _disposed;

    public AndroidNotificationBridge(NotificationService notifications)
    {
        _notifications = notifications;
        _context = global::Android.App.Application.Context;
        _manager = _context.GetSystemService(Context.NotificationService) as NotificationManager;
        EnsureChannel();
        _notifications.Notifications.CollectionChanged += OnNotificationsChanged;
    }

    private void OnNotificationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Volatile.Read(ref _disposed) == 1 || e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null || AndroidRuntimePermissions.IsMainActivityForeground) return;
        foreach (var item in e.NewItems) if (item is ToastNotification notification) Post(notification);
    }

    private void Post(ToastNotification toast)
    {
        if (_manager is null || !AndroidRuntimePermissions.HasNotificationPermission) return;
        try
        {
            var notificationId = ToNotificationId(toast.Id);
            using var launchIntent = new Intent(_context, typeof(MainActivity));
            launchIntent.PutExtra("haven_surface", "notifications");
            launchIntent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
            var pendingIntent = PendingIntent.GetActivity(_context, notificationId, launchIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
            using var builder = OperatingSystem.IsAndroidVersionAtLeast(26) ? new Notification.Builder(_context, ChannelId) : new Notification.Builder(_context);
            builder.SetSmallIcon(Resource.Drawable.haven_icon).SetContentTitle(toast.Title).SetContentText(toast.Message).SetContentIntent(pendingIntent).SetAutoCancel(true).SetWhen(toast.CreatedAt.ToUnixTimeMilliseconds());
            _manager.Notify(notificationId, builder.Build());
        }
        catch (Exception exception) { global::Android.Util.Log.Warn("HavenNotifications", "Could not post an Android notification: " + exception.Message); }
    }

    private void EnsureChannel()
    {
        if (_manager is null || !OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        try { using var channel = new NotificationChannel(ChannelId, "Haven alerts", NotificationImportance.Default) { Description = "Background alerts from Haven." }; _manager.CreateNotificationChannel(channel); }
        catch (Exception exception) { global::Android.Util.Log.Warn("HavenNotifications", "Could not create the Haven notification channel: " + exception.Message); }
    }

    private static int ToNotificationId(Guid id) { var value = BitConverter.ToInt32(id.ToByteArray(), 0) & int.MaxValue; return value == 0 ? 1 : value; }
    public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 1) return; _notifications.Notifications.CollectionChanged -= OnNotificationsChanged; }
}
