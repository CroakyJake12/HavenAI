namespace Haven.Application;

public enum ProjectorSessionState { Connecting, Gallery, Launching, Active, Suspended, Disconnected, Restoring, Stopping, Failed }
public enum ProjectorExperienceSource { BuiltIn, Application, Plugin, Workspace, GeneratedUi, RemoteDevice, RecentActivity }
public enum ProjectorTransportKind { NativeDisplay, HavenReceiver }
public enum ProjectorConnectionKind { Unknown, Wired, NativeWireless, Virtual, Receiver }
public enum ProjectorDisplayTrust { Private, Trusted, Shared, Public }
public enum ProjectorCapabilityState { Unknown, Unavailable, Available }
public enum ProjectorCapability { RenderHavenSurface, LaunchAndroidActivity, LaunchBounds, PointerInput, KeyboardInput, TouchInput, ControllerInput, TransferAudio, TransferVideo, SyncClipboard, Reconnect, PresentationDisplay, NativeWirelessDisplay }
public enum ProjectorInteractionProfile { Desktop, LeanBack, Presentation, Touch, Controller, Mixed }
public enum ProjectorExperiencePersistence { Session, Persistent }
public enum ProjectorLaunchStrategy { HavenSurface, AndroidApplication, Workspace, GeneratedUi, RemoteDevice, RoutedContent }
public enum ProjectorDisplayChangeKind { Added, Changed, Removed }

public sealed record ProjectorCapabilities(
    ProjectorCapabilityState RenderHavenSurface, ProjectorCapabilityState LaunchAndroidActivity,
    ProjectorCapabilityState LaunchBounds, ProjectorCapabilityState PointerInput, ProjectorCapabilityState KeyboardInput,
    ProjectorCapabilityState TouchInput, ProjectorCapabilityState ControllerInput, ProjectorCapabilityState TransferAudio,
    ProjectorCapabilityState TransferVideo, ProjectorCapabilityState SyncClipboard, ProjectorCapabilityState Reconnect,
    ProjectorCapabilityState PresentationDisplay, ProjectorCapabilityState NativeWirelessDisplay)
{
    public static ProjectorCapabilities Unknown { get; } = new(
        ProjectorCapabilityState.Unknown, ProjectorCapabilityState.Unknown, ProjectorCapabilityState.Unknown,
        ProjectorCapabilityState.Unknown, ProjectorCapabilityState.Unknown, ProjectorCapabilityState.Unknown,
        ProjectorCapabilityState.Unknown, ProjectorCapabilityState.Unknown, ProjectorCapabilityState.Unknown,
        ProjectorCapabilityState.Unknown, ProjectorCapabilityState.Unknown, ProjectorCapabilityState.Unknown,
        ProjectorCapabilityState.Unknown);

    public ProjectorCapabilityState Get(ProjectorCapability capability) => capability switch
    {
        ProjectorCapability.RenderHavenSurface => RenderHavenSurface,
        ProjectorCapability.LaunchAndroidActivity => LaunchAndroidActivity,
        ProjectorCapability.LaunchBounds => LaunchBounds,
        ProjectorCapability.PointerInput => PointerInput,
        ProjectorCapability.KeyboardInput => KeyboardInput,
        ProjectorCapability.TouchInput => TouchInput,
        ProjectorCapability.ControllerInput => ControllerInput,
        ProjectorCapability.TransferAudio => TransferAudio,
        ProjectorCapability.TransferVideo => TransferVideo,
        ProjectorCapability.SyncClipboard => SyncClipboard,
        ProjectorCapability.Reconnect => Reconnect,
        ProjectorCapability.PresentationDisplay => PresentationDisplay,
        ProjectorCapability.NativeWirelessDisplay => NativeWirelessDisplay,
        _ => ProjectorCapabilityState.Unknown
    };
}

public sealed record ProjectorDisplay(
    string RuntimeId, string? StableIdentity, string Name, int? WidthPixels, int? HeightPixels, int? DensityDpi,
    double? RefreshRateHz, int? RotationDegrees, bool IsDefault, ProjectorTransportKind Transport,
    ProjectorConnectionKind Connection, ProjectorDisplayTrust Trust, ProjectorCapabilities Capabilities, DateTimeOffset ObservedAt);

