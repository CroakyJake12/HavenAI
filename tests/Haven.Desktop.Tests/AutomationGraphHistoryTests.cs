using Haven.Application.Automations;
using Haven.Desktop.Views.Pages.Automations;

namespace Haven.Desktop.Tests;

public sealed class AutomationGraphHistoryTests
{
    [Fact]
    public void Capture_preserves_mode_timestamps_trace_and_exact_graph_snapshot()
    {
        var nodeId = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow.AddSeconds(-2);
        var completed = started.AddSeconds(1);
        var result = new AutomationGraphRunResult(
            AutomationGraphRunMode.Test,
            false,
            started,
            completed,
            [new AutomationGraphValidationIssue("demo", "Validation detail", nodeId)],
            [new AutomationGraphNodeTrace(nodeId, "Condition", AutomationGraphTraceStatus.Failed, "No match", "false", "false", new Dictionary<Guid, string?> { [Guid.Empty] = "input-value" })],
            "No match");

        var entry = AutomationGraphHistoryJournal.Capture(
            Guid.NewGuid(), null, "Workflow", "Instruction", "{\"nodes\":[]}", result);

        Assert.Equal(AutomationGraphRunMode.Test, entry.Mode);
        Assert.Equal(started, entry.StartedAt);
        Assert.Equal(completed, entry.CompletedAt);
        Assert.False(entry.Succeeded);
        Assert.Equal("{\"nodes\":[]}", entry.GraphJson);
        Assert.Equal(nodeId, entry.Trace.Single().NodeId);
        Assert.Equal("false", entry.Trace.Single().Branch);
        Assert.Equal("input-value", entry.Trace.Single().Inputs![Guid.Empty]);
        var json = System.Text.Json.JsonSerializer.Serialize(new AutomationGraphHistoryState(1, [entry]));
        var restored = System.Text.Json.JsonSerializer.Deserialize<AutomationGraphHistoryState>(json);
        Assert.Equal("input-value", restored!.Entries.Single().Trace.Single().Inputs![Guid.Empty]);
        Assert.Equal("Validation detail", entry.ValidationIssues.Single().Message);
    }

    [Fact]
    public void Append_orders_newest_first_and_trims_to_requested_bound()
    {
        var state = new AutomationGraphHistoryState(1, []);
        for (var index = 0; index < 5; index++)
        {
            var started = new DateTimeOffset(2026, 8, 20, 7, index, 0, TimeSpan.Zero);
            var result = new AutomationGraphRunResult(AutomationGraphRunMode.Test, true, started, started.AddSeconds(1), [], []);
            state = AutomationGraphHistoryJournal.Append(state, AutomationGraphHistoryJournal.Capture(
                Guid.NewGuid(), null, $"Run {index}", string.Empty, "{}", result), maxEntries: 3);
        }

        Assert.Equal(3, state.Entries.Count);
        Assert.Equal(new[] { "Run 4", "Run 3", "Run 2" }, state.Entries.Select(entry => entry.WorkflowName).ToArray());
    }

    [Fact]
    public void ForContainer_never_leaks_runs_between_dashboard_or_space_contexts()
    {
        var left = Guid.NewGuid();
        var right = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        AutomationGraphHistoryEntry Entry(Guid? container, string name)
        {
            var result = new AutomationGraphRunResult(AutomationGraphRunMode.Real, true, now, now.AddSeconds(1), [], []);
            return AutomationGraphHistoryJournal.Capture(Guid.NewGuid(), container, name, "instruction", "{}", result);
        }

        var state = new AutomationGraphHistoryState(1, [Entry(left, "Left"), Entry(right, "Right"), Entry(null, "Global")]);

        Assert.Equal("Left", AutomationGraphHistoryJournal.ForContainer(state, left).Single().WorkflowName);
        Assert.Equal("Right", AutomationGraphHistoryJournal.ForContainer(state, right).Single().WorkflowName);
        Assert.Equal("Global", AutomationGraphHistoryJournal.ForContainer(state, null).Single().WorkflowName);
    }
}
