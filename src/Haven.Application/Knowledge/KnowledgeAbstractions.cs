using Haven.Core;

namespace Haven.Application;

public interface IKnowledgeLibrary
{
    Task<KnowledgeRecord> UpsertAsync(
        KnowledgeRecord record,
        string indexedText,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<KnowledgeRecord>> SearchMetadataAsync(
        string? query,
        KnowledgeCategory? category,
        CancellationToken cancellationToken);

    Task<KnowledgeRecord?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> SetPinnedAsync(Guid id, bool pinned, CancellationToken cancellationToken);
    Task<KnowledgeRecord> CorrectAsync(Guid id, string correctedSummary, string? reason, CancellationToken cancellationToken);
    Task<bool> RejectAsync(Guid id, string? reason, CancellationToken cancellationToken);
    Task<bool> ForgetAsync(Guid id, CancellationToken cancellationToken, bool preserveRejection = false);
    Task<int> ForgetCategoryAsync(KnowledgeCategory category, CancellationToken cancellationToken);
}

public interface IApiBank
{
    Task<ApiBankRecord> UpsertAsync(ApiBankRecord record, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApiBankRecord>> SearchAsync(string? query, CancellationToken cancellationToken);
    Task<bool> SetPinnedAsync(Guid id, bool pinned, CancellationToken cancellationToken);
    Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken);
}

public interface IKnowledgeMaintenanceService
{
    Task<KnowledgeStorageSnapshot> GetStorageAsync(CancellationToken cancellationToken);
    Task<KnowledgeCleanupResult> CleanupAsync(CancellationToken cancellationToken);
}

public interface IBackgroundLearningScheduler
{
    BackgroundLearningMode Mode { get; }
    bool IsGloballyEnabled { get; }
    bool IsEnabled(KnowledgeCategory category);
    Task InitializeAsync(CancellationToken cancellationToken);
    Task SetGlobalEnabledAsync(bool enabled, CancellationToken cancellationToken);
    Task SetModeAsync(BackgroundLearningMode mode, CancellationToken cancellationToken);
    Task SetCategoryEnabledAsync(KnowledgeCategory category, bool enabled, CancellationToken cancellationToken);
    Task<BackgroundLearningTask> EnqueueAsync(
        string title,
        KnowledgeCategory category,
        BackgroundLearningPriority priority,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<BackgroundLearningTask>> ListAsync(CancellationToken cancellationToken);
    Task<bool> PauseAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> ResumeAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> CancelAsync(Guid id, CancellationToken cancellationToken);
    Task<BackgroundLearningSchedulerSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
    bool CanRun(BackgroundLearningTask task, BackgroundLearningResourceState resources);
}

public enum BackgroundLearningPriority
{
    High = 0,
    Normal = 1,
    Low = 2
}

public sealed record BackgroundLearningTask(
    Guid Id,
    string Title,
    KnowledgeCategory Category,
    BackgroundLearningPriority Priority,
    BackgroundLearningTaskStatus Status,
    DateTimeOffset CreatedAt,
    string Source = "Background Learning",
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? LastRunAt = null,
    DateTimeOffset? CompletedAt = null,
    string? Result = null,
    string? Error = null,
    bool RequiresNetwork = false,
    bool RequiresModel = true);

public sealed record BackgroundLearningSchedulerSnapshot(
    bool IsGloballyEnabled,
    BackgroundLearningMode Mode,
    IReadOnlyDictionary<KnowledgeCategory, bool> Categories,
    IReadOnlyList<BackgroundLearningTask> Tasks,
    DateTimeOffset? LastChangedAt);

public sealed record BackgroundLearningResourceState(
    bool IsForegroundBusy,
    bool IsOnBattery,
    bool HasNetwork,
    bool IsMetered,
    bool IsModelBusy);

public enum BackgroundLearningTaskStatus
{
    Queued = 0,
    Running = 1,
    Paused = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5
}
