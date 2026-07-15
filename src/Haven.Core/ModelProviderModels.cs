namespace Haven.Core;

public enum ModelProviderKind { Ollama = 0, OpenAI = 1, OpenAICompatible = 2, Anthropic = 3, Gemini = 4, OpenRouter = 5 }
public enum ModelRoutingMode { ManualFallback = 0, Automatic = 1 }
public enum UsageMeasurementKind { ProviderConfirmed = 0, LocallyCalculated = 1, Estimated = 2 }

public sealed record ProviderConfiguration(string Id, ModelProviderKind Kind, string DisplayName, string Endpoint, bool IsEnabled, bool IsLocal, bool AllowCloudFallback, IReadOnlyDictionary<string, string> Metadata, DateTimeOffset UpdatedAt)
{
    public static ProviderConfiguration LocalOllama(string endpoint) => new("ollama", ModelProviderKind.Ollama, "Ollama", endpoint, true, true, false, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), DateTimeOffset.UtcNow);
}

public sealed record ProviderHealthStatus(string ProviderId, bool IsHealthy, string Message, TimeSpan Latency, DateTimeOffset CheckedAt);

public sealed record ProviderModelDescriptor(string ProviderId, bool IsLocal, ModelDescriptor Model, int? ContextWindow = null, string? DisplayName = null)
{
    public string Name => Model.Name;
    public string Key => $"{ProviderId}:{Model.Name}";
    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Model.Name : DisplayName;
    public IReadOnlySet<ToolCapability> Capabilities => Model.Capabilities;
    public bool Supports(ToolCapability capability) => Model.Supports(capability);
    public bool Matches(string? value) => !string.IsNullOrWhiteSpace(value) && (Name.Equals(value, StringComparison.OrdinalIgnoreCase) || Key.Equals(value, StringComparison.OrdinalIgnoreCase));
}

public sealed record ModelRoutingPolicy(ModelRoutingMode Mode, bool PreferLocal, bool AllowCloud, IReadOnlyList<string> PreferredModelKeys);
public sealed record ModelRoutingRequest(ProviderModelDescriptor? SelectedModel, IReadOnlySet<ToolCapability> RequiredCapabilities, ModelRoutingPolicy Policy);
public sealed record ModelRoutingDecision(ProviderModelDescriptor Model, string Reason, bool UsedFallback);
public sealed record ModelUsageRecord(string ProviderId, string ModelName, long? InputTokens, long? OutputTokens, long? CachedTokens, long? ReasoningTokens, decimal? Cost, string? Currency, UsageMeasurementKind Measurement, TimeSpan Latency, DateTimeOffset RecordedAt);
