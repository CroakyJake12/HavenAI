namespace Haven.Core;

/// <summary>
/// Represents a surface run.
/// </summary>
public sealed record SurfaceRun(
    Guid Id,
    Guid ConversationId,
    SurfaceKind Surface,
    string SurfaceKey,
    string? TargetModeKey,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    bool Succeeded);

/// <summary>
/// Represents an activity event.
/// </summary>
public sealed record ActivityEvent(
    Guid Id,
    ActivityEventKind Kind,
    Guid? ConversationId,
    Guid? ModeId,
    string Summary,
    string DetailJson,
    DateTimeOffset Timestamp);
