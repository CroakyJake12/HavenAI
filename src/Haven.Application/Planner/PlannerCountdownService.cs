/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Planner/PlannerCountdownService.cs, in the Application layer.
 * What: Reads tasks and events from canonical Plan persistence and exposes them as linked countdowns.
 * How: Repository range queries load source records; Core projections calculate source/state/remaining time.
 * Why: Moving or completing the real Plan object must immediately change its countdown without duplicate persistence.
 * Maintenance: Preserve cancellation, recurring event occurrence expansion and source IDs.
 */

using Haven.Core;

namespace Haven.Application;

public sealed class PlannerCountdownService(IPlannerRepository repository) : IPlannerCountdownService
{
    public async Task<IReadOnlyList<PlannerCountdown>> GetCountdownsAsync(
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (windowEnd <= windowStart)
            throw new ArgumentException("The countdown window must end after it starts.", nameof(windowEnd));

        cancellationToken.ThrowIfCancellationRequested();
        var tasksTask = repository.GetTasksAsync(
            new PlannerTaskQuery(
                RangeStart: windowStart,
                RangeEnd: windowEnd,
                IncludeCompleted: true),
            cancellationToken);
        var eventsTask = repository.GetEventsAsync(windowStart, windowEnd, null, cancellationToken);

        await Task.WhenAll(tasksTask, eventsTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var taskCountdowns = (await tasksTask.ConfigureAwait(false))
            .Select(item => PlannerCountdownProjection.FromTask(item, now))
            .Where(item => item is not null && item.TargetAt >= windowStart && item.TargetAt < windowEnd)
            .Select(item => item!);
        var eventCountdowns = (await eventsTask.ConfigureAwait(false))
            .Select(item => PlannerCountdownProjection.FromEvent(item, now))
            .Where(item => item.TargetAt >= windowStart && item.TargetAt < windowEnd);

        return taskCountdowns
            .Concat(eventCountdowns)
            .OrderBy(item => item.TargetAt)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
