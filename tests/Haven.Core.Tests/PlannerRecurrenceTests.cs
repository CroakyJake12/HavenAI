using Haven.Core;

namespace Haven.Core.Tests;

public sealed class PlannerRecurrenceTests
{
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

    [Fact]
    public void WeeklyByDaySelectsNextMatchingDay()
    {
        var monday = new DateTimeOffset(2026, 7, 13, 9, 0, 0, TimeSpan.Zero);
        var next = PlannerRecurrence.GetNextOccurrence(monday, "FREQ=WEEKLY;BYDAY=WE,FR", "UTC");
        Assert.Equal(DayOfWeek.Wednesday, next?.DayOfWeek);
        Assert.Equal(9, next?.Hour);
    }

    [Fact]
    public void MultiWeekIntervalSkipsInactiveWeekAfterLastByDay()
    {
        var friday = new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.Zero);
        var next = PlannerRecurrence.GetNextOccurrence(friday, "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,FR", "UTC");
        Assert.Equal(new DateTimeOffset(2026, 7, 27, 9, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void InvalidRuleIsRejected()
    {
        Assert.Throws<FormatException>(() => PlannerRecurrence.Validate("FREQ=MINUTELY"));
        Assert.Throws<FormatException>(() => PlannerRecurrence.Validate("INTERVAL=2"));
    }
}
