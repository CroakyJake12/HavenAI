namespace Haven.Core;

/// <summary>
/// Default planner IDs.
/// </summary>
public static class PlannerDefaults
{
    /// <summary>
    /// Personal collection ID.
    /// </summary>
    public static readonly Guid PersonalCollectionId = Guid.Parse("8f51f72f-3c1f-4a5f-a101-010000000001");
    /// <summary>
    /// College collection ID.
    /// </summary>
    public static readonly Guid CollegeCollectionId = Guid.Parse("8f51f72f-3c1f-4a5f-a101-010000000002");
    /// <summary>
    /// Work collection ID.
    /// </summary>
    public static readonly Guid WorkCollectionId = Guid.Parse("8f51f72f-3c1f-4a5f-a101-010000000003");
    /// <summary>
    /// Local calendar ID.
    /// </summary>
    public static readonly Guid LocalCalendarId = Guid.Parse("8f51f72f-3c1f-4a5f-a101-020000000001");
}

/// <summary>
/// Represents a planner collection.
/// </summary>
public sealed record PlannerCollection(
    Guid Id,
    string Name,
    int SortOrder,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Represents a planner task.
/// </summary>
public sealed record PlannerTask(
    Guid Id,
    Guid CollectionId,
    Guid? ParentTaskId,
    string Title,
    string Notes,
    PlannerPriority Priority,
    PlannerTaskStatus Status,
    string TagsJson,
    int? EstimatedMinutes,
    DateTimeOffset? StartsAt,
    DateTimeOffset? DueAt,
    string? RecurrenceRule,
    DateTimeOffset? ReminderAt,
    DateTimeOffset? CompletedAt,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string TimeZoneId = "UTC");

/// <summary>
/// Represents a planner task completion.
/// </summary>
public sealed record PlannerTaskCompletion(
    Guid Id,
    Guid TaskId,
    DateTimeOffset CompletedAt,
    DateTimeOffset? OccurrenceDueAt);

/// <summary>
/// Represents a planner calendar.
/// </summary>
public sealed record PlannerCalendar(
    Guid Id,
    Guid? AccountId,
    CalendarProviderKind Provider,
    string ProviderCalendarId,
    string Name,
    string Color,
    CalendarPermission Permission,
    bool IsVisible,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Represents a planner event.
/// </summary>
public sealed record PlannerEvent(
    Guid Id,
    Guid CalendarId,
    string Title,
    string Notes,
    string Location,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    bool IsAllDay,
    string? RecurrenceRule,
    DateTimeOffset? ReminderAt,
    bool IsReadOnly,
    string? ProviderEventId,
    string? ProviderETag,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt = null,
    string TimeZoneId = "UTC");

/// <summary>
/// Represents a calendar account.
/// </summary>
public sealed record CalendarAccount(
    Guid Id,
    CalendarProviderKind Provider,
    string DisplayName,
    string AccountIdentifier,
    CalendarSyncStatus Status,
    string? StatusMessage,
    DateTimeOffset? LastSyncedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Represents a calendar conflict.
/// </summary>
public sealed record CalendarConflict(
    Guid Id,
    Guid EventId,
    Guid AccountId,
    string HavenSnapshotJson,
    string ProviderSnapshotJson,
    DateTimeOffset DetectedAt,
    DateTimeOffset? ResolvedAt,
    CalendarConflictResolution? Resolution);

/// <summary>
/// Represents a planner proposed change.
/// </summary>
public sealed record PlannerProposedChange(
    Guid Id,
    PlannerChangeKind Kind,
    Guid? EntityId,
    string PayloadJson,
    string Description);

/// <summary>
/// Represents a planner change proposal.
/// </summary>
public sealed record PlannerChangeProposal(
    Guid Id,
    string Summary,
    IReadOnlyList<PlannerProposedChange> Changes,
    DateTimeOffset CreatedAt);

/// <summary>
/// Represents a planner reminder.
/// </summary>
public sealed record PlannerReminder(
    PlannerReminderKind Kind,
    Guid EntityId,
    string Title,
    DateTimeOffset ReminderAt,
    DateTimeOffset OccurrenceAt);

/// <summary>
/// Represents a calendar sync cursor.
/// </summary>
public sealed record CalendarSyncCursor(
    Guid AccountId,
    Guid CalendarId,
    string? SyncCursor,
    string? DeltaLink,
    DateTimeOffset? WindowStart,
    DateTimeOffset? WindowEnd,
    DateTimeOffset? LastSyncedAt);

/// <summary>
/// Represents a calendar outbox item.
/// </summary>
public sealed record CalendarOutboxItem(
    Guid Id,
    Guid AccountId,
    Guid? EventId,
    string Operation,
    string PayloadJson,
    int AttemptCount,
    DateTimeOffset NextAttemptAt,
    string? LastError,
    DateTimeOffset CreatedAt);
