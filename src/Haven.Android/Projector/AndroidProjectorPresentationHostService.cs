using Android.App;
using Android.Content;
using Android.Hardware.Display;
using Android.OS;
using Avalonia.Android;
using Avalonia.Threading;
using Haven.Application;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;

namespace Haven.Android;

public sealed class AndroidProjectorPresentationHostService : IDisposable
{
    private const string RuntimePrefix = "android-display:";

    private readonly IProjectorDisplayRegistry _registry;
    private readonly IProjectorSessionCoordinator _sessions;
    private readonly IProjectorExperienceCatalog _catalog;
    private readonly AndroidProjectorApplicationService _applications;
    private readonly DisplayManager? _displayManager;
    private readonly Handler? _mainHandler;
    private PresentationEntry? _active;
    private bool _disposed;

    public AndroidProjectorPresentationHostService(
        IProjectorDisplayRegistry registry,
        IProjectorSessionCoordinator sessions,
        IProjectorExperienceCatalog catalog,
        AndroidProjectorApplicationService applications)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _applications = applications ?? throw new ArgumentNullException(nameof(applications));

        var context = global::Android.App.Application.Context;
        _displayManager = context.GetSystemService(Context.DisplayService) as DisplayManager;
        if (_displayManager is null)
        {
            UnavailableReason = "Android did not expose DisplayManager for Projector presentation hosting.";
            return;
        }

