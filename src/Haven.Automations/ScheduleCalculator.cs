using System.Globalization;
using System.Text.Json;
using Haven.Core;

namespace Haven.Automations;

public sealed class ScheduleCalculator
{
    private readonly TimeZoneInfo _timeZone;

    public ScheduleCalculator(TimeZoneInfo? timeZone = null) => _timeZone = timeZone ?? TimeZoneInfo.Local;
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

    public DateTimeOffset GetInitialRun(AutomationScheduleKind kind, string scheduleJson, DateTimeOffset now)
    {
        var placeholder = new AutomationDefinition(Guid.Empty, string.Empty, HavenMode.Chat, string.Empty, kind, scheduleJson, null, null, true, now, now);
        return GetNextRun(placeholder, now.AddTicks(-1)) ?? now.ToUniversalTime();
    }

    private DateTimeOffset NextDaily(DateTimeOffset after, TimeOnly time)
    {
        var localAfter = TimeZoneInfo.ConvertTime(after, _timeZone);
        var candidate = CreateLocal(localAfter.Date, time);
        if (candidate <= localAfter) candidate = CreateLocal(localAfter.Date.AddDays(1), time);
        return candidate.ToUniversalTime();
    }

    private DateTimeOffset NextWeekly(DateTimeOffset after, DayOfWeek day, TimeOnly time)
    {
        var localAfter = TimeZoneInfo.ConvertTime(after, _timeZone);
        var delta = ((int)day - (int)localAfter.DayOfWeek + 7) % 7;
        var candidate = CreateLocal(localAfter.Date.AddDays(delta), time);
        if (candidate <= localAfter) candidate = CreateLocal(localAfter.Date.AddDays(delta + 7), time);
        return candidate.ToUniversalTime();
    }

    private DateTimeOffset CreateLocal(DateTime date, TimeOnly time)
    {
        var unspecified = DateTime.SpecifyKind(date.Add(time.ToTimeSpan()), DateTimeKind.Unspecified);
        var offset = _timeZone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset);
    }

    private static JsonDocument Parse(string json)
    {
        try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); }
        catch (JsonException ex) { throw new FormatException("The automation schedule is not valid JSON.", ex); }
    }

    private static int ReadInt(JsonElement root, string property, int fallback) =>
        root.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : fallback;

    private static DateTimeOffset? ReadDate(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result) ? result : null;

    private static TimeOnly ReadTime(JsonElement root, string property, TimeOnly fallback) =>
        root.TryGetProperty(property, out var value) && TimeOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var result) ? result : fallback;

    private static DayOfWeek ReadDay(JsonElement root, string property, DayOfWeek fallback) =>
        root.TryGetProperty(property, out var value) && Enum.TryParse<DayOfWeek>(value.GetString(), true, out var result) ? result : fallback;
}
