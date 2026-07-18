/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/UsageAbstractions.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns IModelUsageCapture, IModelUsageRepository, IProviderPricingService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Defines the model usage capture contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IModelUsageCapture
{
    ProviderUsageSnapshot? ConsumeLastUsage();
}

/// <summary>
/// Defines the model usage repository contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IModelUsageRepository
{
    Task RecordAsync(ResponseUsageEntry entry, CancellationToken cancellationToken);
    Task<IReadOnlyList<ResponseUsageEntry>> GetConversationUsageAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<ResponseUsageEntry?> GetMessageUsageAsync(Guid messageId, CancellationToken cancellationToken);
    Task<ConversationUsageSummary> GetSummaryAsync(Guid conversationId, CancellationToken cancellationToken);
}

/// <summary>
/// Defines the provider pricing service contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IProviderPricingService
{
    ProviderPricing? ReadPricing(ProviderConfiguration configuration);
    decimal? CalculateCost(ProviderUsageSnapshot usage, ProviderPricing pricing);
}
