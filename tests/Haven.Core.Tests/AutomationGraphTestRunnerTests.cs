using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class AutomationGraphTestRunnerTests
{
    [Fact]
    public async Task Device_node_is_simulated_with_trace_without_a_physical_executor()
    {
        var target = new DeviceTargetDescriptor("current", "This PC", CapabilityPlatform.Windows, DeviceTargetKind.CurrentDevice, "test.device");
        var node = AutomationGraphNodeDefinition.FromDevice(new DeviceAutomationNodeDefinition(Guid.NewGuid(), target, "ui.snapshot", new Dictionary<string, string>()));
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [node], []);
        var result = await AutomationGraphTestRunner.RunAsync(graph, CancellationToken.None);
        Assert.True(result.Succeeded); Assert.Equal(AutomationGraphRunMode.Test, result.Mode);
        var trace = Assert.Single(result.Trace); Assert.Equal(AutomationGraphTraceStatus.Succeeded, trace.Status); Assert.Contains("would execute", trace.Message, StringComparison.OrdinalIgnoreCase);
    }
}
