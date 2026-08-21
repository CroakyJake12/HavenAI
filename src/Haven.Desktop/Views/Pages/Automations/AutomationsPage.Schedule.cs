using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Automations;

public sealed partial class AutomationsPage
{
    private async Task<string?> SyncScheduledGraphAsync(
        ReusableTaskDefinition workflow,
        string? graphJson,
        AutomationGraphScheduleBinding? binding)
    {
        var existing = (await _automations.GetAllAsync(CancellationToken.None))
            .FirstOrDefault(item => item.Id == workflow.Id);

        if (binding is null || string.IsNullOrWhiteSpace(graphJson))
        {
            if (existing is not null && ScheduledGraphAutomationPayloadCodec.IsPayload(existing.Instruction))
                await _automations.DeleteAsync(existing.Id, CancellationToken.None);
            return null;
        }

        if (existing is not null && !ScheduledGraphAutomationPayloadCodec.IsPayload(existing.Instruction))
            throw new InvalidOperationException("A legacy automation already uses this workflow ID. Haven left it unchanged instead of overwriting it.");

        var now = DateTimeOffset.UtcNow;
        var payload = ScheduledGraphAutomationPayloadCodec.Serialize(
            workflow.Id,
            binding.TriggerNodeId,
            workflow.Name,
            graphJson,
            binding.WatchCondition);
        var definition = new AutomationDefinition(
            workflow.Id,
            workflow.Name,
            HavenMode.Tasks,
            payload,
            binding.ScheduleKind,
            binding.ScheduleJson,
            null,
            workflow.ContainerId,
            workflow.IsEnabled,
            existing?.CreatedAt ?? workflow.CreatedAt,
            now);
        definition = definition with { NextRunAt = new ScheduledTaskScheduleCalculator().GetInitialRun(binding.ScheduleKind, binding.ScheduleJson, now) };
        await _automations.UpsertAsync(definition, CancellationToken.None);
        return binding.Description;
    }

    private Task OpenScheduledAutomationAsync(AutomationDefinition automation, IReadOnlyList<ReusableTaskDefinition> workflows)
    {
        if (!ScheduledGraphAutomationPayloadCodec.IsPayload(automation.Instruction))
            return InvokeAsync(automation.Instruction);

        var linked = workflows.FirstOrDefault(workflow => workflow.Id == automation.Id);
        if (linked is not null)
        {
            ShowEditor(linked);
            _status.Text = $"Opened scheduled graph {linked.Name}.";
            return Task.CompletedTask;
        }

        if (!ScheduledGraphAutomationPayloadCodec.TryDeserialize(automation.Instruction, out var payload))
        {
            _status.Text = "This scheduled graph payload is invalid. Haven did not route it to Tasks or perform a substitute instruction.";
            return Task.CompletedTask;
        }

        var captured = new ReusableTaskDefinition(
            payload.WorkflowId,
            string.IsNullOrWhiteSpace(payload.WorkflowName) ? automation.Name : payload.WorkflowName,
            "Captured scheduled graph snapshot",
            string.Empty,
            automation.ContainerId,
            automation.IsEnabled,
            automation.CreatedAt,
            automation.UpdatedAt,
            payload.GraphJson);
        ShowEditor(captured);
        _status.Text = $"Opened captured scheduled graph {captured.Name}.";
        return Task.CompletedTask;
    }

    private async Task DeleteLinkedScheduledGraphAsync(Guid workflowId)
    {
        var existing = (await _automations.GetAllAsync(CancellationToken.None))
            .FirstOrDefault(item => item.Id == workflowId);
        if (existing is not null && ScheduledGraphAutomationPayloadCodec.IsPayload(existing.Instruction))
            await _automations.DeleteAsync(existing.Id, CancellationToken.None);
    }
}
