using System.Text.Json;

namespace Haven.Core;

/// <summary>
/// Represents a conversation.
/// </summary>
public sealed record Conversation(
    Guid Id,
    HavenMode Mode,
    ConversationKind Kind,
    string Title,
    Guid? ContainerId,
    Guid? LessonId,
    bool IsPinned,
    bool IsTemporary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsArchived = false,
    Guid? ParentConversationId = null,
    DateTimeOffset? CompactedAt = null,
    Guid? SpaceId = null);

/// <summary>
/// Identifies one independently navigable Chat or Study history. Sidebar selection and
/// persisted conversation scope deliberately remain separate so selecting a subject
/// cannot accidentally move a Quick Chat into that subject.
/// </summary>
public sealed record ConversationScope
{
    private ConversationScope(ConversationScopeKind kind, Guid? containerId, Guid? lessonId)
    {
        Kind = kind;
        ContainerId = containerId;
        LessonId = lessonId;
    }

    /// <summary>
    /// The scope kind.
    /// </summary>
    public ConversationScopeKind Kind { get; }
    /// <summary>
    /// The container ID.
    /// </summary>
    public Guid? ContainerId { get; }
    /// <summary>
    /// The lesson ID.
    /// </summary>
    public Guid? LessonId { get; }
    /// <summary>
    /// The resolved mode.
    /// </summary>
    public HavenMode Mode => Kind is ConversationScopeKind.GeneralChat or ConversationScopeKind.ChatGroup
        ? HavenMode.Chat
        : HavenMode.Study;

    /// <summary>
    /// General chat scope.
    /// </summary>
    public static ConversationScope GeneralChat { get; } = new(ConversationScopeKind.GeneralChat, null, null);
    /// <summary>
    /// Study quick chat scope.
    /// </summary>
    public static ConversationScope StudyQuickChat { get; } = new(ConversationScopeKind.StudyQuickChat, null, null);

    /// <summary>Compatibility alias for extensions built before Study replaced Teach.</summary>
    public static ConversationScope TeachQuickChat => StudyQuickChat;

    /// <summary>
    /// Creates a scope for a chat group.
    /// </summary>
    public static ConversationScope ForChatGroup(Guid containerId) =>
        new(ConversationScopeKind.ChatGroup, RequireId(containerId, nameof(containerId)), null);

    /// <summary>
    /// Creates a scope for a Study lesson.
    /// </summary>
    public static ConversationScope ForStudyLesson(Guid subjectId, Guid lessonId) =>
        new(ConversationScopeKind.StudyLesson, RequireId(subjectId, nameof(subjectId)), RequireId(lessonId, nameof(lessonId)));

    /// <summary>Compatibility alias for extensions built before Study replaced Teach.</summary>
    public static ConversationScope ForTeachLesson(Guid subjectId, Guid lessonId) => ForStudyLesson(subjectId, lessonId);

    /// <summary>
    /// Creates a scope from a conversation.
    /// </summary>
    public static ConversationScope From(Conversation conversation) => conversation.Mode switch
    {
        HavenMode.Chat when conversation.Kind == ConversationKind.Chat && conversation.ContainerId is { } groupId => ForChatGroup(groupId),
        HavenMode.Chat when conversation.Kind == ConversationKind.Chat => GeneralChat,
        HavenMode.Study when conversation.Kind == ConversationKind.LessonChat && conversation.ContainerId is { } subjectId && conversation.LessonId is { } lessonId =>
            ForStudyLesson(subjectId, lessonId),
        HavenMode.Study when conversation.Kind == ConversationKind.QuickChat => StudyQuickChat,
        _ => throw new ArgumentOutOfRangeException(nameof(conversation), "The conversation is not a scoped Chat or Study conversation.")
    };

    /// <summary>
    /// Checks whether a conversation matches this scope.
    /// </summary>
    public bool Matches(Conversation conversation) => Kind switch
    {
        ConversationScopeKind.GeneralChat =>
            conversation.Mode == HavenMode.Chat && conversation.Kind == ConversationKind.Chat && conversation.ContainerId is null && conversation.LessonId is null,
        ConversationScopeKind.ChatGroup =>
            conversation.Mode == HavenMode.Chat && conversation.Kind == ConversationKind.Chat && conversation.ContainerId == ContainerId && conversation.LessonId is null,
        ConversationScopeKind.StudyQuickChat =>
            conversation.Mode == HavenMode.Study && conversation.Kind == ConversationKind.QuickChat && conversation.ContainerId is null && conversation.LessonId is null,
        ConversationScopeKind.StudyLesson =>
            conversation.Mode == HavenMode.Study && conversation.Kind == ConversationKind.LessonChat && conversation.ContainerId == ContainerId && conversation.LessonId == LessonId,
        _ => false
    };

    /// <summary>
    /// Validates that an ID is not empty.
    /// </summary>
    private static Guid RequireId(Guid id, string parameterName) =>
        id == Guid.Empty ? throw new ArgumentException("The identifier cannot be empty.", parameterName) : id;
}

/// <summary>
/// Represents a chat message.
/// </summary>
public sealed record ChatMessage(
    Guid Id,
    Guid ConversationId,
    MessageRole Role,
    string Content,
    string? AgentName,
    string? ModelName,
    string? MetadataJson,
    DateTimeOffset CreatedAt,
    bool IsCompacted = false)
{
    /// <summary>
    /// Deserialized metadata.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Metadata =>
        string.IsNullOrWhiteSpace(MetadataJson)
            ? new Dictionary<string, JsonElement>()
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(MetadataJson) ?? new();
}

/// <summary>
/// Represents a conversation context entry.
/// </summary>
public sealed record ConversationContextEntry(
    Guid Id,
    Guid ConversationId,
    ContextEntryKind Kind,
    string Title,
    string Content,
    string Evidence,
    DateTimeOffset CreatedAt);

/// <summary>
/// Represents a conversation move.
/// </summary>
public sealed record ConversationMove(
    Guid Id,
    Guid ConversationId,
    Guid? FromModeId,
    Guid? ToModeId,
    ConversationPlacement FromPlacement,
    ConversationPlacement ToPlacement,
    string Reason,
    DateTimeOffset MovedAt);
