using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Caches the installed model inventory so ordinary messages do not block on model discovery.
/// Mutating model operations must invalidate this cache.
/// </summary>
public sealed class ModelInventoryCache(IOllamaClient ollama)
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(2);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private IReadOnlyList<ModelDescriptor>? _models;
    private DateTimeOffset _expiresAt;

    public async Task<IReadOnlyList<ModelDescriptor>> GetAsync(
        CancellationToken cancellationToken,
        bool forceRefresh = false)
    {
        var now = DateTimeOffset.UtcNow;
        var cached = Volatile.Read(ref _models);
        if (!forceRefresh && cached is not null && now < _expiresAt)
        {
            return cached;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = DateTimeOffset.UtcNow;
            cached = _models;
            if (!forceRefresh && cached is not null && now < _expiresAt)
            {
                return cached;
            }

            var discovered = await ollama.GetModelsAsync(cancellationToken).ConfigureAwait(false);
            var snapshot = discovered.ToArray();
            Volatile.Write(ref _models, snapshot);
            _expiresAt = now.Add(DefaultLifetime);
            return snapshot;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Invalidate()
    {
        Volatile.Write(ref _models, null);
        _expiresAt = default;
    }
}

/// <summary>
/// Provides one application-wide stream of meaningful operation progress.
/// The shell can display the current operation regardless of which surface is active.
/// </summary>
public sealed class GlobalOperationStatus
{
    private readonly object _gate = new();
    private ResponseProgressTracker? _current;

    public event EventHandler? Changed;

    public ResponseProgressTracker? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public ResponseProgressTracker Begin(
        ResponseProgressStage stage = ResponseProgressStage.Preparing,
        string status = "Preparing")
    {
        var tracker = new ResponseProgressTracker();
        tracker.Update(stage, status);
        tracker.Changed += OnTrackerChanged;

        lock (_gate)
        {
            if (_current is not null)
            {
                _current!.Changed -= OnTrackerChanged;
            }

            _current = tracker;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return tracker;
    }

    public void Clear(ResponseProgressTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);

        lock (_gate)
        {
            if (!ReferenceEquals(_current, tracker))
            {
                return;
            }

            _current!.Changed -= OnTrackerChanged;
            _current = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnTrackerChanged(object? sender, EventArgs args) =>
        Changed?.Invoke(this, EventArgs.Empty);
}

