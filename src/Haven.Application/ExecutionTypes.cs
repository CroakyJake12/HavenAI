/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/ExecutionTypes.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns TurnExecutionContext, ExecutionProfile, AssistantUiDirective, AssistantDirectiveKind, ModeSlot, ModeCatalogItem. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents turn execution context and keeps its related state and behavior together.
/// </summary>
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

/// <summary>
/// Represents execution profile and keeps its related state and behavior together.
/// </summary>
public sealed record ExecutionProfile(
    string ModeKey,
    string SystemPromptSuffix,
    IReadOnlySet<ToolCapability> RequiredCapabilities,
    IReadOnlySet<string> ToolAllowlist,
    IReadOnlySet<string> ToolDenylist,
    int MaxToolCalls = 24,
    int MaxTokens = 32768);

/// <summary>
/// Represents assistant ui directive and keeps its related state and behavior together.
/// </summary>
public sealed record AssistantUiDirective(
    AssistantDirectiveKind Kind,
    string? Content = null,
    string? ToolName = null,
    bool? Success = null);

/// <summary>
/// Lists the supported assistant directive kind values used to make state explicit and type-safe.
/// </summary>
public enum AssistantDirectiveKind { TextDelta, ReasoningDelta, ToolStart, ToolComplete, ActivityEvent, SurfaceSwitch }

/// <summary>
/// Represents mode slot and keeps its related state and behavior together.
/// </summary>
public sealed record ModeSlot(
    Guid ModeId,
    string Key,
    string Name,
    string IconKey,
    HavenMode BaseMode,
    int SortOrder,
    bool IsPinned,
    bool IsBuiltIn);

/// <summary>
/// Represents mode catalog item and keeps its related state and behavior together.
/// </summary>
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
