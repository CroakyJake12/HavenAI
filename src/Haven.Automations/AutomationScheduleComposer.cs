using System.Globalization;
using System.Text.Json;
using Haven.Core;

namespace Haven.Automations;

public sealed record AutomationScheduleDraft(
    DateTimeOffset OnceAt,
    TimeOnly Time,
    DayOfWeek DayOfWeek,
    int IntervalHours,
    int ConditionIntervalMinutes);

public static class AutomationScheduleComposer
{
    public static string Compose(AutomationScheduleKind kind, AutomationScheduleDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var value = kind switch
        {
            AutomationScheduleKind.Once => new Dictionary<string, object?>
            {
                ["at"] = draft.OnceAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            },
            AutomationScheduleKind.Hourly => new Dictionary<string, object?>
            {
                ["intervalHours"] = Math.Clamp(draft.IntervalHours, 1, 168)
            },
            AutomationScheduleKind.Daily => new Dictionary<string, object?>
            {
                ["time"] = draft.Time.ToString("HH:mm", CultureInfo.InvariantCulture)
            },
            AutomationScheduleKind.Weekly => new Dictionary<string, object?>
            {
                ["dayOfWeek"] = draft.DayOfWeek.ToString(),
                ["time"] = draft.Time.ToString("HH:mm", CultureInfo.InvariantCulture)
            },
            AutomationScheduleKind.ConditionWatch => new Dictionary<string, object?>
            {
                ["intervalMinutes"] = Math.Clamp(draft.ConditionIntervalMinutes, 60, 10_080)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported automation schedule kind.")
        };
        return JsonSerializer.Serialize(value);
    }

    public static AutomationScheduleDraft Parse(
        AutomationScheduleKind kind,
        string? scheduleJson,
        DateTimeOffset now)
    {
        var fallback = new AutomationScheduleDraft(
            now.AddHours(1),
            new TimeOnly(8, 0),
            DayOfWeek.Monday,
            1,
            60);
        if (string.IsNullOrWhiteSpace(scheduleJson)) return fallback;
        try
        {
            using var document = JsonDocument.Parse(scheduleJson);
            var root = document.RootElement;
            var once = root.TryGetProperty("at", out var at)
                       && DateTimeOffset.TryParse(at.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedAt)
                ? parsedAt.ToLocalTime()
                : fallback.OnceAt;
            var time = root.TryGetProperty("time", out var timeValue)
                       && TimeOnly.TryParse(timeValue.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTime)
                ? parsedTime
                : fallback.Time;
            var day = root.TryGetProperty("dayOfWeek", out var dayValue)
                      && Enum.TryParse<DayOfWeek>(dayValue.GetString(), true, out var parsedDay)
                ? parsedDay
                : fallback.DayOfWeek;
            var hours = root.TryGetProperty("intervalHours", out var hourValue) && hourValue.TryGetInt32(out var parsedHours)
                ? Math.Clamp(parsedHours, 1, 168)
                : fallback.IntervalHours;
            var minutes = root.TryGetProperty("intervalMinutes", out var minuteValue) && minuteValue.TryGetInt32(out var parsedMinutes)
                ? Math.Clamp(parsedMinutes, 60, 10_080)
                : fallback.ConditionIntervalMinutes;
            return new AutomationScheduleDraft(once, time, day, hours, minutes);
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    public static string Describe(AutomationScheduleKind kind, AutomationScheduleDraft draft) => kind switch
    {
        AutomationScheduleKind.Once => $"Once at {draft.OnceAt.ToLocalTime():g}",
        AutomationScheduleKind.Hourly => draft.IntervalHours == 1 ? "Every hour" : $"Every {draft.IntervalHours} hours",
        AutomationScheduleKind.Daily => $"Daily at {draft.Time:HH\\:mm}",
        AutomationScheduleKind.Weekly => $"Every {draft.DayOfWeek} at {draft.Time:HH\\:mm}",
        AutomationScheduleKind.ConditionWatch => draft.ConditionIntervalMinutes == 60
            ? "Check the condition hourly"
            : $"Check the condition every {draft.ConditionIntervalMinutes} minutes",
        _ => kind.ToString()
    };
}
