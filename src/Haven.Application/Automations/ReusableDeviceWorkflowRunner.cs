using Haven.Core;

namespace Haven.Application.Automations;

public enum ReusableDeviceWorkflowRunKind
{
    NotDeviceWorkflow,
    InvalidGraph,
    UnsupportedGraph,
    GraphWorkflow,
    DeviceAction
}

public sealed record ReusableDeviceWorkflowRunResult(ReusableDeviceWorkflowRunKind Kind, string Message, DeviceActionResult? DeviceResult = null, AutomationGraphRunResult? GraphResult = null)
{
    public bool Handled => Kind != ReusableDeviceWorkflowRunKind.NotDeviceWorkflow;
}

/// <summary>Executes persisted workflow graphs through the shared deterministic graph runner.</summary>
public sealed class ReusableDeviceWorkflowRunner(
    DeviceAutomationNodeExecutor? executor = null,
    BuiltInAutomationActionNodeExecutor? builtInExecutor = null)
{
    public async Task<ReusableDeviceWorkflowRunResult> RunAsync(ReusableTaskDefinition definition, bool permissionGranted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.GraphJson)) return new(ReusableDeviceWorkflowRunKind.NotDeviceWorkflow, "This workflow uses its instruction path.");
        if (!AutomationGraphCodec.TryDeserialize(definition.GraphJson, out var graph)) return new(ReusableDeviceWorkflowRunKind.InvalidGraph, "This workflow graph could not be read, so Haven did not execute it.");
        if (graph.Nodes.Count > 0 && graph.Nodes.All(node => string.Equals(node.Category, "Instruction", StringComparison.OrdinalIgnoreCase)))
            return new(ReusableDeviceWorkflowRunKind.NotDeviceWorkflow, "This workflow uses its instruction path.");

        var deviceGraphNodes = graph.Nodes.Where(node => string.Equals(node.Category, DeviceAutomationNodeCategory.Key, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (deviceGraphNodes.Any(node => node.ToDevice() is null)) return new(ReusableDeviceWorkflowRunKind.InvalidGraph, "This DEVICE workflow is missing a target or action, so Haven did not execute it.");
        if (deviceGraphNodes.Length > 0 && executor is null) return new(ReusableDeviceWorkflowRunKind.UnsupportedGraph, "The DEVICE graph executor is unavailable, so Haven did not perform a substitute action.");

        DeviceGraphNodeExecutor? adapter = executor is null ? null : new DeviceGraphNodeExecutor(executor, permissionGranted);
        var executors = new List<IAutomationGraphNodeExecutor>();
        if (adapter is not null) executors.Add(adapter);
        if (builtInExecutor is not null) executors.Add(builtInExecutor.WithPermission(permissionGranted));
        var graphResult = await new AutomationGraphRunner(executors).RunAsync(graph, AutomationGraphRunMode.Real, cancellationToken).ConfigureAwait(false);
        if (graphResult.ValidationIssues.Count > 0)
        {
            var unsupported = graphResult.ValidationIssues.Any(issue => issue.Code == "node.unsupported");
            return new(unsupported ? ReusableDeviceWorkflowRunKind.UnsupportedGraph : ReusableDeviceWorkflowRunKind.InvalidGraph, graphResult.ValidationIssues[0].Message, null, graphResult);
        }
        if (deviceGraphNodes.Length == 0)
            return new(ReusableDeviceWorkflowRunKind.GraphWorkflow, graphResult.Succeeded ? "Workflow graph completed." : graphResult.FailureMessage ?? "Workflow graph failed.", null, graphResult);
        if (adapter?.LastResult is null) return new(ReusableDeviceWorkflowRunKind.DeviceAction, graphResult.Succeeded ? "Workflow completed without selecting a DEVICE action branch." : graphResult.FailureMessage ?? "Workflow failed before a DEVICE action completed.", null, graphResult);
        return new(ReusableDeviceWorkflowRunKind.DeviceAction, graphResult.Succeeded ? "Workflow completed." : graphResult.FailureMessage ?? adapter.LastResult.Message, adapter.LastResult, graphResult);
    }

    private sealed class DeviceGraphNodeExecutor(DeviceAutomationNodeExecutor executor, bool permissionGranted) : IAutomationGraphNodeExecutor
    {
        public DeviceActionResult? LastResult { get; private set; }
        public bool CanExecute(AutomationGraphNodeDefinition node) => string.Equals(node.Category, DeviceAutomationNodeCategory.Key, StringComparison.OrdinalIgnoreCase) && node.ToDevice() is not null;
        public async Task<AutomationGraphNodeExecutionResult> ExecuteAsync(AutomationGraphNodeExecutionContext context, CancellationToken cancellationToken)
        {
            var node = context.Node.ToDevice();
            if (node is null) return new(false, "The DEVICE node is missing a target or action.");
            if (context.Mode == AutomationGraphRunMode.Test) return new(true, $"Test mode would execute {node.ActionKey} on {node.Target.DisplayName} without performing the device action.");
            LastResult = await executor.ExecuteAsync(node, permissionGranted, cancellationToken).ConfigureAwait(false);
            return new(LastResult.Succeeded, LastResult.Message, LastResult.Output);
        }
    }
}
