/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/PlannerRecurrenceTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns PlannerRecurrenceTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>
/// Represents planner recurrence tests and keeps its related state and behavior together.
/// </summary>
public sealed class PlannerRecurrenceTests
{
    /// <summary>
    /// Performs the daily recurrence preserves local wall clock across dst step owned by this component.
    /// </summary>
    [Fact]
    public void DailyRecurrencePreservesLocalWallClockAcrossDst()
    {
        var zone = TimeZoneInfo.Local;
        var originalLocal = new DateTime(2026, 3, 28, 9, 0, 0, DateTimeKind.Unspecified);
        var original = new DateTimeOffset(originalLocal, zone.GetUtcOffset(originalLocal));
        var next = PlannerRecurrence.GetNextOccurrence(original, "FREQ=DAILY", zone.Id);
        Assert.NotNull(next);
        Assert.Equal(9, TimeZoneInfo.ConvertTime(next.Value, zone).Hour);
        Assert.Equal(originalLocal.Date.AddDays(1), TimeZoneInfo.ConvertTime(next.Value, zone).Date);
    }

    /// <summary>
    /// Performs the weekly by day selects next matching day step owned by this component.
    /// </summary>
    [Fact]
    public void WeeklyByDaySelectsNextMatchingDay()
    {
        var monday = new DateTimeOffset(2026, 7, 13, 9, 0, 0, TimeSpan.Zero);
        var next = PlannerRecurrence.GetNextOccurrence(monday, "FREQ=WEEKLY;BYDAY=WE,FR", "UTC");
        Assert.Equal(DayOfWeek.Wednesday, next?.DayOfWeek);
        Assert.Equal(9, next?.Hour);
    }

    /// <summary>
    /// Performs the multi week interval skips inactive week after last by day step owned by this component.
    /// </summary>
    [Fact]
    public void MultiWeekIntervalSkipsInactiveWeekAfterLastByDay()
    {
        var friday = new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.Zero);
        var next = PlannerRecurrence.GetNextOccurrence(friday, "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,FR", "UTC");
        Assert.Equal(new DateTimeOffset(2026, 7, 27, 9, 0, 0, TimeSpan.Zero), next);
    }

    /// <summary>
    /// Performs the invalid rule is rejected step owned by this component.
    /// </summary>
    [Fact]
    public void InvalidRuleIsRejected()
    {
        Assert.Throws<FormatException>(() => PlannerRecurrence.Validate("FREQ=MINUTELY"));
        Assert.Throws<FormatException>(() => PlannerRecurrence.Validate("INTERVAL=2"));
    }
}
