using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class ScheduledGraphAutomationTests
{
    [Fact]
    public void Recurrence_binding_uses_existing_daily_schedule_format()
    {
        var trigger = Node("Schedule", "Recurrence", ("recurrence", "daily 08:30"));
        var graph = new AutomationGraphDefinition(1, [trigger], []);
        var now = new DateTimeOffset(2026, 8, 20, 7, 0, 0, TimeSpan.Zero);

        Assert.True(AutomationGraphScheduleBinder.TryBind(graph, now, out var binding, out var error), error);
        Assert.NotNull(binding);
        Assert.Equal(AutomationScheduleKind.Daily, binding.ScheduleKind);
        Assert.Equal(trigger.Id, binding.TriggerNodeId);
        var draft = ScheduledTaskScheduleComposer.Parse(binding.ScheduleKind, binding.ScheduleJson, now);
        Assert.Equal(new TimeOnly(8, 30), draft.Time);
    }

    [Fact]
    public void Condition_watch_requires_one_hour_minimum_and_preserves_condition()
    {
        var trigger = Node("ConditionWatch", "Condition watch", ("watch", "A new build is available"), ("intervalMinutes", "120"));
        var graph = new AutomationGraphDefinition(1, [trigger], []);

        Assert.True(AutomationGraphScheduleBinder.TryBind(graph, DateTimeOffset.UtcNow, out var binding, out var error), error);
        Assert.Equal(AutomationScheduleKind.ConditionWatch, binding!.ScheduleKind);
        Assert.Equal("A new build is available", binding.WatchCondition);
        Assert.Equal(120, ScheduledTaskScheduleComposer.Parse(binding.ScheduleKind, binding.ScheduleJson, DateTimeOffset.UtcNow).ConditionIntervalMinutes);
    }

    [Fact]
    public void Binder_rejects_multiple_scheduler_roots()
    {
        var graph = new AutomationGraphDefinition(1,
            [Node("Schedule", "Schedule", ("schedule", "2026-09-01T09:00:00+00:00")), Node("Schedule", "Recurrence", ("recurrence", "hourly"))], []);

        Assert.False(AutomationGraphScheduleBinder.TryBind(graph, new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero), out _, out var error));
        Assert.Contains("only one", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Payload_round_trip_and_trigger_scope_exclude_unrelated_manual_root()
    {
        var schedule = Node("Schedule", "Recurrence", ("recurrence", "hourly"));
        var scheduledAction = Node("Condition", "Scheduled branch", ("expression", "true"));
        var manual = Node("Trigger", "Manual trigger");
        var manualAction = Node("Condition", "Manual branch", ("expression", "true"));
        var graph = new AutomationGraphDefinition(1, [schedule, scheduledAction, manual, manualAction],
            [new AutomationGraphEdgeDefinition(schedule.Id, scheduledAction.Id), new AutomationGraphEdgeDefinition(manual.Id, manualAction.Id)]);
        var graphJson = AutomationGraphCodec.Serialize(graph);
        var workflowId = Guid.NewGuid();
        var encoded = ScheduledGraphAutomationPayloadCodec.Serialize(workflowId, schedule.Id, "Demo", graphJson, null);

        Assert.True(ScheduledGraphAutomationPayloadCodec.TryDeserialize(encoded, out var payload));
        Assert.Equal(graphJson, payload.GraphJson);
        Assert.True(AutomationGraphTriggerScope.TrySelect(graph, payload.TriggerNodeId, out var scoped, out var error), error);
        Assert.Contains(scoped.Nodes, node => node.Id == schedule.Id);
        Assert.Contains(scoped.Nodes, node => node.Id == scheduledAction.Id);
        Assert.DoesNotContain(scoped.Nodes, node => node.Id == manual.Id);
        Assert.DoesNotContain(scoped.Nodes, node => node.Id == manualAction.Id);
    }

    private static AutomationGraphNodeDefinition Node(string category, string title, params (string Key, string Value)[] parameters) =>
        new(Guid.NewGuid(), category, null, null, parameters.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase))
        { Title = title };
}
