using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Android.Views;
using Avalonia.Android;
using Avalonia.Controls;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Floating;

namespace Haven.Android.Compatibility;

/// <summary>
/// Hosts Haven floating content in a real Android WindowManager overlay. The
/// native window remains transparent; the shared HavenFloatingSurface owns all
/// visible material. Presentation succeeds only after Android confirms the
/// app has Display-over-other-apps permission and WindowManager accepts the
/// native AvaloniaView.
/// </summary>
public sealed class AndroidFloatingActivityHost : IFloatingActivityHost
{
    private readonly FloatingActivityStateStore _stateStore;
    private readonly Dictionary<Guid, OverlayEntry> _overlays = [];
    private readonly object _gate = new();
    private bool _disposed;

    public AndroidFloatingActivityHost(FloatingActivityStateStore stateStore) => _stateStore = stateStore;

    public string Platform => "Android";

    public bool IsAvailable
    {
        get
        {
            if (!OperatingSystem.IsAndroid()) return false;
            var context = global::Android.App.Application.Context;
            return Build.VERSION.SdkInt < BuildVersionCodes.M || Settings.CanDrawOverlays(context);
        }
    }

    public string? UnavailableReason => !OperatingSystem.IsAndroid()
        ? "Android floating activities require the Android host."
        : IsAvailable
            ? null
            : "Display over other apps is not allowed for Haven. Enable that Android permission, then retry the floating activity.";

    public event EventHandler<FloatingActivitySnapshot>? StateChanged;

    public Task<FloatingActivitySnapshot> PresentAsync(
        FloatingActivityDefinition definition,
        IFloatingActivityContent content,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsAndroid()) throw new PlatformNotSupportedException(UnavailableReason);
        if (!IsAvailable) return Task.FromResult(PublishFailure(definition, UnavailableReason!));

