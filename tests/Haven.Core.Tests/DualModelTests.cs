/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/DualModelTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns DualModelTests and its ScriptedOllama/RecordingSink fakes. The tests protect the
 *       dual-model runtime contract: independent sides, per-side failure capture, bounded labelled
 *       critique rounds, observable execution events, and shared-token cancellation.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>
/// Represents dual model tests and keeps its related state and behavior together.
/// </summary>
public sealed class DualModelTests
{
    /// <summary>
    /// Performs the both sides succeed in compare mode publishes completed side events step owned by this component.
    /// </summary>
    [Fact]
    public async Task BothSidesSucceedInCompareModePublishesCompletedSideEvents()
    {
        var ollama = new ScriptedOllama(request => request.Model == "alpha" ? "alpha-response" : "beta-response");
        var sink = new RecordingSink();
        var service = new DualModelService(ollama, sink);

        var run = await service.RunAsync("Write a haiku", "alpha", "beta", EffortLevel.Medium, CancellationToken.None);

        Assert.Equal(2, ollama.Calls);
        Assert.True(run.First.Succeeded);
        Assert.Null(run.First.Error);
        Assert.Equal("First", run.First.Label);
        Assert.Equal("alpha", run.First.ModelKey);
        Assert.Equal("alpha-response", run.First.Content);
        Assert.True(run.Second.Succeeded);
        Assert.Equal("Second", run.Second.Label);
        Assert.Equal("beta", run.Second.ModelKey);
        Assert.Equal("beta-response", run.Second.Content);
        Assert.DoesNotContain("Critique by", run.Second.Content);

        var sideEvents = sink.Events.Where(item => item.ActionType == ExecutionActionType.ModelExecution).ToList();
        Assert.Equal(2, sideEvents.Count);
        Assert.All(sideEvents, item => Assert.Equal(ExecutionOrigin.Haven, item.Origin));
        Assert.All(sideEvents, item => Assert.Equal(ExecutionActionStatus.Completed, item.Status));
        Assert.Contains(sideEvents, item => item.Name == "Dual model: alpha" && item.SafeMetadata!["side"] == "First" && item.SafeMetadata["model"] == "alpha");
        Assert.Contains(sideEvents, item => item.Name == "Dual model: beta" && item.SafeMetadata!["side"] == "Second" && item.SafeMetadata["model"] == "beta");
        Assert.DoesNotContain(sink.Events, item => item.ActionType == ExecutionActionType.ReasoningSummary);
    }

    /// <summary>
    /// Performs the first side failure keeps sibling result and reports honest error step owned by this component.
    /// </summary>
    [Fact]
    public async Task FirstSideFailureKeepsSiblingResultAndReportsHonestError()
    {
        var ollama = new ScriptedOllama(request => request.Model == "alpha"
            ? throw new HttpRequestException("model alpha exploded")
            : "beta finished");
        var sink = new RecordingSink();
        var service = new DualModelService(ollama, sink);

        var run = await service.RunAsync("Do the task", "alpha", "beta", EffortLevel.Medium, CancellationToken.None);

        Assert.False(run.First.Succeeded);
        Assert.Contains("model alpha exploded", run.First.Error);
        Assert.True(run.Second.Succeeded);
        Assert.Equal("beta finished", run.Second.Content);
        Assert.Equal(2, ollama.Calls);

        var firstEvent = Assert.Single(sink.Events, item => item.ActionType == ExecutionActionType.ModelExecution && item.Name == "Dual model: alpha");
        Assert.Equal(ExecutionActionStatus.Failed, firstEvent.Status);
        Assert.Equal("MODEL_EXECUTION_FAILED", firstEvent.Failure?.Code);
    }

