/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/ModelUsageRepositoryTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns ModelUsageRepositoryTests, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents model usage repository tests and keeps its related state and behavior together.
/// </summary>
public sealed class ModelUsageRepositoryTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the capture buffer aggregates multi call tool usage by provider and model step owned by this component.
    /// </summary>
    [Fact]
    public void CaptureBufferAggregatesMultiCallToolUsageByProviderAndModel()
    {
        var buffer = new ProviderUsageCaptureBuffer();
        buffer.Set(new ProviderUsageSnapshot("openai", "gpt-test", 100, 20, 5, 3, UsageMeasurementKind.ProviderConfirmed, DateTimeOffset.UtcNow));
        buffer.Set(new ProviderUsageSnapshot("openai", "gpt-test", 120, 30, 7, 4, UsageMeasurementKind.ProviderConfirmed, DateTimeOffset.UtcNow.AddSeconds(1)));

        var usage = Assert.IsType<ProviderUsageSnapshot>(buffer.Consume("openai", "gpt-test"));
        Assert.Equal(220, usage.InputTokens);
        Assert.Equal(50, usage.OutputTokens);
        Assert.Equal(12, usage.CachedTokens);
        Assert.Equal(7, usage.ReasoningTokens);
        Assert.Equal(UsageMeasurementKind.ProviderConfirmed, usage.Measurement);
        Assert.Null(buffer.Consume("openai", "gpt-test"));
    }

    /// <summary>
    /// Performs the pricing uses only explicit metadata rates step owned by this component.
    /// </summary>
    [Fact]
    public void PricingUsesOnlyExplicitMetadataRates()
    {
        var service = new ProviderPricingService();
        var configuration = new ProviderConfiguration(
            "openai", ModelProviderKind.OpenAI, "OpenAI", "https://example.test/", true, false, false,
            new Dictionary<string, string>
            {
                ["input-price-per-million"] = "2.5",
                ["output-price-per-million"] = "10",
                ["cached-price-per-million"] = "1.25",
                ["pricing-currency"] = "gbp"
            }, DateTimeOffset.UtcNow);
        var pricing = Assert.IsType<ProviderPricing>(service.ReadPricing(configuration));
        var cost = service.CalculateCost(new ProviderUsageSnapshot(
            "openai", "gpt-test", 1_000_000, 500_000, 200_000, null,
            UsageMeasurementKind.ProviderConfirmed, DateTimeOffset.UtcNow), pricing);

        Assert.Equal("GBP", pricing.Currency);
        Assert.Equal(7.75m, cost);
        Assert.Null(service.ReadPricing(configuration with { Metadata = new Dictionary<string, string>() }));
    }

    /// <summary>
    /// Performs the usage records round trip and summarise without mixing currencies step owned by this component.
    /// </summary>
    [Fact]
    public async Task UsageRecordsRoundTripAndSummariseWithoutMixingCurrencies()
    {
        var database = new SqliteDatabase(_paths);
        await new ConversationProductionDatabase(database).InitializeAsync(CancellationToken.None);
        var conversations = new ConversationRepository(database);
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Usage", null, null, false, false, now, now);
        await conversations.UpsertConversationAsync(conversation, CancellationToken.None);
        var repository = new ModelUsageRepository(database);
        var firstMessage = Guid.NewGuid();
        var secondMessage = Guid.NewGuid();

        await repository.RecordAsync(new ResponseUsageEntry(Guid.NewGuid(), conversation.Id, firstMessage,
            new ModelUsageRecord("openai", "one", 100, 50, 10, 5, 0.002m, "GBP", UsageMeasurementKind.ProviderConfirmed, TimeSpan.FromSeconds(2), now)), CancellationToken.None);
        await repository.RecordAsync(new ResponseUsageEntry(Guid.NewGuid(), conversation.Id, secondMessage,
            new ModelUsageRecord("ollama", "local", 80, 40, null, null, null, null, UsageMeasurementKind.Estimated, TimeSpan.FromSeconds(1), now.AddSeconds(3))), CancellationToken.None);

        var summary = await repository.GetSummaryAsync(conversation.Id, CancellationToken.None);
        Assert.Equal(180, summary.InputTokens);
        Assert.Equal(90, summary.OutputTokens);
        Assert.Equal(0.002m, summary.Cost);
        Assert.Equal("GBP", summary.Currency);
        Assert.Contains(UsageMeasurementKind.ProviderConfirmed, summary.Measurements);
        Assert.Contains(UsageMeasurementKind.Estimated, summary.Measurements);
        Assert.Equal(firstMessage, (await repository.GetMessageUsageAsync(firstMessage, CancellationToken.None))?.MessageId);
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _paths.Dispose();

    /// <summary>
    /// Represents test paths and keeps its related state and behavior together.
    /// </summary>
    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-usage-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "test.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
        }

        /// <summary>
        /// Gets or updates data directory, the bindable or domain state represented by this property.
        /// </summary>
        public string DataDirectory { get; }
        /// <summary>
        /// Gets or updates database path, the bindable or domain state represented by this property.
        /// </summary>
        public string DatabasePath { get; }
        /// <summary>
        /// Gets or updates browser profile directory, the bindable or domain state represented by this property.
        /// </summary>
        public string BrowserProfileDirectory { get; }
        /// <summary>
        /// Gets or updates attachments directory, the bindable or domain state represented by this property.
        /// </summary>
        public string AttachmentsDirectory { get; }
        /// <summary>
        /// Gets or updates logs directory, the bindable or domain state represented by this property.
        /// </summary>
        public string LogsDirectory { get; }
        /// <summary>
        /// Gets or updates legacy state path, the bindable or domain state represented by this property.
        /// </summary>
        public string LegacyStatePath { get; }
        /// <summary>
        /// Performs the dispose step owned by this component.
        /// </summary>
        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch (IOException) { } }
    }
}
