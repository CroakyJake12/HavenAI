/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Evaluation/JudgeService.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns JudgeScore, IJudgeModelInvoker and JudgeService — LLM-as-judge scoring for
 *       Testing Labs training attempts against the fixed criteria set (correctness, taskCompletion,
 *       instructionAdherence, codeQuality, efficiency).
 * How: The judge model is reached through IJudgeModelInvoker (Desktop adapts IOllamaClient onto it), so
 *      policy stays testable and platform-free. The service builds a strict JSON-output prompt, parses the
 *      reply leniently (code fences stripped, numeric strings accepted, values clamped 0..100) and returns
 *      null — never a fabricated score — when the judge reply cannot be parsed. Every outcome publishes an
 *      ExecutionEvent (ActionType JudgeEvaluated) carrying the overall percent in SafeMetadata.
 * Why: Honest automated evaluation must fail loudly (null + Failed event) rather than guess.
 * Maintenance: Keep the fixed criteria set stable, keep parsing failures honest (null + Failed event),
 *              and preserve cancellation flow through the invoker call.
 */

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// One judge evaluation of a training attempt: overall percent plus per-criterion scores on the fixed
/// 0..100 scale, the judge's short reasoning summary, and which model produced the judgement.
/// </summary>
public sealed record JudgeScore(
    double OverallPercent,
    IReadOnlyDictionary<string, int> CriteriaScores,
    string ReasoningSummary,
    string JudgeModel);

/// <summary>
/// Defines the single-turn completion contract JudgeService needs so callers can adapt any model provider
/// (Desktop adapts IOllamaClient) without Application depending on platform details.
/// </summary>
public interface IJudgeModelInvoker
{
    Task<string> CompleteAsync(string modelKey, string prompt, CancellationToken cancellationToken);
}

/// <summary>
/// Scores a training attempt report with a judge model. Parsing is deliberately strict about requiring a
/// usable overall score yet lenient about formatting: code fences are stripped, aliases and numeric
/// strings are accepted, and every value is clamped to 0..100. Unparseable replies return null and emit a
/// Failed execution event instead of inventing data.
/// </summary>
public sealed class JudgeService(IJudgeModelInvoker invoker, IExecutionEventSink? events = null)
{
    /// <summary>The fixed criteria set; persisted scores always contain exactly these keys.</summary>
    public static readonly IReadOnlyList<string> Criteria =
    [
        "correctness",
        "taskCompletion",
        "instructionAdherence",
        "codeQuality",
        "efficiency"
    ];

    private const string ComponentId = "evaluation";

    /// <summary>
    /// Asks <paramref name="judgeModel"/> to score the attempt report and returns the clamped score,
    /// or null when the judge is unavailable, returns nothing parseable, or fails.
    /// </summary>
    /// <exception cref="OperationCanceledException">The caller's token was cancelled.</exception>
    public async Task<JudgeScore?> ScoreAttemptAsync(
        string judgeModel,
        string taskPrompt,
        string attemptReportJsonOrMarkdown,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(judgeModel);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptReportJsonOrMarkdown);
        cancellationToken.ThrowIfCancellationRequested();

