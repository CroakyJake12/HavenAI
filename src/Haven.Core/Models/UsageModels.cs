// Token usage snapshots, pricing, and conversation-level usage summaries.

namespace Haven.Core;

/// <summary>
/// Represents provider usage snapshot and keeps its related state and behavior together.
/// </summary>
public sealed record ProviderUsageSnapshot(
    string ProviderId,
    string ModelName,
    long? InputTokens,
    long? OutputTokens,
    long? CachedTokens,
    long? ReasoningTokens,
    UsageMeasurementKind Measurement,
    DateTimeOffset CapturedAt);

/// <summary>
/// Represents response usage entry and keeps its related state and behavior together.
/// </summary>
public sealed record ResponseUsageEntry(
    Guid Id,
    Guid ConversationId,
    Guid? MessageId,
    ModelUsageRecord Usage);

/// <summary>
/// Represents provider pricing and keeps its related state and behavior together.
/// </summary>
public sealed record ProviderPricing(
    string ProviderId,
    decimal? InputPerMillion,
    decimal? OutputPerMillion,
    decimal? CachedPerMillion,
    decimal? ReasoningPerMillion,
    string Currency,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Represents conversation usage summary and keeps its related state and behavior together.
/// </summary>
public sealed record ConversationUsageSummary(
    long InputTokens,
    long OutputTokens,
    long CachedTokens,
    long ReasoningTokens,
    decimal? Cost,
    string? Currency,
    IReadOnlySet<UsageMeasurementKind> Measurements,
    int Responses);
