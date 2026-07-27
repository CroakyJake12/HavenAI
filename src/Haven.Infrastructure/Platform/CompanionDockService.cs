/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/CompanionDockService.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns CompanionDockService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.Concurrent;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents companion dock service and keeps its related state and behavior together.
/// </summary>
public sealed class CompanionDockService : ICompanionDockService
{
    /// <summary>
    /// Stores docked locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ConcurrentDictionary<SurfaceKind, List<Guid>> _docked = new();

    /// <summary>
    /// Reports whether docked async applies to the current state.
    /// </summary>
    public Task<bool> IsDockedAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var result = _docked.Values.Any(list => list.Contains(conversationId));
        return Task.FromResult(result);
    }

    /// <summary>
    /// Performs dock asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs undock asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Retrieves docked conversations async for the current operation.
    /// </summary>
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
