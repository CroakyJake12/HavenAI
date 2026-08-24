using Haven.Application;
using Haven.Core;

namespace Haven.Android;

public sealed record AndroidAssistantOverlaySurface(
    Guid Id,
    string AppKey,
    string Title,
    DateTimeOffset OpenedAt);

public sealed record AndroidAssistantOverlaySnapshot(
    Guid ActivityId,
    Guid ThreadId,
    FloatingActivityState State,
    Guid? ActiveSurfaceId,
    IReadOnlyList<AndroidAssistantOverlaySurface> Surfaces,
    string? Error = null);

/// <summary>
/// Android-only presentation/session coordinator for Haven Assistant and its expanded Overlay mode.
/// It owns Android window state and an Overlay-local surface register, while the canonical Chat,
/// model, tool and persistence state remains in shared Haven services.
/// </summary>
public sealed class AndroidAssistantOverlayCoordinator : IAsyncDisposable
{
    private readonly IFloatingActivityHost _host;
    private readonly FloatingActivityStateStore _stateStore;
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly object _gate = new();
    private Session? _session;
    private bool _disposed;

    public AndroidAssistantOverlayCoordinator(
        IFloatingActivityHost host,
        FloatingActivityStateStore stateStore)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _host.StateChanged += OnHostStateChanged;
    }

    public event Action<AndroidAssistantOverlaySnapshot>? StateChanged;

    public bool IsAvailable => !_disposed && _host.IsAvailable;
    public string? UnavailableReason => _disposed ? "The Android Assistant overlay coordinator has been disposed." : _host.UnavailableReason;

    public AndroidAssistantOverlaySnapshot? Current
    {
        get
        {
            lock (_gate) return _session is null ? null : SnapshotLocked(_session);
        }
    }

    public async Task<AndroidAssistantOverlaySnapshot> PresentCompactAsync(
        Guid threadId,
        IFloatingActivityContent content,
        string initialAppKey,
        string initialTitle,
        CancellationToken cancellationToken)
    {
        if (threadId == Guid.Empty) throw new ArgumentException("Assistant sessions require a real Haven thread identifier.", nameof(threadId));
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrWhiteSpace(initialAppKey)) throw new ArgumentException("An initial Overlay app key is required.", nameof(initialAppKey));
        if (string.IsNullOrWhiteSpace(initialTitle)) throw new ArgumentException("An initial Overlay surface title is required.", nameof(initialTitle));
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _transitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Session? existing;
            lock (_gate) existing = _session;

            if (existing is not null && existing.ThreadId == threadId && existing.Window.State is not FloatingActivityState.Dismissed)
            {
                lock (_gate)
                {
                    OpenSurfaceCore(existing, initialAppKey, initialTitle, activate: true);
                }
                return await SetPresentationStateAsync(existing, FloatingActivityState.Compact, cancellationToken).ConfigureAwait(false);
            }

            if (existing is not null)
            {
                await _host.DismissAsync(existing.ActivityId, cancellationToken).ConfigureAwait(false);
                lock (_gate)
                {
                    if (ReferenceEquals(_session, existing)) _session = null;
                }
            }

            var now = DateTimeOffset.UtcNow;
            var definition = new FloatingActivityDefinition(
                Guid.NewGuid(),
                threadId,
                "assistant",
                "Haven Assistant",
                "Accent",
                FloatingActivityPresentation.SystemOverlay,
                AlwaysOnTop: true,
                IsDismissible: true,
                now);

            var presented = await _host.PresentAsync(definition, content, cancellationToken).ConfigureAwait(false);
            var surface = new AndroidAssistantOverlaySurface(Guid.NewGuid(), initialAppKey.Trim(), initialTitle.Trim(), now);
            var session = new Session(definition, content, presented, [surface], surface.Id);
            lock (_gate) _session = session;

            if (presented.State == FloatingActivityState.Failed)
            {
                return Publish(session);
            }

            return await SetPresentationStateAsync(session, FloatingActivityState.Compact, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    public Task<AndroidAssistantOverlaySnapshot> ExpandAsync(CancellationToken cancellationToken) =>
        TransitionAsync(FloatingActivityState.Expanded, cancellationToken);

    public Task<AndroidAssistantOverlaySnapshot> CollapseAsync(CancellationToken cancellationToken) =>
        TransitionAsync(FloatingActivityState.Compact, cancellationToken);

    public AndroidAssistantOverlaySnapshot OpenSurface(string appKey, string title, bool activate = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(appKey)) throw new ArgumentException("An Overlay app key is required.", nameof(appKey));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("An Overlay surface title is required.", nameof(title));

        Session session;
        lock (_gate)
        {
            session = _session ?? throw new InvalidOperationException("Open Haven Assistant before adding Overlay surfaces.");
            OpenSurfaceCore(session, appKey, title, activate);
        }
        return Publish(session);
    }

    public AndroidAssistantOverlaySnapshot ActivateSurface(Guid surfaceId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Session session;
        lock (_gate)
        {
            session = _session ?? throw new InvalidOperationException("Open Haven Assistant before activating Overlay surfaces.");
            if (session.Surfaces.All(surface => surface.Id != surfaceId))
                throw new KeyNotFoundException("The requested Overlay surface is not part of this Assistant session.");
            session.ActiveSurfaceId = surfaceId;
        }
        return Publish(session);
    }

    public AndroidAssistantOverlaySnapshot CloseSurface(Guid surfaceId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Session session;
        lock (_gate)
        {
            session = _session ?? throw new InvalidOperationException("Open Haven Assistant before closing Overlay surfaces.");
            var index = session.Surfaces.FindIndex(surface => surface.Id == surfaceId);
            if (index < 0) return SnapshotLocked(session);
            session.Surfaces.RemoveAt(index);
            if (session.ActiveSurfaceId == surfaceId)
                session.ActiveSurfaceId = session.Surfaces.LastOrDefault()?.Id;
        }
        return Publish(session);
    }

    public async Task DismissAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _transitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Session? session;
            lock (_gate) session = _session;
            if (session is null) return;

            await _host.DismissAsync(session.ActivityId, cancellationToken).ConfigureAwait(false);
            session.Window = session.Window with { State = FloatingActivityState.Dismissed };
            _ = Publish(session);
            lock (_gate)
            {
                if (ReferenceEquals(_session, session)) _session = null;
            }
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    private async Task<AndroidAssistantOverlaySnapshot> TransitionAsync(
        FloatingActivityState state,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _transitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Session session;
            lock (_gate) session = _session ?? throw new InvalidOperationException("Open Haven Assistant before changing Overlay mode.");
            return await SetPresentationStateAsync(session, state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    private async Task<AndroidAssistantOverlaySnapshot> SetPresentationStateAsync(
        Session session,
        FloatingActivityState state,
        CancellationToken cancellationToken)
    {
        if (session.Window.State == FloatingActivityState.Failed) return Publish(session);
        var current = _stateStore.Get(session.ActivityId) ?? session.Window;
        var target = CreateTargetWindow(current, state);
        session.Window = await _host.UpdateAsync(target, cancellationToken).ConfigureAwait(false);
        return Publish(session);
    }

    private static FloatingActivitySnapshot CreateTargetWindow(
        FloatingActivitySnapshot current,
        FloatingActivityState state)
    {
        var resources = global::Android.App.Application.Context.Resources;
        var configuration = resources?.Configuration;
        var screenWidth = configuration?.ScreenWidthDp > 0 ? configuration.ScreenWidthDp : 420;
        var screenHeight = configuration?.ScreenHeightDp > 0 ? configuration.ScreenHeightDp : 800;

        if (state == FloatingActivityState.Expanded)
        {
            var width = Math.Max(300d, screenWidth - 24d);
            var height = Math.Max(360d, screenHeight - 48d);
            return current with { State = state, Width = width, Height = height, X = 12, Y = 24, Error = null };
        }

        var compactWidth = Math.Min(380d, Math.Max(300d, screenWidth - 24d));
        var compactHeight = Math.Min(280d, Math.Max(200d, screenHeight * 0.34d));
        var x = Math.Clamp(current.X, 8d, Math.Max(8d, screenWidth - compactWidth - 8d));
        var y = Math.Clamp(current.Y, 16d, Math.Max(16d, screenHeight - compactHeight - 16d));
        return current with { State = FloatingActivityState.Compact, Width = compactWidth, Height = compactHeight, X = x, Y = y, Error = null };
    }

    private static void OpenSurfaceCore(Session session, string appKey, string title, bool activate)
    {
        var normalizedKey = appKey.Trim();
        var existing = session.Surfaces.LastOrDefault(surface => string.Equals(surface.AppKey, normalizedKey, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new AndroidAssistantOverlaySurface(Guid.NewGuid(), normalizedKey, title.Trim(), DateTimeOffset.UtcNow);
            session.Surfaces.Add(existing);
        }
        if (activate) session.ActiveSurfaceId = existing.Id;
    }

    private void OnHostStateChanged(object? sender, FloatingActivitySnapshot snapshot)
    {
        Session? session;
        lock (_gate)
        {
            session = _session;
            if (session is null || session.ActivityId != snapshot.Id) return;
            session.Window = snapshot;
        }
        _ = Publish(session);
    }

    private AndroidAssistantOverlaySnapshot Publish(Session session)
    {
        AndroidAssistantOverlaySnapshot snapshot;
        lock (_gate) snapshot = SnapshotLocked(session);
        StateChanged?.Invoke(snapshot);
        return snapshot;
    }

    private static AndroidAssistantOverlaySnapshot SnapshotLocked(Session session) =>
        new(
            session.ActivityId,
            session.ThreadId,
            session.Window.State,
            session.ActiveSurfaceId,
            session.Surfaces.ToArray(),
            session.Window.Error);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _host.StateChanged -= OnHostStateChanged;

        Session? session;
        lock (_gate)
        {
            session = _session;
            _session = null;
        }

        if (session is not null && session.Window.State is not FloatingActivityState.Dismissed)
        {
            try { await _host.DismissAsync(session.ActivityId, CancellationToken.None).ConfigureAwait(false); }
            catch { }
        }
        _transitionGate.Dispose();
    }

    private sealed class Session(
        FloatingActivityDefinition definition,
        IFloatingActivityContent content,
        FloatingActivitySnapshot window,
        List<AndroidAssistantOverlaySurface> surfaces,
        Guid? activeSurfaceId)
    {
        public FloatingActivityDefinition Definition { get; } = definition;
        public IFloatingActivityContent Content { get; } = content;
        public Guid ActivityId => Definition.Id;
        public Guid ThreadId => Definition.ThreadId;
        public FloatingActivitySnapshot Window { get; set; } = window;
        public List<AndroidAssistantOverlaySurface> Surfaces { get; } = surfaces;
        public Guid? ActiveSurfaceId { get; set; } = activeSurfaceId;
    }
}
