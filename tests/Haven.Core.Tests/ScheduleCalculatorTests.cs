/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/ScheduleCalculatorTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns ScheduleCalculatorTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application.Tasks;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>
/// Represents schedule calculator tests and keeps its related state and behavior together.
/// </summary>
public sealed class ScheduleCalculatorTests
{
    /// <summary>
    /// Stores calculator locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ScheduledTaskScheduleCalculator _calculator = new(TimeZoneInfo.Utc);

    /// <summary>
    /// Performs the daily schedule moves to tomorrow after time has passed step owned by this component.
    /// </summary>
    [Fact]
    public void DailyScheduleMovesToTomorrowAfterTimeHasPassed()
    {
        var now = new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);
        var automation = Create(AutomationScheduleKind.Daily, "{\"time\":\"08:30\"}");
        Assert.Equal(new DateTimeOffset(2026, 7, 14, 8, 30, 0, TimeSpan.Zero), _calculator.GetNextRun(automation, now));
    }

    /// <summary>
    /// Performs the weekly schedule uses requested day and time step owned by this component.
    /// </summary>
    [Fact]
    public void WeeklyScheduleUsesRequestedDayAndTime()
    {
        var now = new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero); // Monday
        var automation = Create(AutomationScheduleKind.Weekly, "{\"dayOfWeek\":\"Wednesday\",\"time\":\"19:15\"}");
        Assert.Equal(new DateTimeOffset(2026, 7, 15, 19, 15, 0, TimeSpan.Zero), _calculator.GetNextRun(automation, now));
    }

    /// <summary>
    /// Performs the disabled automation has no next run step owned by this component.
    /// </summary>
    [Fact]
    public void DisabledAutomationHasNoNextRun()
    {
        var automation = Create(AutomationScheduleKind.Hourly, "{}") with { IsEnabled = false };
        Assert.Null(_calculator.GetNextRun(automation, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Creates this member with the invariants required by its callers.
    /// </summary>
    private static AutomationDefinition Create(AutomationScheduleKind kind, string json)
    {
        var now = DateTimeOffset.UtcNow;
        return new AutomationDefinition(Guid.NewGuid(), "Test", HavenMode.Chat, "Test", kind, json, null, null, true, now, now);
    }
}