    /// <summary>
    /// Performs the critique mode runs exactly one bounded labelled round step owned by this component.
    /// </summary>
    [Fact]
    public async Task CritiqueModeRunsExactlyOneBoundedLabelledRound()
    {
        var ollama = new ScriptedOllama(request => request.SystemPrompt == DualModelService.CritiqueSystemPrompt
            ? "The response ignores error handling."
            : request.Model == "alpha" ? "alpha answer" : "beta answer");
        var sink = new RecordingSink();
        var service = new DualModelService(ollama, sink);

        Assert.Equal(1, DualModelService.MaxCritiqueRounds);
        var run = await service.RunAsync("Ship the feature", "alpha", "beta", EffortLevel.Medium, DualModelMode.Critique, CancellationToken.None);

        Assert.Equal(3, ollama.Calls);
        var critiqueRequest = Assert.Single(ollama.Requests, request => request.SystemPrompt == DualModelService.CritiqueSystemPrompt);
        Assert.Equal("beta", critiqueRequest.Model);
        Assert.Contains("alpha answer", critiqueRequest.Messages.Single().Content);
        Assert.StartsWith("beta answer", run.Second.Content);
        Assert.Contains($"**Critique by beta:**", run.Second.Content);
        Assert.Contains("The response ignores error handling.", run.Second.Content);

        var critiqueEvent = Assert.Single(sink.Events, item => item.ActionType == ExecutionActionType.ReasoningSummary);
        Assert.Equal(ExecutionActionStatus.Completed, critiqueEvent.Status);
        Assert.Equal("Dual model critique: beta", critiqueEvent.Name);
        Assert.Equal("critique", critiqueEvent.SafeMetadata!["kind"]);
    }

    /// <summary>
    /// Performs the critique round failure keeps base response and emits failed event step owned by this component.
    /// </summary>
    [Fact]
    public async Task CritiqueRoundFailureKeepsBaseResponseAndEmitsFailedEvent()
    {
        var ollama = new ScriptedOllama(request => request.SystemPrompt == DualModelService.CritiqueSystemPrompt
            ? throw new HttpRequestException("critic unavailable")
            : request.Model == "alpha" ? "alpha answer" : "beta answer");
        var sink = new RecordingSink();
        var service = new DualModelService(ollama, sink);

        var run = await service.RunAsync("Ship the feature", "alpha", "beta", EffortLevel.Medium, DualModelMode.Critique, CancellationToken.None);

        Assert.Equal(3, ollama.Calls);
        Assert.False(run.Second.Content.Contains("Critique by", StringComparison.Ordinal));
        var critiqueEvent = Assert.Single(sink.Events, item => item.ActionType == ExecutionActionType.ReasoningSummary);
        Assert.Equal(ExecutionActionStatus.Failed, critiqueEvent.Status);
        Assert.Equal("CRITIQUE_ROUND_FAILED", critiqueEvent.Failure?.Code);
    }

    /// <summary>
    /// Performs the cancelled token propagates without invoking models step owned by this component.
    /// </summary>
    [Fact]
    public async Task CancelledTokenPropagatesWithoutInvokingModels()
    {
        var ollama = new ScriptedOllama(_ => "should never be reached");
        var service = new DualModelService(ollama);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.RunAsync("prompt", "alpha", "beta", EffortLevel.Medium, cancelled.Token));

        Assert.Equal(0, ollama.Calls);
    }

    /// <summary>
    /// Represents scripted ollama and keeps its related state and behavior together.
    /// </summary>
    private sealed class ScriptedOllama(Func<OllamaChatRequest, string> respond) : IOllamaClient
    {
        /// <summary>
        /// Gets or updates requests, the bindable or domain state represented by this property.
        /// </summary>
        public List<OllamaChatRequest> Requests { get; } = [];
        /// <summary>
        /// Gets or updates calls, the bindable or domain state represented by this property.
        /// </summary>
        public int Calls { get; private set; }

        /// <summary>
        /// Reports whether available async applies to the current state.
        /// </summary>
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
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
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            Requests.Add(request);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
        /// <summary>
        /// Performs chat with tools asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
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
