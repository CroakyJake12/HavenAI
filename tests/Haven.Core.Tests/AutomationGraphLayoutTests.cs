using Haven.Application.Automations;

namespace Haven.Core.Tests;

public sealed class AutomationGraphLayoutTests
{
    [Fact]
    public void Codec_round_trips_editor_layout_ports_and_branch_metadata_without_breaking_v1()
    {
        var first = new AutomationGraphNodeDefinition(
            Guid.NewGuid(), "Condition", null, null, new Dictionary<string, string> { ["expression"] = "true" })
        {
            X = 120,
            Y = 80,
            Width = 250,
            Height = 132,
            Title = "Check status",
            Subtitle = "Routes true/false",
            Ports =
            [
                new("in", "In", AutomationGraphPortDirection.Input, "flow", true),
                new("yes", "Yes", AutomationGraphPortDirection.Output, "flow", true),
                new("no", "No", AutomationGraphPortDirection.Output, "flow", true)
            ],
            Metadata = new Dictionary<string, string> { ["library"] = "logic" }
        };
        var second = new AutomationGraphNodeDefinition(
            Guid.NewGuid(), "App", null, "open", new Dictionary<string, string>())
        {
            X = 480,
            Y = 40,
            Title = "Open app"
        };
        var edge = new AutomationGraphEdgeDefinition(first.Id, second.Id)
        {
            FromPortId = "yes",
            ToPortId = "in",
            Branch = "true",
            Label = "Ready"
        };
        var source = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [first, second], [edge]);

        var json = AutomationGraphCodec.Serialize(source);

        Assert.True(AutomationGraphCodec.TryDeserialize(json, out var roundTripped));
        var restored = roundTripped.Nodes.Single(node => node.Id == first.Id);
        Assert.Equal(120, restored.X);
        Assert.Equal(250, restored.Width);
        Assert.Equal("Check status", restored.Title);
        Assert.Equal(3, restored.EffectivePorts.Count);
        var restoredEdge = Assert.Single(roundTripped.Edges);
        Assert.Equal("yes", restoredEdge.FromPortId);
        Assert.Equal("true", restoredEdge.Branch);
        Assert.Equal("Ready", restoredEdge.Label);
    }

    [Fact]
    public void Legacy_nodes_and_edges_receive_default_flow_ports()
    {
        var first = new AutomationGraphNodeDefinition(Guid.NewGuid(), "Trigger", null, null, new Dictionary<string, string>());
        var second = new AutomationGraphNodeDefinition(Guid.NewGuid(), "Action", null, null, new Dictionary<string, string>());
        var graph = new AutomationGraphDefinition(
            AutomationGraphDefinition.CurrentVersion,
            [first, second],
            [new AutomationGraphEdgeDefinition(first.Id, second.Id)]);

        var json = AutomationGraphCodec.Serialize(graph);
        Assert.True(AutomationGraphCodec.TryDeserialize(json, out var restored));
        Assert.Equal(new[] { "in", "out" }, restored.Nodes[0].EffectivePorts.Select(port => port.Id).ToArray());
    }
}
