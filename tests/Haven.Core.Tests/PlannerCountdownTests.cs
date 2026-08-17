/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/PlannerCountdownTests.cs, in the Core automated test suite.
 * What: Protects deterministic countdown projections over canonical Plan tasks and events.
 * How: Creates in-memory Plan records and verifies source links, states, reminders and remaining time.
 * Why: Countdown UI must never drift from the real deadline/event that owns its persisted state.
 * Maintenance: Keep these tests free of wall-clock dependencies.
 */

using Haven.Core;

namespace Haven.Core.Tests;

public sealed class PlannerCountdownTests
{
    [Fact]
    public void TaskCountdownUsesDueDateAndKeepsPlanLinkAndReminder()
    {
        var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var due = now.AddDays(5);
        var reminder = due.AddHours(-2);
        var task = NewTask(now) with { DueAt = due, StartsAt = now.AddDays(1), ReminderAt = reminder };

        var countdown = PlannerCountdownProjection.FromTask(task, now);

        Assert.NotNull(countdown);
        Assert.Equal(task.Id, countdown.SourceId);
        Assert.Equal(PlannerCountdownSourceKind.Task, countdown.SourceKind);
        Assert.Equal(due, countdown.TargetAt);
        Assert.Equal(PlannerCountdownState.Upcoming, countdown.State);
        Assert.Equal(reminder, countdown.ReminderAt);
        Assert.Equal(task.CollectionId, countdown.CollectionId);
        Assert.Equal(TimeSpan.FromDays(5), countdown.Remaining);
    }

    [Fact]
    public void CompletedAndCancelledTasksKeepTerminalCountdownState()
    {
        var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var completed = NewTask(now) with { DueAt = now.AddDays(-1), Status = PlannerTaskStatus.Completed, CompletedAt = now };
        var cancelled = NewTask(now) with { DueAt = now.AddDays(1), Status = PlannerTaskStatus.Cancelled };

        Assert.Equal(PlannerCountdownState.Completed, PlannerCountdownProjection.FromTask(completed, now)?.State);
        Assert.Equal(PlannerCountdownState.Cancelled, PlannerCountdownProjection.FromTask(cancelled, now)?.State);
    }

    [Fact]
    public void EventCountdownUsesOccurrenceStartAndPreservesCalendarReadOnlyState()
    {
        var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var created = now.AddDays(-2);
        var item = new PlannerEvent(
            Guid.NewGuid(),
            PlannerDefaults.LocalCalendarId,
            "Results Day",
            string.Empty,
            string.Empty,
            now.AddHours(-1),
            now.AddHours(1),
            false,
            null,
            now.AddHours(-2),
            true,
            "provider-event",
            "etag",
            created,
            created);

        var countdown = PlannerCountdownProjection.FromEvent(item, now);

        Assert.Equal(item.Id, countdown.SourceId);
        Assert.Equal(item.CalendarId, countdown.CalendarId);
        Assert.True(countdown.IsReadOnly);
        Assert.True(countdown.HasPassed);
        Assert.Equal(TimeSpan.FromHours(-1), countdown.Remaining);
    }

    private static PlannerTask NewTask(DateTimeOffset created) =>
        new(
            Guid.NewGuid(),
            PlannerDefaults.CollegeCollectionId,
            null,
            "Mock countdown",
            string.Empty,
            PlannerPriority.None,
            PlannerTaskStatus.Planned,
            "[]",
            null,
            null,
            null,
            null,
            null,
            null,
            0,
            created,
            created);
}
