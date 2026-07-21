/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/CallVoiceLatencyTests.cs, in the automated test suite.
 * What: Protects the low-latency and conversational defaults used by Haven Call.
 * Why: Live voice regressions are easy to miss in ordinary text-chat tests.
 */

using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class CallVoiceLatencyTests
{
    [Fact]
    public void CallDefaultsPreferFastConversationalReplies()
    {
        var options = new CallStartOptions(Model());

        Assert.Equal(EffortLevel.Low, options.Effort);
        Assert.NotNull(options.SystemPrompt);
        Assert.Contains("conversationally", options.SystemPrompt!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SentenceChunkerEmitsCompletedShortSentenceImmediately()
    {
        var chunker = new SentenceChunker();

        var chunks = chunker.Append("Let me think. ");

        Assert.Equal(["Let me think."], chunks);
    }

    [Fact]
    public void SentenceChunkerUsesNaturalFirstPhraseForUnpunctuatedOutput()
    {
        var chunker = new SentenceChunker();
        var streamedText = string.Join(" ", Enumerable.Repeat("quick", 30));

        var chunks = chunker.Append(streamedText);

        var first = Assert.Single(chunks);
        Assert.InRange(first.Length, 48, 96);
        Assert.NotEmpty(chunker.Flush());
    }

    [Fact]
    public async Task GreetingReplyBypassesOllamaInference()
    {
        var inner = new RecordingOllamaClient(["slow model reply"]);
        var client = new CallOptimizedOllamaClient(inner);
        var request = Request("Hello!");
        var output = new List<string>();

        await foreach (var delta in client.StreamChatAsync(request, CancellationToken.None))
            output.Add(delta);

        Assert.Equal(0, inner.StreamCalls);
        Assert.Contains("Hi!", string.Concat(output), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubstantiveCallCapsContextAtFourThousandTokens()
    {
        var inner = new RecordingOllamaClient(["Answer."]);
        var client = new CallOptimizedOllamaClient(inner);
        var output = new List<string>();

        await foreach (var delta in client.StreamChatAsync(Request("Explain gravity."), CancellationToken.None))
            output.Add(delta);

        Assert.Equal(1, inner.StreamCalls);
        Assert.Equal(4096, inner.LastRequest?.Options?.ContextLimit);
        Assert.Equal("Answer.", string.Concat(output));
    }

    private static OllamaChatRequest Request(string text) => new(
        "qwen-test",
        [new OllamaMessage("user", text)],
        EffortLevel.Low,
        "Test call prompt.",
        Options: new GenerationOptions(0.65, 32768, 0));

    private static ModelDescriptor Model() => new(
        "qwen-test",
        1,
        "qwen",
        "test",
        "test",
        new HashSet<ToolCapability> { ToolCapability.Text },
        DateTimeOffset.UtcNow);

    private sealed class RecordingOllamaClient(IReadOnlyList<string> chunks) : IOllamaClient
    {
        public int StreamCalls { get; private set; }
        public OllamaChatRequest? LastRequest { get; private set; }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([Model()]);

        public async IAsyncEnumerable<string> StreamChatAsync(
            OllamaChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            StreamCalls++;
            LastRequest = request;
            foreach (var chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return chunk;
                await Task.Yield();
            }
        }

        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(string.Concat(chunks));

        public Task<OllamaToolResponse> ChatWithToolsAsync(
            OllamaToolRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(string.Concat(chunks), []));
    }
}
