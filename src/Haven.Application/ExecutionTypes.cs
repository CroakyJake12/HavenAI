using Haven.Core;

namespace Haven.Application;

public sealed record TurnExecutionContext(
    Guid ConversationId,
    HavenMode BaseMode,
    string ModeKey,
    string? WorkspaceRoot,
    ModelDescriptor Model,
    EffortLevel Effort,
    IReadOnlyCollection<ActivePlugin> Plugins,
    IReadOnlyCollection<ActivePrompt> Prompts,
    PermissionMode FilePermission,
    PermissionMode CommandPermission,
    PermissionMode BrowserPermission,
    string? AgentName,
    string? AgentInstructions,
    DuoMode DuoMode,
    string? ProjectContext,
    string? ProjectInstructions,
    GenerationOptions? GenerationOptions = null);

public sealed record ExecutionProfile(
    string ModeKey,
    string SystemPromptSuffix,
    IReadOnlySet<ToolCapability> RequiredCapabilities,
    IReadOnlySet<string> ToolAllowlist,
    IReadOnlySet<string> ToolDenylist,
    int MaxToolCalls = 24,
    int MaxTokens = 32768);

public sealed record AssistantUiDirective(
    AssistantDirectiveKind Kind,
    string? Content = null,
    string? ToolName = null,
    bool? Success = null);

public enum AssistantDirectiveKind { TextDelta, ReasoningDelta, ToolStart, ToolComplete, ActivityEvent, SurfaceSwitch }

public sealed record ModeSlot(
    Guid ModeId,
    string Key,
    string Name,
    string IconKey,
    HavenMode BaseMode,
    int SortOrder,
    bool IsPinned,
    bool IsBuiltIn);

public sealed record ModeCatalogItem(
    Guid Id,
    string Key,
    string Name,
    string Description,
    string IconKey,
    HavenMode BaseMode,
    ModeSource Source,
    ModeInstallState InstallState,
    string Author,
    string Version,
    int UseCount,
    DateTimeOffset LastUsedAt,
    bool IsPinned);
