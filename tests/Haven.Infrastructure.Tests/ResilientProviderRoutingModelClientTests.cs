/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/ResilientProviderRoutingModelClientTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns ResilientProviderRoutingModelClientTests, FakeConfigurationStore, FakeLocalClient, FakeProvider. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Net;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents resilient provider routing model client tests and keeps its related state and behavior together.
/// </summary>
public sealed class ResilientProviderRoutingModelClientTests
{
    /// <summary>
    /// Performs the completion falls back after recoverable failure before output step owned by this component.
    /// </summary>
    [Fact]
    public async Task CompletionFallsBackAfterRecoverableFailureBeforeOutput()
    {
        var first = FakeProvider.Failing("first", "model-a");
        var second = FakeProvider.Completing("second", "model-b", "fallback result");
        var client = CreateClient(
            [first, second],
            new Dictionary<string, ProviderConfiguration>(StringComparer.OrdinalIgnoreCase)
            {
                ["first"] = Configuration("first", allowCloudFallback: true, "second:model-b"),
                ["second"] = Configuration("second", allowCloudFallback: true, string.Empty)
            });

        var result = await client.CompleteAsync(
            new OllamaChatRequest("first:model-a", [new OllamaMessage("user", "hello")], EffortLevel.Medium),
            CancellationToken.None);

        Assert.Equal("fallback result", result);
        Assert.Equal(1, first.CompletionCalls);
        Assert.Equal(1, second.CompletionCalls);
    }