        return RunOnMainThreadAsync(() => PresentCore(definition, content), cancellationToken);
    }

    public Task<FloatingActivitySnapshot> UpdateAsync(
        FloatingActivitySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        return RunOnMainThreadAsync(() =>
        {
            OverlayEntry? entry;
            lock (_gate) _overlays.TryGetValue(snapshot.Id, out entry);
            if (entry is not null)
            {
                entry.Layout.Width = ToPixels(snapshot.Width);
                entry.Layout.Height = ToPixels(snapshot.Height);
                entry.Layout.X = ToPixels(snapshot.X);
                entry.Layout.Y = ToPixels(snapshot.Y);
                entry.WindowManager.UpdateViewLayout(entry.View, entry.Layout);
            }

            Publish(snapshot);
            return snapshot;
        }, cancellationToken);
    }

    public Task DismissAsync(Guid activityId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return RunOnMainThreadAsync(() =>
        {
            OverlayEntry? entry;
            lock (_gate) _overlays.Remove(activityId, out entry);

            if (entry is not null && entry.View.IsAttachedToWindow)
                entry.WindowManager.RemoveView(entry.View);

            var previous = _stateStore.Get(activityId);
            if (previous is not null)
                Publish(previous with { State = FloatingActivityState.Dismissed });
            else
                _stateStore.Remove(activityId);
            return true;
        }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (!OperatingSystem.IsAndroid()) return;

        await RunOnMainThreadAsync(() =>
        {
            OverlayEntry[] entries;
            lock (_gate)
            {
                entries = _overlays.Values.ToArray();
                _overlays.Clear();
            }

            foreach (var entry in entries)
            {
                if (entry.View.IsAttachedToWindow)
                    entry.WindowManager.RemoveView(entry.View);
                var previous = _stateStore.Get(entry.ActivityId);
                if (previous is not null)
                    Publish(previous with { State = FloatingActivityState.Dismissed });
            }
            return true;
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private FloatingActivitySnapshot PresentCore(
        FloatingActivityDefinition definition,
        IFloatingActivityContent content)
    {
        var context = global::Android.App.Application.Context;
        var manager = context.GetSystemService(global::Android.Content.Context.WindowService) as IWindowManager
                      ?? throw new InvalidOperationException("Android WindowManager is unavailable.");

        lock (_gate)
        {
            if (_overlays.TryGetValue(definition.Id, out var existing))
            {
                var current = _stateStore.Get(definition.Id)
                              ?? SnapshotFor(definition.Id, existing.Layout, FloatingActivityState.Presented);
                var presented = current with { State = FloatingActivityState.Presented, Error = null };
                Publish(presented);
                return presented;
            }
        }

        var activityContent = content.Content as Control
                              ?? new ContentControl { Content = content.Content };
        var surface = new HavenFloatingSurface { Child = activityContent };
        var view = new AvaloniaView(context) { Content = surface };

        var previous = _stateStore.Get(definition.Id);
        var width = previous?.Width > 0 ? previous.Width : 420;
        var height = previous?.Height > 0 ? previous.Height : 280;
        var x = previous?.X ?? 16;
        var y = previous?.Y ?? 72;
        var type = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? WindowManagerTypes.ApplicationOverlay
#pragma warning disable CS0618
            : WindowManagerTypes.Phone;
#pragma warning restore CS0618
        var layout = new WindowManagerLayoutParams(
            ToPixels(width),
            ToPixels(height),
            type,
            WindowManagerFlags.LayoutInScreen | WindowManagerFlags.NotTouchModal,
            Format.Translucent)
        {
            Gravity = GravityFlags.Top | GravityFlags.Left,
            X = ToPixels(x),
            Y = ToPixels(y)
        };

        try
        {
            manager.AddView(view, layout);
        }
        catch (Exception exception) when (exception is global::Android.Views.WindowManagerBadTokenException or global::Java.Lang.SecurityException or InvalidOperationException)
        {
            view.Dispose();
            return PublishFailure(definition, "Android rejected the floating overlay: " + exception.Message);
        }

        lock (_gate) _overlays[definition.Id] = new OverlayEntry(definition.Id, manager, view, layout);
        var snapshot = SnapshotFor(definition.Id, layout, FloatingActivityState.Presented);
        Publish(snapshot);
        return snapshot;
    }

    private FloatingActivitySnapshot PublishFailure(FloatingActivityDefinition definition, string error)
    {
        var previous = _stateStore.Get(definition.Id);
        var snapshot = new FloatingActivitySnapshot(
            definition.Id,
            FloatingActivityState.Failed,
            previous?.Width ?? 420,
            previous?.Height ?? 280,
            previous?.X ?? 16,
            previous?.Y ?? 72,
            error);
        Publish(snapshot);
        return snapshot;
    }

    private FloatingActivitySnapshot SnapshotFor(Guid id, WindowManagerLayoutParams layout, FloatingActivityState state) =>
        new(id, state, ToDip(layout.Width), ToDip(layout.Height), ToDip(layout.X), ToDip(layout.Y));

    private void Publish(FloatingActivitySnapshot snapshot)
    {
        _stateStore.Set(snapshot);
        StateChanged?.Invoke(this, snapshot);
    }

    private static int ToPixels(double dips)
    {
        var density = global::Android.App.Application.Context.Resources?.DisplayMetrics?.Density ?? 1f;
        return Math.Max(1, (int)Math.Round(dips * density));
    }

    private static double ToDip(int pixels)
    {
        var density = global::Android.App.Application.Context.Resources?.DisplayMetrics?.Density ?? 1f;
        return pixels / Math.Max(0.1f, density);
    }

    private static Task<T> RunOnMainThreadAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        if (Looper.MyLooper() == Looper.MainLooper)
            return Task.FromResult(action());

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.ThrowIfCancellationRequested();
        new Handler(Looper.MainLooper!).Post(() =>
        {
            if (completion.Task.IsCompleted) return;
            try { completion.TrySetResult(action()); }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        return completion.Task.WaitAsync(cancellationToken);
    }

    private sealed record OverlayEntry(
        Guid ActivityId,
        IWindowManager WindowManager,
        AvaloniaView View,
        WindowManagerLayoutParams Layout);
}
