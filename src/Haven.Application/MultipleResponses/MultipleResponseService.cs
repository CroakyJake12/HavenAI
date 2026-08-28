using System.Diagnostics;
using Haven.Core;

namespace Haven.Application;

/// <summary>One independently executed model result in a Multiple Responses run.</summary>
public sealed record MultipleResponseResult(
    string ModelKey,
    string Content,
    bool Succeeded,
    string? Error,
    TimeSpan Duration);

/// <summary>The complete outcome of one prompt executed by two or more selected models.</summary>
public sealed record MultipleResponseRun(
    Guid Id,
    string Prompt,
    IReadOnlyList<MultipleResponseResult> Responses);

/// <summary>
/// Executes the selected models concurrently. Model failures are captured on their own result and do
/// not discard successful sibling responses. Cancelling the caller token cancels the whole run.
/// </summary>
public sealed class MultipleResponseService(IOllamaClient client, IExecutionEventSink? events = null)
{
    private const string ComponentId = "multiple-responses";
    private const int DetailCharacterLimit = 400;

    public async Task<MultipleResponseRun> RunAsync(
        string prompt,
        IReadOnlyCollection<string> modelKeys,
        EffortLevel effort,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(modelKeys);
        cancellationToken.ThrowIfCancellationRequested();

        var distinctModels = modelKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (distinctModels.Length < 2)
            throw new InvalidOperationException("Multiple Responses requires at least two distinct models.");

        var executionId = Guid.NewGuid();
        var tasks = distinctModels
            .Select((modelKey, index) => CompleteModelAsync(executionId, index, modelKey, prompt, effort, cancellationToken))
            .ToArray();
        var responses = await Task.WhenAll(tasks).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new MultipleResponseRun(Guid.NewGuid(), prompt, responses);
    }

    private async Task<MultipleResponseResult> CompleteModelAsync(
        Guid executionId,
        int index,
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
                $"Multiple response: {modelKey}", null, Summarize(content), ComponentId, endedAt, startedAt, endedAt,
                SafeMetadata: Metadata(index, modelKey)));
            return new MultipleResponseResult(modelKey, content, true, null, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            var endedAt = DateTimeOffset.UtcNow;
            var error = exception.Message;
            Publish(new ExecutionEvent(
                Guid.NewGuid(), executionId, actionId, null, ExecutionOrigin.Haven,
                ExecutionActionType.ModelExecution, ExecutionActionStatus.Failed,
                $"Multiple response: {modelKey}", null, Summarize(error), ComponentId, endedAt, startedAt, endedAt,
                Failure: new ExecutionFailure("MODEL_EXECUTION_FAILED", "Model execution failed", error),
                SafeMetadata: Metadata(index, modelKey)));
            return new MultipleResponseResult(modelKey, string.Empty, false, error, stopwatch.Elapsed);
        }
    }

    private void Publish(ExecutionEvent executionEvent) => events?.TryPublish(executionEvent);

    private static Dictionary<string, string> Metadata(int index, string modelKey) => new(StringComparer.Ordinal)
    {
        ["responseIndex"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["model"] = modelKey
    };

    private static string Summarize(string value) =>
        value.Length <= DetailCharacterLimit ? value : value[..DetailCharacterLimit] + "…";
}
