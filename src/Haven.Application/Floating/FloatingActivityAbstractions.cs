using Haven.Core;

namespace Haven.Application;

public interface IFloatingActivityContent
{
    object Content { get; }
    string AutomationName { get; }
}

public interface IFloatingActivityHost : IAsyncDisposable
{
    string Platform { get; }
    bool IsAvailable { get; }
    string? UnavailableReason { get; }
    event EventHandler<FloatingActivitySnapshot>? StateChanged;

    Task<FloatingActivitySnapshot> PresentAsync(
        FloatingActivityDefinition definition,
        IFloatingActivityContent content,
        CancellationToken cancellationToken);

    Task<FloatingActivitySnapshot> UpdateAsync(
        FloatingActivitySnapshot snapshot,
        CancellationToken cancellationToken);

    Task DismissAsync(Guid activityId, CancellationToken cancellationToken);
}

public sealed class FloatingActivityStateStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, FloatingActivitySnapshot> _snapshots = [];

    public event EventHandler<FloatingActivitySnapshot>? Changed;

    public IReadOnlyList<FloatingActivitySnapshot> Snapshot()
    {
        lock (_gate) return _snapshots.Values.ToArray();
    }

    public FloatingActivitySnapshot? Get(Guid id)
    {
        lock (_gate) return _snapshots.TryGetValue(id, out var snapshot) ? snapshot : null;
    }

    public void Set(FloatingActivitySnapshot snapshot)
    {
        lock (_gate) _snapshots[snapshot.Id] = snapshot;
        Changed?.Invoke(this, snapshot);
    }

    public bool Remove(Guid id)
    {
        lock (_gate) return _snapshots.Remove(id);
    }
}
