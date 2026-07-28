/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/SurfaceRouter.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns SurfaceRouter. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents surface router and keeps its related state and behavior together.
/// </summary>
public sealed class SurfaceRouter : ISurfaceRouter
{
    /// <summary>
    /// Stores activity log locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IActivityLogRepository _activityLog;
    /// <summary>
    /// Stores runs locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly List<SurfaceRun> _runs = [];

    public SurfaceRouter(IActivityLogRepository activityLog)
    {
        _activityLog = activityLog;
    }

    /// <summary>
    /// Performs resolve surface asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<SurfaceKind> ResolveSurfaceAsync(string intent, HavenMode currentMode, CancellationToken cancellationToken)
    {
        var lower = intent.ToLowerInvariant();
        if (lower.Contains("browse") || lower.Contains("website") || lower.Contains("web"))
            return Task.FromResult(SurfaceKind.Browse);
        if (lower.Contains("plan") || lower.Contains("schedule") || lower.Contains("calendar"))
            return Task.FromResult(SurfaceKind.Plan);
        if (lower.Contains("training") || lower.Contains("practice"))
            return Task.FromResult(SurfaceKind.Training);
        if (lower.Contains("study") || lower.Contains("teach") || lower.Contains("lesson") || lower.Contains("learn"))
            return Task.FromResult(SurfaceKind.Study);
        if (lower.Contains("studio") || lower.Contains("project") || lower.Contains("code"))
            return Task.FromResult(SurfaceKind.Studio);
        if (lower.Contains("task") || lower.Contains("to-do") || lower.Contains("complete"))
            return Task.FromResult(SurfaceKind.Tasks);

        return Task.FromResult(currentMode switch
        {
            HavenMode.Chat => SurfaceKind.Chat,
            HavenMode.Study => SurfaceKind.Study,
            HavenMode.Tasks => SurfaceKind.Tasks,
            HavenMode.Studio => SurfaceKind.Studio,
            _ => SurfaceKind.Chat
        });
    }

    /// <summary>
    /// Retrieves recent runs async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<SurfaceRun>> GetRecentRunsAsync(int limit, CancellationToken cancellationToken)
    {
        var results = _runs.OrderByDescending(r => r.StartedAt).Take(limit).ToArray();
        return Task.FromResult<IReadOnlyList<SurfaceRun>>(results);
    }

    /// <summary>
    /// Performs record run asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task RecordRunAsync(SurfaceRun run, CancellationToken cancellationToken)
    {
        _runs.Add(run);
        return Task.CompletedTask;
    }
}
