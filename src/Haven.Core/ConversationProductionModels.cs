/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Core/ConversationProductionModels.cs, in the dependency-free Core layer, where shared domain models and rules live.
 * What: This file owns ConversationBranchReason, MessageVersionKind, MessageAttachmentKind, AttachmentProcessingState, AttachmentAnalysisMethod, SharedSessionState, ConversationBranch, ConversationTurn, MessageVersion, MessageAttachment, ConversationDraft, MessageBookmark, SharedSession, ConversationSearchResult, ConversationExportDocument. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: This code stays free of UI and storage dependencies so the same rule or data shape can be reused and tested everywhere.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;

namespace Haven.Core;

/// <summary>
/// Lists the supported conversation branch reason values used to make state explicit and type-safe.
/// </summary>
public enum ConversationBranchReason
{
    Root = 0,
    Manual = 1,
    EditedUserMessage = 2,
    RegeneratedResponse = 3,
    Retry = 4,
    Import = 5
}

/// <summary>
/// Lists the supported message version kind values used to make state explicit and type-safe.
/// </summary>
public enum MessageVersionKind
{
    Original = 0,
    UserEdit = 1,
    Regeneration = 2,
    RecoverySnapshot = 3,
    Imported = 4
}

/// <summary>
/// Lists the supported message attachment kind values used to make state explicit and type-safe.
/// </summary>
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

/// <summary>
/// Lists the supported attachment processing state values used to make state explicit and type-safe.
/// </summary>
public enum AttachmentProcessingState
{
    Pending = 0,
    Ready = 1,
    Failed = 2,
    Unsupported = 3
}

/// <summary>
/// Lists the supported attachment analysis method values used to make state explicit and type-safe.
/// </summary>
public enum AttachmentAnalysisMethod
{
    None = 0,
    DirectlyAnalysed = 1,
    TextExtracted = 2,
    Transcribed = 3,
    SampledFrames = 4,
    InferredFromMetadata = 5
}

/// <summary>
/// Lists the supported shared session state values used to make state explicit and type-safe.
/// </summary>
public enum SharedSessionState
{
    Active = 0,
    Stopped = 1,
    Expired = 2
}

/// <summary>
/// Represents conversation branch and keeps its related state and behavior together.
/// </summary>
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

/// <summary>
/// Represents conversation turn and keeps its related state and behavior together.
/// </summary>
public sealed record ConversationTurn(
    Guid Id,
    Guid ConversationId,
    Guid BranchId,
    int Sequence,
    Guid? UserMessageId,
    Guid? AssistantMessageId,
    DateTimeOffset CreatedAt);

/// <summary>
/// Represents message version and keeps its related state and behavior together.
/// </summary>
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
    /// <summary>
    /// Gets or updates metadata, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Metadata =>
        string.IsNullOrWhiteSpace(MetadataJson)
            ? new Dictionary<string, JsonElement>()
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(MetadataJson) ?? new Dictionary<string, JsonElement>();
}

/// <summary>
/// Represents message attachment and keeps its related state and behavior together.
/// </summary>
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

/// <summary>
/// Represents conversation draft and keeps its related state and behavior together.
/// </summary>
public sealed record ConversationDraft(
    Guid ConversationId,
    Guid? BranchId,
    string Content,
    string AttachmentIdsJson,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Represents message bookmark and keeps its related state and behavior together.
/// </summary>
public sealed record MessageBookmark(
    Guid Id,
    Guid ConversationId,
    Guid MessageId,
    string Label,
    string Note,
    DateTimeOffset CreatedAt);

/// <summary>
/// Represents shared session and keeps its related state and behavior together.
/// </summary>
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

/// <summary>
/// Represents conversation search result and keeps its related state and behavior together.
/// </summary>
public sealed record ConversationSearchResult(
    Guid ConversationId,
    Guid? MessageId,
    string ConversationTitle,
    string Snippet,
    DateTimeOffset Timestamp,
    double Rank);

/// <summary>
/// Represents conversation export document and keeps its related state and behavior together.
/// </summary>
public sealed record ConversationExportDocument(
    Conversation Conversation,
    IReadOnlyList<ConversationBranch> Branches,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<MessageVersion> Versions,
    IReadOnlyList<MessageAttachment> Attachments,
    IReadOnlyList<MessageBookmark> Bookmarks,
    DateTimeOffset ExportedAt);
