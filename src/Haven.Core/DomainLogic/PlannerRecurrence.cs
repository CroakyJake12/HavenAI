/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Core/DomainLogic/PlannerRecurrence.cs, in the dependency-free Core layer, where shared domain models and rules live.
 * What: This file owns PlannerRecurrence. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: This code stays free of UI and storage dependencies so the same rule or data shape can be reused and tested everywhere.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Globalization;

namespace Haven.Core;

/// <summary>
/// Represents planner recurrence and keeps its related state and behavior together.
/// </summary>
public static class PlannerRecurrence
{
    /// <summary>
    /// Retrieves next occurrence for the current operation.
    /// </summary>
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

    /// <summary>
    /// Validates this member before it crosses the next trust or persistence boundary.
    /// </summary>
    public static void Validate(string? rule)
    {
        if (string.IsNullOrWhiteSpace(rule)) return;
        _ = GetNextOccurrence(DateTimeOffset.UtcNow, rule, "UTC");
    }

    /// <summary>
    /// Computes the next weekly occurrence.
    /// </summary>
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

    /// <summary>
    /// Converts a DayOfWeek to a zero-based Monday index.
    /// </summary>
    private static int DayIndex(DayOfWeek day) => ((int)day + 6) % 7;

    /// <summary>
    /// Parses a two-letter weekday abbreviation.
    /// </summary>
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

    /// <summary>
    /// Resolves a time zone identifier to a TimeZoneInfo.
    /// </summary>
    private static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { throw new FormatException($"Unknown time zone '{id}'."); }
        catch (InvalidTimeZoneException) { throw new FormatException($"Invalid time zone '{id}'."); }
    }

    /// <summary>
    /// Converts a local DateTime to a DateTimeOffset in the specified zone.
    /// </summary>
    private static DateTimeOffset InZone(DateTime local, TimeZoneInfo zone)
    {
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        var offset = zone.GetUtcOffset(local);
        if (zone.IsAmbiguousTime(local)) offset = zone.GetAmbiguousTimeOffsets(local).Max();
        return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), offset);
    }
}
