/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/PlannerAvailabilityTests.cs, in the Core automated test suite.
 * What: Protects deterministic free-window calculation over canonical Plan day state.
 * How: Representative day snapshots verify overlap merging, non-blocking item semantics, clamping and argument validation.
 * Why: Study and AI scheduling must not double-book real Plan time or invent availability outside the requested day.
 * Maintenance: Keep tests deterministic and free of wall-clock dependencies.
 */

using Haven.Core;

namespace Haven.Core.Tests;

public sealed class PlannerAvailabilityTests
{
    [Fact]
    public void FindFreeWindowsMergesOverlappingBusyBlocks()
    {
        var dayStart = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);
        var created = dayStart.AddDays(-1);
        var first = NewEvent("Maths", dayStart.AddHours(9), dayStart.AddHours(10), created);
        var overlap = NewEvent("Tutorial", dayStart.AddHours(9.5), dayStart.AddHours(11), created);
        var revision = NewTask("Revision", dayStart.AddHours(13), 60, created);

        var snapshot = PlannerDayTimeline.Build(dayStart, dayEnd, dayStart.AddHours(8), [revision], [first, overlap]);
        var free = PlannerAvailability.FindFreeWindows(
            snapshot,
            dayStart.AddHours(9),
            dayStart.AddHours(17),
            TimeSpan.FromMinutes(45));

        Assert.Equal(2, free.Count);
        Assert.Equal(new PlannerFreeWindow(dayStart.AddHours(11), dayStart.AddHours(13)), free[0]);
        Assert.Equal(new PlannerFreeWindow(dayStart.AddHours(14), dayStart.AddHours(17)), free[1]);
    }

    [Fact]
    public void FindFreeWindowsIgnoresAllDayCompletedCancelledAndUntimedItems()
    {
        var dayStart = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);
        var created = dayStart.AddDays(-1);
        var allDay = NewEvent("Results Day", dayStart, dayEnd, created) with { IsAllDay = true };
        var completed = NewTask("Done", dayStart.AddHours(10), 120, created) with { Status = PlannerTaskStatus.Completed };
        var cancelled = NewTask("Cancelled", dayStart.AddHours(13), 60, created) with { Status = PlannerTaskStatus.Cancelled };
        var untimed = NewTask("Deadline", dayStart.AddHours(15), null, created) with
        {
            StartsAt = null,
            DueAt = dayStart.AddHours(15)
        };

        var snapshot = PlannerDayTimeline.Build(dayStart, dayEnd, dayStart.AddHours(8), [completed, cancelled, untimed], [allDay]);
        var free = PlannerAvailability.FindFreeWindows(
            snapshot,
            dayStart.AddHours(9),
            dayStart.AddHours(17),
            TimeSpan.FromHours(1));

        var only = Assert.Single(free);
        Assert.Equal(dayStart.AddHours(9), only.StartsAt);
        Assert.Equal(dayStart.AddHours(17), only.EndsAt);
    }

    [Fact]
    public void FindFreeWindowsClampsToDayAndValidatesArguments()
    {
        var dayStart = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);
        var snapshot = PlannerDayTimeline.Build(dayStart, dayEnd, dayStart, [], []);

        var free = PlannerAvailability.FindFreeWindows(
            snapshot,
            dayStart.AddDays(-1),
            dayEnd.AddDays(1),
            TimeSpan.FromHours(23));

        var only = Assert.Single(free);
        Assert.Equal(dayStart, only.StartsAt);
        Assert.Equal(dayEnd, only.EndsAt);
        Assert.Throws<ArgumentException>(() =>
            PlannerAvailability.FindFreeWindows(snapshot, dayEnd, dayStart, TimeSpan.FromHours(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlannerAvailability.FindFreeWindows(snapshot, dayStart, dayEnd, TimeSpan.Zero));
    }

    private static PlannerTask NewTask(string title, DateTimeOffset startsAt, int? estimatedMinutes, DateTimeOffset created) =>
        new(
            Guid.NewGuid(),
            PlannerDefaults.CollegeCollectionId,
            null,
            title,
            string.Empty,
            PlannerPriority.None,
            PlannerTaskStatus.Planned,
            "[]",
            estimatedMinutes,
            startsAt,
            startsAt.AddHours(2),
            null,
            null,
            null,
            0,
            created,
            created);

    private static PlannerEvent NewEvent(string title, DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset created) =>
        new(
            Guid.NewGuid(),
            PlannerDefaults.LocalCalendarId,
            title,
            string.Empty,
            string.Empty,
            startsAt,
            endsAt,
            false,
            null,
            null,
            false,
            null,
            null,
            created,
            created);
}
