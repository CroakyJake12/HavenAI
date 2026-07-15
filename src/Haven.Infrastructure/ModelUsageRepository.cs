using System.Collections.Concurrent;
using System.Globalization;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class ProviderUsageCaptureBuffer : IModelUsageCapture
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<ProviderUsageSnapshot>> _usage = new(StringComparer.OrdinalIgnoreCase);

    public void Set(ProviderUsageSnapshot usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        _usage.GetOrAdd(Key(usage.ProviderId, usage.ModelName), static _ => new ConcurrentQueue<ProviderUsageSnapshot>()).Enqueue(usage);
    }

    public ProviderUsageSnapshot? Consume(string providerId, string modelName)
    {
        var key = Key(providerId, modelName);
        if (!_usage.TryGetValue(key, out var queue)) return null;
        var values = new List<ProviderUsageSnapshot>();
        while (queue.TryDequeue(out var value)) values.Add(value);
        _usage.TryRemove(key, out _);
        return Aggregate(values);
    }

    public ProviderUsageSnapshot? ConsumeLastUsage()
    {
        foreach (var key in _usage.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            if (!_usage.TryGetValue(key, out var queue)) continue;
            var values = new List<ProviderUsageSnapshot>();
            while (queue.TryDequeue(out var value)) values.Add(value);
            _usage.TryRemove(key, out _);
            if (Aggregate(values) is { } aggregate) return aggregate;
        }
        return null;
    }

    private static ProviderUsageSnapshot? Aggregate(IReadOnlyList<ProviderUsageSnapshot> values)
    {
        if (values.Count == 0) return null;
        var first = values[0];
        return new ProviderUsageSnapshot(
            first.ProviderId,
            first.ModelName,
            SumNullable(values.Select(item => item.InputTokens)),
            SumNullable(values.Select(item => item.OutputTokens)),
            SumNullable(values.Select(item => item.CachedTokens)),
            SumNullable(values.Select(item => item.ReasoningTokens)),
            values.All(item => item.Measurement == UsageMeasurementKind.ProviderConfirmed)
                ? UsageMeasurementKind.ProviderConfirmed
                : values.All(item => item.Measurement == UsageMeasurementKind.LocallyCalculated)
                    ? UsageMeasurementKind.LocallyCalculated
                    : UsageMeasurementKind.Estimated,
            values.Max(item => item.CapturedAt));
    }

    private static long? SumNullable(IEnumerable<long?> values)
    {
        var materialized = values.ToArray();
        return materialized.Any(value => value is not null) ? materialized.Sum(value => value ?? 0) : null;
    }

    private static string Key(string providerId, string modelName) => providerId.Trim().ToLowerInvariant() + "\n" + modelName.Trim().ToLowerInvariant();
}

public sealed class ProviderPricingService : IProviderPricingService
{
    public ProviderPricing? ReadPricing(ProviderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        decimal? input = ReadDecimal(configuration.Metadata, "input-price-per-million");
        decimal? output = ReadDecimal(configuration.Metadata, "output-price-per-million");
        decimal? cached = ReadDecimal(configuration.Metadata, "cached-price-per-million");
        decimal? reasoning = ReadDecimal(configuration.Metadata, "reasoning-price-per-million");
        if (input is null && output is null && cached is null && reasoning is null) return null;
        var currency = configuration.Metadata.TryGetValue("pricing-currency", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim().ToUpperInvariant()
            : "USD";
        return new ProviderPricing(configuration.Id, input, output, cached, reasoning, currency, configuration.UpdatedAt);
    }

    public decimal? CalculateCost(ProviderUsageSnapshot usage, ProviderPricing pricing)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(pricing);
        decimal total = 0;
        var hasPrice = false;
        Add(usage.InputTokens, pricing.InputPerMillion);
        Add(usage.OutputTokens, pricing.OutputPerMillion);
        Add(usage.CachedTokens, pricing.CachedPerMillion);
        Add(usage.ReasoningTokens, pricing.ReasoningPerMillion);
        return hasPrice ? decimal.Round(total, 8, MidpointRounding.AwayFromZero) : null;

        void Add(long? tokens, decimal? rate)
        {
            if (tokens is null || rate is null) return;
            hasPrice = true;
            total += tokens.Value * rate.Value / 1_000_000m;
        }
    }

