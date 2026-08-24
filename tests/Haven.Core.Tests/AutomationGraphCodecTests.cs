using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class AutomationGraphCodecTests
{
    [Fact]
    public void DeviceNodeRoundTrips()
    {
        var target = new DeviceTargetDescriptor("current", "This PC", CapabilityPlatform.Windows, DeviceTargetKind.CurrentDevice);
        var source = new DeviceAutomationNodeDefinition(Guid.NewGuid(), target, "applications.launch", new Dictionary<string,string> { ["name"] = "Calculator" });
        var json = AutomationGraphCodec.Serialize(new(1, [AutomationGraphNodeDefinition.FromDevice(source)], []));
        Assert.True(AutomationGraphCodec.TryDeserialize(json, out var graph));
        var node = Assert.Single(graph.Nodes).ToDevice();
        Assert.NotNull(node);
        Assert.Equal(source.Id, node.Id);
        Assert.Equal(target, node.Target);
        Assert.Equal("Calculator", node.Parameters["name"]);
    }

    [Fact]
    public void MissingEdgeNodesAreRejected()
    {
        var graph = new AutomationGraphDefinition(1, [], [new(Guid.NewGuid(), Guid.NewGuid())]);
        Assert.Throws<InvalidOperationException>(() => AutomationGraphCodec.Serialize(graph));
    }
}
