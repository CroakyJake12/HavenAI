using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class MultipleResponseTests
{
    [Fact]
    public async Task Runs_three_distinct_models_and_preserves_each_identity()
    {
        var ollama = new ScriptedOllama(request => request.Model + "-response");
        var sink = new RecordingSink();
        var service = new MultipleResponseService(ollama, sink);

        var run = await service.RunAsync("prompt", ["alpha", "beta", "gamma"], EffortLevel.Medium, CancellationToken.None);

        Assert.Equal(3, ollama.Calls);
        Assert.Equal(["alpha", "beta", "gamma"], run.Responses.Select(response => response.ModelKey).ToArray());
        Assert.All(run.Responses, response => Assert.True(response.Succeeded));
        Assert.Equal("gamma-response", run.Responses[2].Content);
        Assert.Equal(3, sink.Events.Count(item => item.ActionType == ExecutionActionType.ModelExecution));
    }

    [Fact]
    public async Task Starts_selected_models_concurrently()
    {
        var ollama = new BlockingOllama(expectedStarts: 3);
        var service = new MultipleResponseService(ollama);

        var runTask = service.RunAsync(
            "prompt",
            ["alpha", "beta", "gamma"],
            EffortLevel.Medium,
            CancellationToken.None);

        await ollama.AllStarted.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(3, ollama.StartedCount);
        ollama.Release();
        var run = await runTask;

        Assert.Equal(3, run.Responses.Count);
        Assert.All(run.Responses, response => Assert.True(response.Succeeded));
    }

    [Fact]
    public async Task One_model_failure_does_not_discard_successful_siblings()
    {
        var ollama = new ScriptedOllama(request => request.Model == "beta"
            ? throw new HttpRequestException("beta unavailable")
            : request.Model + " ok");
        var sink = new RecordingSink();
        var service = new MultipleResponseService(ollama, sink);

        var run = await service.RunAsync("prompt", ["alpha", "beta", "gamma", "delta"], EffortLevel.High, CancellationToken.None);

        Assert.Equal(4, run.Responses.Count);
        Assert.Equal(3, run.Responses.Count(response => response.Succeeded));
        var failed = Assert.Single(run.Responses, response => !response.Succeeded);
        Assert.Equal("beta", failed.ModelKey);
        Assert.Contains("beta unavailable", failed.Error);
        var failureEvent = Assert.Single(sink.Events, item => item.Status == ExecutionActionStatus.Failed);
        Assert.Equal("MODEL_EXECUTION_FAILED", failureEvent.Failure?.Code);
    }

    [Fact]
    public async Task Requires_at_least_two_distinct_models()
    {
        var service = new MultipleResponseService(new ScriptedOllama(_ => "unused"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RunAsync("prompt", ["alpha", "ALPHA"], EffortLevel.Medium, CancellationToken.None));
    }

    [Fact]
    public async Task Cancelled_token_stops_before_model_invocation()
    {
        var ollama = new ScriptedOllama(_ => "unused");
        var service = new MultipleResponseService(ollama);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RunAsync("prompt", ["alpha", "beta"], EffortLevel.Medium, cancelled.Token));
        Assert.Equal(0, ollama.Calls);
    }

    private sealed class BlockingOllama(int expectedStarts) : IOllamaClient
    {
        private readonly TaskCompletionSource<bool> _allStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _startedCount;

        public Task AllStarted => _allStarted.Task;
        public int StartedCount => Volatile.Read(ref _startedCount);
        public void Release() => _release.TrySetResult(true);

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { await Task.CompletedTask; yield break; }
        public async Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _startedCount) == expectedStarts) _allStarted.TrySetResult(true);
            await _release.Task.WaitAsync(cancellationToken);
            return request.Model + "-response";
        }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ScriptedOllama(Func<OllamaChatRequest, string> respond) : IOllamaClient
    {
        public int Calls { get; private set; }
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { await Task.CompletedTask; yield break; }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(respond(request));
        }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingSink : IExecutionEventSink
    {
        public List<ExecutionEvent> Events { get; } = [];
        public bool TryPublish(ExecutionEvent executionEvent) { Events.Add(executionEvent); return true; }
    }
}
