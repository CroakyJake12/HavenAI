using System.Text.Json;

namespace Haven.Core;

public enum GenUiEventType
{
    ActionInvoked,
    OptionSelected,
    ToggleChanged,
    SliderChanged,
    TextSubmitted,
    SearchSubmitted,
    FormSubmitted,
    StepCompleted,
    AnswerRecorded,
    ConfidenceRecorded,
    ItemSelected,
    ItemOpened,
    ItemCreated,
    ItemEdited,
    ItemDeleted,
    ItemDuplicated,
    ItemReordered,
    DragCompleted,
    ViewportChanged,
    FilterChanged,
    ApprovalAccepted,
    ApprovalDenied,
    RetryRequested,
    CancelRequested,
    ValidationRequested,
    ActivityCompleted
}

public enum GenUiEventSource { User, App, Agent, Capability, Runtime }
public enum GenUiRouteKind { Local, App, Agent, Capability, External }
public enum GenUiActionStatus { Completed, PermissionRequired, Denied, Unavailable, PlatformRestricted, Failed, Cancelled }
public enum GenUiPatchOperation { Add, Replace, Remove, Append }
public enum GenUiTemplateScale { Primitive, Pattern, FullExperience, Composition }
public enum GenUiTemplateMaturity { Foundation, Preview, Production }
public enum GenUiAgentInteractionMode { None, Optional, Required }
public enum GenUiStateOwnership { Local, Thread, App, External }

/// <summary>Stable origin identity shared by directives, events, results, and patches.</summary>
public sealed record GenUiOrigin(
    Guid ThreadId,
    string AppKey,
    Guid? TemplateId,
    Guid InstanceId);

/// <summary>A meaningful semantic interaction; presentation-only events never enter this contract.</summary>
public sealed record GenUiEvent(
    Guid EventId,
    GenUiEventType EventType,
    DateTimeOffset Timestamp,
    GenUiOrigin Origin,
    string ComponentId,
    string ActionId,
    string? ResourceId,
    JsonElement? PreviousValue,
    JsonElement? Value,
    JsonElement StructuredPayload,
    GenUiEventSource Source,
    string InteractionContext);

/// <summary>Routes one semantic action without embedding executable code in generated UI.</summary>
public sealed record GenUiActionBinding(
    string ActionId,
    GenUiRouteKind Route,
    string TargetKey,
    CapabilityRiskClass RiskClass,
    bool RequiresPermission);

/// <summary>A node in the trusted HavenUI vocabulary.</summary>
public sealed record GenUiComponent(
    string ComponentId,
    string ComponentType,
    IReadOnlyDictionary<string, JsonElement> Properties,
    IReadOnlyList<GenUiActionBinding> Actions,
    IReadOnlyList<GenUiComponent> Children);

/// <summary>Versioned structured UI state for one thread/App/template instance.</summary>
public sealed record GenUiDocument(
    Guid DocumentId,
    int ContractVersion,
    GenUiOrigin Origin,
    string Title,
    string AccentKey,
    GenUiComponent Root,
    IReadOnlyDictionary<string, JsonElement> State,
    DateTimeOffset UpdatedAt);

/// <summary>Targets one component property or one state key without rebuilding the document.</summary>
public sealed record GenUiStatePatch(
    Guid PatchId,
    Guid InstanceId,
    GenUiPatchOperation Operation,
    string TargetId,
    string Path,
    JsonElement? Value,
    DateTimeOffset Timestamp);

/// <summary>Structured result returned to the exact originating experience.</summary>
public sealed record GenUiActionResult(
    Guid ResultId,
    Guid EventId,
    GenUiOrigin Origin,
    string ComponentId,
    string ActionId,
    GenUiActionStatus Status,
    string Summary,
    JsonElement StructuredResult,
    IReadOnlyList<GenUiStatePatch> Patches,
    DateTimeOffset Timestamp);

/// <summary>Discoverable metadata. CanonicalImplementation resolves to trusted runtime code.</summary>
public sealed record GenUiTemplateDefinition(
    Guid Id,
    string Key,
    string Version,
    string Name,
    string Description,
    string Category,
    IReadOnlyList<string> Tags,
    string CanonicalImplementation,
    GenUiTemplateScale Scale,
    IReadOnlyList<string> RecommendedApps,
    IReadOnlyList<string> CompatibleApps,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Outputs,
    IReadOnlyList<GenUiEventType> EmittedEvents,
    IReadOnlyList<string> ConfigurableProperties,
    IReadOnlyList<string> DataRequirements,
    IReadOnlyList<string> SupportedInteractions,
    IReadOnlyList<string> RequiredHavenUiPrimitives,
    IReadOnlyList<string> RequiredAppServices,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<ToolCapability> RequiredModelCapabilities,
    bool RequiresNetwork,
    bool SupportsOffline,
    CapabilityPlatform Platforms,
    string AccessibilitySummary,
    bool SupportsPersistence,
    bool SupportsThreadScope,
    bool SupportsUserApps,
    bool SupportsMiniApps,
    bool SupportsEmbedding,
    GenUiAgentInteractionMode AgentInteraction,
    bool IsDeterministicWithoutModel,
    GenUiStateOwnership StateOwnership,
    GenUiTemplateMaturity Maturity,
    bool IsBuiltIn,
    bool IsEnabled,
    DateTimeOffset UpdatedAt);