public sealed record ProjectorExperience(
    string Id, string Name, string Description, string IconKey, string? ArtworkKey, ProjectorExperienceSource Source,
    ProjectorLaunchStrategy LaunchStrategy, ProjectorInteractionProfile InteractionProfile,
    ProjectorExperiencePersistence Persistence, IReadOnlyList<ProjectorCapability> RequiredCapabilities)
{
    public bool IsAvailable(ProjectorCapabilities capabilities) => RequiredCapabilities.All(capability =>
        capabilities.Get(capability) == ProjectorCapabilityState.Available);
}

public sealed record ProjectorControllerAction(string Id, string Label, string IconKey, string ActionKey);
public sealed record ProjectorControllerDefinition(string Id, IReadOnlyList<ProjectorControllerAction> Actions);
public sealed record ProjectorDisplayProfile(Guid Id, string? StableIdentity, string FriendlyName, ProjectorDisplayTrust Trust, string? PreferredExperienceId, string? PreferredControllerId);
public sealed record ProjectorSessionSnapshot(
    Guid Id, ProjectorSessionState State, ProjectorDisplay TargetDisplay, string? CurrentExperienceId, string? PreviousExperienceId,
    ProjectorControllerDefinition? Controller, string? WorkspaceId, IReadOnlyList<string> InputDeviceKinds,
    DateTimeOffset StartedAt, DateTimeOffset UpdatedAt, DateTimeOffset? DisconnectedAt = null, string? Error = null);
public sealed record ProjectorDisplayChange(ProjectorDisplayChangeKind Kind, string RuntimeId, ProjectorDisplay? Display);

public interface IProjectorTransport
{
    string Id { get; }
    ProjectorTransportKind Kind { get; }
    ProjectorCapabilities Capabilities { get; }
}

public interface IProjectorExperienceProvider
{
    ValueTask<IReadOnlyList<ProjectorExperience>> GetExperiencesAsync(ProjectorSessionSnapshot? session, CancellationToken cancellationToken);
}

public interface IProjectorDisplayRegistry
{
    event Action<ProjectorDisplayChange>? Changed;
    IReadOnlyList<ProjectorDisplay> Snapshot();
    ProjectorDisplay? Get(string runtimeId);
    ProjectorDisplayChange Upsert(ProjectorDisplay display);
    ProjectorDisplayChange? Remove(string runtimeId);
}

public sealed class ProjectorDisplayRegistry : IProjectorDisplayRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ProjectorDisplay> _displays = new(StringComparer.Ordinal);
    public event Action<ProjectorDisplayChange>? Changed;

    public IReadOnlyList<ProjectorDisplay> Snapshot()
    {
        lock (_gate) return _displays.Values.OrderBy(display => display.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(display => display.RuntimeId, StringComparer.Ordinal).ToArray();
    }

    public ProjectorDisplay? Get(string runtimeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeId);
        lock (_gate) return _displays.TryGetValue(runtimeId, out var display) ? display : null;
    }

    public ProjectorDisplayChange Upsert(ProjectorDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentException.ThrowIfNullOrWhiteSpace(display.RuntimeId);
        ProjectorDisplayChange change;
        var publish = false;
        lock (_gate)
        {
            if (!_displays.TryGetValue(display.RuntimeId, out var existing))
            {
                _displays[display.RuntimeId] = display;
                change = new(ProjectorDisplayChangeKind.Added, display.RuntimeId, display);
                publish = true;
            }
            else
            {
                _displays[display.RuntimeId] = display;
                change = new(ProjectorDisplayChangeKind.Changed, display.RuntimeId, display);
                publish = existing with { ObservedAt = display.ObservedAt } != display;
            }
        }
        if (publish) Changed?.Invoke(change);
        return change;
    }

    public ProjectorDisplayChange? Remove(string runtimeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeId);
        ProjectorDisplayChange? change = null;
        lock (_gate) if (_displays.Remove(runtimeId)) change = new(ProjectorDisplayChangeKind.Removed, runtimeId, null);
        if (change is not null) Changed?.Invoke(change);
        return change;
    }
}

public interface IProjectorSessionCoordinator
{
    ProjectorSessionSnapshot? Current { get; }
    event Action<ProjectorSessionSnapshot?>? StateChanged;
    ProjectorSessionSnapshot Start(ProjectorDisplay targetDisplay);
    ProjectorSessionSnapshot Activate(ProjectorExperience experience, ProjectorControllerDefinition? controller = null);
    bool TryReconnect(ProjectorDisplay display, out ProjectorSessionSnapshot? snapshot);
    ProjectorSessionSnapshot? Stop();
}

