/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Core/ResponseUsageModels.cs, in the dependency-free Core layer, where shared domain models and rules live.
 * What: This file owns ProviderUsageSnapshot, ResponseUsageEntry, ProviderPricing, ConversationUsageSummary. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: This code stays free of UI and storage dependencies so the same rule or data shape can be reused and tested everywhere.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

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
