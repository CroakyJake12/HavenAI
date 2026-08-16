/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/PlannerDayTimelineTests.cs, in the Core automated test suite.
 * What: Protects deterministic Today/day-organiser timeline behavior.
 * How: Builds snapshots from representative canonical Plan tasks and events without UI or persistence.
 * Why: Now/next/progress semantics are shared by Plan, Study and AI planning surfaces.
 * Maintenance: Keep cases deterministic and add coverage when timeline semantics expand.
 */

using Haven.Core;

namespace Haven.Core.Tests;

public sealed class PlannerDayTimelineTests
{
    [Fact]
    public void BuildReportsOverlappingCurrentItemsNextItemAndProgress()
    {
        var dayStart = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);
        var now = dayStart.AddHours(10.5);
        var created = dayStart.AddDays(-1);

        var task = NewTask("Revision", dayStart.AddHours(10.25), 60, created);
        var currentEvent = NewEvent("Maths lesson", dayStart.AddHours(10), dayStart.AddHours(11), created);
        var nextEvent = NewEvent("Law lesson", dayStart.AddHours(12), dayStart.AddHours(13), created);

        var snapshot = PlannerDayTimeline.Build(dayStart, dayEnd, now, [task], [currentEvent, nextEvent]);

        Assert.Equal(3, snapshot.Items.Count);
        Assert.Equal(2, snapshot.ActiveItems.Count);
        Assert.Equal(currentEvent.Id, snapshot.CurrentItem?.EntityId);
        Assert.Equal(nextEvent.Id, snapshot.NextItem?.EntityId);
        Assert.Equal(dayStart.AddHours(10), snapshot.ScheduleStart);
        Assert.Equal(dayStart.AddHours(13), snapshot.ScheduleEnd);
        Assert.Equal(1d / 6d, snapshot.Progress, 6);
    }

    [Fact]
    public void BuildIncludesTimedTaskWhoseEstimateOverlapsDayStart()
    {
        var dayStart = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);
        var now = dayStart.AddMinutes(15);
        var created = dayStart.AddDays(-1);
        var spanning = NewTask("Late revision", dayStart.AddMinutes(-30), 90, created) with { DueAt = null };
        var endedBeforeDay = NewTask("Earlier revision", dayStart.AddHours(-2), 60, created) with { DueAt = null };
        var untimedFromPreviousDay = NewTask("Previous note", dayStart.AddMinutes(-30), null, created) with { DueAt = null };

        var snapshot = PlannerDayTimeline.Build(dayStart, dayEnd, now, [spanning, endedBeforeDay, untimedFromPreviousDay], []);

        var item = Assert.Single(snapshot.Items);
        Assert.Equal(spanning.Id, item.EntityId);
        Assert.Equal(dayStart.AddHours(1), item.EndsAt);
        Assert.Equal(spanning.Id, snapshot.CurrentItem?.EntityId);
    }

    [Fact]
    public void AllDayAndCompletedItemsDoNotBecomeCurrentOrNextScheduleBlocks()
    {
        var dayStart = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);
        var now = dayStart.AddHours(9);
        var created = dayStart.AddDays(-1);
        var allDay = NewEvent("Results Day", dayStart, dayEnd, created) with { IsAllDay = true };
        var completed = NewTask("Pack bag", dayStart.AddHours(9), 30, created) with
        {
            Status = PlannerTaskStatus.Completed,
            CompletedAt = dayStart.AddHours(8.5)
        };
        var future = NewTask("Revision", dayStart.AddHours(11), null, created);

        var snapshot = PlannerDayTimeline.Build(dayStart, dayEnd, now, [completed, future], [allDay]);

        Assert.Empty(snapshot.ActiveItems);
        Assert.Null(snapshot.CurrentItem);
        Assert.Equal(future.Id, snapshot.NextItem?.EntityId);
        Assert.Equal(completed.StartsAt, snapshot.ScheduleStart);
        Assert.Equal(future.StartsAt, snapshot.ScheduleEnd);
        Assert.Equal(0d, snapshot.Progress);
    }

    [Fact]
    public void GetDayBoundsUsesRequestedTimeZoneInsteadOfUtcDate()
    {
        var instant = new DateTimeOffset(2026, 8, 19, 23, 30, 0, TimeSpan.Zero);
        var (start, end) = PlannerDayTimeline.GetDayBounds(instant, "UTC");

        Assert.Equal(new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(start.AddDays(1), end);
        Assert.Throws<FormatException>(() => PlannerDayTimeline.GetDayBounds(instant, "not-a-real-zone"));
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
