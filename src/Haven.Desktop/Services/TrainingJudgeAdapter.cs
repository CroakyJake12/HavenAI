/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Services/TrainingJudgeAdapter.cs, in the Desktop services layer, adapting application behavior to Windows and Avalonia concerns.
 * What: This file owns TrainingJudgeAdapter (IJudgeModelInvoker over IOllamaClient) and
 *       TrainingJudgeIntegration.TryScoreAsync — the optional, non-invasive judge hook for Testing Labs.
 * How: The adapter forwards a single-turn prompt to Ollama; the integration helper preflights model
 *      availability and then delegates to JudgeService.ScoreAttemptAsync, returning null when scoring is
 *      not possible so callers never receive a fabricated score.
 * Why: Judge scoring must stay optional and honest: no TrainingRunner rewrite, no score without evidence.
 * Maintenance: Keep the adapter single-turn and cancellation-safe; keep wiring notes below current with
 *              TrainingPageViewModel call sites.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Services;

/// <summary>
/// Adapts <see cref="IOllamaClient"/> onto <see cref="IJudgeModelInvoker"/> so Desktop can supply the
/// local Ollama provider to JudgeService without any Application-layer dependency on platform details.
/// </summary>
public sealed class TrainingJudgeAdapter(IOllamaClient ollama) : IJudgeModelInvoker
{
    /// <summary>
    /// Performs one judge completion over the local Ollama provider at Medium effort.
    /// </summary>
    public async Task<string> CompleteAsync(string modelKey, string prompt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelKey);
        return await ollama.CompleteAsync(
            new OllamaChatRequest(
                modelKey,
                [new OllamaMessage("user", prompt)],
                EffortLevel.Medium),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// The optional Testing Labs judge hook. Desktop calls this AFTER an attempt's report already exists;
/// it never changes how attempts run or are scored by the existing heuristics.
/// </summary>
public static class TrainingJudgeIntegration
{
    // FUTURE WIRING NOTE (for the parent agent applying the Desktop call site):
    //
    // In src/Haven.Desktop/ViewModels/TrainingPageViewModel.cs inside RunAllAttemptsAsync, immediately
    // AFTER `CurrentReport = TrainingRunner.GenerateMarkdownReport(result);` (~line 409), an optional
    // judge pass can be added without touching TrainingRunner:
    //
    //     var judge = new JudgeService(new TrainingJudgeAdapter(_runner's IOllamaClient), executionEvents);
    //     var score = await TrainingJudgeIntegration.TryScoreAsync(
    //         judge, ollama, judgeModelName, taskPrompt, CurrentReport, cancellationToken);
    //     if (score is { } judged)
    //         Status = $"Judge scored attempt #{result.AttemptNumber}: {judged.OverallPercent:0.#}%";
    //     else
    //         Status = "Judge scoring unavailable — heuristic results kept.";
    //
    // The same helper works per attempt from the history path (~line 521) where GenerateMarkdownReport
    // produces SelectedAttemptReport content. TryScoreAsync returns null on any failure, so callers only
    // ever render a real JudgeScore.

    /// <summary>
    /// Scores an attempt report with the judge model after confirming the local provider is available.
    /// Returns null — instead of throwing or fabricating data — whenever scoring cannot complete.
    /// </summary>
    public static async Task<JudgeScore?> TryScoreAsync(
        JudgeService judge,
        IOllamaClient ollama,
        string judgeModel,
        string taskPrompt,
        string reportMarkdown,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(judge);
        ArgumentNullException.ThrowIfNull(ollama);
        if (!await ollama.IsAvailableAsync(cancellationToken).ConfigureAwait(false)) return null;
        return await judge.ScoreAttemptAsync(judgeModel, taskPrompt, reportMarkdown, cancellationToken).ConfigureAwait(false);
    }
}
