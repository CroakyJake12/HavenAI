using Haven.Application.Automations;

namespace Haven.Core.Tests;

public sealed class AutomationGraphRuntimeTests
{
    [Fact]
    public async Task Invalid_cycle_is_rejected_before_any_executor_side_effect()
    {
        var first = Node("Action"); var second = Node("Action"); var executor = new RecordingExecutor("Action");
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [first, second], [new(first.Id, second.Id), new(second.Id, first.Id)]);
        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Real, CancellationToken.None);
        Assert.False(result.Succeeded); Assert.Contains(result.ValidationIssues, issue => issue.Code == "graph.cycle"); Assert.Equal(0, executor.ExecuteCount); Assert.Empty(result.Trace);
    }

    [Fact]
    public async Task Condition_selects_only_matching_branch_and_marks_other_branch_skipped()
    {
        var trigger = Node("Trigger");
        var condition = new AutomationGraphNodeDefinition(Guid.NewGuid(), "Condition", null, null, new Dictionary<string, string> { ["expression"] = "true" });
        var yes = Node("Action"); var no = Node("Action"); var executor = new RecordingExecutor("Action");
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [trigger, condition, yes, no], [new AutomationGraphEdgeDefinition(trigger.Id, condition.Id), new AutomationGraphEdgeDefinition(condition.Id, yes.Id) { Branch = "true" }, new AutomationGraphEdgeDefinition(condition.Id, no.Id) { Branch = "false" }]);
        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Real, CancellationToken.None);
        Assert.True(result.Succeeded); Assert.Equal(1, executor.ExecuteCount); Assert.Equal(yes.Id, executor.ExecutedNodes.Single()); Assert.Contains(result.Trace, item => item.NodeId == condition.Id && item.Branch == "true"); Assert.Contains(result.Trace, item => item.NodeId == no.Id && item.Status == AutomationGraphTraceStatus.Skipped);
    }

    [Fact]
    public async Task Test_mode_is_forwarded_and_preserved_in_trace_result()
    {
        var action = Node("Action"); var executor = new RecordingExecutor("Action"); var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [action], []);
        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Test, CancellationToken.None);
        Assert.True(result.Succeeded); Assert.Equal(AutomationGraphRunMode.Test, result.Mode); Assert.Equal(AutomationGraphRunMode.Test, executor.LastMode); Assert.Equal(AutomationGraphTraceStatus.Succeeded, Assert.Single(result.Trace).Status);
    }

    [Fact]
    public async Task Port_direction_errors_fail_validation_before_executor_runs()
    {
        var first = Node("Action") with { Ports = [new("in", "In", AutomationGraphPortDirection.Input, "flow", true)] }; var second = Node("Action"); var executor = new RecordingExecutor("Action");
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [first, second], [new AutomationGraphEdgeDefinition(first.Id, second.Id) { FromPortId = "in" }]);
        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Real, CancellationToken.None);
        Assert.False(result.Succeeded); Assert.Contains(result.ValidationIssues, issue => issue.Code == "edge.from-port.direction"); Assert.Equal(0, executor.ExecuteCount);
    }

    [Fact]
    public async Task Trace_captures_resolved_upstream_inputs_for_each_executed_node()
    {
        var first = Node("Action");
        var second = Node("Action");
        var executor = new RecordingExecutor("Action");
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [first, second], [new(first.Id, second.Id)]);

        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Test, CancellationToken.None);

        Assert.True(result.Succeeded);
        var firstTrace = result.Trace.Single(trace => trace.NodeId == first.Id);
        var secondTrace = result.Trace.Single(trace => trace.NodeId == second.Id);
        Assert.Empty(firstTrace.Inputs ?? []);
        Assert.NotNull(secondTrace.Inputs);
        Assert.Equal(first.Id.ToString(), secondTrace.Inputs[first.Id]);
    }

    [Fact]
    public async Task Runtime_executes_150_node_chain_with_complete_ordered_trace()
    {
        const int count = 150;
        var nodes = Enumerable.Range(0, count).Select(_ => Node("StressAction")).ToArray();
        var edges = Enumerable.Range(0, count - 1).Select(index => new AutomationGraphEdgeDefinition(nodes[index].Id, nodes[index + 1].Id)).ToArray();
        var executor = new RecordingExecutor("StressAction");
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, nodes, edges);

        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Test, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(count, executor.ExecuteCount);
        Assert.Equal(count, result.Trace.Count);
        Assert.Equal(nodes.Select(node => node.Id), result.Trace.Select(trace => trace.NodeId));
        Assert.All(result.Trace, trace => Assert.Equal(AutomationGraphTraceStatus.Succeeded, trace.Status));
    }

    private static AutomationGraphNodeDefinition Node(string category) => new(Guid.NewGuid(), category, null, null, new Dictionary<string, string>());
    private sealed class RecordingExecutor(string category) : IAutomationGraphNodeExecutor
    {
        public int ExecuteCount { get; private set; } public List<Guid> ExecutedNodes { get; } = []; public AutomationGraphRunMode? LastMode { get; private set; }
        public bool CanExecute(AutomationGraphNodeDefinition node) => string.Equals(node.Category, category, StringComparison.OrdinalIgnoreCase);
        public Task<AutomationGraphNodeExecutionResult> ExecuteAsync(AutomationGraphNodeExecutionContext context, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); ExecuteCount++; ExecutedNodes.Add(context.Node.Id); LastMode = context.Mode; return Task.FromResult(new AutomationGraphNodeExecutionResult(true, "Executed.", context.Node.Id.ToString())); }
    }
}
