/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/TrainingJudgeTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns TrainingJudgeTests and its FakeJudgeInvoker/RecordingSink fakes. The tests protect
 *       the judge scoring contract: strict JSON parsing with code-fence stripping, 0..100 clamping,
 *       honest null failures with Failed events, and cancellation propagation.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>
/// Represents training judge tests and keeps its related state and behavior together.
/// </summary>
public sealed class TrainingJudgeTests
{
    /// <summary>
    /// Performs the strict json is parsed clamped and emits judge event step owned by this component.
    /// </summary>
    [Fact]
    public async Task StrictJsonIsParsedClampedAndEmitsJudgeEvent()
    {
        var invoker = new FakeJudgeInvoker((_, _) => """
            {"correctness":150,"taskCompletion":-20,"instructionAdherence":80,"codeQuality":"90","efficiency":70,"overall":88.5,"reasoning":"Solid work overall."}
            """);
        var sink = new RecordingSink();
        var service = new JudgeService(invoker, sink);

        var score = await service.ScoreAttemptAsync("judge-model", "Build a calculator", "# Attempt report\nIt built.", CancellationToken.None);

        Assert.NotNull(score);
        Assert.Equal(88.5, score.OverallPercent);
        Assert.Equal("judge-model", score.JudgeModel);
        Assert.Equal("Solid work overall.", score.ReasoningSummary);
        Assert.Equal(100, score.CriteriaScores["correctness"]);
        Assert.Equal(0, score.CriteriaScores["taskCompletion"]);
        Assert.Equal(80, score.CriteriaScores["instructionAdherence"]);
        Assert.Equal(90, score.CriteriaScores["codeQuality"]);
        Assert.Equal(70, score.CriteriaScores["efficiency"]);

        var prompt = invoker.LastPrompt!;
        Assert.Contains("correctness", prompt);
        Assert.Contains("taskCompletion", prompt);
        Assert.Contains("instructionAdherence", prompt);
        Assert.Contains("codeQuality", prompt);
        Assert.Contains("efficiency", prompt);
        Assert.Contains("Build a calculator", prompt);
        Assert.Contains("# Attempt report", prompt);

        var judged = Assert.Single(sink.Events, item => item.ActionType == ExecutionActionType.JudgeEvaluated);
        Assert.Equal(ExecutionActionStatus.Completed, judged.Status);
        Assert.Equal("judge-model", judged.SafeMetadata!["judgeModel"]);
        Assert.Equal("88.5", judged.SafeMetadata["overall"]);
    }

    /// <summary>
    /// Performs the fenced reply with prose still parses step owned by this component.
    /// </summary>
    [Fact]
    public async Task FencedReplyWithProseStillParses()
    {
        var invoker = new FakeJudgeInvoker((_, _) =>
            "Here is my evaluation:\n```json\n{\"correctness\":60,\"taskCompletion\":60,\"instructionAdherence\":60,\"codeQuality\":60,\"efficiency\":60,\"overall\":60,\"reasoning\":\"Adequate.\"}\n```\nThanks!");
        var service = new JudgeService(invoker);

        var score = await service.ScoreAttemptAsync("judge-model", "Task", "Report", CancellationToken.None);

        Assert.NotNull(score);
        Assert.Equal(60, score.OverallPercent);
        Assert.All(JudgeService.Criteria, criterion => Assert.Equal(60, score.CriteriaScores[criterion]));
    }

    /// <summary>
    /// Performs the missing criterion falls back to documented overall step owned by this component.
    /// </summary>
    [Fact]
    public async Task MissingCriterionFallsBackToDocumentedOverall()
    {
        var invoker = new FakeJudgeInvoker((_, _) => "{\"correctness\":50,\"taskCompletion\":50,\"instructionAdherence\":50,\"codeQuality\":50,\"overall\":72,\"reasoning\":\"r\"}");
        var service = new JudgeService(invoker);

        var score = await service.ScoreAttemptAsync("judge-model", "Task", "Report", CancellationToken.None);

        Assert.NotNull(score);
        Assert.Equal(72, score.OverallPercent);
        Assert.Equal(72, score.CriteriaScores["efficiency"]);
        Assert.Equal(5, score.CriteriaScores.Count);
    }

