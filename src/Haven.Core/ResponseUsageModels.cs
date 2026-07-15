namespace Haven.Core;

public sealed record ProviderUsageSnapshot(
    string ProviderId,
    string ModelName,
    long? InputTokens,
    long? OutputTokens,
    long? CachedTokens,
    long? ReasoningTokens,
    UsageMeasurementKind Measurement,
    DateTimeOffset CapturedAt);

public sealed record ResponseUsageEntry(
    Guid Id,
    Guid ConversationId,
    Guid? MessageId,
    ModelUsageRecord Usage);

public sealed record ProviderPricing(
    string ProviderId,
    decimal? InputPerMillion,
    decimal? OutputPerMillion,
    decimal? CachedPerMillion,
    decimal? ReasoningPerMillion,
    string Currency,
    DateTimeOffset UpdatedAt);

public sealed record ConversationUsageSummary(
    long InputTokens,
    long OutputTokens,
    long CachedTokens,
    long ReasoningTokens,
    decimal? Cost,
    string? Currency,
    IReadOnlySet<UsageMeasurementKind> Measurements,
    int Responses);