        string raw;
        try
        {
            raw = await invoker.CompleteAsync(
                judgeModel,
                BuildScoringPrompt(taskPrompt, attemptReportJsonOrMarkdown),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            PublishFailed("JUDGE_INVOCATION_FAILED", "The judge model could not be reached.", ex.Message, judgeModel);
            return null;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            PublishFailed("JUDGE_EMPTY_RESPONSE", "The judge returned no content.", "Empty judge response.", judgeModel);
            return null;
        }

        if (!TryParseScore(raw, judgeModel, out var score))
        {
            PublishFailed("JUDGE_PARSE_FAILED", "The judge reply was not valid scoring JSON.", Summarize(raw), judgeModel);
            return null;
        }

        var completedAt = DateTimeOffset.UtcNow;
        events?.TryPublish(new ExecutionEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, ExecutionOrigin.Haven,
            ExecutionActionType.JudgeEvaluated, ExecutionActionStatus.Completed,
            "Judge evaluated attempt", null, Summarize(score.ReasoningSummary), ComponentId, completedAt, completedAt, completedAt,
            SafeMetadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["judgeModel"] = judgeModel,
                ["overall"] = score.OverallPercent.ToString("0.#", CultureInfo.InvariantCulture)
            }));
        return score;
    }

    private static string BuildScoringPrompt(string taskPrompt, string attemptReport)
    {
        var criteriaLines = string.Join(Environment.NewLine, Criteria.Select(criterion => $"- {criterion}"));
        return "You are an impartial judge scoring a coding agent's attempt at a task." + Environment.NewLine +
               Environment.NewLine +
               "Score the attempt from 0 to 100 for each criterion:" + Environment.NewLine +
               criteriaLines + Environment.NewLine +
               "- overall: your holistic score for the attempt" + Environment.NewLine +
               Environment.NewLine +
               "Respond with ONLY minified JSON in exactly this shape and nothing else — no prose, no code fences:" + Environment.NewLine +
               "{\"correctness\":0,\"taskCompletion\":0,\"instructionAdherence\":0,\"codeQuality\":0,\"efficiency\":0,\"overall\":0,\"reasoning\":\"one short paragraph\"}" +
               Environment.NewLine + Environment.NewLine +
               "Task given to the agent:" + Environment.NewLine +
               taskPrompt + Environment.NewLine + Environment.NewLine +
               "Attempt report to score:" + Environment.NewLine +
               attemptReport;
    }

    private static bool TryParseScore(string raw, string judgeModel, [NotNullWhen(true)] out JudgeScore? score)
    {
        score = null;
        if (!TryExtractJson(raw, out var json)) return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            if (!TryReadNumber(root, ["overall", "overallPercent", "overall_percent", "Overall"], out var overall))
                return false;

            var criteriaScores = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var criterion in Criteria)
            {
                // A missing criterion falls back to the holistic overall rather than fabricating detail.
                if (!TryReadNumber(root, [criterion, ToSnakeCase(criterion)], out var value)) value = overall;
                criteriaScores[criterion] = (int)Math.Round(Math.Clamp(value, 0d, 100d));
            }

            var reasoning = TryReadString(root, ["reasoning", "reasoningSummary", "reasoning_summary", "explanation", "summary"], out var summary)
                ? summary.Trim()
                : string.Empty;

            score = new JudgeScore(Math.Clamp(overall, 0d, 100d), criteriaScores, reasoning, judgeModel);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Strips markdown code fences and surrounding prose, keeping only the outermost JSON object.</summary>
    private static bool TryExtractJson(string raw, out string json)
    {
        var candidate = raw.Trim();
        var fenceStart = candidate.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var innerStart = candidate.IndexOf('\n', fenceStart);
            var fenceEnd = innerStart >= 0 ? candidate.IndexOf("```", innerStart, StringComparison.Ordinal) : -1;
            if (innerStart >= 0 && fenceEnd > innerStart)
                candidate = candidate[(innerStart + 1)..fenceEnd].Trim();
        }

        var objectStart = candidate.IndexOf('{');
        var objectEnd = candidate.LastIndexOf('}');
        if (objectStart < 0 || objectEnd <= objectStart)
        {
            json = string.Empty;
            return false;
        }

        json = candidate[objectStart..(objectEnd + 1)];
        return true;
    }

    private static bool TryReadNumber(JsonElement root, IReadOnlyList<string> aliases, out double value)
    {
        value = 0d;
        foreach (var property in root.EnumerateObject())
        {
            if (!Matches(property.Name, aliases)) continue;
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Number:
                    value = property.Value.GetDouble();
                    return true;
                case JsonValueKind.String:
                    if (double.TryParse(property.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                    {
                        value = parsed;
                        return true;
                    }
                    return false;
                default:
                    return false;
            }
        }
        return false;
    }

    private static bool TryReadString(JsonElement root, IReadOnlyList<string> aliases, out string value)
    {
        value = string.Empty;
        foreach (var property in root.EnumerateObject())
        {
            if (!Matches(property.Name, aliases)) continue;
            if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            {
                value = property.Value.ToString() ?? string.Empty;
                return true;
            }
            return false;
        }
        return false;
    }

    private static bool Matches(string propertyName, IReadOnlyList<string> aliases)
    {
        foreach (var alias in aliases)
        {
            if (propertyName.Equals(alias, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string ToSnakeCase(string criterion) =>
        string.Concat(criterion.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? "_" + character : character.ToString())).ToLowerInvariant();

    private void PublishFailed(string code, string title, string message, string judgeModel)
    {
        var failedAt = DateTimeOffset.UtcNow;
        events?.TryPublish(new ExecutionEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, ExecutionOrigin.Haven,
            ExecutionActionType.JudgeEvaluated, ExecutionActionStatus.Failed,
            "Judge evaluation failed", null, Summarize(message), ComponentId, failedAt, failedAt, failedAt,
            Failure: new ExecutionFailure(code, title, message),
            SafeMetadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["judgeModel"] = judgeModel
            }));
    }

    private static string Summarize(string value) =>
        value.Length <= 400 ? value : value[..400] + "…";
}
