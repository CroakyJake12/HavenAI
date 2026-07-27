namespace Haven.Core;

/// <summary>
/// Represents an automation definition.
/// </summary>
public sealed record AutomationDefinition(
    Guid Id,
    string Name,
    HavenMode Mode,
    string Instruction,
    AutomationScheduleKind ScheduleKind,
    string ScheduleJson,
    DateTimeOffset? NextRunAt,
    Guid? ContainerId,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Represents an automation run.
/// </summary>
public sealed record AutomationRun(
    Guid Id,
    Guid AutomationId,
    AutomationRunStatus Status,
    DateTimeOffset ScheduledFor,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? Result,
    string? Error,
    string? LeaseToken);
