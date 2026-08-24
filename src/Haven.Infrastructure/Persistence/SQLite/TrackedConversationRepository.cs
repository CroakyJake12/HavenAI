/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/UsageTrackingConversationRepository.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns UsageTrackingConversationRepository. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents usage tracking conversation repository and keeps its related state and behavior together.
/// </summary>
public sealed class UsageTrackingConversationRepository(
    ConversationRepository inner,
    IModelUsageRepository usageRepository,
    IProviderConfigurationStore configurations,
    IProviderPricingService pricingService,
    ProviderUsageCaptureBuffer usageCapture) : IConversationRepository
{
    /// <summary>
    /// Stores turn started locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, long> _turnStarted = new();

    /// <summary>
    /// Retrieves recent async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) =>
        inner.GetRecentAsync(mode, limit, cancellationToken);

    /// <summary>
    /// Retrieves recent in scope async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<Conversation>> GetRecentInScopeAsync(ConversationScope scope, int limit, CancellationToken cancellationToken) =>
        inner.GetRecentInScopeAsync(scope, limit, cancellationToken);
    public Task<IReadOnlyList<Conversation>> GetBySpaceAsync(Guid spaceId, int limit, CancellationToken cancellationToken) =>
        inner.GetBySpaceAsync(spaceId, limit, cancellationToken);
    public Task DetachSpaceAsync(Guid spaceId, CancellationToken cancellationToken) =>
        inner.DetachSpaceAsync(spaceId, cancellationToken);

    /// <summary>
    /// Retrieves archived async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<Conversation>> GetArchivedAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) =>
        inner.GetArchivedAsync(mode, limit, cancellationToken);

    /// <summary>
    /// Retrieves async for the current operation.
    /// </summary>
    public Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken) => inner.GetAsync(id, cancellationToken);
    /// <summary>
    /// Retrieves messages async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken) => inner.GetMessagesAsync(conversationId, cancellationToken);
    /// <summary>
    /// Retrieves context messages async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<ChatMessage>> GetContextMessagesAsync(Guid conversationId, CancellationToken cancellationToken) => inner.GetContextMessagesAsync(conversationId, cancellationToken);
    /// <summary>
    /// Performs upsert conversation asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task UpsertConversationAsync(Conversation conversation, CancellationToken cancellationToken) => inner.UpsertConversationAsync(conversation, cancellationToken);

    /// <summary>
    /// Performs add message asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken)
    {
        if (message.Role == MessageRole.User) _turnStarted[message.ConversationId] = Stopwatch.GetTimestamp();
        await inner.AddMessageAsync(message, cancellationToken).ConfigureAwait(false);
        if (message.Role != MessageRole.Assistant) return;

        try
        {
            var expectedProvider = ResolveProviderId(message.ModelName);
            var expectedModel = ResolveModelName(message.ModelName);
            var captured = usageCapture.Consume(expectedProvider, expectedModel);
            var providerId = captured?.ProviderId ?? expectedProvider;
            var modelName = captured?.ModelName ?? expectedModel;
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
            Debug.WriteLine("Usage recording failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Permanently removes one message from the underlying conversation store.
    /// </summary>
    public Task DeleteMessageAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken) =>
        inner.DeleteMessageAsync(conversationId, messageId, cancellationToken);

    /// <summary>
    /// Performs mark messages compacted asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task MarkMessagesCompactedAsync(Guid conversationId, IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken) =>
        inner.MarkMessagesCompactedAsync(conversationId, messageIds, cancellationToken);

    /// <summary>
    /// Retrieves context entries async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<ConversationContextEntry>> GetContextEntriesAsync(Guid conversationId, CancellationToken cancellationToken) =>
        inner.GetContextEntriesAsync(conversationId, cancellationToken);

    /// <summary>
    /// Performs add context entry asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task AddContextEntryAsync(ConversationContextEntry entry, CancellationToken cancellationToken) => inner.AddContextEntryAsync(entry, cancellationToken);
    /// <summary>
    /// Performs delete conversation asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken) => inner.DeleteConversationAsync(id, cancellationToken);

    /// <summary>
    /// Performs the estimate tokens step owned by this component.
    /// </summary>
    private static long EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var bytes = Encoding.UTF8.GetByteCount(text);
        return Math.Max(1, (long)Math.Ceiling(bytes / 4d));
    }

    /// <summary>
    /// Performs the resolve provider id step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the resolve model name step owned by this component.
    /// </summary>
    private static string ResolveModelName(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return "unknown";
        var provider = ResolveProviderId(model);
        return provider == "ollama" || !model.StartsWith(provider + ":", StringComparison.OrdinalIgnoreCase)
            ? model
            : model[(provider.Length + 1)..];
    }
}
