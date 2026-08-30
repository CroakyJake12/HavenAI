using Haven.Core;

namespace Haven.Application;

[Flags]
public enum MailProviderCapabilities
{
    None = 0,
    Search = 1 << 0,
    Threads = 1 << 1,
    Drafts = 1 << 2,
    Send = 1 << 3,
    Attachments = 1 << 4,
    Archive = 1 << 5,
    Delete = 1 << 6,
    ReadState = 1 << 7,
    Flag = 1 << 8,
    Folders = 1 << 9,
    FolderManagement = 1 << 10,
    Move = 1 << 11,
    Spam = 1 << 12,
    Important = 1 << 13,
    Bulk = 1 << 14
}

public enum MailFolderKind
{
    Inbox, Sent, Drafts, Archive, Trash, Spam, Important, Custom
}

public enum MailResponseKind
{
    New, Reply, ReplyAll, Forward
}

public enum MailFailureKind
{
    None, NotConnected, ReconnectRequired, PermissionDenied, Offline, InvalidRequest, ProviderError
}

public enum MailDraftPersistenceState
{
    Unsaved, Saving, Saved, SaveFailed, Sending, SendFailed, Sent
}

public sealed record MailAccount(Guid AccountId, CalendarProviderKind Provider, string DisplayName, string Address, MailProviderCapabilities Capabilities);
public sealed record MailFolder(string Id, string DisplayName, MailFolderKind Kind, int? UnreadCount = null, int? TotalCount = null, bool IsSystem = false);

public sealed record MailAddress(string DisplayName, string Address)
{
    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Address : $"{DisplayName} <{Address}>";
}

public sealed record MailAttachmentDescriptor(string Id, string FileName, string ContentType, long Size, bool IsInline = false);

public sealed record MailMessageSummary(
    string Id, string ThreadId, MailAddress From, IReadOnlyList<MailAddress> To, string Subject, string Preview,
    DateTimeOffset ReceivedAt, bool IsRead, bool IsFlagged, bool IsImportant, bool HasAttachments, IReadOnlyList<string> Labels);

public sealed record MailMessage(
    string Id, string ThreadId, string? InternetMessageId, MailAddress From, IReadOnlyList<MailAddress> To,
    IReadOnlyList<MailAddress> Cc, IReadOnlyList<MailAddress> Bcc, string Subject, string PlainTextBody, string? HtmlBody,
    DateTimeOffset ReceivedAt, bool IsRead, bool IsFlagged, bool IsImportant, IReadOnlyList<string> Labels, IReadOnlyList<MailAttachmentDescriptor> Attachments);

public sealed record MailQuery(
    Guid AccountId, string? FolderId = null, string? Text = null, bool? UnreadOnly = null, bool? FlaggedOnly = null,
    DateTimeOffset? After = null, DateTimeOffset? Before = null, int PageSize = 40, string? ContinuationToken = null);

public sealed record MailPage(IReadOnlyList<MailMessageSummary> Messages, string? ContinuationToken, DateTimeOffset FetchedAt, bool IsPartial = false);
public sealed record MailDraftAttachment(string FileName, string ContentType, byte[] Content, Guid LocalId = default);

/// <summary>A durable semantic draft. OAuth credentials are deliberately not part of this record.</summary>
public sealed record MailDraft(
    Guid AccountId, string? DraftId, MailResponseKind ResponseKind, string? SourceMessageId, string? ThreadId,
    IReadOnlyList<MailAddress> To, IReadOnlyList<MailAddress> Cc, IReadOnlyList<MailAddress> Bcc, string Subject,
    string Body, bool IsHtml, IReadOnlyList<MailDraftAttachment> Attachments,
    Guid LocalId = default, CalendarProviderKind? Provider = null, DateTimeOffset? UpdatedAt = null,
    MailDraftPersistenceState PersistenceState = MailDraftPersistenceState.Unsaved, string? LastSafeError = null)
{
    public string? ProviderDraftId => DraftId;
}

public enum MailBulkActionKind
{
    Archive, Delete, MarkRead, MarkUnread, Flag, Unflag, Spam, NotSpam, Important, NotImportant
}

public sealed record MailOperationResult(
    bool Succeeded, string Message, MailFailureKind FailureKind = MailFailureKind.None, string? ProviderId = null,
    Guid? LocalDraftId = null, RemediationType? SuggestedRemediation = null);
public sealed record MailBulkOperationResult(int Requested, int Succeeded, int Failed, string Message);
public sealed record ConnectedAccountAccessToken(string AccessToken, IReadOnlySet<string> Scopes, DateTimeOffset ExpiresAt);

public interface IConnectedAccountAccessTokenProvider
{
    Task<ConnectedAccountAccessToken> GetAsync(Guid accountId, CalendarProviderKind provider, IReadOnlyCollection<string> requiredScopes, CancellationToken cancellationToken);
}

