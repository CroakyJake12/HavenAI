/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/DualModel/DualModelService.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns DualModelSide, DualModelRun, DualModelMode and DualModelService — a side-by-side
 *       two-model runtime that executes both models independently, keeps one failure from killing the
 *       other side, and (in Critique mode) runs exactly MaxCritiqueRounds bounded follow-up rounds.
 * How: Both sides are issued concurrently over IOllamaClient.CompleteAsync with the caller's
 *      CancellationToken; per-side errors are captured on the DualModelSide instead of failing the run.
 *      Cancellation of the shared token cancels both sides and propagates as OperationCanceledException.
 *      Every side publishes an ExecutionEvent (ActionType ModelExecution, Origin Haven) whose status is the
 *      completion status; a successful critique round additionally publishes a ReasoningSummary event.
 * Why: Honest dual-model comparison requires independent outcomes, visible failures, and bounded critique.
 * Maintenance: Preserve per-side error capture, the shared cancellation token flow, event metadata keys
 *              (side/model/kind), and the bounded critique round constant when changing this file.
 */

using System.Diagnostics;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// One independently executed side of a dual-model run. A failed side carries its safe error text
/// instead of throwing so the sibling side's result survives.
/// </summary>
public sealed record DualModelSide(
    string Label,
    string ModelKey,
    string Content,
    bool Succeeded,
    string? Error,
    TimeSpan Duration);

/// <summary>
/// The complete outcome of one dual-model run: the original prompt plus both side results. In Critique
/// mode the second side's Content has the labelled critique appended after it.
/// </summary>
public sealed record DualModelRun(Guid Id, string Prompt, DualModelSide First, DualModelSide Second);

/// <summary>
/// Lists the supported dual-model run modes: Compare returns both raw responses; Critique appends one
/// bounded critique of the first response produced by the second model.
/// </summary>
public enum DualModelMode
{
    Compare = 0,
    Critique = 1
}

/// <summary>
/// Executes two models side by side over one prompt. Both models run independently so one failure never
/// kills the sibling side; failures surface as per-side <see cref="DualModelSide.Error"/> values and
/// Failed-status execution events. Cancelling the shared token cancels both sides.
/// </summary>
public sealed class DualModelService(IOllamaClient client, IExecutionEventSink? events = null)
{
    /// <summary>Hard bound on critique follow-up rounds; the runtime never chains critiques.</summary>
    public const int MaxCritiqueRounds = 1;

    /// <summary>System prompt for the bounded critique round so tests and callers can identify it.</summary>
    public const string CritiqueSystemPrompt =
        "You are a rigorous but fair reviewer. Critique the provided response concisely and constructively. " +
        "Do not answer the original task yourself.";

    private const string ComponentId = "dual-model";
    private const int DetailCharacterLimit = 400;

    /// <summary>
    /// Runs both models over the prompt in Compare mode and returns both side outcomes.
    /// </summary>
    public Task<DualModelRun> RunAsync(
        string prompt,
        string firstModelKey,
        string secondModelKey,
        EffortLevel effort,
        CancellationToken cancellationToken) =>
        RunAsync(prompt, firstModelKey, secondModelKey, effort, DualModelMode.Compare, cancellationToken);

    /// <summary>
    /// Runs both models over the prompt concurrently. In Critique mode, once BOTH sides succeed, the
    /// second model runs at most <see cref="MaxCritiqueRounds"/> bounded follow-up critiquing the first
    /// response; the critique is appended to the second side's content under a clear label.
    /// </summary>
    /// <exception cref="OperationCanceledException">The caller's token was cancelled.</exception>
    public async Task<DualModelRun> RunAsync(
        string prompt,
        string firstModelKey,
        string secondModelKey,
        EffortLevel effort,
        DualModelMode mode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstModelKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(secondModelKey);
        cancellationToken.ThrowIfCancellationRequested();

        var executionId = Guid.NewGuid();
        var firstTask = CompleteSideAsync(executionId, "First", firstModelKey, prompt, effort, cancellationToken);
        var secondTask = CompleteSideAsync(executionId, "Second", secondModelKey, prompt, effort, cancellationToken);
        await Task.WhenAll(firstTask, secondTask).ConfigureAwait(false);

        // Per-side errors were captured above; a cancelled shared token must still fail the whole run.
        cancellationToken.ThrowIfCancellationRequested();

        var first = firstTask.Result;
        var second = secondTask.Result;
        if (mode == DualModelMode.Critique && first.Succeeded && second.Succeeded)
            second = await AppendCritiqueAsync(executionId, prompt, effort, first, second, cancellationToken).ConfigureAwait(false);

        return new DualModelRun(Guid.NewGuid(), prompt, first, second);
    }

