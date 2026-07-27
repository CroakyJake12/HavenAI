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
    DateTimeOffset? CompactedAt = null);

/// <summary>
/// Identifies one independently navigable Chat or Teach history. Sidebar selection and
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
        : HavenMode.Teach;

    /// <summary>
    /// General chat scope.
    /// </summary>
    public static ConversationScope GeneralChat { get; } = new(ConversationScopeKind.GeneralChat, null, null);
    /// <summary>
    /// Teach quick chat scope.
    /// </summary>
    public static ConversationScope TeachQuickChat { get; } = new(ConversationScopeKind.TeachQuickChat, null, null);

    /// <summary>
    /// Creates a scope for a chat group.
    /// </summary>
    public static ConversationScope ForChatGroup(Guid containerId) =>
        new(ConversationScopeKind.ChatGroup, RequireId(containerId, nameof(containerId)), null);

    /// <summary>
    /// Creates a scope for a teach lesson.
    /// </summary>
    public static ConversationScope ForTeachLesson(Guid subjectId, Guid lessonId) =>
        new(ConversationScopeKind.TeachLesson, RequireId(subjectId, nameof(subjectId)), RequireId(lessonId, nameof(lessonId)));

    /// <summary>
    /// Creates a scope from a conversation.
    /// </summary>
    public static ConversationScope From(Conversation conversation) => conversation.Mode switch
    {
        HavenMode.Chat when conversation.Kind == ConversationKind.Chat && conversation.ContainerId is { } groupId => ForChatGroup(groupId),
        HavenMode.Chat when conversation.Kind == ConversationKind.Chat => GeneralChat,
        HavenMode.Teach when conversation.Kind == ConversationKind.LessonChat && conversation.ContainerId is { } subjectId && conversation.LessonId is { } lessonId =>
            ForTeachLesson(subjectId, lessonId),
        HavenMode.Teach when conversation.Kind == ConversationKind.QuickChat => TeachQuickChat,
        _ => throw new ArgumentOutOfRangeException(nameof(conversation), "The conversation is not a scoped Chat or Teach conversation.")
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
        ConversationScopeKind.TeachQuickChat =>
            conversation.Mode == HavenMode.Teach && conversation.Kind == ConversationKind.QuickChat && conversation.ContainerId is null && conversation.LessonId is null,
        ConversationScopeKind.TeachLesson =>
            conversation.Mode == HavenMode.Teach && conversation.Kind == ConversationKind.LessonChat && conversation.ContainerId == ContainerId && conversation.LessonId == LessonId,
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
