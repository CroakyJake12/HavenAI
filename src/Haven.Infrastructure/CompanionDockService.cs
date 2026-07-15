using System.Collections.Concurrent;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class CompanionDockService : ICompanionDockService
{
    private readonly ConcurrentDictionary<SurfaceKind, List<Guid>> _docked = new();

    public Task<bool> IsDockedAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var result = _docked.Values.Any(list => list.Contains(conversationId));
        return Task.FromResult(result);
    }

    public Task DockAsync(Guid conversationId, SurfaceKind surface, CancellationToken cancellationToken)
    {
        var list = _docked.GetOrAdd(surface, _ => []);
        lock (list)
        {
            if (!list.Contains(conversationId))
                list.Add(conversationId);
        }
        return Task.CompletedTask;
    }

    public Task UndockAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        foreach (var list in _docked.Values)
        {
            lock (list)
            {
                list.Remove(conversationId);
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> GetDockedConversationsAsync(SurfaceKind surface, CancellationToken cancellationToken)
    {
        if (_docked.TryGetValue(surface, out var list))
        {
            lock (list)
            {
                return Task.FromResult<IReadOnlyList<Guid>>(list.ToArray());
            }
        }
        return Task.FromResult<IReadOnlyList<Guid>>([]);
    }
}
