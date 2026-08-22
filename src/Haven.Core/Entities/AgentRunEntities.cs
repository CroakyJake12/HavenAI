namespace Haven.Core;

/// <summary>Lifecycle state for one persisted autonomous Agent task.</summary>
public enum AgentRunStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

/// <summary>
/// Persisted evidence for one Agent execution. JSON fields remain machine-readable so
/// activity and capability provenance can evolve without breaking existing databases.
/// </summary>
public sealed record AgentRun(
    Guid Id,
    Guid AgentId,
    string AgentName,
    string Task,
    AgentRunStatus Status,
    string ModelName,
    string Result,
    string Error,
    string CapabilitiesJson,
    string ActivityJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    Guid? RetryOfRunId = null,
    string? ResourceReference = null,
    int ProgressPercent = 0);
