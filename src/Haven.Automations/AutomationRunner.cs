using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Automations;

public sealed record AutomationBatchResult(int Due, int Started, int Succeeded, int Failed, int Skipped);

public sealed class AutomationRunner(
    IAutomationRepository repository,
    IOllamaClient ollama,
    ScheduleCalculator schedules)
{
    private const int MaximumAttempts = 3;

    public async Task<AutomationBatchResult> RunDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var due = await repository.GetDueAsync(now, cancellationToken).ConfigureAwait(false);
        var started = 0;
        var succeeded = 0;
        var failed = 0;
        var skipped = 0;

        foreach (var automation in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var leaseToken = Guid.NewGuid().ToString("N");
            if (!await repository.TryAcquireLeaseAsync(automation.Id, leaseToken, now.AddMinutes(15), cancellationToken).ConfigureAwait(false))
            {
                skipped++;
                continue;
            }

            started++;
            var scheduledFor = automation.NextRunAt ?? now;
            var startedAt = DateTimeOffset.UtcNow;
            try
            {
                var result = await ExecuteWithRetryAsync(automation, cancellationToken).ConfigureAwait(false);
                var next = schedules.GetNextRun(automation, DateTimeOffset.UtcNow);
                var run = new AutomationRun(
                    Guid.NewGuid(), automation.Id, AutomationRunStatus.Succeeded, scheduledFor,
                    startedAt, DateTimeOffset.UtcNow, result, null, leaseToken);
                await repository.CompleteRunAsync(run, next, cancellationToken).ConfigureAwait(false);
                succeeded++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var run = new AutomationRun(
                    Guid.NewGuid(), automation.Id, AutomationRunStatus.Cancelled, scheduledFor,
                    startedAt, DateTimeOffset.UtcNow, null, "Cancelled.", leaseToken);
                await repository.CompleteRunAsync(
                    run,
                    schedules.GetNextRun(automation, DateTimeOffset.UtcNow),
                    CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                var run = new AutomationRun(
                    Guid.NewGuid(), automation.Id, AutomationRunStatus.Failed, scheduledFor,
                    startedAt, DateTimeOffset.UtcNow, null, ex.Message, leaseToken);
                await repository.CompleteRunAsync(
                    run,
                    schedules.GetNextRun(automation, DateTimeOffset.UtcNow),
                    cancellationToken).ConfigureAwait(false);
                failed++;
            }
        }

        return new AutomationBatchResult(due.Count, started, succeeded, failed, skipped);
    }

    private async Task<string> ExecuteWithRetryAsync(
        AutomationDefinition automation,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await ExecuteOnceAsync(automation, attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (attempt == MaximumAttempts) break;
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
            }
        }
        throw new InvalidOperationException(
            $"Automation failed after {MaximumAttempts} attempts: {lastError?.Message}",
            lastError);
    }

    private async Task<string> ExecuteOnceAsync(
        AutomationDefinition automation,
        int attempt,
        CancellationToken cancellationToken)
    {
        var models = await ollama.GetModelsAsync(cancellationToken).ConfigureAwait(false);
        var model = models.FirstOrDefault(model => model.Supports(ToolCapability.Text))
            ?? throw new InvalidOperationException("No text-capable Ollama model is installed.");

        var conditionWatch = automation.ScheduleKind == AutomationScheduleKind.ConditionWatch;
        var system = conditionWatch
            ? $"You are Haven's condition-watch worker. Mode: {automation.Mode}. Evaluate the stated condition using only information actually available in this run. Return one JSON object and no markdown: {{\"conditionMet\":true|false,\"report\":\"concise evidence-based report\"}}. Fail closed: when evidence is missing or ambiguous, conditionMet must be false. Never claim an external action unless it was confirmed. Attempt {attempt} of {MaximumAttempts}."
            : $"You are Haven's background automation worker. Mode: {automation.Mode}. Execute the instruction safely. Do not claim external actions unless confirmed. Return a concise run report. Attempt {attempt} of {MaximumAttempts}.";
        var raw = await ollama.CompleteAsync(new OllamaChatRequest(
            model.Name,
            [new OllamaMessage("user", automation.Instruction)],
            EffortLevel.Medium,
            system), cancellationToken).ConfigureAwait(false);

        if (!conditionWatch) return raw;
        var result = ParseConditionResult(raw);
        return JsonSerializer.Serialize(new
        {
            conditionMet = result.ConditionMet,
            report = result.Report,
            evaluatedAt = DateTimeOffset.UtcNow
        });
    }

    internal static AutomationConditionResult ParseConditionResult(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return new(false, "The condition check returned no evidence and was treated as not met.");
        try
        {
            var start = response.IndexOf('{');
            var end = response.LastIndexOf('}');
            if (start < 0 || end <= start) throw new JsonException("No JSON object was returned.");
            using var document = JsonDocument.Parse(response[start..(end + 1)]);
            var root = document.RootElement;
            var met = root.TryGetProperty("conditionMet", out var condition)
                      && condition.ValueKind is JsonValueKind.True or JsonValueKind.False
                      && condition.GetBoolean();
            var report = root.TryGetProperty("report", out var reportElement)
                ? reportElement.GetString()?.Trim()
                : null;
            return new(met, string.IsNullOrWhiteSpace(report)
                ? met ? "The condition was reported as met without further detail." : "The condition was not met."
                : report);
        }
        catch (JsonException)
        {
            var bounded = response.Trim();
            if (bounded.Length > 1000) bounded = bounded[..1000] + "…";
            return new(false, "The condition check returned an unstructured response and was treated as not met. Response: " + bounded);
        }
    }
}

public sealed record AutomationConditionResult(bool ConditionMet, string Report);
