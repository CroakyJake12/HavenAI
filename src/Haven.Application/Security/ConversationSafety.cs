namespace Haven.Application;

public enum ConversationSafetyState { Active = 0, Locked = 1 }

public sealed record ConfirmedSafetyFlag(
    Guid EventId,
    string Source,
    string Category,
    string EvidenceHash,
    DateTimeOffset ConfirmedAt);

public sealed record ConversationSafetySnapshot(
    Guid ConversationId,
    int ConfirmedCount,
    ConversationSafetyState State,
    DateTimeOffset? LockedAt,
    long Version);

public sealed record ConversationSafetyFlagResult(
    bool Added,
    bool LockedNow,
    ConversationSafetySnapshot Snapshot);

public interface IConversationSafetyService
{
    Task<ConversationSafetySnapshot> GetSnapshotAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<ConversationSafetyFlagResult> RecordConfirmedFlagAsync(
        Guid conversationId,
        ConfirmedSafetyFlag flag,
        CancellationToken cancellationToken);
    Task EnsureMayActAsync(Guid conversationId, string operation, CancellationToken cancellationToken);
}

public sealed class ConversationSafetyLockException(Guid conversationId, string operation)
    : InvalidOperationException($"Conversation '{conversationId}' is safety-locked; '{operation}' was blocked.")
{
    public Guid ConversationId { get; } = conversationId;
    public string Operation { get; } = operation;
}
