using Haven.Application;
using Haven.Core;

namespace Haven.Automations;

public sealed record AutomationBatchResult(int Due, int Started, int Succeeded, int Failed, int Skipped);

public sealed class AutomationRunner(
    IAutomationRepository repository,
    IOllamaClient ollama,
    ScheduleCalculator schedules)
{
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
                var models = await ollama.GetModelsAsync(cancellationToken).ConfigureAwait(false);
                var model = models.FirstOrDefault(x => x.Supports(ToolCapability.Text))
                    ?? throw new InvalidOperationException("No text-capable Ollama model is installed.");
                var system = $"You are Haven's background automation worker. Mode: {automation.Mode}. Execute the instruction safely. Do not claim external actions unless confirmed. Return a concise run report.";
                var result = await ollama.CompleteAsync(new OllamaChatRequest(
                    model.Name,
                    [new OllamaMessage("user", automation.Instruction)],
                    EffortLevel.Medium,
                    system), cancellationToken).ConfigureAwait(false);
                var next = schedules.GetNextRun(automation, DateTimeOffset.UtcNow);
                var run = new AutomationRun(Guid.NewGuid(), automation.Id, AutomationRunStatus.Succeeded, scheduledFor, startedAt, DateTimeOffset.UtcNow, result, null, leaseToken);
                await repository.CompleteRunAsync(run, next, cancellationToken).ConfigureAwait(false);
                succeeded++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var run = new AutomationRun(Guid.NewGuid(), automation.Id, AutomationRunStatus.Cancelled, scheduledFor, startedAt, DateTimeOffset.UtcNow, null, "Cancelled.", leaseToken);
                await repository.CompleteRunAsync(run, schedules.GetNextRun(automation, DateTimeOffset.UtcNow), CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                var run = new AutomationRun(Guid.NewGuid(), automation.Id, AutomationRunStatus.Failed, scheduledFor, startedAt, DateTimeOffset.UtcNow, null, ex.Message, leaseToken);
                await repository.CompleteRunAsync(run, schedules.GetNextRun(automation, DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
                failed++;
            }
        }

        return new AutomationBatchResult(due.Count, started, succeeded, failed, skipped);
    }
}
