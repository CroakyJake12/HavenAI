namespace Haven.Core;

/// <summary>Identifies the trusted origin of an execution trace.</summary>
public enum ExecutionOrigin
{
    Haven = 0,
    NativePlugin = 1,
    Mcp = 2,
    ExternalAgent = 3,
    ChatGptViaPlugin = 4
}

/// <summary>Describes observable work without exposing private model chain-of-thought.</summary>
public enum ExecutionActionType
{
    UserPrompt = 0,
    Planning = 1,
    ReasoningSummary = 2,
    ModelExecution = 3,
    ToolCall = 4,
    ToolResult = 5,
    AppCall = 6,
    PluginCall = 7,
    McpCall = 8,
    ConnectorCall = 9,
    ExternalAgent = 10,
    ProjectAction = 11,
    FileAction = 12,
    Search = 13,
    Preview = 14,
    Retry = 15,
    AutomaticDiagnosis = 16,
    AutomaticRepair = 17,
    UserActionRequired = 18,
    Warning = 19,
    Error = 20,
    FinalResponse = 21,
    Steer = 22,
    Queue = 23,
    Replan = 24,
    Resume = 25
}

public enum ExecutionActionStatus
{
    Queued = 0,
    Running = 1,
    Waiting = 2,
    Blocked = 3,
    UserActionRequired = 4,
    Suspended = 5,
    Completed = 6,
    Failed = 7,
    Cancelled = 8,
    Warning = 9,
    Superseded = 10,
    PendingSafeBoundary = 11
}

/// <summary>A redacted failure safe to retain in history and telemetry.</summary>
public sealed record ExecutionFailure(
    string Code,
    string Title,
    string Message,
    string? ProviderMessage = null,
    int? HttpStatus = null,
    int Attempt = 1,
    DateTimeOffset? RetryAfter = null,
    string? AffectedComponent = null,
    bool Recovered = false);

/// <summary>
/// One lightweight authoritative event consumed by Action Graph, notifications and task status.
/// Input/output metadata must already be redacted before publication.
/// </summary>
public sealed record ExecutionEvent(
    Guid EventId,
    Guid ExecutionId,
    Guid ActionId,
    Guid? ParentActionId,
    ExecutionOrigin Origin,
    ExecutionActionType ActionType,
    ExecutionActionStatus Status,
    string Name,
    string? SafeReasoningSummary,
    string? SafeDetail,
    string? ComponentId,
    DateTimeOffset Timestamp,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? EndedAt = null,
    Guid? RetryOfActionId = null,
    Guid? RecoveryOfActionId = null,
    Guid? RemediationId = null,
    Guid? TaskId = null,
    Guid? TabId = null,
    Guid? ProjectId = null,
    ExecutionFailure? Failure = null,
    IReadOnlyDictionary<string, string>? SafeMetadata = null)
{
    public TimeSpan? Duration => StartedAt is { } start && EndedAt is { } end
        ? end - start
        : null;
}

public sealed record ExecutionSummary(
    Guid ExecutionId,
    string PromptSummary,
    ExecutionOrigin Origin,
    ExecutionActionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    int ActionCount,
    TimeSpan Duration,
    Guid? TabId,
    Guid? TaskId);

public enum ActionFeedbackRating { Positive = 1, Negative = 2 }

public sealed record ActionFeedback(
    Guid Id,
    Guid ExecutionId,
    Guid ActionId,
    ActionFeedbackRating? Rating,
    string? Comment,
    string? ActionType,
    string? ComponentId,
    string? SafeContext,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
