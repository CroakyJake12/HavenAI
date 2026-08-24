namespace Haven.Application.Automations;

/// <summary>Runs Automation graphs in a graph-local simulation that cannot invoke physical-device side effects.</summary>
public static class AutomationGraphTestRunner
{
    public static Task<AutomationGraphRunResult> RunAsync(AutomationGraphDefinition graph, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return new AutomationGraphRunner([new SimulatedDeviceNodeExecutor(), new BuiltInAutomationActionNodeExecutor()])
            .RunAsync(graph, AutomationGraphRunMode.Test, cancellationToken);
    }

    private sealed class SimulatedDeviceNodeExecutor : IAutomationGraphNodeExecutor
    {
        public bool CanExecute(AutomationGraphNodeDefinition node) =>
            string.Equals(node.Category, DeviceAutomationNodeCategory.Key, StringComparison.OrdinalIgnoreCase) && node.ToDevice() is not null;

        public Task<AutomationGraphNodeExecutionResult> ExecuteAsync(AutomationGraphNodeExecutionContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = context.Node.ToDevice();
            if (node is null) return Task.FromResult(new AutomationGraphNodeExecutionResult(false, "The DEVICE node is missing a target or action."));
            return Task.FromResult(new AutomationGraphNodeExecutionResult(true, $"Test mode would execute {node.ActionKey} on {node.Target.DisplayName} without performing the device action."));
        }
    }
}
