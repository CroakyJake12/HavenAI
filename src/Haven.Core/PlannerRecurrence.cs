using System.Globalization;

namespace Haven.Core;

public static class PlannerRecurrence
{
    public static DateTimeOffset? GetNextOccurrence(DateTimeOffset occurrence, string? rule, string? timeZoneId = null)
    {
        if (string.IsNullOrWhiteSpace(rule)) return null;
        var values = rule.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].ToUpperInvariant(), parts => parts[1].ToUpperInvariant(), StringComparer.OrdinalIgnoreCase);
        if (!values.TryGetValue("FREQ", out var frequency)) throw new FormatException("Recurrence requires FREQ.");
        var interval = values.TryGetValue("INTERVAL", out var intervalText)
            && int.TryParse(intervalText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 1;
        if (interval is < 1 or > 999) throw new FormatException("Recurrence INTERVAL must be between 1 and 999.");

        var zone = ResolveTimeZone(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(occurrence, zone).DateTime;
        var nextLocal = frequency switch
        {
            "DAILY" => local.AddDays(interval),
            "WEEKLY" => NextWeekly(local, interval, values.GetValueOrDefault("BYDAY")),
            "MONTHLY" => local.AddMonths(interval),
            "YEARLY" => local.AddYears(interval),
            _ => throw new FormatException($"Unsupported recurrence frequency '{frequency}'.")
        };
        return InZone(nextLocal, zone);
    }

    public static void Validate(string? rule)
    {
        if (string.IsNullOrWhiteSpace(rule)) return;
        _ = GetNextOccurrence(DateTimeOffset.UtcNow, rule, "UTC");
    }

    private static DateTime NextWeekly(DateTime local, int interval, string? byDay)
    {
        if (string.IsNullOrWhiteSpace(byDay)) return local.AddDays(7 * interval);
        var days = byDay.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseDay)
            .Distinct()
            .OrderBy(day => day)
            .ToArray();
        if (days.Length == 0) throw new FormatException("BYDAY contains no recognised weekdays.");
        var currentDayIndex = DayIndex(local.DayOfWeek);
        var laterThisWeek = days.Select(DayIndex).Where(index => index > currentDayIndex).OrderBy(index => index).FirstOrDefault(-1);
        if (laterThisWeek >= 0) return local.AddDays(laterThisWeek - currentDayIndex);

        var firstNextWeek = days.Select(DayIndex).Min();
        var daysToNextActiveWeek = 7 * interval - currentDayIndex + firstNextWeek;
        return local.AddDays(daysToNextActiveWeek);
    }

    private static int DayIndex(DayOfWeek day) => ((int)day + 6) % 7;

    private static DayOfWeek ParseDay(string value) => value switch
    {
        "MO" => DayOfWeek.Monday,
        "TU" => DayOfWeek.Tuesday,
        "WE" => DayOfWeek.Wednesday,
        "TH" => DayOfWeek.Thursday,
        "FR" => DayOfWeek.Friday,
        "SA" => DayOfWeek.Saturday,
        "SU" => DayOfWeek.Sunday,
        _ => throw new FormatException($"Unknown weekday '{value}'.")
    };

    private static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { throw new FormatException($"Unknown time zone '{id}'."); }
        catch (InvalidTimeZoneException) { throw new FormatException($"Invalid time zone '{id}'."); }
    }

    private static DateTimeOffset InZone(DateTime local, TimeZoneInfo zone)
    {
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        var offset = zone.GetUtcOffset(local);
        if (zone.IsAmbiguousTime(local)) offset = zone.GetAmbiguousTimeOffsets(local).Max();
        return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), offset);
    }
}
