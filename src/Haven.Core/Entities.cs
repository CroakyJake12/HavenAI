using System.Text.Json;

namespace Haven.Core;

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

    public ConversationScopeKind Kind { get; }
    public Guid? ContainerId { get; }
    public Guid? LessonId { get; }
    public HavenMode Mode => Kind is ConversationScopeKind.GeneralChat or ConversationScopeKind.ChatGroup
        ? HavenMode.Chat
        : HavenMode.Teach;

    public static ConversationScope GeneralChat { get; } = new(ConversationScopeKind.GeneralChat, null, null);
    public static ConversationScope TeachQuickChat { get; } = new(ConversationScopeKind.TeachQuickChat, null, null);

    public static ConversationScope ForChatGroup(Guid containerId) =>
        new(ConversationScopeKind.ChatGroup, RequireId(containerId, nameof(containerId)), null);

    public static ConversationScope ForTeachLesson(Guid subjectId, Guid lessonId) =>
        new(ConversationScopeKind.TeachLesson, RequireId(subjectId, nameof(subjectId)), RequireId(lessonId, nameof(lessonId)));

    public static ConversationScope From(Conversation conversation) => conversation.Mode switch
    {
        HavenMode.Chat when conversation.Kind == ConversationKind.Chat && conversation.ContainerId is { } groupId => ForChatGroup(groupId),
        HavenMode.Chat when conversation.Kind == ConversationKind.Chat => GeneralChat,
        HavenMode.Teach when conversation.Kind == ConversationKind.LessonChat && conversation.ContainerId is { } subjectId && conversation.LessonId is { } lessonId =>
            ForTeachLesson(subjectId, lessonId),
        HavenMode.Teach when conversation.Kind == ConversationKind.QuickChat => TeachQuickChat,
        _ => throw new ArgumentOutOfRangeException(nameof(conversation), "The conversation is not a scoped Chat or Teach conversation.")
    };

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

    private static Guid RequireId(Guid id, string parameterName) =>
        id == Guid.Empty ? throw new ArgumentException("The identifier cannot be empty.", parameterName) : id;
}

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
    public IReadOnlyDictionary<string, JsonElement> Metadata =>
        string.IsNullOrWhiteSpace(MetadataJson)
            ? new Dictionary<string, JsonElement>()
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(MetadataJson) ?? new();
}

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