public sealed class ProjectorSessionCoordinator : IProjectorSessionCoordinator, IDisposable
{
    private readonly IProjectorDisplayRegistry _displays;
    private readonly object _gate = new();
    private ProjectorSessionSnapshot? _current;
    private bool _disposed;

    public ProjectorSessionCoordinator(IProjectorDisplayRegistry displays)
    {
        _displays = displays ?? throw new ArgumentNullException(nameof(displays));
        _displays.Changed += OnDisplayChanged;
    }

    public event Action<ProjectorSessionSnapshot?>? StateChanged;
    public ProjectorSessionSnapshot? Current { get { lock (_gate) return _current; } }

    public ProjectorSessionSnapshot Start(ProjectorDisplay targetDisplay)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(targetDisplay);
        var now = DateTimeOffset.UtcNow;
        var session = new ProjectorSessionSnapshot(Guid.NewGuid(), ProjectorSessionState.Gallery, targetDisplay, null, null, null, null, [], now, now);
        SetCurrent(session);
        return session;
    }

    public ProjectorSessionSnapshot Activate(ProjectorExperience experience, ProjectorControllerDefinition? controller = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(experience);
        ProjectorSessionSnapshot current;
        lock (_gate) current = _current ?? throw new InvalidOperationException("Start Projector before launching an experience.");
        if (current.State is ProjectorSessionState.Disconnected or ProjectorSessionState.Stopping or ProjectorSessionState.Failed)
            throw new InvalidOperationException($"Projector cannot launch an experience while {current.State}.");
        if (!experience.IsAvailable(current.TargetDisplay.Capabilities))
            throw new InvalidOperationException($"{experience.Name} is not available on the selected Projector target.");
        var active = current with { State = ProjectorSessionState.Active, PreviousExperienceId = current.CurrentExperienceId, CurrentExperienceId = experience.Id, Controller = controller, UpdatedAt = DateTimeOffset.UtcNow, Error = null };
        SetCurrent(active);
        return active;
    }

    public bool TryReconnect(ProjectorDisplay display, out ProjectorSessionSnapshot? snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(display);
        ProjectorSessionSnapshot current;
        lock (_gate)
        {
            if (_current is null || _current.State != ProjectorSessionState.Disconnected) { snapshot = _current; return false; }
            current = _current;
        }
        if (string.IsNullOrWhiteSpace(current.TargetDisplay.StableIdentity) || string.IsNullOrWhiteSpace(display.StableIdentity)
            || !string.Equals(current.TargetDisplay.StableIdentity, display.StableIdentity, StringComparison.Ordinal))
        {
            snapshot = current;
            return false;
        }
        snapshot = current with { State = current.CurrentExperienceId is null ? ProjectorSessionState.Gallery : ProjectorSessionState.Active, TargetDisplay = display, UpdatedAt = DateTimeOffset.UtcNow, DisconnectedAt = null, Error = null };
        SetCurrent(snapshot);
        return true;
    }

    public ProjectorSessionSnapshot? Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ProjectorSessionSnapshot? stopping;
        lock (_gate)
        {
            if (_current is null) return null;
            stopping = _current with { State = ProjectorSessionState.Stopping, UpdatedAt = DateTimeOffset.UtcNow };
            _current = null;
        }
        StateChanged?.Invoke(stopping);
        StateChanged?.Invoke(null);
        return stopping;
    }

    private void OnDisplayChanged(ProjectorDisplayChange change)
    {
        ProjectorSessionSnapshot? next = null;
        lock (_gate)
        {
            if (_current is null || !string.Equals(_current.TargetDisplay.RuntimeId, change.RuntimeId, StringComparison.Ordinal)) return;
            if (change.Kind == ProjectorDisplayChangeKind.Removed)
            {
                var now = DateTimeOffset.UtcNow;
                next = _current with { State = ProjectorSessionState.Disconnected, UpdatedAt = now, DisconnectedAt = now };
                _current = next;
            }
            else if (_current.State != ProjectorSessionState.Disconnected && change.Display is not null)
            {
                next = _current with { TargetDisplay = change.Display, UpdatedAt = DateTimeOffset.UtcNow };
                _current = next;
            }
        }
        if (next is not null) StateChanged?.Invoke(next);
    }

    private void SetCurrent(ProjectorSessionSnapshot snapshot)
    {
        lock (_gate) _current = snapshot;
        StateChanged?.Invoke(snapshot);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _displays.Changed -= OnDisplayChanged;
    }
}
