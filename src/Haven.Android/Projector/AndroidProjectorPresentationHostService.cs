using Android.App;
using Android.Content;
using Android.Hardware.Display;
using Android.OS;
using Avalonia.Android;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.HavenUI.GenerativeUi;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

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
    private readonly AndroidProjectorRemoteExperienceService _remoteExperiences;
    private readonly GenUiAppSessionService _generatedApps;
    private readonly GenerativeUiEventRouter _genUiRouter;
    private readonly GenUiInstanceStore _genUiInstances;
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
        AndroidProjectorApplicationService applications,
        AndroidProjectorRemoteExperienceService remoteExperiences,
        GenUiAppSessionService generatedApps,
        GenerativeUiEventRouter genUiRouter,
        GenUiInstanceStore genUiInstances)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _applications = applications ?? throw new ArgumentNullException(nameof(applications));
        _remoteExperiences = remoteExperiences ?? throw new ArgumentNullException(nameof(remoteExperiences));
        _generatedApps = generatedApps ?? throw new ArgumentNullException(nameof(generatedApps));
        _genUiRouter = genUiRouter ?? throw new ArgumentNullException(nameof(genUiRouter));
        _genUiInstances = genUiInstances ?? throw new ArgumentNullException(nameof(genUiInstances));

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
            var generated = entry.GeneratedMount;
            if (generated is not null && generated.Surface.OwnsInput(input))
            {
                _ = generated.Surface.SubmitInputAsync(input);
                return;
            }

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

        if (session.State == ProjectorSessionState.Active
            && !string.IsNullOrWhiteSpace(session.CurrentExperienceId)
            && GeneratedProjectorExperienceProvider.TryGetInstanceId(session.CurrentExperienceId, out _))
        {
            var restored = (await _catalog.GetExperiencesAsync(session, CancellationToken.None).ConfigureAwait(false))
                .FirstOrDefault(experience =>
                    experience.Source == ProjectorExperienceSource.GeneratedUi
                    && experience.LaunchStrategy == ProjectorLaunchStrategy.GeneratedUi
                    && string.Equals(experience.Id, session.CurrentExperienceId, StringComparison.OrdinalIgnoreCase));
            if (restored is not null)
            {
                await OpenGeneratedExperienceAsync(entry, restored, activateSession: false, CancellationToken.None).ConfigureAwait(false);
                return;
            }

            session = _sessions.ReturnToGallery();
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
            return true;

        if (experience.Source == ProjectorExperienceSource.GeneratedUi
            && experience.LaunchStrategy == ProjectorLaunchStrategy.GeneratedUi)
            return GeneratedProjectorExperienceProvider.TryGetInstanceId(experience.Id, out _);

        if (experience.Source == ProjectorExperienceSource.RemoteDevice
            && experience.LaunchStrategy == ProjectorLaunchStrategy.RemoteDevice)
            return true;

        return experience.Source == ProjectorExperienceSource.BuiltIn
            && experience.LaunchStrategy == ProjectorLaunchStrategy.HavenSurface
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

        if (experience.Source == ProjectorExperienceSource.GeneratedUi
            && experience.LaunchStrategy == ProjectorLaunchStrategy.GeneratedUi)
        {
            _ = OpenGeneratedExperienceAsync(entry, experience, activateSession: true, CancellationToken.None);
            return;
        }

        if (experience.Source == ProjectorExperienceSource.RemoteDevice
            && experience.LaunchStrategy == ProjectorLaunchStrategy.RemoteDevice)
        {
            _ = RouteRemoteExperienceAsync(entry, experience, CancellationToken.None);
            return;
        }

        if (experience.Source == ProjectorExperienceSource.BuiltIn
            && experience.LaunchStrategy == ProjectorLaunchStrategy.HavenSurface
            && string.Equals(experience.Id, "desktop", StringComparison.Ordinal))
        {
            try
            {
                ReleaseGeneratedSurface(entry, persist: true);
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

        PostExperienceStatus(entry, experience, "No Android Projector executor is registered for this experience.");
    }

    private async Task OpenGeneratedExperienceAsync(
        PresentationEntry entry,
        ProjectorExperience experience,
        bool activateSession,
        CancellationToken cancellationToken)
    {
        if (!GeneratedProjectorExperienceProvider.TryGetInstanceId(experience.Id, out var instanceId))
        {
            PostExperienceStatus(entry, experience, "That generated experience has an invalid instance identity.");
            return;
        }

        PostExperienceStatus(entry, experience, "Loading the saved generated experience…");
        await ReleaseGeneratedSurfaceAsync(entry, persist: true).ConfigureAwait(false);

        GenUiAppDefinition definition;
        try
        {
            definition = await _generatedApps.OpenAsync(instanceId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or IOException)
        {
            PostExperienceStatus(entry, experience, "Generated experience could not be opened: " + exception.Message);
            return;
        }

        var completion = new TaskCompletionSource<AndroidProjectorRemoteRouteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        RunOnMainThread(() =>
        {
            HavenGenUiSceneSurface? generated = null;
            var activatedHere = false;
            try
            {
                if (_disposed || !ReferenceEquals(_active, entry))
                {
                    completion.TrySetResult(new(false, "The Projector display disappeared while the generated experience was loading."));
                    return;
                }
                if (entry.View.Content is not HavenSceneControl host)
                {
                    completion.TrySetResult(new(false, "The Projector display no longer has a Haven surface host."));
                    return;
                }

                var current = _sessions.Current;
                if (!activateSession
                    && (current is null
                        || current.State != ProjectorSessionState.Active
                        || !string.Equals(current.CurrentExperienceId, experience.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    completion.TrySetResult(new(false, "The generated Projector session is no longer active."));
                    return;
                }

                generated = new HavenGenUiSceneSurface(_genUiRouter, _genUiInstances);
                generated.PresentExisting(definition.Document);
                var root = BuildGeneratedExperienceRoot(entry, experience, generated);
                if (activateSession)
                {
                    _sessions.Activate(experience);
                    activatedHere = true;
                }

                host.Root = root;
                entry.GeneratedMount = new(instanceId, generated);
                generated = null;
                completion.TrySetResult(new(true, "Generated experience is active on the Projector display."));
            }
            catch (Exception exception)
            {
                generated?.Dispose();
                if (activatedHere)
                {
                    try
                    {
                        var current = _sessions.Current;
                        if (current is not null
                            && current.State == ProjectorSessionState.Active
                            && string.Equals(current.CurrentExperienceId, experience.Id, StringComparison.OrdinalIgnoreCase))
                            _sessions.ReturnToGallery();
                    }
                    catch (Exception rollbackException)
                    {
                        global::Android.Util.Log.Warn("HavenProjector", "Could not roll back failed generated Projector activation: " + rollbackException.Message);
                    }
                }
                completion.TrySetResult(new(false, "Generated experience could not be hosted: " + exception.Message));
            }
        });

        AndroidProjectorRemoteRouteResult mounted;
        try
        {
            mounted = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CloseGeneratedInstanceSafelyAsync(instanceId, persist: false).ConfigureAwait(false);
            throw;
        }

        if (!mounted.Succeeded)
        {
            await CloseGeneratedInstanceSafelyAsync(instanceId, persist: false).ConfigureAwait(false);
            PostExperienceStatus(entry, experience, mounted.Message);
            if (!activateSession)
            {
                var current = _sessions.Current;
                if (current is not null && current.State == ProjectorSessionState.Active
                    && string.Equals(current.CurrentExperienceId, experience.Id, StringComparison.OrdinalIgnoreCase))
                    _sessions.ReturnToGallery();
            }
            return;
        }

        PostExperienceStatus(entry, experience, mounted.Message);
    }

    private Page BuildGeneratedExperienceRoot(
        PresentationEntry entry,
        ProjectorExperience experience,
        HavenGenUiSceneSurface generated)
    {
        var root = new Page
        {
            Name = "Projector.Generated.Root",
            Layout = HavenLayout.Grid,
            Columns = "1fr",
            Rows = "Auto 1fr"
        };
        root.SetValue(HavenProperties.Padding, HavenThickness.Parse("18px 24px"));
        root.SetValue(HavenProperties.Background, "Surface");
        root.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);

        var header = new Container { Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "Auto" };
        header.SetValue(HavenProperties.Row, 0);
        header.SetValue(HavenProperties.Margin, HavenThickness.Parse("0px 0px 14px 0px"));
        var title = new HavenText(experience.Name) { Level = TextLevel.H2 };
        title.SetValue(HavenProperties.Column, 0);
        header.Add(title);
        var gallery = new HavenButton
        {
            Content = "Gallery",
            IconKey = "chevron-left",
            Variant = ButtonVariant.Secondary
        };
        gallery.SetValue(HavenProperties.Column, 1);
        gallery.Invoked += (_, _) => RunOnMainThread(() => ReturnToGallery(entry, "Generated experience saved."));
        header.Add(gallery);
        root.Add(header);

        generated.Root.SetValue(HavenProperties.Row, 1);
        generated.Root.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        root.Add(generated.Root);
        return root;
    }

    private async Task RouteRemoteExperienceAsync(
        PresentationEntry entry,
        ProjectorExperience experience,
        CancellationToken cancellationToken)
    {
        PostExperienceStatus(entry, experience, "Waiting for the remote Projector screen to acknowledge the route…");
        var result = await _remoteExperiences.RouteAsync(experience, cancellationToken).ConfigureAwait(false);
        if (_disposed || !ReferenceEquals(_active, entry))
            return;

        if (result.Succeeded)
            PostExperienceStatus(entry, experience, result.Message);
        else
            PostExperienceStatus(entry, experience, "Remote Projector route failed: " + result.Message);
    }

    public IReadOnlyList<AndroidProjectorRemoteTarget> GetRoutableRemoteTargets()
    {
        if (_disposed)
            return [];
        var entry = Volatile.Read(ref _active);
        if (entry is null)
            return [];
        var display = _registry.Get(entry.RuntimeId);
        var session = _sessions.Current;
        if (display is null || !IsEligible(display)
            || session is null
            || session.State is ProjectorSessionState.Disconnected or ProjectorSessionState.Stopping or ProjectorSessionState.Failed
            || !string.Equals(session.TargetDisplay.RuntimeId, entry.RuntimeId, StringComparison.Ordinal))
            return [];

        return [new AndroidProjectorRemoteTarget(display.RuntimeId, display.StableIdentity, display.Name)];
    }

    public async Task<AndroidProjectorRemoteRouteResult> RouteRemoteCommandAsync(
        string runtimeId,
        string experienceId,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(experienceId?.Trim(), "desktop", StringComparison.OrdinalIgnoreCase))
            return new(false, "This Android Projector host only exposes the real Desktop experience to Mesh.");
        if (string.IsNullOrWhiteSpace(runtimeId))
            return new(false, "The remote Projector runtime id is required.");

        var entry = Volatile.Read(ref _active);
        var display = _registry.Get(runtimeId.Trim());
        var session = _sessions.Current;
        if (entry is null
            || display is null
            || !IsEligible(display)
            || !string.Equals(entry.RuntimeId, display.RuntimeId, StringComparison.Ordinal)
            || session is null
            || session.State is ProjectorSessionState.Disconnected or ProjectorSessionState.Stopping or ProjectorSessionState.Failed
            || !string.Equals(session.TargetDisplay.RuntimeId, display.RuntimeId, StringComparison.Ordinal))
        {
            return new(false, "That Projector screen is disconnected or no longer hosted on this Android device.");
        }

        var experiences = (await _catalog.GetExperiencesAsync(session, cancellationToken).ConfigureAwait(false))
            .Where(CanExecuteExperience)
            .ToArray();
        var desktop = experiences.FirstOrDefault(candidate =>
            candidate.Source == ProjectorExperienceSource.BuiltIn
            && candidate.LaunchStrategy == ProjectorLaunchStrategy.HavenSurface
            && string.Equals(candidate.Id, "desktop", StringComparison.Ordinal));
        if (desktop is null)
            return new(false, "Projector Desktop is not executable on that target.");

        var completion = new TaskCompletionSource<AndroidProjectorRemoteRouteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        RunOnMainThread(() =>
        {
            try
            {
                if (_disposed || !ReferenceEquals(_active, entry))
                {
                    completion.TrySetResult(new(false, "The Projector screen disappeared before the route was acknowledged."));
                    return;
                }
                var latestDisplay = _registry.Get(entry.RuntimeId);
                var latestSession = _sessions.Current;
                if (latestDisplay is null || !IsEligible(latestDisplay)
                    || latestSession is null
                    || latestSession.State is ProjectorSessionState.Disconnected or ProjectorSessionState.Stopping or ProjectorSessionState.Failed
                    || !string.Equals(latestSession.TargetDisplay.RuntimeId, entry.RuntimeId, StringComparison.Ordinal))
                {
                    completion.TrySetResult(new(false, "The Projector screen disconnected before the route was acknowledged."));
                    return;
                }

                ReleaseGeneratedSurface(entry, persist: true);
                var active = _sessions.Activate(desktop);
                if (!ShowDesktop(entry, active, desktop, experiences, "Routed from a trusted Mesh device."))
                {
                    completion.TrySetResult(new(false, "Android could not attach Projector Desktop to the requested screen."));
                    return;
                }

                completion.TrySetResult(new(true, $"Projector Desktop is active on {latestDisplay.Name}."));
            }
            catch (Exception exception)
            {
                completion.TrySetResult(new(false, "Remote Projector route was rejected: " + exception.Message));
            }
        });

        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void ReturnToGallery(PresentationEntry entry, string status)
    {
        if (_disposed || !ReferenceEquals(_active, entry))
            return;

        try
        {
            ReleaseGeneratedSurface(entry, persist: true);
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
            ReleaseGeneratedSurface(entry, persist: true);
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

    private bool ShowDesktop(
        PresentationEntry entry,
        ProjectorSessionSnapshot session,
        ProjectorExperience desktopExperience,
        IReadOnlyList<ProjectorExperience> experiences,
        string status)
    {
        if (_disposed || !ReferenceEquals(_active, entry) || entry.View.Content is not HavenSceneControl surface)
            return false;
        if (session.State != ProjectorSessionState.Active
            || !string.Equals(session.CurrentExperienceId, desktopExperience.Id, StringComparison.OrdinalIgnoreCase)
            || !desktopExperience.IsAvailable(session.TargetDisplay))
            return false;

        ReleaseGeneratedSurface(entry, persist: true);
        var desktop = new ProjectorDesktopScene(session.TargetDisplay);
        desktop.SetApplications(experiences);
        desktop.SetStatus(status);
        desktop.GalleryRequested += () => RunOnMainThread(() => ReturnToGallery(entry, "Choose what this screen should become next."));
        desktop.ApplicationInvoked += application => RunOnMainThread(() => LaunchApplication(entry, application, desktop));
        surface.Root = desktop.Root;
        return true;
    }

    private void ReleaseGeneratedSurface(PresentationEntry entry, bool persist)
    {
        var mount = entry.GeneratedMount;
        entry.GeneratedMount = null;
        if (mount is null)
            return;
        mount.Surface.Dispose();
        _ = CloseGeneratedInstanceSafelyAsync(mount.InstanceId, persist);
    }

    private async Task ReleaseGeneratedSurfaceAsync(PresentationEntry entry, bool persist)
    {
        var mount = entry.GeneratedMount;
        entry.GeneratedMount = null;
        if (mount is null)
            return;
        mount.Surface.Dispose();
        await CloseGeneratedInstanceSafelyAsync(mount.InstanceId, persist).ConfigureAwait(false);
    }

    private async Task CloseGeneratedInstanceSafelyAsync(Guid instanceId, bool persist)
    {
        try
        {
            await _generatedApps.CloseAsync(instanceId, persist, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn("HavenProjector", "Could not close generated Projector state: " + exception.Message);
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
                    && ShowDesktop(
                        entry,
                        latest,
                        desktopExperience,
                        experiences,
                        desktopStatus ?? "Projector Desktop is active."))
                {
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
        ReleaseGeneratedSurface(active, persist: true);

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
        ProjectorGalleryScene Scene)
    {
        public ProjectorGeneratedSurfaceMount? GeneratedMount { get; set; }
    }

    private sealed record ProjectorGeneratedSurfaceMount(Guid InstanceId, HavenGenUiSceneSurface Surface);
}