public sealed record Lesson(
    Guid Id,
    Guid SubjectId,
    string TopicGroup,
    string Name,
    string StructureJson,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

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

public sealed record ConversationContextEntry(
    Guid Id,
    Guid ConversationId,
    ContextEntryKind Kind,
    string Title,
    string Content,
    string Evidence,
    DateTimeOffset CreatedAt);

public sealed record MacroDefinition(
    Guid Id,
    string Name,
    string Description,
    string Instruction,
    Guid? ContainerId,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

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

public sealed record ReleaseRiskReport(
    int Score,
    string Level,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<string> RiskAreas,
    IReadOnlyList<string> RecommendedTests,
    IReadOnlyList<string> CriticalFindings);

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

public sealed record ModelDescriptor(
    string Name,
    long SizeBytes,
    string Family,
    string ParameterSize,
    string Quantization,
    IReadOnlySet<ToolCapability> Capabilities,
    DateTimeOffset ModifiedAt)
{
    public bool Supports(ToolCapability capability) => Capabilities.Contains(capability);
    public string SizeLabel => FormatBytes(SizeBytes);
    public string EstimatedRamLabel => $"Approx. {FormatBytes((long)(SizeBytes * 1.25))} RAM";

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
}

public sealed record ActivePlugin(string Name, string IconKey, bool Persists, string Instructions = "");
public sealed record ActivePrompt(string Name, string IconKey, bool Persists, string Instructions = "");

public sealed record CapabilityRequirement(ToolCapability Capability, string Reason);

public sealed record CapabilityPreflightResult(
    bool IsCompatible,
    IReadOnlyList<CapabilityRequirement> Requirements,
    IReadOnlyList<CapabilityRequirement> Missing,
    ModelDescriptor? SuggestedModel)
{
    public static CapabilityPreflightResult Compatible(IReadOnlyList<CapabilityRequirement> requirements) =>
        new(true, requirements, Array.Empty<CapabilityRequirement>(), null);
}

public sealed record ToolActivity(
    Guid Id,
    string Title,
    string Detail,
    bool Succeeded,
    TimeSpan Duration,
    DateTimeOffset Timestamp,
    int LinesAdded = 0,
    int LinesRemoved = 0);

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

public sealed record ModeVersion(
    Guid Id,
    Guid ModeId,
    int Major,
    int Minor,
    int Patch,
    string ManifestJson,
    string Changelog,
    DateTimeOffset PublishedAt);

public sealed record ModePermissionGrant(
    Guid Id,
    Guid ModeId,
    PermissionMode FilePermission,
    PermissionMode CommandPermission,
    PermissionMode BrowserPermission,
    bool AllowDesktopTools,
    bool AllowFileSystemWrites,
    DateTimeOffset GrantedAt);

public sealed record ModePin(
    Guid Id,
    Guid ModeId,
    int SortOrder,
    DateTimeOffset PinnedAt);

public sealed record ModeUsage(
    Guid Id,
    Guid ModeId,
    DateOnly Date,
    int TurnCount,
    int CompletionCount,
    TimeSpan TotalDuration);

public sealed record SurfaceRun(
    Guid Id,
    Guid ConversationId,
    SurfaceKind Surface,
    string SurfaceKey,
    string? TargetModeKey,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    bool Succeeded);

public sealed record ActivityEvent(
    Guid Id,
    ActivityEventKind Kind,
    Guid? ConversationId,
    Guid? ModeId,
    string Summary,
    string DetailJson,
    DateTimeOffset Timestamp);

public sealed record ConversationMove(
    Guid Id,
    Guid ConversationId,
    Guid? FromModeId,
    Guid? ToModeId,
    ConversationPlacement FromPlacement,
    ConversationPlacement ToPlacement,
    string Reason,
    DateTimeOffset MovedAt);

public static class PlannerDefaults
{
    public static readonly Guid PersonalCollectionId = Guid.Parse("8f51f72f-3c1f-4a5f-a101-010000000001");
    public static readonly Guid CollegeCollectionId = Guid.Parse("8f51f72f-3c1f-4a5f-a101-010000000002");
    public static readonly Guid WorkCollectionId = Guid.Parse("8f51f72f-3c1f-4a5f-a101-010000000003");
    public static readonly Guid LocalCalendarId = Guid.Parse("8f51f72f-3c1f-4a5f-a101-020000000001");
}

public sealed record PlannerCollection(
    Guid Id,
    string Name,
    int SortOrder,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

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

public sealed record PlannerTaskCompletion(
    Guid Id,
    Guid TaskId,
    DateTimeOffset CompletedAt,
    DateTimeOffset? OccurrenceDueAt);

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

public sealed record CalendarConflict(
    Guid Id,
    Guid EventId,
    Guid AccountId,
    string HavenSnapshotJson,
    string ProviderSnapshotJson,
    DateTimeOffset DetectedAt,
    DateTimeOffset? ResolvedAt,
    CalendarConflictResolution? Resolution);

public sealed record PlannerProposedChange(
    Guid Id,
    PlannerChangeKind Kind,
    Guid? EntityId,
    string PayloadJson,
    string Description);

public sealed record PlannerChangeProposal(
    Guid Id,
    string Summary,
    IReadOnlyList<PlannerProposedChange> Changes,
    DateTimeOffset CreatedAt);

public sealed record PlannerReminder(
    PlannerReminderKind Kind,
    Guid EntityId,
    string Title,
    DateTimeOffset ReminderAt,
    DateTimeOffset OccurrenceAt);

public sealed record CalendarSyncCursor(
    Guid AccountId,
    Guid CalendarId,
    string? SyncCursor,
    string? DeltaLink,
    DateTimeOffset? WindowStart,
    DateTimeOffset? WindowEnd,
    DateTimeOffset? LastSyncedAt);

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
