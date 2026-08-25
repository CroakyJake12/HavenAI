namespace Haven.Core;

/// <summary>Whether a message sent while work is active changes the active run or creates durable follow-up work.</summary>
public enum TaskFollowUpMode
{
    Steer = 0,
    Queue = 1
}

public enum TaskFollowUpInference
{
    Explicit = 0,
    Inferred = 1
}

public enum TaskExecutionLifecycle
{
    Running = 0,
    WaitingSafeBoundary = 1,
    Blocked = 2,
    Suspended = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6
}

public enum TaskExecutionDurability
{
    PersistedPlan = 0,
    RecoverableCheckpoint = 1,
    InMemoryContinuation = 2
}

public enum TaskPlanNodeState
{
    Pending = 0,
    Running = 1,
    WaitingSafeBoundary = 2,
    Blocked = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6,
    Superseded = 7,
    RequiresReexecution = 8
}

public enum TaskActionInterruptionPolicy
{
    ReadOnlyCancellable = 0,
    SafeBoundary = 1,
    AtomicCommit = 2
}

public enum SteerInstructionState
{
    Pending = 0,
    WaitingSafeBoundary = 1,
    Applied = 2,
    Superseded = 3,
    Blocked = 4
}

public enum QueuedFollowUpState
{
    Queued = 0,
    Ready = 1,
    Running = 2,
    Blocked = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6,
    PromotedToSteer = 7
}

public sealed record TaskPlanNode(
    Guid ActionId,
    Guid? ParentActionId,
    string Summary,
    TaskPlanNodeState State,
    TaskActionInterruptionPolicy InterruptionPolicy,
    int PlanVersion,
    Guid? SupersedesActionId = null,
    Guid? RemediationId = null,
    IReadOnlyList<string>? RequiredPermissionScopes = null);

public sealed record SteerInstruction(
    Guid Id,
    Guid TaskId,
    Guid ExecutionId,
    long Sequence,
    string Summary,
    TaskFollowUpInference Inference,
    SteerInstructionState State,
    IReadOnlyList<Guid> AffectedActionIds,
    DateTimeOffset CreatedAt,
    Guid? SupersededById = null,
    IReadOnlyList<string>? RequiredPermissionScopes = null);

public sealed record QueuedFollowUpTask(
    Guid TaskId,
    Guid OwnerTaskId,
    Guid? OriginatingExecutionId,
    string Summary,
    long CreationOrder,
    int Position,
    QueuedFollowUpState State,
    Guid? DependencyTaskId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? IdempotencyKey = null);

public sealed record TaskExecutionSnapshot(
    Guid TaskId,
    Guid ContextId,
    Guid ExecutionId,
    string PromptSummary,
    TaskExecutionLifecycle State,
    TaskExecutionDurability Durability,
    int PlanVersion,
    IReadOnlyList<TaskPlanNode> Plan,
    IReadOnlyList<SteerInstruction> Steers,
    IReadOnlyList<QueuedFollowUpTask> Queue,
    IReadOnlyList<string> ApprovedPermissionScopes,
    Guid? LastCheckpointActionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record FollowUpDecision(
    TaskFollowUpMode Mode,
    TaskFollowUpInference Inference,
    TaskExecutionSnapshot Snapshot,
    SteerInstruction? Steer = null,
    QueuedFollowUpTask? QueuedTask = null,
    bool CancellationRequested = false,
    bool WaitingForSafeBoundary = false,
    bool RequiresApproval = false);

public sealed record QueueCheckpointResult(
    TaskExecutionSnapshot Snapshot,
    QueuedFollowUpTask? ReadyTask);
