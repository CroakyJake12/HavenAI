using System.Net;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class ResilientProviderRoutingModelClientTests
{
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

    private static ResilientProviderRoutingModelClient CreateClient(
        IReadOnlyList<IModelProvider> providers,
        IReadOnlyDictionary<string, ProviderConfiguration> configurations)
    {
        var registry = new ModelProviderRegistry(providers);
        var localClient = new FakeLocalClient();
        var primary = new ProviderRoutingModelClient(localClient, registry);
        return new ResilientProviderRoutingModelClient(primary, registry, new FakeConfigurationStore(configurations));
    }

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

    private sealed class FakeConfigurationStore(IReadOnlyDictionary<string, ProviderConfiguration> values) : IProviderConfigurationStore
    {
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderConfiguration>>(values.Values.ToArray());
        public Task<ProviderConfiguration?> GetAsync(string providerId, CancellationToken cancellationToken) =>
            Task.FromResult(values.TryGetValue(providerId, out var value) ? value : null);
        public Task UpsertAsync(ProviderConfiguration configuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string providerId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeLocalClient : IOllamaClient
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { await Task.CompletedTask; yield break; }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) => throw new HttpRequestException("local unavailable");
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) => throw new HttpRequestException("local unavailable");
        public Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteModelAsync(string model, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeProvider : IModelProvider
    {
        private readonly string _model;
        private readonly string? _completion;
        private readonly string? _partial;
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

        public static FakeProvider Failing(string id, string model, bool isLocal = false) => new(id, model, null, null, true, isLocal);
        public static FakeProvider Completing(string id, string model, string completion) => new(id, model, completion, null, false, false);
        public static FakeProvider PartialThenFailing(string id, string model, string partial) => new(id, model, null, partial, true, false);

        public int CompletionCalls { get; private set; }
        public int StreamCalls { get; private set; }
        public string Id { get; }
        public string DisplayName => Id;
        public ModelProviderKind Kind => Id == "ollama" ? ModelProviderKind.Ollama : ModelProviderKind.OpenAICompatible;
        public bool IsLocal { get; }
        public bool CanManageModels => false;

        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderHealthStatus(Id, !_fail, _fail ? "unavailable" : "ready", TimeSpan.Zero, DateTimeOffset.UtcNow));

        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken)
        {
            var capabilities = new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Streaming };
            var model = new ModelDescriptor(_model, 0, Id, string.Empty, string.Empty, capabilities, DateTimeOffset.UtcNow);
            return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([new ProviderModelDescriptor(Id, IsLocal, model, 32_000, _model)]);
        }

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

        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
        {
            CompletionCalls++;
            if (_fail) throw new HttpRequestException("temporary", null, HttpStatusCode.ServiceUnavailable);
            return Task.FromResult(_completion ?? string.Empty);
        }

        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