    /// <summary>
    /// Performs the unparseable reply returns null and emits failed event step owned by this component.
    /// </summary>
    [Fact]
    public async Task UnparseableReplyReturnsNullAndEmitsFailedEvent()
    {
        var invoker = new FakeJudgeInvoker((_, _) => "I cannot score this attempt.");
        var sink = new RecordingSink();
        var service = new JudgeService(invoker, sink);

        var score = await service.ScoreAttemptAsync("judge-model", "Task", "Report", CancellationToken.None);

        Assert.Null(score);
        var failed = Assert.Single(sink.Events, item => item.ActionType == ExecutionActionType.JudgeEvaluated);
        Assert.Equal(ExecutionActionStatus.Failed, failed.Status);
        Assert.Equal("JUDGE_PARSE_FAILED", failed.Failure?.Code);
    }

    /// <summary>
    /// Performs the missing overall returns null instead of fabricating step owned by this component.
    /// </summary>
    [Fact]
    public async Task MissingOverallReturnsNullInsteadOfFabricating()
    {
        var invoker = new FakeJudgeInvoker((_, _) => "{\"correctness\":50,\"reasoning\":\"partial\"}");
        var service = new JudgeService(invoker);

        var score = await service.ScoreAttemptAsync("judge-model", "Task", "Report", CancellationToken.None);

        Assert.Null(score);
    }

    /// <summary>
    /// Performs the invoker failure returns null and emits failed event step owned by this component.
    /// </summary>
    [Fact]
    public async Task InvokerFailureReturnsNullAndEmitsFailedEvent()
    {
        var invoker = new FakeJudgeInvoker((_, _) => throw new HttpRequestException("provider offline"));
        var sink = new RecordingSink();
        var service = new JudgeService(invoker, sink);

        var score = await service.ScoreAttemptAsync("judge-model", "Task", "Report", CancellationToken.None);

        Assert.Null(score);
        var failed = Assert.Single(sink.Events, item => item.ActionType == ExecutionActionType.JudgeEvaluated);
        Assert.Equal(ExecutionActionStatus.Failed, failed.Status);
        Assert.Equal("JUDGE_INVOCATION_FAILED", failed.Failure?.Code);
        Assert.Contains("provider offline", failed.Failure!.Message);
    }

    /// <summary>
    /// Performs the cancelled token propagates without invoking judge step owned by this component.
    /// </summary>
    [Fact]
    public async Task CancelledTokenPropagatesWithoutInvokingJudge()
    {
        var invoker = new FakeJudgeInvoker((_, _) => "unused");
        var service = new JudgeService(invoker);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ScoreAttemptAsync("judge-model", "Task", "Report", cancelled.Token));

        Assert.Equal(0, invoker.Calls);
    }

    /// <summary>
    /// Represents fake judge invoker and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeJudgeInvoker(Func<string, string, string> respond) : IJudgeModelInvoker
    {
        /// <summary>
        /// Gets or updates calls, the bindable or domain state represented by this property.
        /// </summary>
        public int Calls { get; private set; }
        /// <summary>
        /// Gets or updates last prompt, the bindable or domain state represented by this property.
        /// </summary>
        public string? LastPrompt { get; private set; }

        /// <summary>
        /// Performs complete asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> CompleteAsync(string modelKey, string prompt, CancellationToken cancellationToken)
        {
            Calls++;
            LastPrompt = prompt;
            return Task.FromResult(respond(modelKey, prompt));
        }
    }

    /// <summary>
    /// Represents recording sink and keeps its related state and behavior together.
    /// </summary>
    private sealed class RecordingSink : IExecutionEventSink
    {
        /// <summary>
        /// Gets or updates events, the bindable or domain state represented by this property.
        /// </summary>
        public List<ExecutionEvent> Events { get; } = [];
        /// <summary>
        /// Attempts to try publish and reports whether the outcome succeeded without throwing.
        /// </summary>
        public bool TryPublish(ExecutionEvent executionEvent) { Events.Add(executionEvent); return true; }
    }
}
