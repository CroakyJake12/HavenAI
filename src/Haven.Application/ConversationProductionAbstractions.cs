using Haven.Core;

namespace Haven.Application;

public enum MessageEditMode
{
    NewBranch = 0,
    OverwriteCurrentBranch = 1
}

public enum ResponseRegenerationMode
{
    Here = 0,
    NewBranch = 1
}

public interface IConversationProductionRepository
{
    Task<ConversationBranch> EnsureRootBranchAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ConversationBranch>> GetBranchesAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<ConversationBranch?> GetCurrentBranchAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<ConversationBranch> CreateBranchAsync(
        Guid conversationId,
        Guid parentBranchId,
        Guid? forkedFromMessageId,
        string name,
        ConversationBranchReason reason,
        CancellationToken cancellationToken);
    Task SetCurrentBranchAsync(Guid conversationId, Guid branchId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ConversationTurn>> GetTurnsAsync(Guid conversationId, Guid branchId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MessageVersion>> GetVersionsAsync(Guid messageId, Guid branchId, CancellationToken cancellationToken);
    Task<MessageVersion?> GetCurrentVersionAsync(Guid messageId, Guid branchId, CancellationToken cancellationToken);
    Task<MessageVersion> AddVersionAsync(
        Guid messageId,
        Guid branchId,
        MessageVersionKind kind,
        string content,
        string? metadataJson,
        bool makeCurrent,
        CancellationToken cancellationToken);
    Task ReplaceMessageContentAsync(Guid messageId, string content, string? metadataJson, CancellationToken cancellationToken);
    Task RemoveBranchMessagesAfterAsync(Guid branchId, Guid messageId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MessageAttachment>> GetAttachmentsAsync(Guid conversationId, Guid? messageId, CancellationToken cancellationToken);
    Task<MessageAttachment> UpsertAttachmentAsync(MessageAttachment attachment, CancellationToken cancellationToken);
    Task DeleteAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken);

    Task<ConversationDraft?> GetDraftAsync(Guid conversationId, Guid? branchId, CancellationToken cancellationToken);
    Task SaveDraftAsync(ConversationDraft draft, CancellationToken cancellationToken);
    Task DeleteDraftAsync(Guid conversationId, Guid? branchId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MessageBookmark>> GetBookmarksAsync(Guid conversationId, CancellationToken cancellationToken);
    Task UpsertBookmarkAsync(MessageBookmark bookmark, CancellationToken cancellationToken);
    Task DeleteBookmarkAsync(Guid bookmarkId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationSearchResult>> SearchAsync(string query, Guid? conversationId, int limit, CancellationToken cancellationToken);

    Task<SharedSession?> GetActiveShareAsync(Guid conversationId, CancellationToken cancellationToken);
    Task UpsertShareAsync(SharedSession session, CancellationToken cancellationToken);
    Task StopShareAsync(Guid shareId, DateTimeOffset stoppedAt, CancellationToken cancellationToken);

    Task<ConversationExportDocument> BuildExportAsync(Guid conversationId, CancellationToken cancellationToken);
}

public interface IConversationVersioningService
{
    Task<ConversationBranch> EnsureCurrentBranchAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<ConversationBranch> EditUserMessageAsync(
        Guid conversationId,
        Guid messageId,
        string content,
        MessageEditMode mode,
        CancellationToken cancellationToken);
    Task<ConversationBranch> PrepareRegenerationAsync(
        Guid conversationId,
        Guid messageId,
        bool isLatestAssistantMessage,
        ResponseRegenerationMode mode,
        CancellationToken cancellationToken);
}

public interface IConversationExportService
{
    Task<string> ExportMarkdownAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<string> ExportPlainTextAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<string> ExportJsonAsync(Guid conversationId, CancellationToken cancellationToken);
}
