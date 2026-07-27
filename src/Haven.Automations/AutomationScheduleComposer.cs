/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Automations/AutomationScheduleComposer.cs, in the Automations layer, which parses schedules and runs durable background actions.
 * What: This file owns AutomationScheduleDraft, AutomationScheduleComposer. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Globalization;
using System.Text.Json;
using Haven.Core;

namespace Haven.Automations;

/// <summary>
/// Represents automation schedule draft and keeps its related state and behavior together.
/// </summary>
public sealed record AutomationScheduleDraft(
    DateTimeOffset OnceAt,
    TimeOnly Time,
    DayOfWeek DayOfWeek,
    int IntervalHours,
    int ConditionIntervalMinutes);

/// <summary>
/// Represents automation schedule composer and keeps its related state and behavior together.
/// </summary>
public static class AutomationScheduleComposer
{
    /// <summary>
    /// Performs the compose step owned by this component.
    /// </summary>
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
                ["time"] = FormatTime(draft.Time)
            },
            AutomationScheduleKind.Weekly => new Dictionary<string, object?>
            {
                ["dayOfWeek"] = draft.DayOfWeek.ToString(),
                ["time"] = FormatTime(draft.Time)
            },
            AutomationScheduleKind.ConditionWatch => new Dictionary<string, object?>
            {
                ["intervalMinutes"] = Math.Clamp(draft.ConditionIntervalMinutes, 60, 10_080)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported automation schedule kind.")
        };
        return JsonSerializer.Serialize(value);
    }

    /// <summary>
    /// Performs the parse step owned by this component.
    /// </summary>
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
                       && DateTimeOffset.TryParse(
                           at.GetString(),
                           CultureInfo.InvariantCulture,
                           DateTimeStyles.RoundtripKind,
                           out var parsedAt)
                ? parsedAt.ToLocalTime()
                : fallback.OnceAt;
            var time = root.TryGetProperty("time", out var timeValue)
                       && TimeOnly.TryParse(
                           timeValue.GetString(),
                           CultureInfo.InvariantCulture,
                           DateTimeStyles.None,
                           out var parsedTime)
                ? parsedTime
                : fallback.Time;
            var day = root.TryGetProperty("dayOfWeek", out var dayValue)
                      && Enum.TryParse<DayOfWeek>(dayValue.GetString(), true, out var parsedDay)
                ? parsedDay
                : fallback.DayOfWeek;
            var hours = root.TryGetProperty("intervalHours", out var hourValue)
                        && hourValue.TryGetInt32(out var parsedHours)
                ? Math.Clamp(parsedHours, 1, 168)
                : fallback.IntervalHours;
            var minutes = root.TryGetProperty("intervalMinutes", out var minuteValue)
                          && minuteValue.TryGetInt32(out var parsedMinutes)
                ? Math.Clamp(parsedMinutes, 60, 10_080)
                : fallback.ConditionIntervalMinutes;
            return new AutomationScheduleDraft(once, time, day, hours, minutes);
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    /// <summary>
    /// Performs the describe step owned by this component.
    /// </summary>
    public static string Describe(AutomationScheduleKind kind, AutomationScheduleDraft draft) => kind switch
    {
        AutomationScheduleKind.Once => $"Once at {draft.OnceAt.ToLocalTime():g}",
        AutomationScheduleKind.Hourly => draft.IntervalHours == 1
            ? "Every hour"
            : $"Every {draft.IntervalHours} hours",
        AutomationScheduleKind.Daily => $"Daily at {FormatTime(draft.Time)}",
        AutomationScheduleKind.Weekly => $"Every {draft.DayOfWeek} at {FormatTime(draft.Time)}",
        AutomationScheduleKind.ConditionWatch => draft.ConditionIntervalMinutes == 60
            ? "Check the condition hourly"
            : $"Check the condition every {draft.ConditionIntervalMinutes} minutes",
        _ => kind.ToString()
    };

    /// <summary>
    /// Performs the format time step owned by this component.
    /// </summary>
    private static string FormatTime(TimeOnly time) =>
        time.ToString("HH:mm", CultureInfo.InvariantCulture);
}
