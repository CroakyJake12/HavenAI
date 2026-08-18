namespace Haven.Application;

public interface IProjectorSessionRecoveryService
{
    Task<ProjectorSessionSnapshot?> RecoverAsync(ProjectorDisplay display, CancellationToken cancellationToken);
    Task FlushAsync(CancellationToken cancellationToken);
    string? LastPersistenceError { get; }
}

public sealed class ProjectorSessionRecoveryService : IProjectorSessionRecoveryService, IDisposable
{
    private const string SettingsKey = "projector.session.recovery.v1";
    private const int MaximumIdentityLength = 512;
    private const int MaximumExperienceIdLength = 512;

    private readonly IProjectorSessionCoordinator _sessions;
    private readonly IProjectorDisplayRegistry _displays;
    private readonly IProjectorExperienceCatalog _catalog;
    private readonly IVersionedSettingsStore _settings;
    private readonly object _queueGate = new();
    private Task _persistenceTail = Task.CompletedTask;
    private string? _lastPersistenceError;
    private bool _disposed;

    public ProjectorSessionRecoveryService(
        IProjectorSessionCoordinator sessions,
        IProjectorDisplayRegistry displays,
        IProjectorExperienceCatalog catalog,
        IVersionedSettingsStore settings)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _displays = displays ?? throw new ArgumentNullException(nameof(displays));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _sessions.StateChanged += OnSessionStateChanged;
    }

    public string? LastPersistenceError
    {
        get
        {
            lock (_queueGate)
                return _lastPersistenceError;
        }
    }

    public async Task<ProjectorSessionSnapshot?> RecoverAsync(
        ProjectorDisplay display,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(display);
        cancellationToken.ThrowIfCancellationRequested();

        var identity = NormalizeStableIdentity(display.StableIdentity);
        if (identity is null)
            return null;

        var current = _sessions.Current;
        if (current is not null && current.State != ProjectorSessionState.Disconnected)
        {
            return string.Equals(current.TargetDisplay.RuntimeId, display.RuntimeId, StringComparison.Ordinal)
                ? current
                : null;
        }

        var latest = CurrentDisplay(display, identity);
        if (latest is null)
            return null;

        if (current is not null
            && current.State == ProjectorSessionState.Disconnected
            && StableIdentityEquals(current.TargetDisplay.StableIdentity, identity))
        {
            var validated = await ResolvePersistentExperienceAsync(
                current.CurrentExperienceId,
                current with { State = ProjectorSessionState.Restoring, TargetDisplay = latest },
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            latest = CurrentDisplay(latest, identity);
            if (latest is null || !_sessions.TryReconnect(latest, out var reconnected) || reconnected is null)
                return null;

            return validated is null ? reconnected : _sessions.Activate(validated);
        }

        var checkpoint = await _settings.GetAsync<ProjectorSessionCheckpoint>(SettingsKey, cancellationToken)
            .ConfigureAwait(false);
        if (checkpoint is null || !StableIdentityEquals(checkpoint.StableIdentity, identity))
            return null;

        cancellationToken.ThrowIfCancellationRequested();
        latest = CurrentDisplay(latest, identity);
        if (latest is null)
            return null;

        var session = _sessions.Start(latest);
        var validatedExperience = await ResolvePersistentExperienceAsync(
            checkpoint.ExperienceId,
            session with { State = ProjectorSessionState.Restoring },
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        var activeSession = _sessions.Current;
        if (activeSession is null
            || activeSession.Id != session.Id
            || activeSession.State == ProjectorSessionState.Disconnected
            || validatedExperience is null)
        {
            return activeSession;
        }

        return _sessions.Activate(validatedExperience);
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        Task pending;
        lock (_queueGate)
            pending = _persistenceTail;
        await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProjectorExperience?> ResolvePersistentExperienceAsync(
        string? experienceId,
        ProjectorSessionSnapshot session,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeExperienceId(experienceId);
        if (normalized is null)
            return null;

        var experiences = await _catalog.GetExperiencesAsync(session, cancellationToken).ConfigureAwait(false);
        return experiences.FirstOrDefault(experience =>
            experience.Persistence == ProjectorExperiencePersistence.Persistent
            && string.Equals(experience.Id, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private ProjectorDisplay? CurrentDisplay(ProjectorDisplay requested, string stableIdentity)
    {
        var latest = _displays.Get(requested.RuntimeId);
        return latest is not null && StableIdentityEquals(latest.StableIdentity, stableIdentity)
            ? latest
            : null;
    }

    private void OnSessionStateChanged(ProjectorSessionSnapshot? snapshot)
    {
        if (_disposed || snapshot is null)
            return;

        lock (_queueGate)
        {
            _persistenceTail = _persistenceTail
                .ContinueWith(
                    _ => PersistSnapshotSafelyAsync(snapshot),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default)
                .Unwrap();
        }
    }

    private async Task PersistSnapshotSafelyAsync(ProjectorSessionSnapshot snapshot)
    {
        try
        {
            var identity = NormalizeStableIdentity(snapshot.TargetDisplay.StableIdentity);
            if (identity is null || snapshot.State is ProjectorSessionState.Stopping or ProjectorSessionState.Failed)
            {
                await _settings.RemoveAsync(SettingsKey, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                await _settings.SetAsync(
                    SettingsKey,
                    new ProjectorSessionCheckpoint
                    {
                        StableIdentity = identity,
                        ExperienceId = NormalizeExperienceId(snapshot.CurrentExperienceId),
                        UpdatedAt = snapshot.UpdatedAt
                    },
                    CancellationToken.None).ConfigureAwait(false);
            }

            lock (_queueGate)
                _lastPersistenceError = null;
        }
        catch (Exception exception)
        {
            lock (_queueGate)
                _lastPersistenceError = exception.Message;
        }
    }

    private static bool StableIdentityEquals(string? candidate, string expected)
    {
        var normalized = NormalizeStableIdentity(candidate);
        return normalized is not null && string.Equals(normalized, expected, StringComparison.Ordinal);
    }

    private static string? NormalizeStableIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        return normalized.Length <= MaximumIdentityLength ? normalized : null;
    }

    private static string? NormalizeExperienceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        return normalized.Length <= MaximumExperienceIdLength ? normalized : null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _sessions.StateChanged -= OnSessionStateChanged;
    }
}

internal sealed class ProjectorSessionCheckpoint
{
    public ProjectorSessionCheckpoint()
    {
    }

    public int Version { get; init; } = 1;
    public string StableIdentity { get; init; } = string.Empty;
    public string? ExperienceId { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
