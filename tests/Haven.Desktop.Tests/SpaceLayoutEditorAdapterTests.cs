using Haven.Application;
using Haven.Desktop.Views.Pages.Spaces;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class SpaceLayoutEditorAdapterTests
{
    [Fact]
    public void Space_layout_round_trips_through_the_canonical_node_editor_document()
    {
        var first = new SpaceLayoutNode(Guid.NewGuid(), "Context", "Registered context")
        {
            X = 20,
            Y = 40,
            Ports = [new SpaceLayoutPort("out", "Out", SpaceLayoutPortDirection.Output)],
            Metadata = new Dictionary<string, string> { ["source"] = "space" }
        };
        var second = new SpaceLayoutNode(Guid.NewGuid(), "AdditionalLogic", "Filter")
        {
            X = 320,
            Y = 40,
            Ports = [new SpaceLayoutPort("in", "In", SpaceLayoutPortDirection.Input)]
        };
        var edge = new SpaceLayoutEdge(Guid.NewGuid(), first.Id, "out", second.Id, "in") { Label = "context" };
        var source = new SpaceLayoutDocument([first, second], [edge]);

        var editor = SpaceLayoutEditorAdapter.ToEditor(source);
        var restored = SpaceLayoutEditorAdapter.FromEditor(editor);

        Assert.Equal(2, editor.Nodes.Count);
        Assert.Equal(NodeEditorPortDirection.Output, editor.Nodes[0].Ports[0].Direction);
        Assert.Equal(source.Nodes.Count, restored.Nodes.Count);
        Assert.Equal(first.Id, restored.Nodes[0].Id);
        Assert.Equal(first.Category, restored.Nodes[0].Category);
        Assert.Equal(first.Title, restored.Nodes[0].Title);
        Assert.Equal(first.Ports[0], restored.Nodes[0].Ports[0]);
        Assert.Equal("space", restored.Nodes[0].Metadata["source"]);
        Assert.Single(restored.Edges);
        Assert.Equal(edge.Id, restored.Edges[0].Id);
        Assert.Equal(edge.FromNodeId, restored.Edges[0].FromNodeId);
        Assert.Equal(edge.ToNodeId, restored.Edges[0].ToNodeId);
        Assert.Equal(edge.Label, restored.Edges[0].Label);
    }
}
