/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Core/Entities.cs, in the dependency-free Core layer, where shared domain models and rules live.
 * What: This file owns Conversation, ConversationScope, ChatMessage, ContainerDefinition, Lesson, ContainerResource, AgentDefinition, PluginDefinition, PromptDefinition, ConversationContextEntry, MacroDefinition, WorkspaceVersion, DecisionRecord, ProjectStateSnapshot, ReleaseRiskReport, AutomationDefinition, AutomationRun, ModelDescriptor, ActivePlugin, ActivePrompt, CapabilityRequirement, CapabilityPreflightResult, ToolActivity, TrainingRun, TrainingAttempt, ModeDefinition, ModeVersion, ModePermissionGrant, ModePin, ModeUsage, SurfaceRun, ActivityEvent, ConversationMove, PlannerDefaults, PlannerCollection, PlannerTask, PlannerTaskCompletion, PlannerCalendar, PlannerEvent, CalendarAccount, CalendarConflict, PlannerProposedChange, PlannerChangeProposal, PlannerReminder, CalendarSyncCursor, CalendarOutboxItem. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: This code stays free of UI and storage dependencies so the same rule or data shape can be reused and tested everywhere.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;

namespace Haven.Core;

/// <summary>
/// Represents conversation and keeps its related state and behavior together.
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
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public ConversationScopeKind Kind { get; }
    /// <summary>
    /// Gets or updates container id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid? ContainerId { get; }
    /// <summary>
    /// Gets or updates lesson id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid? LessonId { get; }
    /// <summary>
    /// Gets or updates mode, the bindable or domain state represented by this property.
    /// </summary>
    public HavenMode Mode => Kind is ConversationScopeKind.GeneralChat or ConversationScopeKind.ChatGroup
        ? HavenMode.Chat
        : HavenMode.Teach;

    /// <summary>
    /// Gets or updates general chat, the bindable or domain state represented by this property.
    /// </summary>
    public static ConversationScope GeneralChat { get; } = new(ConversationScopeKind.GeneralChat, null, null);
    /// <summary>
    /// Gets or updates teach quick chat, the bindable or domain state represented by this property.
    /// </summary>
    public static ConversationScope TeachQuickChat { get; } = new(ConversationScopeKind.TeachQuickChat, null, null);

    /// <summary>
    /// Performs the for chat group step owned by this component.
    /// </summary>
    public static ConversationScope ForChatGroup(Guid containerId) =>
        new(ConversationScopeKind.ChatGroup, RequireId(containerId, nameof(containerId)), null);

    /// <summary>
    /// Performs the for teach lesson step owned by this component.
    /// </summary>
    public static ConversationScope ForTeachLesson(Guid subjectId, Guid lessonId) =>
        new(ConversationScopeKind.TeachLesson, RequireId(subjectId, nameof(subjectId)), RequireId(lessonId, nameof(lessonId)));

    /// <summary>
    /// Performs the from step owned by this component.
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
    /// Performs the matches step owned by this component.
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
    /// Performs the require id step owned by this component.
    /// </summary>
    private static Guid RequireId(Guid id, string parameterName) =>
        id == Guid.Empty ? throw new ArgumentException("The identifier cannot be empty.", parameterName) : id;
}

/// <summary>
/// Represents chat message and keeps its related state and behavior together.
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
    /// Gets or updates metadata, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Metadata =>
        string.IsNullOrWhiteSpace(MetadataJson)
            ? new Dictionary<string, JsonElement>()
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(MetadataJson) ?? new();
}

/// <summary>
/// Represents container definition and keeps its related state and behavior together.
/// </summary>
public sealed record ContainerDefinition(
    Guid Id,
    HavenMode Mode,
    string Name,
    string? RootPath,
    string Context,
    string Instructions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsArchived = false);

