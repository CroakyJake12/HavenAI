/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Core/DomainLogic/PlannerAvailability.cs, in the dependency-free Core layer.
 * What: Derives usable free windows from one canonical PlannerDaySnapshot.
 * How: Actionable timed items are clamped, sorted and merged before gaps that satisfy a minimum duration are returned.
 * Why: Plan, Study and AI scheduling need one deterministic answer to "when am I free?" instead of duplicating timetable logic.
 * Maintenance: Keep this projection side-effect free and do not treat all-day, completed, cancelled or untimed items as busy time.
 */

namespace Haven.Core;

public sealed record PlannerFreeWindow(DateTimeOffset StartsAt, DateTimeOffset EndsAt)
{
    public TimeSpan Duration => EndsAt - StartsAt;
}

public static class PlannerAvailability
{
    public static IReadOnlyList<PlannerFreeWindow> FindFreeWindows(
        PlannerDaySnapshot snapshot,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        TimeSpan minimumDuration)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (windowEnd <= windowStart)
            throw new ArgumentException("The availability window must end after it starts.", nameof(windowEnd));
        if (minimumDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(minimumDuration), "The minimum duration must be positive.");

        var effectiveStart = windowStart > snapshot.DayStart ? windowStart : snapshot.DayStart;
        var effectiveEnd = windowEnd < snapshot.DayEnd ? windowEnd : snapshot.DayEnd;
        if (effectiveEnd <= effectiveStart) return [];

        var busy = snapshot.Items
            .Where(item =>
                item.IsActionable
                && item.IsTimed
                && item.StartsAt < effectiveEnd
                && item.EndsAt > effectiveStart)
            .Select(item => new PlannerFreeWindow(
                item.StartsAt!.Value > effectiveStart ? item.StartsAt.Value : effectiveStart,
                item.EndsAt!.Value < effectiveEnd ? item.EndsAt.Value : effectiveEnd))
            .OrderBy(item => item.StartsAt)
            .ThenBy(item => item.EndsAt)
            .ToArray();

        if (busy.Length == 0)
            return effectiveEnd - effectiveStart >= minimumDuration
                ? [new PlannerFreeWindow(effectiveStart, effectiveEnd)]
                : [];

        var merged = new List<PlannerFreeWindow>(busy.Length);
        foreach (var block in busy)
        {
            if (merged.Count == 0 || block.StartsAt > merged[^1].EndsAt)
            {
                merged.Add(block);
                continue;
            }

            if (block.EndsAt > merged[^1].EndsAt)
                merged[^1] = merged[^1] with { EndsAt = block.EndsAt };
        }

        var free = new List<PlannerFreeWindow>();
        var cursor = effectiveStart;
        foreach (var block in merged)
        {
            if (block.StartsAt > cursor && block.StartsAt - cursor >= minimumDuration)
                free.Add(new PlannerFreeWindow(cursor, block.StartsAt));
            if (block.EndsAt > cursor) cursor = block.EndsAt;
        }

        if (effectiveEnd > cursor && effectiveEnd - cursor >= minimumDuration)
            free.Add(new PlannerFreeWindow(cursor, effectiveEnd));

        return free;
    }
}