    /// <summary>
    /// Performs the streaming does not replay after any chunk was emitted step owned by this component.
    /// </summary>
    [Fact]
    public async Task StreamingDoesNotReplayAfterAnyChunkWasEmitted()
    {
        var first = FakeProvider.PartialThenFailing("first", "model-a", "partial");
        var second = FakeProvider.Completing("second", "model-b", "should not run");
        var client = CreateClient(
            [first, second],
            new Dictionary<string, ProviderConfiguration>(StringComparer.OrdinalIgnoreCase)
            {
                ["first"] = Configuration("first", allowCloudFallback: true, "second:model-b"),
                ["second"] = Configuration("second", allowCloudFallback: true, string.Empty)
            });
        var chunks = new List<string>();

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var chunk in client.StreamChatAsync(
                               new OllamaChatRequest("first:model-a", [new OllamaMessage("user", "hello")], EffortLevel.Medium),
                               CancellationToken.None))
                chunks.Add(chunk);
        });

        Assert.Equal(["partial"], chunks);
        Assert.Equal(0, second.StreamCalls);
    }

    /// <summary>
    /// Performs the local selection does not cross into cloud without explicit permission step owned by this component.
    /// </summary>
    [Fact]
    public async Task LocalSelectionDoesNotCrossIntoCloudWithoutExplicitPermission()
    {
        var local = FakeProvider.Failing("ollama", "local-model", isLocal: true);
        var cloud = FakeProvider.Completing("second", "model-b", "cloud");
        var client = CreateClient(
            [local, cloud],
            new Dictionary<string, ProviderConfiguration>(StringComparer.OrdinalIgnoreCase)
            {
                ["ollama"] = new ProviderConfiguration(
                    "ollama", ModelProviderKind.Ollama, "Ollama", "http://127.0.0.1:11434/", true, true, false,
                    new Dictionary<string, string>(), DateTimeOffset.UtcNow),
                ["second"] = Configuration("second", allowCloudFallback: true, string.Empty)
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(
            new OllamaChatRequest("local-model", [new OllamaMessage("user", "hello")], EffortLevel.Medium),
            CancellationToken.None));
        Assert.Equal(0, cloud.CompletionCalls);
    }

    /// <summary>
    /// Creates client with the invariants required by its callers.
    /// </summary>
    private static ResilientProviderRoutingModelClient CreateClient(
        IReadOnlyList<IModelProvider> providers,
        IReadOnlyDictionary<string, ProviderConfiguration> configurations)
    {
        var registry = new ModelProviderRegistry(providers);
        var localClient = new FakeLocalClient();
        var primary = new ProviderRoutingModelClient(localClient, registry);
        return new ResilientProviderRoutingModelClient(primary, registry, new FakeConfigurationStore(configurations));
    }

    /// <summary>
    /// Performs the configuration step owned by this component.
    /// </summary>
    private static ProviderConfiguration Configuration(string id, bool allowCloudFallback, string chain)
    {
        var metadata = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(chain)) metadata["fallback-chain"] = chain;
        return new ProviderConfiguration(
            id,
            ModelProviderKind.OpenAICompatible,
            id,
            "https://example.test/",
            true,
            false,
            allowCloudFallback,
            metadata,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Represents fake configuration store and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeConfigurationStore(IReadOnlyDictionary<string, ProviderConfiguration> values) : IProviderConfigurationStore
    {
        /// <summary>
        /// Retrieves all async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderConfiguration>>(values.Values.ToArray());
        /// <summary>
        /// Retrieves async for the current operation.
        /// </summary>
        public Task<ProviderConfiguration?> GetAsync(string providerId, CancellationToken cancellationToken) =>
            Task.FromResult(values.TryGetValue(providerId, out var value) ? value : null);
        /// <summary>
        /// Performs upsert asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task UpsertAsync(ProviderConfiguration configuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        /// <summary>
        /// Performs delete asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task DeleteAsync(string providerId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>
    /// Represents fake local client and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeLocalClient : IOllamaClient
    {
        /// <summary>
        /// Reports whether available async applies to the current state.
        /// </summary>
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        /// <summary>
        /// Retrieves models async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        /// <summary>
        /// Performs stream chat asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { await Task.CompletedTask; yield break; }
        /// <summary>
        /// Performs complete asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) => throw new HttpRequestException("local unavailable");
        /// <summary>
        /// Performs chat with tools asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) => throw new HttpRequestException("local unavailable");
        /// <summary>
        /// Performs pull model asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
        /// <summary>
        /// Performs delete model asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task DeleteModelAsync(string model, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>
    /// Represents fake provider and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeProvider : IModelProvider
    {
        /// <summary>
        /// Stores model locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private readonly string _model;
        /// <summary>
        /// Stores completion locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private readonly string? _completion;
        /// <summary>
        /// Stores partial locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private readonly string? _partial;
        /// <summary>
        /// Stores fail locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private readonly bool _fail;

        private FakeProvider(string id, string model, string? completion, string? partial, bool fail, bool isLocal)
        {
            Id = id;
            _model = model;
            _completion = completion;
            _partial = partial;
            _fail = fail;
            IsLocal = isLocal;
        }

        /// <summary>
        /// Performs the failing step owned by this component.
        /// </summary>
        public static FakeProvider Failing(string id, string model, bool isLocal = false) => new(id, model, null, null, true, isLocal);
        /// <summary>
        /// Performs the completing step owned by this component.
        /// </summary>
        public static FakeProvider Completing(string id, string model, string completion) => new(id, model, completion, null, false, false);
        /// <summary>
        /// Performs the partial then failing step owned by this component.
        /// </summary>
        public static FakeProvider PartialThenFailing(string id, string model, string partial) => new(id, model, null, partial, true, false);

        /// <summary>
        /// Gets or updates completion calls, the bindable or domain state represented by this property.
        /// </summary>
        public int CompletionCalls { get; private set; }
        /// <summary>
        /// Gets or updates stream calls, the bindable or domain state represented by this property.
        /// </summary>
        public int StreamCalls { get; private set; }
        /// <summary>
        /// Gets or updates id, the bindable or domain state represented by this property.
        /// </summary>
        public string Id { get; }
        /// <summary>
        /// Gets or updates display name, the bindable or domain state represented by this property.
        /// </summary>
        public string DisplayName => Id;
        /// <summary>
        /// Gets or updates kind, the bindable or domain state represented by this property.
        /// </summary>
        public ModelProviderKind Kind => Id == "ollama" ? ModelProviderKind.Ollama : ModelProviderKind.OpenAICompatible;
        /// <summary>
        /// Reports whether local applies to the current state.
        /// </summary>
        public bool IsLocal { get; }
        /// <summary>
        /// Reports whether manage models applies to the current state.
        /// </summary>
        public bool CanManageModels => false;

        /// <summary>
        /// Performs check health asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderHealthStatus(Id, !_fail, _fail ? "unavailable" : "ready", TimeSpan.Zero, DateTimeOffset.UtcNow));

        /// <summary>
        /// Retrieves models async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken)
        {
            var capabilities = new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Streaming };
            var model = new ModelDescriptor(_model, 0, Id, string.Empty, string.Empty, capabilities, DateTimeOffset.UtcNow);
            return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([new ProviderModelDescriptor(Id, IsLocal, model, 32_000, _model)]);
        }

        /// <summary>
        /// Performs stream chat asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public async IAsyncEnumerable<string> StreamChatAsync(
            OllamaChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            StreamCalls++;
            await Task.Yield();
            if (_partial is not null) yield return _partial;
            if (_fail) throw new HttpRequestException("temporary", null, HttpStatusCode.ServiceUnavailable);
            if (_completion is not null) yield return _completion;
        }

        /// <summary>
        /// Performs complete asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
        {
            CompletionCalls++;
            if (_fail) throw new HttpRequestException("temporary", null, HttpStatusCode.ServiceUnavailable);
            return Task.FromResult(_completion ?? string.Empty);
        }

        /// <summary>
        /// Performs chat with tools asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
