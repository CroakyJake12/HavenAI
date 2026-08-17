/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Core/DomainLogic/PlannerCountdown.cs, in the dependency-free Core layer.
 * What: Projects persisted Plan tasks and events into countdown state without creating a parallel countdown database.
 * How: Each countdown retains its source Plan entity ID, occurrence target and reminder metadata.
 * Why: Countdowns should remain editable through real Plan objects and update automatically when their source deadline/event moves.
 * Maintenance: Keep countdown state deterministic; persistence belongs to the linked Plan source entity.
 */

namespace Haven.Core;

public enum PlannerCountdownSourceKind
{
    Task = 0,
    Event = 1
}

public enum PlannerCountdownState
{
    Upcoming = 0,
    Due = 1,
    Passed = 2,
    Completed = 3,
    Cancelled = 4
}

public sealed record PlannerCountdown(
    Guid SourceId,
    PlannerCountdownSourceKind SourceKind,
    string Title,
    DateTimeOffset TargetAt,
    DateTimeOffset Now,
    PlannerCountdownState State,
    DateTimeOffset? ReminderAt,
    Guid? CollectionId,
    Guid? CalendarId,
    bool IsReadOnly)
{
    public TimeSpan Remaining => TargetAt - Now;
    public bool HasPassed => State == PlannerCountdownState.Passed;
}

public static class PlannerCountdownProjection
{
    public static PlannerCountdown? FromTask(PlannerTask task, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(task);
        var target = task.DueAt ?? task.StartsAt;
        if (target is null) return null;

        var state = task.Status switch
        {
            PlannerTaskStatus.Completed => PlannerCountdownState.Completed,
            PlannerTaskStatus.Cancelled => PlannerCountdownState.Cancelled,
            _ when target.Value > now => PlannerCountdownState.Upcoming,
            _ when target.Value < now => PlannerCountdownState.Passed,
            _ => PlannerCountdownState.Due
        };

        return new(
            task.Id,
            PlannerCountdownSourceKind.Task,
            task.Title,
            target.Value,
            now,
            state,
            task.ReminderAt,
            task.CollectionId,
            null,
            false);
    }

    public static PlannerCountdown FromEvent(PlannerEvent item, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);
        var state = item.StartsAt > now
            ? PlannerCountdownState.Upcoming
            : item.StartsAt < now
                ? PlannerCountdownState.Passed
                : PlannerCountdownState.Due;

        return new(
            item.Id,
            PlannerCountdownSourceKind.Event,
            item.Title,
            item.StartsAt,
            now,
            state,
            item.ReminderAt,
            null,
            item.CalendarId,
            item.IsReadOnly);
    }
}