        _mainHandler = new Handler(Looper.MainLooper!);
        _registry.Changed += OnDisplayChanged;
        RunOnMainThread(ReconcileCore);
    }

    public bool IsAvailable => !_disposed && _displayManager is not null;
    public string? UnavailableReason { get; }

    private void OnDisplayChanged(ProjectorDisplayChange change)
    {
        if (_disposed) return;
        RunOnMainThread(() => HandleDisplayChange(change));
    }

    private void HandleDisplayChange(ProjectorDisplayChange change)
    {
        if (_disposed) return;

        if (change.Kind == ProjectorDisplayChangeKind.Removed
            && _active is not null
            && string.Equals(_active.RuntimeId, change.RuntimeId, StringComparison.Ordinal))
        {
            DismissActive(stopSession: false);
            ReconcileCore();
            return;
        }

        if (change.Display is not null
            && _active is not null
            && string.Equals(_active.RuntimeId, change.Display.RuntimeId, StringComparison.Ordinal))
        {
            if (!IsEligible(change.Display))
            {
                DismissActive(stopSession: true);
                ReconcileCore();
                return;
            }

            if (change.Display.Capabilities.RenderHavenSurface != ProjectorCapabilityState.Available)
                PromoteRenderCapability(change.Display);
            return;
        }

        if (_active is null)
            ReconcileCore();
    }

    private void ReconcileCore()
    {
        if (_disposed || _displayManager is null || _active is not null) return;

        var current = _sessions.Current;
        if (current is not null
            && current.State != ProjectorSessionState.Disconnected
            && IsEligible(current.TargetDisplay))
        {
            EnsureHosted(current.TargetDisplay);
            if (_active is not null) return;
        }

        var target = _registry.Snapshot().FirstOrDefault(IsEligible);
        if (target is not null)
            EnsureHosted(target);
    }

    private void EnsureHosted(ProjectorDisplay display)
    {
        if (_disposed || _displayManager is null || _active is not null || !IsEligible(display))
            return;
        if (!TryGetDisplayId(display.RuntimeId, out var displayId))
            return;

        var platformDisplay = _displayManager.GetDisplay(displayId);
        if (platformDisplay is null)
            return;

        var scene = new ProjectorGalleryScene(display.Name);
        var surface = new HavenSceneControl
        {
            Platform = HavenPlatform.Android,
            Root = scene.Root
        };
        var presentation = new Presentation(global::Android.App.Application.Context, platformDisplay);
        var view = new AvaloniaView(presentation.Context) { Content = surface };
        presentation.SetContentView(view);

        try
        {
            presentation.Show();
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn("HavenProjector", "Could not open Projector presentation: " + exception.Message);
            view.Dispose();
            presentation.Dispose();
            return;
        }

        var entry = new PresentationEntry(display.RuntimeId, presentation, view, scene);
        scene.ExperienceInvoked += experience => OnExperienceInvoked(entry, experience);
        _active = entry;
        var promoted = PromoteRenderCapability(display);

        var current = _sessions.Current;
        ProjectorSessionSnapshot session;
        if (current is null
            || current.State == ProjectorSessionState.Disconnected
            || !string.Equals(current.TargetDisplay.RuntimeId, promoted.RuntimeId, StringComparison.Ordinal))
        {
            session = _sessions.Start(promoted);
        }
        else
        {
            session = _sessions.Current ?? current;
        }

        _ = PopulateGalleryAsync(entry, session);
    }

    private void OnExperienceInvoked(PresentationEntry entry, ProjectorExperience experience)
    {
        if (_disposed || !ReferenceEquals(_active, entry))
            return;
        if (experience.Source != ProjectorExperienceSource.Application
            || experience.LaunchStrategy != ProjectorLaunchStrategy.AndroidApplication)
        {
            return;
        }

        RunOnMainThread(() => LaunchApplication(entry, experience));
    }

    private void LaunchApplication(PresentationEntry entry, ProjectorExperience experience)
    {
        if (_disposed || !ReferenceEquals(_active, entry))
            return;

        var display = _registry.Snapshot().FirstOrDefault(item =>
            string.Equals(item.RuntimeId, entry.RuntimeId, StringComparison.Ordinal));
        if (display is null)
        {
            PostExperienceStatus(entry, experience, "This Projector display is no longer available.");
            return;
        }

        var result = _applications.TryLaunch(experience, display);
        if (!result.Started)
        {
            PostExperienceStatus(entry, experience, result.Error ?? "Android did not start this application on the Projector display.");
            return;
        }

        try
        {
            _sessions.Activate(experience);
            PostExperienceStatus(entry, experience, $"Opened on {display.Name}.");
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn("HavenProjector", "Application opened but Projector session state could not be updated: " + exception.Message);
            PostExperienceStatus(entry, experience, "Opened, but Haven could not update the Projector session state.");
        }
    }

    private void PostExperienceStatus(PresentationEntry entry, ProjectorExperience experience, string description)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !ReferenceEquals(_active, entry))
                return;
            entry.Scene.SetExperienceStatus(experience, description);
        });
    }

    private ProjectorDisplay PromoteRenderCapability(ProjectorDisplay display)
    {
        if (display.Capabilities.RenderHavenSurface == ProjectorCapabilityState.Available)
            return display;

        var promoted = display with
        {
            Capabilities = display.Capabilities with
            {
                RenderHavenSurface = ProjectorCapabilityState.Available
            },
            ObservedAt = DateTimeOffset.UtcNow
        };
        _registry.Upsert(promoted);
        return promoted;
    }

    private async Task PopulateGalleryAsync(PresentationEntry entry, ProjectorSessionSnapshot session)
    {
        try
        {
            var experiences = await _catalog.GetExperiencesAsync(session, CancellationToken.None).ConfigureAwait(false);
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed || !ReferenceEquals(_active, entry))
                    return;
                entry.Scene.SetExperiences(experiences);
            });
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn("HavenProjector", "Could not populate Projector gallery: " + exception.Message);
        }
    }

    private void DismissActive(bool stopSession)
    {
        var active = _active;
        _active = null;
        if (active is null) return;

        if (stopSession)
        {
            var current = _sessions.Current;
            if (current is not null
                && string.Equals(current.TargetDisplay.RuntimeId, active.RuntimeId, StringComparison.Ordinal))
            {
                _sessions.Stop();
            }
        }

        try { active.Presentation.Dismiss(); }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn("HavenProjector", "Could not dismiss Projector presentation: " + exception.Message);
        }

        active.View.Dispose();
        active.Presentation.Dispose();
    }

    private void RunOnMainThread(Action action)
    {
        if (_disposed || _mainHandler is null) return;
        if (Looper.MyLooper() == Looper.MainLooper)
        {
            action();
            return;
        }

        _mainHandler.Post(action);
    }

    private static bool IsEligible(ProjectorDisplay display) =>
        display.Transport == ProjectorTransportKind.NativeDisplay
        && !display.IsDefault
        && display.Capabilities.PresentationDisplay == ProjectorCapabilityState.Available;

    private static bool TryGetDisplayId(string runtimeId, out int displayId)
    {
        displayId = -1;
        return runtimeId.StartsWith(RuntimePrefix, StringComparison.Ordinal)
            && int.TryParse(runtimeId.AsSpan(RuntimePrefix.Length), out displayId)
            && displayId >= 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _registry.Changed -= OnDisplayChanged;
        RunOnMainThread(() => DismissActive(stopSession: true));
        _disposed = true;
    }

    private sealed record PresentationEntry(
        string RuntimeId,
        Presentation Presentation,
        AvaloniaView View,
        ProjectorGalleryScene Scene);
}
