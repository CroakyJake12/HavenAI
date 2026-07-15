using Haven.Core;

namespace Haven.Application;

public interface IModelUsageCapture
{
    ProviderUsageSnapshot? ConsumeLastUsage();
}

public interface IModelUsageRepository
{
    Task RecordAsync(ResponseUsageEntry entry, CancellationToken cancellationToken);
    Task<IReadOnlyList<ResponseUsageEntry>> GetConversationUsageAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<ResponseUsageEntry?> GetMessageUsageAsync(Guid messageId, CancellationToken cancellationToken);
    Task<ConversationUsageSummary> GetSummaryAsync(Guid conversationId, CancellationToken cancellationToken);
}

public interface IProviderPricingService
{
    ProviderPricing? ReadPricing(ProviderConfiguration configuration);
    decimal? CalculateCost(ProviderUsageSnapshot usage, ProviderPricing pricing);
}
