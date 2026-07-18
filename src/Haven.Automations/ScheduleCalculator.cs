/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Automations/ScheduleCalculator.cs, in the Automations layer, which parses schedules and runs durable background actions.
 * What: This file owns ScheduleCalculator. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Globalization;
using System.Text.Json;
using Haven.Core;

namespace Haven.Automations;

/// <summary>
/// Represents schedule calculator and keeps its related state and behavior together.
/// </summary>
public sealed class ScheduleCalculator
{
    /// <summary>
    /// Stores time zone locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TimeZoneInfo _timeZone;

    public ScheduleCalculator(TimeZoneInfo? timeZone = null) => _timeZone = timeZone ?? TimeZoneInfo.Local;
    /// <summary>
    /// Retrieves next run for the current operation.
    /// </summary>
    public DateTimeOffset? GetNextRun(AutomationDefinition automation, DateTimeOffset after)
    {
        if (!automation.IsEnabled) return null;
        using var document = Parse(automation.ScheduleJson);
        var root = document.RootElement;
        return automation.ScheduleKind switch
        {
            AutomationScheduleKind.Once => ReadDate(root, "at") is { } once && once > after ? once.ToUniversalTime() : null,
            AutomationScheduleKind.Hourly => after.ToUniversalTime().AddHours(Math.Max(1, ReadInt(root, "intervalHours", 1))),
            AutomationScheduleKind.Daily => NextDaily(after, ReadTime(root, "time", new TimeOnly(8, 0))),
            AutomationScheduleKind.Weekly => NextWeekly(after, ReadDay(root, "dayOfWeek", DayOfWeek.Monday), ReadTime(root, "time", new TimeOnly(8, 0))),
            AutomationScheduleKind.ConditionWatch => after.ToUniversalTime().AddMinutes(Math.Max(60, ReadInt(root, "intervalMinutes", 60))),
            _ => null
        };
    }

    /// <summary>
    /// Retrieves initial run for the current operation.
    /// </summary>
    public DateTimeOffset GetInitialRun(AutomationScheduleKind kind, string scheduleJson, DateTimeOffset now)
    {
        var placeholder = new AutomationDefinition(Guid.Empty, string.Empty, HavenMode.Chat, string.Empty, kind, scheduleJson, null, null, true, now, now);
        return GetNextRun(placeholder, now.AddTicks(-1)) ?? now.ToUniversalTime();
    }

    /// <summary>
    /// Performs the next daily step owned by this component.
    /// </summary>
    private DateTimeOffset NextDaily(DateTimeOffset after, TimeOnly time)
    {
        var localAfter = TimeZoneInfo.ConvertTime(after, _timeZone);
        var candidate = CreateLocal(localAfter.Date, time);
        if (candidate <= localAfter) candidate = CreateLocal(localAfter.Date.AddDays(1), time);
        return candidate.ToUniversalTime();
    }

    /// <summary>
    /// Performs the next weekly step owned by this component.
    /// </summary>
    private DateTimeOffset NextWeekly(DateTimeOffset after, DayOfWeek day, TimeOnly time)
    {
        var localAfter = TimeZoneInfo.ConvertTime(after, _timeZone);
        var delta = ((int)day - (int)localAfter.DayOfWeek + 7) % 7;
        var candidate = CreateLocal(localAfter.Date.AddDays(delta), time);
        if (candidate <= localAfter) candidate = CreateLocal(localAfter.Date.AddDays(delta + 7), time);
        return candidate.ToUniversalTime();
    }

    /// <summary>
    /// Creates local with the invariants required by its callers.
    /// </summary>
    private DateTimeOffset CreateLocal(DateTime date, TimeOnly time)
    {
        var unspecified = DateTime.SpecifyKind(date.Add(time.ToTimeSpan()), DateTimeKind.Unspecified);
        var offset = _timeZone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset);
    }

    /// <summary>
    /// Performs the parse step owned by this component.
    /// </summary>
    private static JsonDocument Parse(string json)
    {
        try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); }
        catch (JsonException ex) { throw new FormatException("The automation schedule is not valid JSON.", ex); }
    }

    /// <summary>
    /// Performs the read int step owned by this component.
    /// </summary>
    private static int ReadInt(JsonElement root, string property, int fallback) =>
        root.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : fallback;

    /// <summary>
    /// Performs the read date step owned by this component.
    /// </summary>
    private static DateTimeOffset? ReadDate(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result) ? result : null;

    /// <summary>
    /// Performs the read time step owned by this component.
    /// </summary>
    private static TimeOnly ReadTime(JsonElement root, string property, TimeOnly fallback) =>
        root.TryGetProperty(property, out var value) && TimeOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var result) ? result : fallback;

    /// <summary>
    /// Performs the read day step owned by this component.
    /// </summary>
    private static DayOfWeek ReadDay(JsonElement root, string property, DayOfWeek fallback) =>
        root.TryGetProperty(property, out var value) && Enum.TryParse<DayOfWeek>(value.GetString(), true, out var result) ? result : fallback;
}
