#if !ANDROID
using Haven.Application;

namespace Haven.Desktop.Overlay;

/// <summary>
/// Owns Overlay-only UI/session state. Product data/services remain shared with Haven; this registry
/// deliberately persists only pinned surface identity/geometry and never persists captured context payloads.
/// </summary>
internal sealed class OverlayWorkspaceRegistry
{
    private const string SettingsKey = "desktop.overlay.workspace.v1";
    private readonly IVersionedSettingsStore _settings;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _mutations = new(1, 1);
    private readonly object _stateGate = new();
    private readonly Dictionary<Guid, OverlaySessionState> _sessions = [];
    private bool _initialized;
    private Guid? _activeSessionId;

    public OverlayWorkspaceRegistry(IVersionedSettingsStore settings) : this(settings, () => DateTimeOffset.UtcNow)
    {
    }

    internal OverlayWorkspaceRegistry(IVersionedSettingsStore settings, Func<DateTimeOffset> clock)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public event EventHandler<OverlayWorkspaceSnapshot>? Changed;

    public OverlayWorkspaceSnapshot Snapshot
    {
        get
        {
            lock (_stateGate)
            {
                var now = _clock();
                var sessions = _sessions.Values
                    .OrderBy(session => session.CreatedAt)
                    .Select(session => session.Context?.IsExpired(now) == true
                        ? session with { Context = null }
                        : session)
                    .ToArray();
                return new OverlayWorkspaceSnapshot(_activeSessionId, sessions);
            }
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutations.Release();
        }
        Publish();
    }

    public async Task<OverlaySessionState> OpenSessionAsync(
        string appKey,
        string title,
        Guid? threadId,
        string? sourceAssociation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(appKey)) throw new ArgumentException("An Overlay session requires an app key.", nameof(appKey));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("An Overlay session requires a title.", nameof(title));

        OverlaySessionState session;
        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedCoreAsync(cancellationToken).ConfigureAwait(false);
            var now = _clock();
            session = new OverlaySessionState(
                Guid.NewGuid(),
                appKey.Trim(),
                title.Trim(),
                threadId,
                false,
                true,
                OverlaySurfaceGeometry.Default,
                null,
                now,
                now,
                sourceAssociation);
            lock (_stateGate)
            {
                _sessions[session.Id] = session;
                _activeSessionId = session.Id;
            }
        }
        finally
        {
            _mutations.Release();
        }
        Publish();
        return session;
    }

    public async Task<bool> ActivateAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var changed = false;
        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedCoreAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateGate)
            {
                if (_sessions.TryGetValue(sessionId, out var session))
                {
                    _sessions[sessionId] = session with { IsVisible = true, UpdatedAt = _clock() };
                    _activeSessionId = sessionId;
                    changed = true;
                }
            }
            if (changed) await PersistPinnedCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutations.Release();
        }
        if (changed) Publish();
        return changed;
    }

    public async Task<bool> CloseSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var changed = false;
        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedCoreAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateGate)
            {
                if (_sessions.TryGetValue(sessionId, out var session))
                {
                    if (session.IsPinned)
                        _sessions[sessionId] = session with { IsVisible = false, UpdatedAt = _clock() };
                    else
                        _sessions.Remove(sessionId);

                    if (_activeSessionId == sessionId)
                        _activeSessionId = _sessions.Values.FirstOrDefault(candidate => candidate.IsVisible)?.Id;
                    changed = true;
                }
            }
            if (changed) await PersistPinnedCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutations.Release();
        }
        if (changed) Publish();
        return changed;
    }

    public async Task<bool> RemoveSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var changed = false;
        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedCoreAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateGate)
            {
                changed = _sessions.Remove(sessionId);
                if (changed && _activeSessionId == sessionId)
                    _activeSessionId = _sessions.Values.FirstOrDefault(candidate => candidate.IsVisible)?.Id;
            }
            if (changed) await PersistPinnedCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutations.Release();
        }
        if (changed) Publish();
        return changed;
    }

    public async Task<bool> SetPinnedAsync(Guid sessionId, bool isPinned, CancellationToken cancellationToken)
    {
        var changed = false;
        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedCoreAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateGate)
            {
                if (_sessions.TryGetValue(sessionId, out var session) && session.IsPinned != isPinned)
                {
                    _sessions[sessionId] = session with
                    {
                        IsPinned = isPinned,
                        IsVisible = isPinned ? session.IsVisible : true,
                        UpdatedAt = _clock()
                    };
                    changed = true;
                }
            }
            if (changed) await PersistPinnedCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutations.Release();
        }
        if (changed) Publish();
        return changed;
    }

    public async Task<bool> UpdateGeometryAsync(Guid sessionId, OverlaySurfaceGeometry geometry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var changed = false;
        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedCoreAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateGate)
            {
                if (_sessions.TryGetValue(sessionId, out var session))
                {
                    _sessions[sessionId] = session with { Geometry = geometry.Bound(), UpdatedAt = _clock() };
                    changed = true;
                }
            }
            if (changed) await PersistPinnedCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutations.Release();
        }
        if (changed) Publish();
        return changed;
    }

    public async Task<bool> SetContextAsync(Guid sessionId, OverlayContextEnvelope? context, CancellationToken cancellationToken)
    {
        var changed = false;
        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedCoreAsync(cancellationToken).ConfigureAwait(false);
            var now = _clock();
            if (context?.IsExpired(now) == true)
                throw new ArgumentException("Overlay context must still be inside its declared retention window.", nameof(context));
            context = context?.Bound();

            lock (_stateGate)
            {
                if (_sessions.TryGetValue(sessionId, out var session))
                {
                    _sessions[sessionId] = session with { Context = context, UpdatedAt = now };
                    changed = true;
                }
            }
            if (changed) await PersistPinnedCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutations.Release();
        }
        if (changed) Publish();
        return changed;
    }

    private async Task EnsureInitializedCoreAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        var persisted = await _settings.GetAsync<OverlayWorkspacePersistedState>(SettingsKey, cancellationToken).ConfigureAwait(false);
        lock (_stateGate)
        {
            _sessions.Clear();
            if (persisted is not null)
            {
                foreach (var persistedSession in persisted.PinnedSessions.Where(session => session.Id != Guid.Empty))
                {
                    _sessions[persistedSession.Id] = persistedSession with
                    {
                        IsPinned = true,
                        IsVisible = true,
                        Context = null,
                        Geometry = persistedSession.Geometry.Bound()
                    };
                }
            }
            _activeSessionId = _sessions.Values.FirstOrDefault()?.Id;
            _initialized = true;
        }
    }

    private async Task PersistPinnedCoreAsync(CancellationToken cancellationToken)
    {
        OverlayWorkspacePersistedState persisted;
        lock (_stateGate)
        {
            persisted = new OverlayWorkspacePersistedState
            {
                PinnedSessions = _sessions.Values
                    .Where(session => session.IsPinned)
                    .OrderBy(session => session.CreatedAt)
                    .Select(session => session with { Context = null })
                    .ToList()
            };
        }

        if (persisted.PinnedSessions.Count == 0)
            await _settings.RemoveAsync(SettingsKey, cancellationToken).ConfigureAwait(false);
        else
            await _settings.SetAsync(SettingsKey, persisted, cancellationToken).ConfigureAwait(false);
    }

    private void Publish() => Changed?.Invoke(this, Snapshot);
}
#endif
