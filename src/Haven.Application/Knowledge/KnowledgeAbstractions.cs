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

    Task<bool> SetPinnedAsync(Guid id, bool pinned, CancellationToken cancellationToken);
    Task<bool> ForgetAsync(Guid id, CancellationToken cancellationToken);
    Task<int> ForgetCategoryAsync(KnowledgeCategory category, CancellationToken cancellationToken);
}

public interface IApiBank
{
    Task<ApiBankRecord> UpsertAsync(ApiBankRecord record, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApiBankRecord>> SearchAsync(string? query, CancellationToken cancellationToken);
    Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken);
}

public interface IBackgroundLearningScheduler
{
    BackgroundLearningMode Mode { get; }
    bool IsEnabled(KnowledgeCategory category);
    Task<BackgroundLearningTask> EnqueueAsync(
        string title,
        KnowledgeCategory category,
        BackgroundLearningPriority priority,
        CancellationToken cancellationToken);
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
    DateTimeOffset CreatedAt);

public enum BackgroundLearningTaskStatus
{
    Queued = 0,
    Running = 1,
    Paused = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5
}
