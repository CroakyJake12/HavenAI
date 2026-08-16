/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Planner/PlannerDayService.cs, in the Application layer.
 * What: Loads canonical Plan tasks/events for a requested local day and creates the Today/day-organiser snapshot.
 * How: Repository I/O stays behind IPlannerRepository while deterministic timeline rules live in Haven.Core.
 * Why: Plan UI, Study and AI planning should consume the same real persisted day state rather than recreate schedule logic.
 * Maintenance: Preserve cancellation and keep provider/calendar semantics in the repository.
 */

using Haven.Core;

namespace Haven.Application;

public sealed class PlannerDayService(IPlannerRepository repository) : IPlannerDayService
{
    public async Task<PlannerDaySnapshot> GetDayAsync(
        DateTimeOffset day,
        DateTimeOffset now,
        string? timeZoneId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (dayStart, dayEnd) = PlannerDayTimeline.GetDayBounds(day, timeZoneId);

        var eventsTask = repository.GetEventsAsync(dayStart, dayEnd, null, cancellationToken);
        var tasksTask = repository.GetTasksAsync(
            new PlannerTaskQuery(
                RangeStart: dayStart,
                RangeEnd: dayEnd,
                IncludeCompleted: true),
            cancellationToken);

        await Task.WhenAll(eventsTask, tasksTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return PlannerDayTimeline.Build(
            dayStart,
            dayEnd,
            now,
            await tasksTask.ConfigureAwait(false),
            await eventsTask.ConfigureAwait(false));
    }
}