/// <summary>
/// Represents lesson and keeps its related state and behavior together.
/// </summary>
public sealed record Lesson(
    Guid Id,
    Guid SubjectId,
    string TopicGroup,
    string Name,
    string StructureJson,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Represents container resource and keeps its related state and behavior together.
/// </summary>
public sealed record ContainerResource(
    Guid Id,
    Guid ContainerId,
    string Name,
    string StoredName,
    string MediaType,
    ContainerResourceKind Kind,
    long SizeBytes,
    string Sha256,
    DateTimeOffset CreatedAt);

/// <summary>
/// Represents agent definition and keeps its related state and behavior together.
/// </summary>
public sealed record AgentDefinition(
    Guid Id,
    string Name,
    string Description,
    string Instructions,
    string IconKey,
    string PreferredModel,
    string? FallbackModel,
    string DetectionRules,
    string PermissionsJson,
    bool IsBuiltIn,
    bool IsEnabled,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Represents plugin definition and keeps its related state and behavior together.
/// </summary>
public sealed record PluginDefinition(
    Guid Id,
    string Name,
    string Description,
    string IconKey,
    string Instructions,
    string CapabilitiesJson,
    string ConflictsJson,
    bool Persists,
    bool IsBuiltIn,
    bool IsEnabled,
    DateTimeOffset UpdatedAt,
    bool IsAgentic = false,
    string AllowedModesJson = "[]",
    string DashboardTilesJson = "[]");

/// <summary>
/// Represents prompt definition and keeps its related state and behavior together.
/// </summary>
public sealed record PromptDefinition(
    Guid Id,
    string Name,
    string Description,
    string IconKey,
    string Instructions,
    bool Persists,
    bool IsBuiltIn,
    bool IsEnabled,
    DateTimeOffset UpdatedAt,
    bool IsAgentic = false,
    string AllowedModesJson = "[]");

/// <summary>
/// Represents conversation context entry and keeps its related state and behavior together.
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
/// Represents macro definition and keeps its related state and behavior together.
/// </summary>
public sealed record MacroDefinition(
    Guid Id,
    string Name,
    string Description,
    string Instruction,
    Guid? ContainerId,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Represents workspace version and keeps its related state and behavior together.
/// </summary>
public sealed record WorkspaceVersion(
    Guid Id,
    Guid? ConversationId,
    Guid? ContainerId,
    string WorkspaceRoot,
    string RelativePath,
    WorkspaceVersionKind Kind,
    string BeforeContent,
    string AfterContent,
    string Summary,
    int LinesAdded,
    int LinesRemoved,
    DateTimeOffset CreatedAt);

/// <summary>
/// Represents decision record and keeps its related state and behavior together.
/// </summary>
public sealed record DecisionRecord(
    Guid Id,
    Guid ContainerId,
    string Title,
    string Decision,
    string Alternatives,
    string Reasoning,
    string Evidence,
    string Consequences,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Represents project state snapshot and keeps its related state and behavior together.
/// </summary>
public sealed record ProjectStateSnapshot(
    string RootPath,
    string Branch,
    bool HasUncommittedWork,
    int Ahead,
    int Behind,
    string LastCommit,
    string LastBuildResult,
    string MostRecentError,
    string RecommendedAction,
    DateTimeOffset CapturedAt);

/// <summary>
/// Represents release risk report and keeps its related state and behavior together.
/// </summary>
public sealed record ReleaseRiskReport(
    int Score,
    string Level,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<string> RiskAreas,
    IReadOnlyList<string> RecommendedTests,
    IReadOnlyList<string> CriticalFindings);

/// <summary>
/// Represents automation definition and keeps its related state and behavior together.
/// </summary>
public sealed record AutomationDefinition(
    Guid Id,
    string Name,
    HavenMode Mode,
    string Instruction,
    AutomationScheduleKind ScheduleKind,
    string ScheduleJson,
    DateTimeOffset? NextRunAt,
    Guid? ContainerId,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Represents automation run and keeps its related state and behavior together.
/// </summary>
public sealed record AutomationRun(
    Guid Id,
    Guid AutomationId,
    AutomationRunStatus Status,
    DateTimeOffset ScheduledFor,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? Result,
    string? Error,
    string? LeaseToken);

/// <summary>
/// Represents model descriptor and keeps its related state and behavior together.
/// </summary>
public sealed record ModelDescriptor(
    string Name,
    long SizeBytes,
    string Family,
    string ParameterSize,
    string Quantization,
    IReadOnlySet<ToolCapability> Capabilities,
    DateTimeOffset ModifiedAt)
{
    /// <summary>
    /// Performs the supports step owned by this component.
    /// </summary>
    public bool Supports(ToolCapability capability) => Capabilities.Contains(capability);
    /// <summary>
    /// Gets or updates size label, the bindable or domain state represented by this property.
    /// </summary>
    public string SizeLabel => FormatBytes(SizeBytes);
    /// <summary>
    /// Gets or updates estimated ram label, the bindable or domain state represented by this property.
    /// </summary>
    public string EstimatedRamLabel => $"Approx. {FormatBytes((long)(SizeBytes * 1.25))} RAM";

    /// <summary>
    /// Performs the format bytes step owned by this component.
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
}

/// <summary>
/// Represents active plugin and keeps its related state and behavior together.
/// </summary>
public sealed record ActivePlugin(string Name, string IconKey, bool Persists, string Instructions = "");
/// <summary>
/// Represents active prompt and keeps its related state and behavior together.
/// </summary>
public sealed record ActivePrompt(string Name, string IconKey, bool Persists, string Instructions = "");

/// <summary>
/// Represents capability requirement and keeps its related state and behavior together.
/// </summary>
public sealed record CapabilityRequirement(ToolCapability Capability, string Reason);

/// <summary>
/// Represents capability preflight result and keeps its related state and behavior together.
/// </summary>
public sealed record CapabilityPreflightResult(
    bool IsCompatible,
    IReadOnlyList<CapabilityRequirement> Requirements,
    IReadOnlyList<CapabilityRequirement> Missing,
    ModelDescriptor? SuggestedModel)
{
    /// <summary>
    /// Performs the compatible step owned by this component.
    /// </summary>
    public static CapabilityPreflightResult Compatible(IReadOnlyList<CapabilityRequirement> requirements) =>
        new(true, requirements, Array.Empty<CapabilityRequirement>(), null);
}

/// <summary>
/// Represents tool activity and keeps its related state and behavior together.
/// </summary>
public sealed record ToolActivity(
    Guid Id,
    string Title,
    string Detail,
    bool Succeeded,
    TimeSpan Duration,
    DateTimeOffset Timestamp,
    int LinesAdded = 0,
    int LinesRemoved = 0);

/// <summary>
/// Represents training run and keeps its related state and behavior together.
/// </summary>
public sealed record TrainingRun(
    Guid Id,
    string TaskPrompt,
    string WorkspacePath,
    string SnapshotPath,
    string ModelName,
    int MaxAttempts,
    int DurationMinutes,
    PermissionMode FilePermission,
    PermissionMode CommandPermission,
    PermissionMode BrowserPermission,
    bool AllowDesktopTools,
    bool AllowFileSystemWrites,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt = null);

/// <summary>
/// Represents training attempt and keeps its related state and behavior together.
/// </summary>
public sealed record TrainingAttempt(
    Guid Id,
    Guid TrainingRunId,
    int AttemptNumber,
    string ReportMarkdown,
    string? Feedback,
    string ActionLog,
    bool Succeeded,
    TimeSpan Duration,
    DateTimeOffset CreatedAt);

/// <summary>
/// Represents mode definition and keeps its related state and behavior together.
/// </summary>
public sealed record ModeDefinition(
    Guid Id,
    string Key,
    string Name,
    string Description,
    string IconKey,
    HavenMode BaseMode,
    string SurfacesJson,
    string ToolAllowlistJson,
    string ToolDenylistJson,
    string PluginsJson,
    string SystemPromptSuffix,
    ModeSource Source,
    ModeInstallState InstallState,
    string Author,
    string Version,
    string TagsJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsEnabled = true);

/// <summary>
/// Represents mode version and keeps its related state and behavior together.
/// </summary>
public sealed record ModeVersion(
    Guid Id,
    Guid ModeId,
    int Major,
    int Minor,
    int Patch,
    string ManifestJson,
    string Changelog,
    DateTimeOffset PublishedAt);

/// <summary>
/// Represents mode permission grant and keeps its related state and behavior together.
/// </summary>
public sealed record ModePermissionGrant(
    Guid Id,
    Guid ModeId,
    PermissionMode FilePermission,
    PermissionMode CommandPermission,
    PermissionMode BrowserPermission,
    bool AllowDesktopTools,
    bool AllowFileSystemWrites,
    DateTimeOffset GrantedAt);

/// <summary>
/// Represents mode pin and keeps its related state and behavior together.
/// </summary>
public sealed record ModePin(
    Guid Id,
    Guid ModeId,
    int SortOrder,
    DateTimeOffset PinnedAt);

/// <summary>
/// Represents mode usage and keeps its related state and behavior together.
/// </summary>
public sealed record ModeUsage(
    Guid Id,
    Guid ModeId,
    DateOnly Date,
    int TurnCount,
    int CompletionCount,
    TimeSpan TotalDuration);

/// <summary>
/// Represents surface run and keeps its related state and behavior together.
/// </summary>
public sealed record SurfaceRun(
    Guid Id,
    Guid ConversationId,
    SurfaceKind Surface,
    string SurfaceKey,
    string? TargetModeKey,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    bool Succeeded);

/// <summary>
/// Represents activity event and keeps its related state and behavior together.
/// </summary>
public sealed record ActivityEvent(
    Guid Id,
    ActivityEventKind Kind,
    Guid? ConversationId,
    Guid? ModeId,
    string Summary,
    string DetailJson,
    DateTimeOffset Timestamp);

/// <summary>
/// Represents conversation move and keeps its related state and behavior together.
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

/// <summary>
/// Represents planner defaults and keeps its related state and behavior together.
/// </summary>
public static class PlannerDefaults
{
    /// <summary>
    /// Stores personal collection id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public static readonly Guid PersonalCollectionId = Guid.Parse("8f51f72f-3c1f-4a5f-a101-010000000001");
    /// <summary>
    /// Stores college collection id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public static readonly Guid CollegeCollectionId = Guid.Parse("8f51f72f-3c1f-4a5f-a101-010000000002");
    /// <summary>
    /// Stores work collection id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public static readonly Guid WorkCollectionId = Guid.Parse("8f51f72f-3c1f-4a5f-a101-010000000003");
    /// <summary>
    /// Stores local calendar id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public static readonly Guid LocalCalendarId = Guid.Parse("8f51f72f-3c1f-4a5f-a101-020000000001");
}

/// <summary>
/// Represents planner collection and keeps its related state and behavior together.
/// </summary>
public sealed record PlannerCollection(
    Guid Id,
    string Name,
    int SortOrder,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Represents planner task and keeps its related state and behavior together.
/// </summary>
public sealed record PlannerTask(
    Guid Id,
    Guid CollectionId,
    Guid? ParentTaskId,
    string Title,
    string Notes,
    PlannerPriority Priority,
    PlannerTaskStatus Status,
    string TagsJson,
    int? EstimatedMinutes,
    DateTimeOffset? StartsAt,
    DateTimeOffset? DueAt,
    string? RecurrenceRule,
    DateTimeOffset? ReminderAt,
    DateTimeOffset? CompletedAt,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string TimeZoneId = "UTC");

/// <summary>
/// Represents planner task completion and keeps its related state and behavior together.
/// </summary>
public sealed record PlannerTaskCompletion(
    Guid Id,
    Guid TaskId,
    DateTimeOffset CompletedAt,
    DateTimeOffset? OccurrenceDueAt);

/// <summary>
/// Represents planner calendar and keeps its related state and behavior together.
/// </summary>
public sealed record PlannerCalendar(
    Guid Id,
    Guid? AccountId,
    CalendarProviderKind Provider,
    string ProviderCalendarId,
    string Name,
    string Color,
    CalendarPermission Permission,
    bool IsVisible,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Represents planner event and keeps its related state and behavior together.
/// </summary>
public sealed record PlannerEvent(
    Guid Id,
    Guid CalendarId,
    string Title,
    string Notes,
    string Location,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    bool IsAllDay,
    string? RecurrenceRule,
    DateTimeOffset? ReminderAt,
    bool IsReadOnly,
    string? ProviderEventId,
    string? ProviderETag,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt = null,
    string TimeZoneId = "UTC");

/// <summary>
/// Represents calendar account and keeps its related state and behavior together.
/// </summary>
public sealed record CalendarAccount(
    Guid Id,
    CalendarProviderKind Provider,
    string DisplayName,
    string AccountIdentifier,
    CalendarSyncStatus Status,
    string? StatusMessage,
    DateTimeOffset? LastSyncedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Represents calendar conflict and keeps its related state and behavior together.
/// </summary>
public sealed record CalendarConflict(
    Guid Id,
    Guid EventId,
    Guid AccountId,
    string HavenSnapshotJson,
    string ProviderSnapshotJson,
    DateTimeOffset DetectedAt,
    DateTimeOffset? ResolvedAt,
    CalendarConflictResolution? Resolution);

/// <summary>
/// Represents planner proposed change and keeps its related state and behavior together.
/// </summary>
public sealed record PlannerProposedChange(
    Guid Id,
    PlannerChangeKind Kind,
    Guid? EntityId,
    string PayloadJson,
    string Description);

/// <summary>
/// Represents planner change proposal and keeps its related state and behavior together.
/// </summary>
public sealed record PlannerChangeProposal(
    Guid Id,
    string Summary,
    IReadOnlyList<PlannerProposedChange> Changes,
    DateTimeOffset CreatedAt);

/// <summary>
/// Represents planner reminder and keeps its related state and behavior together.
/// </summary>
public sealed record PlannerReminder(
    PlannerReminderKind Kind,
    Guid EntityId,
    string Title,
    DateTimeOffset ReminderAt,
    DateTimeOffset OccurrenceAt);

/// <summary>
/// Represents calendar sync cursor and keeps its related state and behavior together.
/// </summary>
public sealed record CalendarSyncCursor(
    Guid AccountId,
    Guid CalendarId,
    string? SyncCursor,
    string? DeltaLink,
    DateTimeOffset? WindowStart,
    DateTimeOffset? WindowEnd,
    DateTimeOffset? LastSyncedAt);

/// <summary>
/// Represents calendar outbox item and keeps its related state and behavior together.
/// </summary>
public sealed record CalendarOutboxItem(
    Guid Id,
    Guid AccountId,
    Guid? EventId,
    string Operation,
    string PayloadJson,
    int AttemptCount,
    DateTimeOffset NextAttemptAt,
    string? LastError,
    DateTimeOffset CreatedAt);
