using Haven.Core;

namespace Haven.Application;

public sealed record PlannerTaskQuery(
    Guid? CollectionId = null,
    PlannerTaskStatus? Status = null,
    DateTimeOffset? RangeStart = null,
    DateTimeOffset? RangeEnd = null,
    bool IncludeCompleted = false,
    string? Search = null);

public interface IPlannerRepository
{
    Task EnsureDefaultsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PlannerCollection>> GetCollectionsAsync(bool includeArchived, CancellationToken cancellationToken);
    Task UpsertCollectionAsync(PlannerCollection collection, CancellationToken cancellationToken);
    Task ArchiveCollectionAsync(Guid id, bool archived, CancellationToken cancellationToken);

    Task<PlannerTask?> GetTaskAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlannerTask>> GetTasksAsync(PlannerTaskQuery query, CancellationToken cancellationToken);
    Task UpsertTaskAsync(PlannerTask task, CancellationToken cancellationToken);
    Task CompleteTaskAsync(Guid id, DateTimeOffset completedAt, CancellationToken cancellationToken);
    Task DeleteTaskAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlannerTaskCompletion>> GetCompletionHistoryAsync(Guid taskId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlannerCalendar>> GetCalendarsAsync(bool visibleOnly, CancellationToken cancellationToken);
    Task UpsertCalendarAsync(PlannerCalendar calendar, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlannerEvent>> GetEventsAsync(DateTimeOffset rangeStart, DateTimeOffset rangeEnd, Guid? calendarId, CancellationToken cancellationToken);
    Task<PlannerEvent?> GetEventAsync(Guid id, CancellationToken cancellationToken);
    Task UpsertEventAsync(PlannerEvent plannerEvent, CancellationToken cancellationToken);
    Task DeleteEventAsync(Guid id, DateTimeOffset deletedAt, CancellationToken cancellationToken);

    Task<IReadOnlyList<CalendarAccount>> GetCalendarAccountsAsync(CancellationToken cancellationToken);
    Task UpsertCalendarAccountAsync(CalendarAccount account, CancellationToken cancellationToken);
    Task<IReadOnlyList<CalendarConflict>> GetUnresolvedConflictsAsync(CancellationToken cancellationToken);
    Task ResolveConflictAsync(Guid id, CalendarConflictResolution resolution, DateTimeOffset resolvedAt, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlannerReminder>> GetDueRemindersAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);
    Task MarkReminderDeliveredAsync(PlannerReminder reminder, DateTimeOffset deliveredAt, CancellationToken cancellationToken);

    Task ApplyProposalAsync(PlannerChangeProposal proposal, CancellationToken cancellationToken);
}

public sealed record PlannerProposalValidation(bool IsValid, IReadOnlyList<string> Errors)
{
    public static PlannerProposalValidation Valid { get; } = new(true, []);
}

public interface IPlannerProposalService
{
    OllamaToolDefinition ToolDefinition { get; }
    PlannerChangeProposal ParseToolCall(IReadOnlyDictionary<string, System.Text.Json.JsonElement> arguments);
    PlannerProposalValidation Validate(PlannerChangeProposal proposal);
    Task ApplyAsync(PlannerChangeProposal proposal, CancellationToken cancellationToken);
}

public sealed record CalendarProviderConfiguration(
    CalendarProviderKind Provider,
    string? ClientId,
    Uri RedirectUri,
    IReadOnlyList<string> Scopes)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);
}

public sealed record CalendarAuthorizationResult(bool Succeeded, CalendarSyncStatus Status, string Message);
public sealed record CalendarSyncRequest(Guid AccountId, bool FullSync, DateTimeOffset WindowStart, DateTimeOffset WindowEnd);
public sealed record CalendarSyncResult(bool Succeeded, CalendarSyncStatus Status, int Added, int Updated, int Deleted, int Conflicts, string Message);

public interface ICalendarSyncProvider
{
    CalendarProviderKind Kind { get; }
    bool IsConfigured { get; }
    string ConfigurationStatus { get; }
    Task<CalendarAuthorizationResult> ConnectAsync(CancellationToken cancellationToken);
    Task<CalendarSyncResult> SyncAsync(CalendarSyncRequest request, CancellationToken cancellationToken);
    Task DisconnectAsync(Guid accountId, CancellationToken cancellationToken);
}

public interface ICalendarSyncProviderRegistry
{
    IReadOnlyList<ICalendarSyncProvider> Providers { get; }
    ICalendarSyncProvider Get(CalendarProviderKind kind);
}

public sealed record CalendarTokenEnvelope(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt, string Scope);

public interface ICalendarTokenStore
{
    Task SaveAsync(Guid accountId, CalendarTokenEnvelope token, CancellationToken cancellationToken);
    Task<CalendarTokenEnvelope?> GetAsync(Guid accountId, CancellationToken cancellationToken);
    Task DeleteAsync(Guid accountId, CancellationToken cancellationToken);
}

/// <summary>Transport seam used by provider implementations and deterministic sync tests.</summary>
public interface ICalendarProviderTransport
{
    CalendarProviderKind Kind { get; }
    Task<CalendarAuthorizationResult> ConnectAsync(CalendarProviderConfiguration configuration, CancellationToken cancellationToken);
    Task<CalendarSyncResult> SyncAsync(CalendarSyncRequest request, CancellationToken cancellationToken);
    Task DisconnectAsync(Guid accountId, CancellationToken cancellationToken);
}

/// <summary>Provider-safe calendar persistence operations used by OAuth sync transports.</summary>
public interface ICalendarSyncStore
{
    Task<PlannerCalendar?> GetCalendarAsync(Guid id, CancellationToken cancellationToken);
    Task<PlannerCalendar?> GetCalendarByProviderIdAsync(Guid accountId, string providerCalendarId, CancellationToken cancellationToken);
    Task<PlannerEvent?> GetEventByProviderIdAsync(Guid calendarId, string providerEventId, CancellationToken cancellationToken);
    Task UpsertProviderEventAsync(PlannerEvent plannerEvent, CancellationToken cancellationToken);
    Task DeleteProviderEventAsync(Guid calendarId, string providerEventId, DateTimeOffset deletedAt, CancellationToken cancellationToken);
    Task<CalendarSyncCursor?> GetSyncCursorAsync(Guid accountId, Guid calendarId, CancellationToken cancellationToken);
    Task UpsertSyncCursorAsync(CalendarSyncCursor cursor, CancellationToken cancellationToken);
    Task<IReadOnlyList<CalendarOutboxItem>> GetDueOutboxAsync(Guid accountId, DateTimeOffset now, int limit, CancellationToken cancellationToken);
    Task CompleteOutboxAsync(Guid id, CancellationToken cancellationToken);
    Task FailOutboxAsync(Guid id, string error, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken);
    Task AddConflictAsync(CalendarConflict conflict, CancellationToken cancellationToken);
    Task<bool> HasUnresolvedConflictAsync(Guid eventId, CancellationToken cancellationToken);
}
