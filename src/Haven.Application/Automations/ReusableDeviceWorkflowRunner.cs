using Haven.Core;

namespace Haven.Application.Automations;

public enum ReusableDeviceWorkflowRunKind
{
    NotDeviceWorkflow,
    InvalidGraph,
    UnsupportedGraph,
    DeviceAction
}

public sealed record ReusableDeviceWorkflowRunResult(
    ReusableDeviceWorkflowRunKind Kind,
    string Message,
    DeviceActionResult? DeviceResult = null)
{
    public bool Handled => Kind != ReusableDeviceWorkflowRunKind.NotDeviceWorkflow;
}

/// <summary>
/// Executes the narrow DEVICE subset of persisted reusable-workflow graphs.
/// Generic graph ordering and multi-node execution remain outside this adapter.
/// </summary>
public sealed class ReusableDeviceWorkflowRunner(DeviceAutomationNodeExecutor executor)
{
    public async Task<ReusableDeviceWorkflowRunResult> RunAsync(
        ReusableTaskDefinition definition,
        bool permissionGranted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(definition.GraphJson))
            return new(ReusableDeviceWorkflowRunKind.NotDeviceWorkflow, "This workflow uses its instruction path.");

        if (!AutomationGraphCodec.TryDeserialize(definition.GraphJson, out var graph))
            return new(ReusableDeviceWorkflowRunKind.InvalidGraph, "This workflow graph could not be read, so Haven did not execute it.");

        var deviceGraphNodes = graph.Nodes
            .Where(node => string.Equals(node.Category, DeviceAutomationNodeCategory.Key, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (deviceGraphNodes.Length == 0)
            return new(ReusableDeviceWorkflowRunKind.NotDeviceWorkflow, "This workflow uses its instruction path.");

        if (deviceGraphNodes.Any(node => node.ToDevice() is null))
            return new(ReusableDeviceWorkflowRunKind.InvalidGraph, "This DEVICE workflow is missing a target or action, so Haven did not execute it.");

        if (graph.Nodes.Count != 1 || deviceGraphNodes.Length != 1 || graph.Edges.Count != 0)
            return new(ReusableDeviceWorkflowRunKind.UnsupportedGraph, "This DEVICE workflow contains multiple graph nodes or edges. Haven will not partially execute it.");

        var deviceNode = deviceGraphNodes[0].ToDevice()!;
        var result = await executor.ExecuteAsync(deviceNode, permissionGranted, cancellationToken).ConfigureAwait(false);
        return new(ReusableDeviceWorkflowRunKind.DeviceAction, result.Message, result);
    }
}