public interface IMailProvider
{
    CalendarProviderKind Kind { get; }
    MailProviderCapabilities Capabilities { get; }
    IReadOnlyCollection<string> RequiredScopes { get; }
    Task<MailOperationResult> CheckAccessAsync(Guid accountId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MailFolder>> GetFoldersAsync(Guid accountId, CancellationToken cancellationToken);
    Task<MailPage> GetMessagesAsync(MailQuery query, CancellationToken cancellationToken);
    Task<MailMessage> GetMessageAsync(Guid accountId, string messageId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MailMessage>> GetThreadAsync(Guid accountId, string threadId, CancellationToken cancellationToken);
    Task<byte[]> DownloadAttachmentAsync(Guid accountId, string messageId, string attachmentId, CancellationToken cancellationToken);
    Task<MailOperationResult> SaveDraftAsync(MailDraft draft, CancellationToken cancellationToken);
    Task<MailOperationResult> DeleteDraftAsync(Guid accountId, string draftId, CancellationToken cancellationToken);
    Task<MailOperationResult> SendAsync(MailDraft draft, CancellationToken cancellationToken);
    Task<MailOperationResult> ArchiveAsync(Guid accountId, string messageId, CancellationToken cancellationToken);
    Task<MailOperationResult> DeleteAsync(Guid accountId, string messageId, CancellationToken cancellationToken);
    Task<MailOperationResult> SetReadAsync(Guid accountId, string messageId, bool isRead, CancellationToken cancellationToken);
    Task<MailOperationResult> SetFlaggedAsync(Guid accountId, string messageId, bool isFlagged, CancellationToken cancellationToken);
    Task<MailOperationResult> CreateFolderAsync(Guid accountId, string displayName, CancellationToken cancellationToken);
    Task<MailOperationResult> RenameFolderAsync(Guid accountId, string folderId, string displayName, CancellationToken cancellationToken);
    Task<MailOperationResult> DeleteFolderAsync(Guid accountId, string folderId, CancellationToken cancellationToken);
    Task<MailOperationResult> MoveToFolderAsync(Guid accountId, string messageId, string folderId, CancellationToken cancellationToken);
    Task<MailOperationResult> SetSpamAsync(Guid accountId, string messageId, bool isSpam, CancellationToken cancellationToken);
    Task<MailOperationResult> SetImportantAsync(Guid accountId, string messageId, bool isImportant, CancellationToken cancellationToken);
}

public interface IMailDraftStore
{
    Task<IReadOnlyList<MailDraft>> GetAllAsync(CancellationToken cancellationToken);
    Task<MailDraft?> GetAsync(Guid localDraftId, CancellationToken cancellationToken);
    Task UpsertAsync(MailDraft draft, CancellationToken cancellationToken);
    Task DeleteAsync(Guid localDraftId, CancellationToken cancellationToken);
}

public interface IMailProviderRegistry
{
    IReadOnlyList<IMailProvider> Providers { get; }
    IMailProvider Get(CalendarProviderKind kind);
}

public interface IMailService
{
    Task<IReadOnlyList<MailAccount>> GetAccountsAsync(CancellationToken cancellationToken);
    Task<MailOperationResult> CheckAccessAsync(Guid accountId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MailFolder>> GetFoldersAsync(Guid accountId, CancellationToken cancellationToken);
    Task<MailPage> GetMessagesAsync(MailQuery query, CancellationToken cancellationToken);
    Task<MailMessage> GetMessageAsync(Guid accountId, string messageId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MailMessage>> GetThreadAsync(Guid accountId, string threadId, CancellationToken cancellationToken);
    Task<byte[]> DownloadAttachmentAsync(Guid accountId, string messageId, string attachmentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MailDraft>> GetDraftsAsync(CancellationToken cancellationToken);
    Task<MailDraft?> GetDraftAsync(Guid localDraftId, CancellationToken cancellationToken);
    Task<MailOperationResult> SaveDraftAsync(MailDraft draft, CancellationToken cancellationToken);
    Task<MailOperationResult> DeleteDraftAsync(Guid accountId, string draftId, CancellationToken cancellationToken);
    Task<MailOperationResult> DiscardDraftAsync(Guid localDraftId, CancellationToken cancellationToken);
    Task<MailOperationResult> SendAsync(MailDraft draft, CancellationToken cancellationToken);
    Task<MailOperationResult> ArchiveAsync(Guid accountId, string messageId, CancellationToken cancellationToken);
    Task<MailOperationResult> DeleteAsync(Guid accountId, string messageId, CancellationToken cancellationToken);
    Task<MailOperationResult> SetReadAsync(Guid accountId, string messageId, bool isRead, CancellationToken cancellationToken);
    Task<MailOperationResult> SetFlaggedAsync(Guid accountId, string messageId, bool isFlagged, CancellationToken cancellationToken);
    Task<MailOperationResult> CreateFolderAsync(Guid accountId, string displayName, CancellationToken cancellationToken);
    Task<MailOperationResult> RenameFolderAsync(Guid accountId, string folderId, string displayName, CancellationToken cancellationToken);
    Task<MailOperationResult> DeleteFolderAsync(Guid accountId, string folderId, CancellationToken cancellationToken);
    Task<MailOperationResult> MoveToFolderAsync(Guid accountId, string messageId, string folderId, CancellationToken cancellationToken);
    Task<MailOperationResult> SetSpamAsync(Guid accountId, string messageId, bool isSpam, CancellationToken cancellationToken);
    Task<MailOperationResult> SetImportantAsync(Guid accountId, string messageId, bool isImportant, CancellationToken cancellationToken);
    Task<MailBulkOperationResult> ExecuteBulkAsync(Guid accountId, IReadOnlyCollection<string> messageIds, MailBulkActionKind action, CancellationToken cancellationToken);
    Task<MailBulkOperationResult> MoveMessagesToFolderAsync(Guid accountId, IReadOnlyCollection<string> messageIds, string folderId, CancellationToken cancellationToken);
}

public sealed class MailProviderException : Exception
{
    public MailProviderException(MailFailureKind failureKind, string message, Exception? innerException = null) : base(message, innerException)
        => FailureKind = failureKind;

    public MailFailureKind FailureKind { get; }
}
