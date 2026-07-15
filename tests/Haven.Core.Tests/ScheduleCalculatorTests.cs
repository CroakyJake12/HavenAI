using Haven.Automations;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class ScheduleCalculatorTests
{
    private readonly ScheduleCalculator _calculator = new(TimeZoneInfo.Utc);

    [Fact]
    public void DailyScheduleMovesToTomorrowAfterTimeHasPassed()
    {
        var now = new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);
        var automation = Create(AutomationScheduleKind.Daily, "{\"time\":\"08:30\"}");
        Assert.Equal(new DateTimeOffset(2026, 7, 14, 8, 30, 0, TimeSpan.Zero), _calculator.GetNextRun(automation, now));
    }

    [Fact]
    public void WeeklyScheduleUsesRequestedDayAndTime()
    {
        var now = new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero); // Monday
        var automation = Create(AutomationScheduleKind.Weekly, "{\"dayOfWeek\":\"Wednesday\",\"time\":\"19:15\"}");
        Assert.Equal(new DateTimeOffset(2026, 7, 15, 19, 15, 0, TimeSpan.Zero), _calculator.GetNextRun(automation, now));
    }

    [Fact]
    public void DisabledAutomationHasNoNextRun()
    {
        var automation = Create(AutomationScheduleKind.Hourly, "{}") with { IsEnabled = false };
        Assert.Null(_calculator.GetNextRun(automation, DateTimeOffset.UtcNow));
    }

    private static AutomationDefinition Create(AutomationScheduleKind kind, string json)
    {
        var now = DateTimeOffset.UtcNow;
        return new AutomationDefinition(Guid.NewGuid(), "Test", HavenMode.Chat, "Test", kind, json, null, null, true, now, now);
    }
}
