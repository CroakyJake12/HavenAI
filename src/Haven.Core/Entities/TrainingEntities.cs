namespace Haven.Core;

/// <summary>
/// Represents a training run.
/// </summary>
public sealed record TrainingRun(
    Guid Id,
    string TaskPrompt,
    string WorkspacePath,
    string SnapshotPath,
    string ModelName,
    int MaxAttempts,
    int DurationMinutes,
    PermissionMode FilePermission,
    PermissionMode CommandPermission,
    PermissionMode BrowserPermission,
    bool AllowDesktopTools,
    bool AllowFileSystemWrites,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt = null);

/// <summary>
/// Represents a training attempt.
/// </summary>
public sealed record TrainingAttempt(
    Guid Id,
    Guid TrainingRunId,
    int AttemptNumber,
    string ReportMarkdown,
    string? Feedback,
    string ActionLog,
    bool Succeeded,
    TimeSpan Duration,
    DateTimeOffset CreatedAt);
