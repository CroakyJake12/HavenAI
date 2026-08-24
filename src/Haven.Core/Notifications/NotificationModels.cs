namespace Haven.Core;

public enum HavenNotificationPriority { Information = 0, Success = 1, Warning = 2, Error = 3, AttentionRequired = 4 }
public enum HavenNotificationKind { LiveActivity = 0, ResponseReady = 1, Task = 2, Failure = 3, ApprovalRequired = 4, UserActionRequired = 5, Plugin = 6 }

public sealed record HavenNavigationTarget(
    Guid? TabId = null,
    Guid? ConversationId = null,
    Guid? TaskId = null,
    Guid? ActionId = null,
    Guid? ExecutionId = null,
    Guid? ProjectId = null,
    string? PluginId = null,
    Guid? RemediationId = null);

public sealed record HavenNotificationAction(string CommandId, string Label, HavenNavigationTarget Target);

public sealed record HavenNotification(
    Guid Id,
    HavenNotificationKind Kind,
    HavenNotificationPriority Priority,
    string SourceId,
    string SourceName,
    string Title,
    string Message,
    bool IsLive,
    bool IsRead,
    bool IsDismissed,
    bool RequiresAttention,
    string? CoalescingKey,
    HavenNavigationTarget Target,
    IReadOnlyList<HavenNotificationAction> Actions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt = null);
