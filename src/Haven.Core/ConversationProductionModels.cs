using System.Text.Json;

namespace Haven.Core;

public enum ConversationBranchReason
{
    Root = 0,
    Manual = 1,
    EditedUserMessage = 2,
    RegeneratedResponse = 3,
    Retry = 4,
    Import = 5
}

public enum MessageVersionKind
{
    Original = 0,
    UserEdit = 1,
    Regeneration = 2,
    RecoverySnapshot = 3,
    Imported = 4
}

public enum MessageAttachmentKind
{
    Image = 0,
    PlainText = 1,
    SourceCode = 2,
    Pdf = 3,
    Word = 4,
    PowerPoint = 5,
    Spreadsheet = 6,
    Audio = 7,
    Video = 8,
    Other = 9
}

public enum AttachmentProcessingState
{
    Pending = 0,
    Ready = 1,
    Failed = 2,
    Unsupported = 3
}

public enum AttachmentAnalysisMethod
{
    None = 0,
    DirectlyAnalysed = 1,
    TextExtracted = 2,
    Transcribed = 3,
    SampledFrames = 4,
    InferredFromMetadata = 5
}

public enum SharedSessionState
{
    Active = 0,
    Stopped = 1,
    Expired = 2
}

public sealed record ConversationBranch(
    Guid Id,
    Guid ConversationId,
    Guid? ParentBranchId,
    Guid? ForkedFromMessageId,
    string Name,
    ConversationBranchReason Reason,
    bool IsCurrent,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ConversationTurn(
    Guid Id,
    Guid ConversationId,
    Guid BranchId,
    int Sequence,
    Guid? UserMessageId,
    Guid? AssistantMessageId,
    DateTimeOffset CreatedAt);

public sealed record MessageVersion(
    Guid Id,
    Guid MessageId,
    Guid BranchId,
    int VersionNumber,
    MessageVersionKind Kind,
    string Content,
    string? MetadataJson,
    bool IsCurrent,
    DateTimeOffset CreatedAt)
{
    public IReadOnlyDictionary<string, JsonElement> Metadata =>
        string.IsNullOrWhiteSpace(MetadataJson)
            ? new Dictionary<string, JsonElement>()
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(MetadataJson) ?? new Dictionary<string, JsonElement>();
}

public sealed record MessageAttachment(
    Guid Id,
    Guid ConversationId,
    Guid? MessageId,
    Guid? BranchId,
    string OriginalName,
    string StoredName,
    string MediaType,
    MessageAttachmentKind Kind,
    long SizeBytes,
    string Sha256,
    AttachmentProcessingState ProcessingState,
    AttachmentAnalysisMethod AnalysisMethod,
    string ExtractedText,
    string MetadataJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ConversationDraft(
    Guid ConversationId,
    Guid? BranchId,
    string Content,
    string AttachmentIdsJson,
    DateTimeOffset UpdatedAt);

public sealed record MessageBookmark(
    Guid Id,
    Guid ConversationId,
    Guid MessageId,
    string Label,
    string Note,
    DateTimeOffset CreatedAt);

public sealed record SharedSession(
    Guid Id,
    Guid ConversationId,
    string TokenHash,
    string BindAddress,
    int Port,
    SharedSessionState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? StoppedAt);

public sealed record ConversationSearchResult(
    Guid ConversationId,
    Guid? MessageId,
    string ConversationTitle,
    string Snippet,
    DateTimeOffset Timestamp,
    double Rank);

public sealed record ConversationExportDocument(
    Conversation Conversation,
    IReadOnlyList<ConversationBranch> Branches,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<MessageVersion> Versions,
    IReadOnlyList<MessageAttachment> Attachments,
    IReadOnlyList<MessageBookmark> Bookmarks,
    DateTimeOffset ExportedAt);
