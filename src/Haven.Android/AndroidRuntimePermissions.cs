using Android;
using Android.Content.PM;

namespace Haven.Android;

internal static class AndroidRuntimePermissions
{
    private const int RecordAudioRequestCode = 7401;
    private const int NotificationsRequestCode = 7402;
    private static readonly object Sync = new();
    private static WeakReference<MainActivity>? _activity;
    private static bool _foreground;
    private static bool _notificationPrompted;
    private static TaskCompletionSource<bool>? _recordAudioRequest;
    private static TaskCompletionSource<bool>? _notificationRequest;

    public static MainActivity? CurrentActivity
    {
        get { lock (Sync) return _activity is not null && _activity.TryGetTarget(out var activity) ? activity : null; }
    }

    public static bool IsMainActivityForeground
    {
        get { lock (Sync) return _foreground && _activity is not null && _activity.TryGetTarget(out _); }
    }

    public static bool HasRecordAudioPermission => global::Android.App.Application.Context.CheckSelfPermission(Manifest.Permission.RecordAudio) == Permission.Granted;
    public static bool HasNotificationPermission => !OperatingSystem.IsAndroidVersionAtLeast(33) || global::Android.App.Application.Context.CheckSelfPermission(Manifest.Permission.PostNotifications) == Permission.Granted;

    public static void Attach(MainActivity activity, bool isForeground)
    {
        ArgumentNullException.ThrowIfNull(activity);
        lock (Sync) { _activity = new WeakReference<MainActivity>(activity); _foreground = isForeground; }
    }

    public static void SetForeground(MainActivity activity, bool isForeground)
    {
        ArgumentNullException.ThrowIfNull(activity);
        lock (Sync)
        {
            if (_activity is null || !_activity.TryGetTarget(out var current) || !ReferenceEquals(current, activity)) _activity = new WeakReference<MainActivity>(activity);
            _foreground = isForeground;
        }
    }

    public static Task<bool> EnsureRecordAudioPermissionAsync(CancellationToken cancellationToken)
    {
        if (HasRecordAudioPermission) return Task.FromResult(true);
        return RequestPermissionAsync(Manifest.Permission.RecordAudio, RecordAudioRequestCode, false, cancellationToken);
    }

    public static Task<bool> EnsureNotificationsPermissionAsync(CancellationToken cancellationToken)
    {
        if (HasNotificationPermission || !OperatingSystem.IsAndroidVersionAtLeast(33)) return Task.FromResult(true);
        lock (Sync) { if (_notificationPrompted && _notificationRequest is null) return Task.FromResult(false); }
        return RequestPermissionAsync(Manifest.Permission.PostNotifications, NotificationsRequestCode, true, cancellationToken);
    }

    public static bool HandlePermissionResult(int requestCode, Permission[] grantResults)
    {
        TaskCompletionSource<bool>? completion;
        lock (Sync)
        {
            completion = requestCode switch { RecordAudioRequestCode => _recordAudioRequest, NotificationsRequestCode => _notificationRequest, _ => null };
            if (requestCode == RecordAudioRequestCode) _recordAudioRequest = null;
            else if (requestCode == NotificationsRequestCode) _notificationRequest = null;
            else return false;
        }
        completion?.TrySetResult(grantResults.Length > 0 && grantResults[0] == Permission.Granted);
        return true;
    }

    private static Task<bool> RequestPermissionAsync(string permission, int requestCode, bool notificationRequest, CancellationToken cancellationToken)
    {
        MainActivity activity;
        TaskCompletionSource<bool> completion;
        lock (Sync)
        {
            var existing = requestCode == RecordAudioRequestCode ? _recordAudioRequest : _notificationRequest;
            if (existing is not null) return existing.Task.WaitAsync(cancellationToken);
            if (!_foreground || _activity is null || !_activity.TryGetTarget(out activity!)) return Task.FromResult(false);
            completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (requestCode == RecordAudioRequestCode) _recordAudioRequest = completion;
            else { _notificationRequest = completion; if (notificationRequest) _notificationPrompted = true; }
        }
        activity.RunOnUiThread(() =>
        {
            try { activity.RequestPermissions([permission], requestCode); }
            catch
            {
                lock (Sync)
                {
                    if (requestCode == RecordAudioRequestCode && ReferenceEquals(_recordAudioRequest, completion)) _recordAudioRequest = null;
                    if (requestCode == NotificationsRequestCode && ReferenceEquals(_notificationRequest, completion)) _notificationRequest = null;
                }
                completion.TrySetResult(false);
            }
        });
        return completion.Task.WaitAsync(cancellationToken);
    }
}
