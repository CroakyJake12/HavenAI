/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Core/DomainLogic/PlannerDayTimeline.cs, in the dependency-free Core layer.
 * What: Builds an inspectable day timeline from canonical PlannerTask and PlannerEvent records.
 * How: Callers supply persisted Plan records for one day; the result exposes active/next items and schedule progress.
 * Why: Today, Study and AI planning need one deterministic definition of now/next without coupling to UI or storage.
 * Maintenance: Keep this code dependency-free and preserve wall-clock day boundaries across time zones.
 */

namespace Haven.Core;

public enum PlannerDayItemKind
{
    Task = 0,
    Event = 1
}

public sealed record PlannerDayItem(
    Guid EntityId,
    PlannerDayItemKind Kind,
    string Title,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    DateTimeOffset? DueAt,
    bool IsAllDay,
    bool IsCompleted,
    bool IsCancelled,
    bool IsReadOnly,
    Guid? CollectionId,
    Guid? CalendarId)
{
    public bool IsActionable => !IsCompleted && !IsCancelled;
    public bool IsTimed => !IsAllDay && StartsAt is not null && EndsAt is not null && EndsAt > StartsAt;
}

public sealed record PlannerDaySnapshot(
    DateTimeOffset DayStart,
    DateTimeOffset DayEnd,
    DateTimeOffset Now,
    IReadOnlyList<PlannerDayItem> Items,
    IReadOnlyList<PlannerDayItem> ActiveItems,
    PlannerDayItem? CurrentItem,
    PlannerDayItem? NextItem,
    DateTimeOffset? ScheduleStart,
    DateTimeOffset? ScheduleEnd,
    double Progress);

public static class PlannerDayTimeline
{
    public static (DateTimeOffset Start, DateTimeOffset End) GetDayBounds(DateTimeOffset day, string? timeZoneId)
    {
        var zone = ResolveTimeZone(timeZoneId);
        var localDate = TimeZoneInfo.ConvertTime(day, zone).Date;
        return (InZone(localDate, zone), InZone(localDate.AddDays(1), zone));
    }

    public static PlannerDaySnapshot Build(
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        DateTimeOffset now,
        IEnumerable<PlannerTask> tasks,
        IEnumerable<PlannerEvent> events)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(events);
        if (dayEnd <= dayStart) throw new ArgumentException("The day must end after it starts.", nameof(dayEnd));

        var items = events
            .Where(item => item.DeletedAt is null && item.StartsAt < dayEnd && item.EndsAt > dayStart)
            .Select(ToDayItem)
            .Concat(tasks
                .Where(item => OccursInDay(item, dayStart, dayEnd))
                .Select(ToDayItem))
            .OrderBy(item => item.IsAllDay ? 0 : 1)
            .ThenBy(item => item.StartsAt ?? item.DueAt ?? dayEnd)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var active = items
            .Where(item => item.IsActionable && item.IsTimed && item.StartsAt <= now && now < item.EndsAt)
            .OrderBy(item => item.EndsAt)
            .ThenBy(item => item.StartsAt)
            .ToArray();
        var current = active.FirstOrDefault();
        var next = items
            .Where(item => item.IsActionable && !item.IsAllDay && item.StartsAt > now)
            .OrderBy(item => item.StartsAt)
            .ThenBy(item => item.EndsAt ?? item.StartsAt)
            .FirstOrDefault();

        var scheduled = items
            .Where(item => !item.IsAllDay && item.StartsAt is not null && !item.IsCancelled)
            .ToArray();
        var scheduleStart = scheduled.Length == 0 ? null : scheduled.Min(item => item.StartsAt);
        var scheduleEnd = scheduled.Length == 0 ? null : scheduled.Max(item => item.EndsAt ?? item.StartsAt);
        var progress = CalculateProgress(now, scheduleStart, scheduleEnd);

        return new(dayStart, dayEnd, now, items, active, current, next, scheduleStart, scheduleEnd, progress);
    }

    private static PlannerDayItem ToDayItem(PlannerEvent item) =>
        new(item.Id, PlannerDayItemKind.Event, item.Title, item.StartsAt, item.EndsAt, null, item.IsAllDay, false, false, item.IsReadOnly, null, item.CalendarId);

    private static PlannerDayItem ToDayItem(PlannerTask item)
    {
        var endsAt = GetEstimatedEnd(item);

        return new(
            item.Id,
            PlannerDayItemKind.Task,
            item.Title,
            item.StartsAt,
            endsAt,
            item.DueAt,
            false,
            item.Status == PlannerTaskStatus.Completed,
            item.Status == PlannerTaskStatus.Cancelled,
            false,
            item.CollectionId,
            null);
    }

    private static bool OccursInDay(PlannerTask item, DateTimeOffset dayStart, DateTimeOffset dayEnd)
    {
        if (item.StartsAt is not null)
        {
            if (item.StartsAt >= dayStart && item.StartsAt < dayEnd) return true;

            var estimatedEnd = GetEstimatedEnd(item);
            if (estimatedEnd is not null && item.StartsAt < dayEnd && estimatedEnd > dayStart) return true;
        }

        return item.DueAt is not null && item.DueAt >= dayStart && item.DueAt < dayEnd;
    }

    private static DateTimeOffset? GetEstimatedEnd(PlannerTask item) =>
        item.StartsAt is not null && item.EstimatedMinutes is > 0
            ? item.StartsAt.Value.AddMinutes(item.EstimatedMinutes.Value)
            : null;

    private static double CalculateProgress(DateTimeOffset now, DateTimeOffset? start, DateTimeOffset? end)
    {
        if (start is null || end is null) return 0d;
        if (end <= start) return now < start ? 0d : 1d;
        if (now <= start) return 0d;
        if (now >= end) return 1d;
        return Math.Clamp((now - start.Value).TotalSeconds / (end.Value - start.Value).TotalSeconds, 0d, 1d);
    }

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
