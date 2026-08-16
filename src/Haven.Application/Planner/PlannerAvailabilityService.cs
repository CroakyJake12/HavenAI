/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Planner/PlannerAvailabilityService.cs, in the Application layer.
 * What: Exposes free-time queries over canonical persisted Plan day state.
 * How: The service loads a PlannerDaySnapshot through IPlannerDayService and delegates deterministic gap calculation to Haven.Core.
 * Why: Study and AI planning should schedule around the same tasks and calendar events shown by Plan.
 * Maintenance: Keep persistence behind IPlannerDayService and carry cancellation through the day-state load.
 */

using Haven.Core;

namespace Haven.Application;

public sealed class PlannerAvailabilityService(IPlannerDayService dayService) : IPlannerAvailabilityService
{
    public async Task<IReadOnlyList<PlannerFreeWindow>> GetFreeWindowsAsync(
        DateTimeOffset day,
        DateTimeOffset now,
        string? timeZoneId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        TimeSpan minimumDuration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = await dayService.GetDayAsync(day, now, timeZoneId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return PlannerAvailability.FindFreeWindows(snapshot, windowStart, windowEnd, minimumDuration);
    }
}
