using Android.Content;
using Android.Hardware.Display;
using Android.OS;
using Android.Views;
using Haven.Application;

namespace Haven.Android;

/// <summary>
/// Observes Android logical displays and publishes only externally targetable displays into
/// Projector's platform-neutral registry. Android runtime display IDs remain ephemeral; this
/// adapter deliberately leaves stable identity unknown until the platform can prove one.
/// </summary>
public sealed class AndroidProjectorDisplayService : Java.Lang.Object, DisplayManager.IDisplayListener
{
    private readonly IProjectorDisplayRegistry _registry;
    private readonly DisplayManager? _displayManager;
    private readonly Handler? _mainHandler;
    private readonly object _gate = new();
    private readonly HashSet<string> _knownRuntimeIds = new(StringComparer.Ordinal);
    private bool _disposed;

    public AndroidProjectorDisplayService(IProjectorDisplayRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        var context = global::Android.App.Application.Context;
        _displayManager = context.GetSystemService(Context.DisplayService) as DisplayManager;
        if (_displayManager is null)
        {
            UnavailableReason = "Android did not expose DisplayManager.";
            return;
        }

        _mainHandler = new Handler(Looper.MainLooper!);
        _displayManager.RegisterDisplayListener(this, _mainHandler);
        RefreshAll();
    }

    public bool IsAvailable => !_disposed && _displayManager is not null;
    public string? UnavailableReason { get; }

    public void OnDisplayAdded(int displayId)
    {
        if (_disposed) return;
        RefreshDisplay(displayId);
    }

    public void OnDisplayChanged(int displayId)
    {
        if (_disposed) return;
        RefreshDisplay(displayId);
    }

    public void OnDisplayRemoved(int displayId)
    {
        if (_disposed) return;
        RemoveRuntimeId(RuntimeId(displayId));
    }

    private void RefreshAll()
    {
        var manager = _displayManager;
        if (_disposed || manager is null) return;

        var presentationIds = PresentationDisplayIds(manager);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var display in manager.GetDisplays() ?? [])
        {
            if (display.DisplayId == 0) continue;
            var snapshot = BuildSnapshot(display, presentationIds.Contains(display.DisplayId));
            observed.Add(snapshot.RuntimeId);
            _registry.Upsert(snapshot);
        }

        string[] stale;
        lock (_gate)
        {
            stale = _knownRuntimeIds.Where(id => !observed.Contains(id)).ToArray();
            _knownRuntimeIds.Clear();
            foreach (var id in observed) _knownRuntimeIds.Add(id);
        }

        foreach (var id in stale) _registry.Remove(id);
    }

    private void RefreshDisplay(int displayId)
    {
        if (displayId == 0) return;
        var manager = _displayManager;
        if (_disposed || manager is null) return;

        var display = manager.GetDisplay(displayId);
        if (display is null)
        {
            RemoveRuntimeId(RuntimeId(displayId));
            return;
        }

        var isPresentationDisplay = PresentationDisplayIds(manager).Contains(displayId);
        var snapshot = BuildSnapshot(display, isPresentationDisplay);
        lock (_gate) _knownRuntimeIds.Add(snapshot.RuntimeId);
        _registry.Upsert(snapshot);
    }

    private void RemoveRuntimeId(string runtimeId)
    {
        lock (_gate) _knownRuntimeIds.Remove(runtimeId);
        _registry.Remove(runtimeId);
    }

    private static HashSet<int> PresentationDisplayIds(DisplayManager manager) =>
        (manager.GetDisplays(DisplayManager.DisplayCategoryPresentation) ?? [])
            .Select(display => display.DisplayId)
            .ToHashSet();

    private static ProjectorDisplay BuildSnapshot(Display display, bool isPresentationDisplay)
    {
        var mode = display.GetMode();
        var presentation = isPresentationDisplay
            ? ProjectorCapabilityState.Available
            : ProjectorCapabilityState.Unavailable;
        var capabilities = ProjectorCapabilities.Unknown with
        {
            PresentationDisplay = presentation
        };

        return new ProjectorDisplay(
            RuntimeId(display.DisplayId),
            StableIdentity: null,
            string.IsNullOrWhiteSpace(display.Name) ? $"Display {display.DisplayId}" : display.Name,
            mode?.PhysicalWidth,
            mode?.PhysicalHeight,
            DensityDpi: null,
            mode is null ? null : mode.RefreshRate,
            RotationDegrees(display),
            IsDefault: false,
            ProjectorTransportKind.NativeDisplay,
            ProjectorConnectionKind.Unknown,
            ProjectorDisplayTrust.Public,
            capabilities,
            DateTimeOffset.UtcNow);
    }

    private static int RotationDegrees(Display display) => ((int)display.Rotation) switch
    {
        1 => 90,
        2 => 180,
        3 => 270,
        _ => 0
    };

    private static string RuntimeId(int displayId) => $"android-display:{displayId}";

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            if (disposing && _displayManager is not null)
            {
                try { _displayManager.UnregisterDisplayListener(this); }
                catch (Exception exception)
                {
                    global::Android.Util.Log.Warn("HavenProjector", "Could not unregister display listener: " + exception.Message);
                }
            }

            string[] known;
            lock (_gate)
            {
                known = _knownRuntimeIds.ToArray();
                _knownRuntimeIds.Clear();
            }
            foreach (var id in known) _registry.Remove(id);
        }

        base.Dispose(disposing);
    }
}
