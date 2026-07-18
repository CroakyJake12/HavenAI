/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Core/ModelProviderModels.cs, in the dependency-free Core layer, where shared domain models and rules live.
 * What: This file owns ModelProviderKind, ModelRoutingMode, UsageMeasurementKind, ProviderConfiguration, ProviderHealthStatus, ProviderModelDescriptor, ModelRoutingPolicy, ModelRoutingRequest, ModelRoutingDecision, ModelUsageRecord. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: This code stays free of UI and storage dependencies so the same rule or data shape can be reused and tested everywhere.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

namespace Haven.Core;

/// <summary>
/// Lists the supported model provider kind values used to make state explicit and type-safe.
/// </summary>
public enum ModelProviderKind { Ollama = 0, OpenAI = 1, OpenAICompatible = 2, Anthropic = 3, Gemini = 4, OpenRouter = 5 }
/// <summary>
/// Lists the supported model routing mode values used to make state explicit and type-safe.
/// </summary>
public enum ModelRoutingMode { ManualFallback = 0, Automatic = 1 }
/// <summary>
/// Lists the supported usage measurement kind values used to make state explicit and type-safe.
/// </summary>
public enum UsageMeasurementKind { ProviderConfirmed = 0, LocallyCalculated = 1, Estimated = 2 }

/// <summary>
/// Represents provider configuration and keeps its related state and behavior together.
/// </summary>
public sealed record ProviderConfiguration(string Id, ModelProviderKind Kind, string DisplayName, string Endpoint, bool IsEnabled, bool IsLocal, bool AllowCloudFallback, IReadOnlyDictionary<string, string> Metadata, DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// Performs the local ollama step owned by this component.
    /// </summary>
    public static ProviderConfiguration LocalOllama(string endpoint) => new("ollama", ModelProviderKind.Ollama, "Ollama", endpoint, true, true, false, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), DateTimeOffset.UtcNow);
}

/// <summary>
/// Represents provider health status and keeps its related state and behavior together.
/// </summary>
public sealed record ProviderHealthStatus(string ProviderId, bool IsHealthy, string Message, TimeSpan Latency, DateTimeOffset CheckedAt);

/// <summary>
/// Represents provider model descriptor and keeps its related state and behavior together.
/// </summary>
public sealed record ProviderModelDescriptor(string ProviderId, bool IsLocal, ModelDescriptor Model, int? ContextWindow = null, string? DisplayName = null)
{
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => Model.Name;
    /// <summary>
    /// Gets or updates key, the bindable or domain state represented by this property.
    /// </summary>
    public string Key => $"{ProviderId}:{Model.Name}";
    /// <summary>
    /// Gets or updates label, the bindable or domain state represented by this property.
    /// </summary>
    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Model.Name : DisplayName;
    /// <summary>
    /// Gets or updates capabilities, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlySet<ToolCapability> Capabilities => Model.Capabilities;
    /// <summary>
    /// Performs the supports step owned by this component.
    /// </summary>
    public bool Supports(ToolCapability capability) => Model.Supports(capability);
    /// <summary>
    /// Performs the matches step owned by this component.
    /// </summary>
    public bool Matches(string? value) => !string.IsNullOrWhiteSpace(value) && (Name.Equals(value, StringComparison.OrdinalIgnoreCase) || Key.Equals(value, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Represents model routing policy and keeps its related state and behavior together.
/// </summary>
public sealed record ModelRoutingPolicy(ModelRoutingMode Mode, bool PreferLocal, bool AllowCloud, IReadOnlyList<string> PreferredModelKeys);
/// <summary>
/// Represents model routing request and keeps its related state and behavior together.
/// </summary>
public sealed record ModelRoutingRequest(ProviderModelDescriptor? SelectedModel, IReadOnlySet<ToolCapability> RequiredCapabilities, ModelRoutingPolicy Policy);
/// <summary>
/// Represents model routing decision and keeps its related state and behavior together.
/// </summary>
public sealed record ModelRoutingDecision(ProviderModelDescriptor Model, string Reason, bool UsedFallback);
/// <summary>
/// Represents model usage record and keeps its related state and behavior together.
/// </summary>
public sealed record ModelUsageRecord(string ProviderId, string ModelName, long? InputTokens, long? OutputTokens, long? CachedTokens, long? ReasoningTokens, decimal? Cost, string? Currency, UsageMeasurementKind Measurement, TimeSpan Latency, DateTimeOffset RecordedAt);
