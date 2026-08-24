namespace Haven.Core;

public enum MeshWorkRuntimeKind { Model, Agent }
public enum MeshWorkChannelKind { Direct, SharedPool, Coordinator }
public enum MeshWorkMessageRole { User, Worker, Coordinator, System }
public enum MeshWorkMessageStatus { Pending, Running, Succeeded, Failed }
public enum MeshWorkItemStatus { Planned, Delegated, Running, AwaitingReview, Succeeded, Failed, Cancelled }

/// <summary>A friendly named AI worker bound to one trusted Mesh device and one concrete runtime.</summary>
public sealed record MeshWorkMember(
    Guid WorkerId,
    string Name,
    Guid DeviceId,
    MeshWorkRuntimeKind RuntimeKind,
    string? ProviderId,
    string? ModelName,
    Guid? AgentId,
    string RuntimeDisplayName,
    string? Role,
    IReadOnlyList<string> Specialties,
    bool IsCoordinator,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>One message in a direct, shared-pool, or coordinator Work Mode conversation.</summary>
public sealed record MeshWorkMessage(
    Guid MessageId,
    MeshWorkChannelKind Channel,
    MeshWorkMessageRole Role,
    string Content,
    DateTimeOffset CreatedAt,
    Guid? SenderWorkerId = null,
    Guid? TargetWorkerId = null,
    Guid? ParentMessageId = null,
    MeshWorkMessageStatus Status = MeshWorkMessageStatus.Succeeded,
    string? Error = null);

/// <summary>Tracks coordinator-delegated work independently from the lower-level Mesh transport receipt.</summary>
public sealed record MeshWorkItem(
    Guid WorkItemId,
    string Goal,
    Guid AssignedWorkerId,
    Guid? ReviewerWorkerId,
    MeshWorkItemStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Result = null,
    string? Review = null,
    string? Error = null);

public sealed record MeshWorkMemberStatus(
    MeshWorkMember Member,
    MeshPresenceState Presence,
    MeshConnectionState Connection,
    DateTimeOffset? LastSeenAt,
    IReadOnlyList<MeshWorkItem> ActiveWork,
    DateTimeOffset? LastMessageAt,
    string Summary);

public sealed record MeshWorkModeSnapshot(
    IReadOnlyList<MeshWorkMemberStatus> Members,
    MeshWorkMember? Coordinator,
    IReadOnlyList<MeshWorkMessage> RecentMessages,
    IReadOnlyList<MeshWorkItem> RecentWork);

public sealed record MeshWorkPlanAssignment(string WorkerName, string Task, string? ReviewerName = null);
public sealed record MeshWorkPlan(string Summary, IReadOnlyList<MeshWorkPlanAssignment> Assignments, bool UsedCoordinator);
public sealed record MeshWorkRunResult(string Summary, MeshWorkPlan Plan, IReadOnlyList<MeshWorkItem> WorkItems);
public sealed record MeshWorkCommandResult(string Message, MeshWorkModeSnapshot Snapshot, IReadOnlyList<MeshWorkMessage> NewMessages, IReadOnlyList<MeshWorkItem> NewWork);
