/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/ConversationProductionAbstractions.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns MessageEditMode, ResponseRegenerationMode, IConversationProductionRepository, IConversationVersioningService, IConversationExportService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Lists the supported message edit mode values used to make state explicit and type-safe.
/// </summary>
public enum MessageEditMode
{
    NewBranch = 0,
    OverwriteCurrentBranch = 1
}

/// <summary>
/// Lists the supported response regeneration mode values used to make state explicit and type-safe.
/// </summary>
public enum ResponseRegenerationMode
{
    Here = 0,
    NewBranch = 1
}

/// <summary>
/// Defines the conversation production repository contract so callers depend on a capability rather than one implementation.
/// </summary>
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
    Task<IReadOnlyList<MessageAttachment>> GetRecentAttachmentsAsync(int limit, CancellationToken cancellationToken);
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

/// <summary>
/// Defines the conversation versioning service contract so callers depend on a capability rather than one implementation.
/// </summary>
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

/// <summary>
/// Defines the conversation export service contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IConversationExportService
{
    Task<string> ExportMarkdownAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<string> ExportPlainTextAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<string> ExportJsonAsync(Guid conversationId, CancellationToken cancellationToken);
}
