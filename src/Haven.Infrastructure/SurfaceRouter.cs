using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class SurfaceRouter : ISurfaceRouter
{
    private readonly IActivityLogRepository _activityLog;
    private readonly List<SurfaceRun> _runs = [];

    public SurfaceRouter(IActivityLogRepository activityLog)
    {
        _activityLog = activityLog;
    }

    public Task<SurfaceKind> ResolveSurfaceAsync(string intent, HavenMode currentMode, CancellationToken cancellationToken)
    {
        var lower = intent.ToLowerInvariant();
        if (lower.Contains("browse") || lower.Contains("website") || lower.Contains("web"))
            return Task.FromResult(SurfaceKind.Browse);
        if (lower.Contains("plan") || lower.Contains("schedule") || lower.Contains("calendar"))
            return Task.FromResult(SurfaceKind.Plan);
        if (lower.Contains("training") || lower.Contains("practice"))
            return Task.FromResult(SurfaceKind.Training);
        if (lower.Contains("teach") || lower.Contains("lesson") || lower.Contains("learn"))
            return Task.FromResult(SurfaceKind.Teach);
        if (lower.Contains("studio") || lower.Contains("project") || lower.Contains("code"))
            return Task.FromResult(SurfaceKind.Studio);
        if (lower.Contains("do") || lower.Contains("task") || lower.Contains("complete"))
            return Task.FromResult(SurfaceKind.Do);

        return Task.FromResult(currentMode switch
        {
            HavenMode.Chat => SurfaceKind.Chat,
            HavenMode.Teach => SurfaceKind.Teach,
            HavenMode.Do => SurfaceKind.Do,
            HavenMode.Studio => SurfaceKind.Studio,
            _ => SurfaceKind.Chat
        });
    }

    public Task<IReadOnlyList<SurfaceRun>> GetRecentRunsAsync(int limit, CancellationToken cancellationToken)
    {
        var results = _runs.OrderByDescending(r => r.StartedAt).Take(limit).ToArray();
        return Task.FromResult<IReadOnlyList<SurfaceRun>>(results);
    }

    public Task RecordRunAsync(SurfaceRun run, CancellationToken cancellationToken)
    {
        _runs.Add(run);
        return Task.CompletedTask;
    }
}
