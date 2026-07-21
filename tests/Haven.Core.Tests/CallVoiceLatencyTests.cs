/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/CallVoiceLatencyTests.cs, in the automated test suite.
 * What: Protects the low-latency and conversational defaults used by Haven Call.
 * Why: Live voice regressions are easy to miss in ordinary text-chat tests.
 */

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

    private static ModelDescriptor Model() => new(
        "qwen-test",
        1,
        "qwen",
        "test",
        "test",
        new HashSet<ToolCapability> { ToolCapability.Text },
        DateTimeOffset.UtcNow);
}
