using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class UsageTrackingConversationRepository(
    ConversationRepository inner,
    IModelUsageRepository usageRepository,
    IProviderConfigurationStore configurations,
    IProviderPricingService pricingService,
    ProviderUsageCaptureBuffer usageCapture) : IConversationRepository
{
    private readonly ConcurrentDictionary<Guid, long> _turnStarted = new();

    public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) =>
        inner.GetRecentAsync(mode, limit, cancellationToken);

    public Task<IReadOnlyList<Conversation>> GetRecentInScopeAsync(ConversationScope scope, int limit, CancellationToken cancellationToken) =>
        inner.GetRecentInScopeAsync(scope, limit, cancellationToken);

    public Task<IReadOnlyList<Conversation>> GetArchivedAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) =>
        inner.GetArchivedAsync(mode, limit, cancellationToken);

    public Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken) => inner.GetAsync(id, cancellationToken);
    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken) => inner.GetMessagesAsync(conversationId, cancellationToken);
    public Task<IReadOnlyList<ChatMessage>> GetContextMessagesAsync(Guid conversationId, CancellationToken cancellationToken) => inner.GetContextMessagesAsync(conversationId, cancellationToken);
    public Task UpsertConversationAsync(Conversation conversation, CancellationToken cancellationToken) => inner.UpsertConversationAsync(conversation, cancellationToken);

    public async Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken)
    {
        if (message.Role == MessageRole.User) _turnStarted[message.ConversationId] = Stopwatch.GetTimestamp();
        await inner.AddMessageAsync(message, cancellationToken).ConfigureAwait(false);
        if (message.Role != MessageRole.Assistant) return;

        try
        {
            var captured = usageCapture.ConsumeLastUsage();
            var providerId = captured?.ProviderId ?? ResolveProviderId(message.ModelName);
            var modelName = captured?.ModelName ?? ResolveModelName(message.ModelName);
            var history = captured is null
                ? await inner.GetContextMessagesAsync(message.ConversationId, cancellationToken).ConfigureAwait(false)
                : [];
            var inputTokens = captured?.InputTokens ?? EstimateTokens(string.Join('\n', history.Where(item => item.Id != message.Id).Select(item => item.Content)));
            var outputTokens = captured?.OutputTokens ?? EstimateTokens(message.Content);
            var measurement = captured?.Measurement ?? UsageMeasurementKind.Estimated;
            var latency = _turnStarted.TryRemove(message.ConversationId, out var started)
                ? Stopwatch.GetElapsedTime(started)
                : TimeSpan.Zero;

            decimal? cost = null;
            string? currency = null;
            if (!providerId.Equals("ollama", StringComparison.OrdinalIgnoreCase))
            {
                var configuration = await configurations.GetAsync(providerId, cancellationToken).ConfigureAwait(false);
                var pricing = configuration is null ? null : pricingService.ReadPricing(configuration);
                if (pricing is not null)
                {
                    var pricedUsage = captured ?? new ProviderUsageSnapshot(
                        providerId, modelName, inputTokens, outputTokens, null, null, measurement, DateTimeOffset.UtcNow);
                    cost = pricingService.CalculateCost(pricedUsage, pricing);
                    currency = cost is null ? null : pricing.Currency;
                }
            }

            await usageRepository.RecordAsync(new ResponseUsageEntry(
                Guid.NewGuid(),
                message.ConversationId,
                message.Id,
                new ModelUsageRecord(
                    providerId,
                    modelName,
                    inputTokens,
                    outputTokens,
                    captured?.CachedTokens,
                    captured?.ReasoningTokens,
                    cost,
                    currency,
                    measurement,
                    latency,
                    DateTimeOffset.UtcNow)), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            // Usage accounting must never make a completed response disappear.
            Debug.WriteLine("Usage recording failed: " + ex.Message);
        }
    }

    public Task MarkMessagesCompactedAsync(Guid conversationId, IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken) =>
        inner.MarkMessagesCompactedAsync(conversationId, messageIds, cancellationToken);

    public Task<IReadOnlyList<ConversationContextEntry>> GetContextEntriesAsync(Guid conversationId, CancellationToken cancellationToken) =>
        inner.GetContextEntriesAsync(conversationId, cancellationToken);

    public Task AddContextEntryAsync(ConversationContextEntry entry, CancellationToken cancellationToken) => inner.AddContextEntryAsync(entry, cancellationToken);
    public Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken) => inner.DeleteConversationAsync(id, cancellationToken);

    private static long EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var bytes = Encoding.UTF8.GetByteCount(text);
        return Math.Max(1, (long)Math.Ceiling(bytes / 4d));
    }

    private static string ResolveProviderId(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return "ollama";
        var separator = model.IndexOf(':');
        if (separator <= 0) return "ollama";
        var prefix = model[..separator];
        return prefix is "openai" or "anthropic" or "gemini" or "openrouter" or "openai-compatible" or "ollama"
            ? prefix
            : "ollama";
    }

    private static string ResolveModelName(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return "unknown";
        var provider = ResolveProviderId(model);
        return provider == "ollama" || !model.StartsWith(provider + ":", StringComparison.OrdinalIgnoreCase)
            ? model
            : model[(provider.Length + 1)..];
    }
}
