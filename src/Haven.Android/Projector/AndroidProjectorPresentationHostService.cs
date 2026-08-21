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
    private readonly IProjectorSessionRecoveryService _recovery;
    private readonly IProjectorExperienceCatalog _catalog;
    private readonly IProjectorActionPlanner _planner;
    private readonly AndroidProjectorApplicationService _applications;
    private readonly DisplayManager? _displayManager;
    private readonly Handler? _mainHandler;
    private PresentationEntry? _active;
    private CancellationTokenSource? _routeCancellation;
    private bool _disposed;

    public AndroidProjectorPresentationHostService(
        IProjectorDisplayRegistry registry,
        IProjectorSessionCoordinator sessions,
        IProjectorSessionRecoveryService recovery,
        IProjectorExperienceCatalog catalog,
        IProjectorActionPlanner planner,
        AndroidProjectorApplicationService applications)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
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
            {
                PromoteRenderCapability(change.Display);
                return;
            }

            var session = _sessions.Current;
            if (session is not null
                && string.Equals(session.TargetDisplay.RuntimeId, change.Display.RuntimeId, StringComparison.Ordinal))
            {
                _ = PopulateGalleryAsync(_active, session);
            }
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
        scene.RouteRequested += request => OnRouteRequested(entry, request);
        surface.InputSubmitted += input =>
        {
            if (ReferenceEquals(input, scene.RouteInput))
                scene.SubmitRoute();
        };
        _active = entry;
        var promoted = PromoteRenderCapability(display);
        _ = RestoreOrStartSessionAsync(entry, promoted);
    }

    private async Task RestoreOrStartSessionAsync(PresentationEntry entry, ProjectorDisplay display)
    {
        ProjectorSessionSnapshot? session = null;
        try
        {
            session = await _recovery.RecoverAsync(display, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn("HavenProjector", "Could not restore Projector session: " + exception.Message);
        }

        if (_disposed || !ReferenceEquals(_active, entry))
            return;

        var latest = _registry.Get(display.RuntimeId);
        if (latest is null || !IsEligible(latest))
            return;

        if (session is null)
        {
            var current = _sessions.Current;
            if (current is not null
                && current.State != ProjectorSessionState.Disconnected
                && string.Equals(current.TargetDisplay.RuntimeId, latest.RuntimeId, StringComparison.Ordinal))
            {
                session = current;
            }
            else
            {
                session = _sessions.Start(latest);
            }
        }

        await PopulateGalleryAsync(entry, session).ConfigureAwait(false);
    }

    private void OnRouteRequested(PresentationEntry entry, string request)
    {
        if (_disposed || !ReferenceEquals(_active, entry))
            return;

        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _routeCancellation, cancellation);
        previous?.Cancel();
        _ = PlanAndExecuteRouteAsync(entry, request, cancellation);
    }

    private async Task PlanAndExecuteRouteAsync(
        PresentationEntry entry,
        string request,
        CancellationTokenSource cancellation)
    {
        try
        {
            var session = _sessions.Current;
            if (session is null
                || session.State == ProjectorSessionState.Disconnected
                || !string.Equals(session.TargetDisplay.RuntimeId, entry.RuntimeId, StringComparison.Ordinal))
            {
                PostRouteStatus(entry, "Projector unavailable", "The target display is no longer the active Projector session.");
                return;
            }

            var plan = await _planner.PlanAsync(request, session, cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(Volatile.Read(ref _routeCancellation), cancellation))
                return;

            if (!plan.CanExecute)
            {
                var title = plan.Status switch
                {
                    ProjectorActionPlanStatus.BlockedByTrust => "Private on this display",
                    ProjectorActionPlanStatus.BlockedByCapability => "Not supported by this display",
                    ProjectorActionPlanStatus.Ambiguous => "Choose a more specific target",
                    ProjectorActionPlanStatus.EmptyRequest => "Describe this screen",
                    _ => "No supported route"
                };
                PostRouteStatus(entry, title, plan.Summary);
                return;
            }

            var available = (await _catalog.GetExperiencesAsync(session, cancellation.Token).ConfigureAwait(false))
                .Where(CanExecuteExperience)
                .ToArray();
            cancellation.Token.ThrowIfCancellationRequested();
            var target = available.FirstOrDefault(experience =>
                string.Equals(experience.Id, plan.TargetExperienceId, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                PostRouteStatus(entry, "Route changed", "That experience is no longer available for this display.");
                return;
            }

            RunOnMainThread(() =>
            {
                if (_disposed || !ReferenceEquals(_active, entry) || cancellation.IsCancellationRequested)
                    return;
                ExecuteExperience(entry, target, plan.Summary);
            });
        }
        catch (System.OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn("HavenProjector", "Could not plan Projector route: " + exception.Message);
            PostRouteStatus(entry, "Projector route failed", exception.Message);
        }
        finally
        {
            Interlocked.CompareExchange(ref _routeCancellation, null, cancellation);
            cancellation.Dispose();
        }
    }

    private static bool CanExecuteExperience(ProjectorExperience experience)
    {
        if (experience.Source == ProjectorExperienceSource.Application
            && experience.LaunchStrategy == ProjectorLaunchStrategy.AndroidApplication)
        {
            return true;
        }

        return experience.LaunchStrategy == ProjectorLaunchStrategy.HavenSurface
            && string.Equals(experience.Id, "desktop", StringComparison.Ordinal);
    }

    private void PostRouteStatus(PresentationEntry entry, string title, string description)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !ReferenceEquals(_active, entry))
                return;
            entry.Scene.SetRouteStatus(title, description);
        });
    }

    private void OnExperienceInvoked(PresentationEntry entry, ProjectorExperience experience)
    {
        if (_disposed || !ReferenceEquals(_active, entry))
            return;
        RunOnMainThread(() => ExecuteExperience(entry, experience, experience.Description));
    }

    private void ExecuteExperience(PresentationEntry entry, ProjectorExperience experience, string status)
    {
        if (_disposed || !ReferenceEquals(_active, entry))
            return;

        if (experience.Source == ProjectorExperienceSource.Application
            && experience.LaunchStrategy == ProjectorLaunchStrategy.AndroidApplication)
        {
            LaunchApplication(entry, experience);
            return;
        }

        if (experience.LaunchStrategy == ProjectorLaunchStrategy.HavenSurface
            && string.Equals(experience.Id, "desktop", StringComparison.Ordinal))
        {
            try
            {
                var active = _sessions.Activate(experience);
                _ = PopulateGalleryAsync(entry, active, status);
            }
            catch (Exception exception)
            {
                global::Android.Util.Log.Warn("HavenProjector", "Could not activate Projector Desktop: " + exception.Message);
                PostExperienceStatus(entry, experience, exception.Message);
            }
            return;
        }

        if (experience.LaunchStrategy == ProjectorLaunchStrategy.HavenSurface)
        {
            PostExperienceStatus(entry, experience, experience.Name + " does not have an Android Projector surface host yet.");
            return;
        }

        PostExperienceStatus(entry, experience, "No Android Projector executor is registered for this experience.");
    }

    private void ReturnToGallery(PresentationEntry entry, string status)
    {
        if (_disposed || !ReferenceEquals(_active, entry))
            return;

        try
        {
            var gallery = _sessions.ReturnToGallery();
            entry.Scene.SetRouteStatus("Projector Gallery", status);
            _ = PopulateGalleryAsync(entry, gallery);
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn("HavenProjector", "Could not return Projector to Gallery: " + exception.Message);
            PostRouteStatus(entry, "Gallery unavailable", exception.Message);
        }
    }

    private void LaunchApplication(PresentationEntry entry, ProjectorExperience experience, ProjectorDesktopScene? desktop = null)
    {
        if (_disposed || !ReferenceEquals(_active, entry))
            return;

        var display = _registry.Snapshot().FirstOrDefault(item =>
            string.Equals(item.RuntimeId, entry.RuntimeId, StringComparison.Ordinal));
        if (display is null)
        {
            var message = "This Projector display is no longer available.";
            desktop?.SetStatus(message);
            PostExperienceStatus(entry, experience, message);
            return;
        }

        var result = _applications.TryLaunch(experience, display);
        if (!result.Started)
        {
            var message = result.Error ?? "Android did not start this application on the Projector display.";
            desktop?.SetStatus(message);
            PostExperienceStatus(entry, experience, message);
            return;
        }

        try
        {
            _sessions.Activate(experience);
            desktop?.SetStatus($"Opened {experience.Name} on {display.Name}.");
            PostExperienceStatus(entry, experience, $"Opened on {display.Name}.");
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn("HavenProjector", "Application opened but Projector session state could not be updated: " + exception.Message);
            desktop?.SetStatus("Opened, but Haven could not update the Projector session state.");
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

    private async Task PopulateGalleryAsync(PresentationEntry entry, ProjectorSessionSnapshot session, string? desktopStatus = null)
    {
        try
        {
            var experiences = (await _catalog.GetExperiencesAsync(session, CancellationToken.None).ConfigureAwait(false))
                .Where(CanExecuteExperience)
                .ToArray();
            var desktopExperience = session.State == ProjectorSessionState.Active
                && string.Equals(session.CurrentExperienceId, "desktop", StringComparison.Ordinal)
                ? experiences.FirstOrDefault(experience =>
                    string.Equals(experience.Id, "desktop", StringComparison.Ordinal)
                    && experience.LaunchStrategy == ProjectorLaunchStrategy.HavenSurface)
                : null;

            if (session.State == ProjectorSessionState.Active
                && string.Equals(session.CurrentExperienceId, "desktop", StringComparison.Ordinal)
                && desktopExperience is null)
            {
                var current = _sessions.Current;
                if (current is not null
                    && current.Id == session.Id
                    && string.Equals(current.CurrentExperienceId, "desktop", StringComparison.Ordinal))
                {
                    session = _sessions.ReturnToGallery();
                    experiences = (await _catalog.GetExperiencesAsync(session, CancellationToken.None).ConfigureAwait(false))
                        .Where(CanExecuteExperience)
                        .ToArray();
                }
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed || !ReferenceEquals(_active, entry))
                    return;
                if (entry.View.Content is not HavenSceneControl surface)
                    return;

                var latest = _sessions.Current;
                if (desktopExperience is not null
                    && latest is not null
                    && latest.Id == session.Id
                    && latest.State == ProjectorSessionState.Active
                    && string.Equals(latest.CurrentExperienceId, desktopExperience.Id, StringComparison.Ordinal)
                    && desktopExperience.IsAvailable(latest.TargetDisplay))
                {
                    var desktop = new ProjectorDesktopScene(latest.TargetDisplay);
                    desktop.SetApplications(experiences);
                    desktop.SetStatus(desktopStatus ?? "Projector Desktop is active.");
                    desktop.GalleryRequested += () => RunOnMainThread(() => ReturnToGallery(entry, "Choose what this screen should become next."));
                    desktop.ApplicationInvoked += application => RunOnMainThread(() => LaunchApplication(entry, application, desktop));
                    surface.Root = desktop.Root;
                    return;
                }

                surface.Root = entry.Scene.Root;
                entry.Scene.SetExperiences(experiences);
            });
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn("HavenProjector", "Could not populate Projector surface: " + exception.Message);
        }
    }

    private void DismissActive(bool stopSession)
    {
        Interlocked.Exchange(ref _routeCancellation, null)?.Cancel();
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
