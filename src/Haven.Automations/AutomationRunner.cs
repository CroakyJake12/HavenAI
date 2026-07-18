/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Automations/AutomationRunner.cs, in the Automations layer, which parses schedules and runs durable background actions.
 * What: This file owns AutomationBatchResult, AutomationRunner, AutomationConditionResult. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Automations;

/// <summary>
/// Represents automation batch result and keeps its related state and behavior together.
/// </summary>
public sealed record AutomationBatchResult(int Due, int Started, int Succeeded, int Failed, int Skipped);

/// <summary>
/// Represents automation runner and keeps its related state and behavior together.
/// </summary>
public sealed class AutomationRunner(
    IAutomationRepository repository,
    IOllamaClient ollama,
    ScheduleCalculator schedules,
    IAutomationDeliveryOutbox? deliveries = null)
{
    /// <summary>
    /// Stores maximum attempts locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int MaximumAttempts = 3;

    /// <summary>
    /// Runs run due async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
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
            if (!await repository.TryAcquireLeaseAsync(
                    automation.Id,
                    leaseToken,
                    now.AddMinutes(15),
                    cancellationToken).ConfigureAwait(false))
            {
                skipped++;
                continue;
            }

            started++;
            var run = await CompleteLeasedRunAsync(
                automation,
                leaseToken,
                automation.NextRunAt ?? now,
                cancellationToken).ConfigureAwait(false);
            if (run.Status == AutomationRunStatus.Succeeded) succeeded++;
            else if (run.Status == AutomationRunStatus.Failed) failed++;
        }

        return new AutomationBatchResult(due.Count, started, succeeded, failed, skipped);
    }

    /// <summary>
    /// Runs exactly one selected definition. It uses the same persisted lease and run
    /// history as scheduled work, but it does not require the definition to be due.
    /// </summary>
    public async Task<AutomationRun> RunOneAsync(
        AutomationDefinition automation,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(automation);
        var leaseToken = Guid.NewGuid().ToString("N");
        if (!await repository.TryAcquireLeaseAsync(
                automation.Id,
                leaseToken,
                requestedAt.AddMinutes(15),
                cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("This automation is already running in another Haven process.");

        return await CompleteLeasedRunAsync(
            automation,
            leaseToken,
            requestedAt,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs complete leased run async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<AutomationRun> CompleteLeasedRunAsync(
        AutomationDefinition automation,
        string leaseToken,
        DateTimeOffset scheduledFor,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            var result = await ExecuteWithRetryAsync(automation, cancellationToken).ConfigureAwait(false);
            var completedAt = DateTimeOffset.UtcNow;
            var run = new AutomationRun(
                Guid.NewGuid(),
                automation.Id,
                AutomationRunStatus.Succeeded,
                scheduledFor,
                startedAt,
                completedAt,
                result,
                null,
                leaseToken);
            await repository.CompleteRunAsync(
                run,
                NextRunAfterCompletion(automation, completedAt),
                cancellationToken).ConfigureAwait(false);
            await TryPublishDeliveryAsync(automation, run).ConfigureAwait(false);
            return run;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var completedAt = DateTimeOffset.UtcNow;
            var run = new AutomationRun(
                Guid.NewGuid(),
                automation.Id,
                AutomationRunStatus.Cancelled,
                scheduledFor,
                startedAt,
                completedAt,
                null,
                "Cancelled.",
                leaseToken);
            await repository.CompleteRunAsync(
                run,
                NextRunAfterCompletion(automation, completedAt),
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            var completedAt = DateTimeOffset.UtcNow;
            var run = new AutomationRun(
                Guid.NewGuid(),
                automation.Id,
                AutomationRunStatus.Failed,
                scheduledFor,
                startedAt,
                completedAt,
                null,
                ex.Message,
                leaseToken);
            await repository.CompleteRunAsync(
                run,
                NextRunAfterCompletion(automation, completedAt),
                CancellationToken.None).ConfigureAwait(false);
            await TryPublishDeliveryAsync(automation, run).ConfigureAwait(false);
            return run;
        }
    }

    /// <summary>
    /// Performs the next run after completion step owned by this component.
    /// </summary>
    private DateTimeOffset? NextRunAfterCompletion(
        AutomationDefinition automation,
        DateTimeOffset completedAt) =>
        automation.IsEnabled ? schedules.GetNextRun(automation, completedAt) : null;

    /// <summary>
    /// Attempts to publish delivery async and reports the result without using failure for normal control flow.
    /// </summary>
    private async Task TryPublishDeliveryAsync(
        AutomationDefinition automation,
        AutomationRun run)
    {
        if (deliveries is null) return;
        AutomationDelivery? delivery = null;
        if (run.Status == AutomationRunStatus.Failed)
        {
            delivery = new AutomationDelivery(
                Guid.NewGuid(),
                automation.Id,
                automation.Name,
                AutomationDeliveryKind.Failed,
                "Automation failed: " + automation.Name,
                Bound(run.Error ?? "The automation failed without an error report.", 900),
                run.CompletedAt ?? DateTimeOffset.UtcNow);
        }
        else if (run.Status == AutomationRunStatus.Succeeded
                 && automation.ScheduleKind == AutomationScheduleKind.ConditionWatch
                 && TryReadNormalizedCondition(run.Result, out var condition)
                 && condition.ConditionMet)
        {
            delivery = new AutomationDelivery(
                Guid.NewGuid(),
                automation.Id,
                automation.Name,
                AutomationDeliveryKind.ConditionMet,
                "Condition met: " + automation.Name,
                Bound(condition.Report, 900),
                run.CompletedAt ?? DateTimeOffset.UtcNow);
        }

        if (delivery is null) return;
        try
        {
            await deliveries.EnqueueAsync(delivery, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Delivery is secondary to the already-persisted run. The next run must not
            // be duplicated or marked failed because the notification outbox is locked.
        }
    }

    /// <summary>
    /// Runs execute with retry async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
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

    /// <summary>
    /// Runs execute once async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private async Task<string> ExecuteOnceAsync(
        AutomationDefinition automation,
        int attempt,
        CancellationToken cancellationToken)
    {
        var models = await ollama.GetModelsAsync(cancellationToken).ConfigureAwait(false);
        var model = models.FirstOrDefault(candidate => candidate.Supports(ToolCapability.Text))
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

    /// <summary>
    /// Attempts to read normalized condition and reports the result without using failure for normal control flow.
    /// </summary>
    private static bool TryReadNormalizedCondition(
        string? response,
        out AutomationConditionResult result)
    {
        result = new AutomationConditionResult(false, "The condition was not met.");
        if (string.IsNullOrWhiteSpace(response)) return false;
        try
        {
            using var document = JsonDocument.Parse(response);
            var root = document.RootElement;
            if (!root.TryGetProperty("conditionMet", out var met)
                || met.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return false;
            var report = root.TryGetProperty("report", out var reportValue)
                ? reportValue.GetString()?.Trim()
                : null;
            result = new AutomationConditionResult(
                met.GetBoolean(),
                string.IsNullOrWhiteSpace(report) ? "The condition was met." : report);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Performs the parse condition result step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the bound step owned by this component.
    /// </summary>
    private static string Bound(string value, int maximum)
    {
        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= maximum ? normalized : normalized[..maximum] + "…";
    }
}

/// <summary>
/// Represents automation condition result and keeps its related state and behavior together.
/// </summary>
public sealed record AutomationConditionResult(bool ConditionMet, string Report);