    private static decimal? ReadDecimal(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : null;
}

public sealed class ModelUsageRepository(ISqliteConnectionFactory factory) : IModelUsageRepository
{
    public async Task RecordAsync(ResponseUsageEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await ConversationProductionSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO response_usage(
                id,conversation_id,message_id,provider_id,model_name,input_tokens,output_tokens,cached_tokens,reasoning_tokens,
                cost,currency,measurement,latency_ms,recorded_at)
            VALUES($id,$conversationId,$messageId,$providerId,$modelName,$inputTokens,$outputTokens,$cachedTokens,$reasoningTokens,
                $cost,$currency,$measurement,$latencyMs,$recordedAt);
            """;
        command.Parameters.AddWithValue("$id", entry.Id.ToString());
        command.Parameters.AddWithValue("$conversationId", entry.ConversationId.ToString());
        command.Parameters.AddWithValue("$messageId", (object?)entry.MessageId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$providerId", entry.Usage.ProviderId);
        command.Parameters.AddWithValue("$modelName", entry.Usage.ModelName);
        command.Parameters.AddWithValue("$inputTokens", (object?)entry.Usage.InputTokens ?? DBNull.Value);
        command.Parameters.AddWithValue("$outputTokens", (object?)entry.Usage.OutputTokens ?? DBNull.Value);
        command.Parameters.AddWithValue("$cachedTokens", (object?)entry.Usage.CachedTokens ?? DBNull.Value);
        command.Parameters.AddWithValue("$reasoningTokens", (object?)entry.Usage.ReasoningTokens ?? DBNull.Value);
        command.Parameters.AddWithValue("$cost", entry.Usage.Cost is null ? DBNull.Value : entry.Usage.Cost.Value.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$currency", (object?)entry.Usage.Currency ?? DBNull.Value);
        command.Parameters.AddWithValue("$measurement", (int)entry.Usage.Measurement);
        command.Parameters.AddWithValue("$latencyMs", Math.Max(0L, (long)entry.Usage.Latency.TotalMilliseconds));
        command.Parameters.AddWithValue("$recordedAt", entry.Usage.RecordedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ResponseUsageEntry>> GetConversationUsageAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        await ConversationProductionSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM response_usage WHERE conversation_id=$conversationId ORDER BY recorded_at;";
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString());
        return await ReadAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ResponseUsageEntry?> GetMessageUsageAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await ConversationProductionSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM response_usage WHERE message_id=$messageId ORDER BY recorded_at DESC LIMIT 1;";
        command.Parameters.AddWithValue("$messageId", messageId.ToString());
        return (await ReadAsync(command, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
    }

    public async Task<ConversationUsageSummary> GetSummaryAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var entries = await GetConversationUsageAsync(conversationId, cancellationToken).ConfigureAwait(false);
        var currencies = entries.Select(item => item.Usage.Currency).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        decimal? cost = entries.Any(item => item.Usage.Cost is not null) && currencies.Length <= 1
            ? entries.Sum(item => item.Usage.Cost ?? 0)
            : null;
        return new ConversationUsageSummary(
            entries.Sum(item => item.Usage.InputTokens ?? 0),
            entries.Sum(item => item.Usage.OutputTokens ?? 0),
            entries.Sum(item => item.Usage.CachedTokens ?? 0),
            entries.Sum(item => item.Usage.ReasoningTokens ?? 0),
            cost,
            currencies.Length == 1 ? currencies[0] : null,
            entries.Select(item => item.Usage.Measurement).ToHashSet(),
            entries.Count);
    }

    private static async Task<IReadOnlyList<ResponseUsageEntry>> ReadAsync(Microsoft.Data.Sqlite.SqliteCommand command, CancellationToken cancellationToken)
    {
        var result = new List<ResponseUsageEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var costOrdinal = reader.GetOrdinal("cost");
            var currencyOrdinal = reader.GetOrdinal("currency");
            var messageOrdinal = reader.GetOrdinal("message_id");
            result.Add(new ResponseUsageEntry(
                Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
                Guid.Parse(reader.GetString(reader.GetOrdinal("conversation_id"))),
                reader.IsDBNull(messageOrdinal) ? null : Guid.Parse(reader.GetString(messageOrdinal)),
                new ModelUsageRecord(
                    reader.GetString(reader.GetOrdinal("provider_id")),
                    reader.GetString(reader.GetOrdinal("model_name")),
                    ReadNullableInt64(reader, "input_tokens"),
                    ReadNullableInt64(reader, "output_tokens"),
                    ReadNullableInt64(reader, "cached_tokens"),
                    ReadNullableInt64(reader, "reasoning_tokens"),
                    reader.IsDBNull(costOrdinal) ? null : decimal.Parse(reader.GetString(costOrdinal), CultureInfo.InvariantCulture),
                    reader.IsDBNull(currencyOrdinal) ? null : reader.GetString(currencyOrdinal),
                    (UsageMeasurementKind)reader.GetInt32(reader.GetOrdinal("measurement")),
                    TimeSpan.FromMilliseconds(reader.GetInt64(reader.GetOrdinal("latency_ms"))),
                    DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("recorded_at")), CultureInfo.InvariantCulture))));
        }
        return result;
    }

    private static long? ReadNullableInt64(Microsoft.Data.Sqlite.SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }
}