    private async Task<DualModelSide> CompleteSideAsync(
        Guid executionId,
        string label,
        string modelKey,
        string prompt,
        EffortLevel effort,
        CancellationToken cancellationToken)
    {
        var actionId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var content = await client.CompleteAsync(
                new OllamaChatRequest(modelKey, [new OllamaMessage("user", prompt)], effort),
                cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            var endedAt = DateTimeOffset.UtcNow;
            Publish(new ExecutionEvent(
                Guid.NewGuid(), executionId, actionId, null, ExecutionOrigin.Haven,
                ExecutionActionType.ModelExecution, ExecutionActionStatus.Completed,
                $"Dual model: {modelKey}", null, Summarize(content), ComponentId, endedAt, startedAt, endedAt,
                SafeMetadata: SideMetadata(label, modelKey)));
            return new DualModelSide(label, modelKey, content, true, null, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var endedAt = DateTimeOffset.UtcNow;
            var error = ex is OperationCanceledException
                ? "The operation was cancelled."
                : ex.Message;
            Publish(new ExecutionEvent(
                Guid.NewGuid(), executionId, actionId, null, ExecutionOrigin.Haven,
                ExecutionActionType.ModelExecution, ExecutionActionStatus.Failed,
                $"Dual model: {modelKey}", null, Summarize(error), ComponentId, endedAt, startedAt, endedAt,
                Failure: new ExecutionFailure("MODEL_EXECUTION_FAILED", "Model execution failed", error),
                SafeMetadata: SideMetadata(label, modelKey)));
            return new DualModelSide(label, modelKey, string.Empty, false, error, stopwatch.Elapsed);
        }
    }

    private async Task<DualModelSide> AppendCritiqueAsync(
        Guid executionId,
        string prompt,
        EffortLevel effort,
        DualModelSide first,
        DualModelSide critic,
        CancellationToken cancellationToken)
    {
        var actionId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var critique = await client.CompleteAsync(
                new OllamaChatRequest(
                    critic.ModelKey,
                    [new OllamaMessage("user", BuildCritiquePrompt(prompt, first))],
                    effort,
                    CritiqueSystemPrompt),
                cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            var endedAt = DateTimeOffset.UtcNow;
            Publish(new ExecutionEvent(
                Guid.NewGuid(), executionId, actionId, null, ExecutionOrigin.Haven,
                ExecutionActionType.ReasoningSummary, ExecutionActionStatus.Completed,
                $"Dual model critique: {critic.ModelKey}", null, Summarize(critique), ComponentId, endedAt, startedAt, endedAt,
                SafeMetadata: SideMetadata(critic.Label, critic.ModelKey, "critique")));
            return critic with
            {
                Content = $"{critic.Content}{Environment.NewLine}{Environment.NewLine}---{Environment.NewLine}{Environment.NewLine}**Critique by {critic.ModelKey}:**{Environment.NewLine}{critique}"
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var endedAt = DateTimeOffset.UtcNow;
            Publish(new ExecutionEvent(
                Guid.NewGuid(), executionId, actionId, null, ExecutionOrigin.Haven,
                ExecutionActionType.ReasoningSummary, ExecutionActionStatus.Failed,
                $"Dual model critique: {critic.ModelKey}", null, Summarize(ex.Message), ComponentId, endedAt, startedAt, endedAt,
                Failure: new ExecutionFailure("CRITIQUE_ROUND_FAILED", "Critique round failed", ex.Message),
                SafeMetadata: SideMetadata(critic.Label, critic.ModelKey, "critique")));
            return critic;
        }
    }

    private static string BuildCritiquePrompt(string prompt, DualModelSide first) =>
        $"Original task:{Environment.NewLine}{prompt}{Environment.NewLine}{Environment.NewLine}" +
        $"Response from {first.ModelKey} to critique:{Environment.NewLine}{first.Content}{Environment.NewLine}{Environment.NewLine}" +
        "Critique the response above. Be specific about strengths, weaknesses and concrete improvements.";

    private void Publish(ExecutionEvent executionEvent) => events?.TryPublish(executionEvent);

    private static Dictionary<string, string> SideMetadata(string label, string modelKey, string? kind = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["side"] = label,
            ["model"] = modelKey
        };
        if (kind is not null) metadata["kind"] = kind;
        return metadata;
    }

    private static string Summarize(string value) =>
        value.Length <= DetailCharacterLimit ? value : value[..DetailCharacterLimit] + "…";
}
