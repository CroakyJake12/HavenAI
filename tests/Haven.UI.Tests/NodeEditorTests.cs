using Haven.UI.Components;
using Xunit;

namespace Haven.UI.Tests;

public sealed class NodeEditorTests
{
    [Fact]
    public void Large_graph_culls_offscreen_nodes_and_keeps_minimap_retained()
    {
        var nodes = Enumerable.Range(0, 150).Select(index => Node(index * 260, (index % 5) * 150, $"Node {index}")).ToArray();
        var editor = Editor(new NodeEditorDocument(nodes, []), 900, 620);
        var commands = new HavenSceneRenderer().Render(editor);
        Assert.InRange(editor.RealizedNodeCount, 1, 30);
        Assert.True(editor.RealizedNodeCount < nodes.Length);
        Assert.Contains(commands, command => command is HavenStrokeRoundedRectCommand);
    }

    [Fact]
    public void Connections_validate_direction_type_capacity_and_cycles()
    {
        var first = Node(0, 0, "First"); var second = Node(320, 0, "Second"); var editor = Editor(new NodeEditorDocument([first, second], []));
        Assert.True(editor.Connect(first.Id, "out", second.Id, "in"));
        Assert.Single(editor.Document.Edges);
        Assert.False(editor.Connect(second.Id, "out", first.Id, "in"));
        Assert.False(editor.Connect(first.Id, "in", second.Id, "out"));
        Assert.Empty(editor.ValidateDocument());
    }

    [Fact]
    public void Move_copy_paste_delete_undo_and_redo_preserve_graph_semantics()
    {
        var first = Node(10, 20, "First"); var second = Node(320, 20, "Second");
        var edge = new NodeEditorEdge(Guid.NewGuid(), first.Id, "out", second.Id, "in");
        var editor = Editor(new NodeEditorDocument([first, second], [edge])); editor.SelectNode(first.Id); editor.SelectNode(second.Id, true);
        editor.MoveSelectionBy(40, 15); Assert.Equal(50, editor.Document.Nodes.Single(node => node.Id == first.Id).X);
        Assert.True(editor.Undo()); Assert.Equal(10, editor.Document.Nodes.Single(node => node.Id == first.Id).X);
        Assert.True(editor.Redo()); Assert.Equal(50, editor.Document.Nodes.Single(node => node.Id == first.Id).X);
        var copied = editor.CopySelection(); var pastedIds = editor.PasteSelection(copied);
        Assert.Equal(2, pastedIds.Count); Assert.Equal(4, editor.Document.Nodes.Count); Assert.Equal(2, editor.Document.Edges.Count);
        Assert.DoesNotContain(pastedIds, id => id == first.Id || id == second.Id);
        editor.DeleteSelection(); Assert.Equal(2, editor.Document.Nodes.Count); Assert.Single(editor.Document.Edges);
        Assert.True(editor.Undo()); Assert.Equal(4, editor.Document.Nodes.Count);
    }

    [Fact]
    public void Marquee_selection_supports_additive_multi_select()
    {
        var first = Node(0, 0, "One"); var second = Node(300, 0, "Two"); var third = Node(700, 0, "Three");
        var editor = Editor(new NodeEditorDocument([first, second, third], []));
        editor.SelectNodesInWorldRect(new HavenRect(-10, -10, 550, 180)); Assert.Equal(2, editor.SelectedNodeIds.Count);
        editor.SelectNodesInWorldRect(new HavenRect(680, -10, 260, 180), true); Assert.Equal(3, editor.SelectedNodeIds.Count);
    }

    [Fact]
    public void Viewport_pan_and_zoom_are_clamped_and_resettable()
    {
        var editor = Editor(new NodeEditorDocument([Node(0, 0, "One")], [])); editor.PanBy(80, 40);
        Assert.Equal(80, editor.PanX); Assert.Equal(40, editor.PanY); editor.ZoomAt(100, new HavenPoint(300, 200)); Assert.Equal(3, editor.Zoom);
        editor.ZoomAt(0.0001, new HavenPoint(300, 200)); Assert.Equal(0.2, editor.Zoom); editor.ResetViewport();
        Assert.Equal(1, editor.Zoom); Assert.Equal(0, editor.PanX); Assert.Equal(0, editor.PanY);
    }

    [Fact]
    public void Search_and_add_use_app_supplied_node_library()
    {
        var editor = Editor(NodeEditorDocument.Empty);
        var templates = new[] { new NodeEditorTemplate("Trigger", "Workspace opened", "Starts when a workspace opens", Ports()), new NodeEditorTemplate("Action", "Send message", "Sends a message", Ports()) };
        var result = editor.SearchTemplates(templates, "workspace"); Assert.Single(result); var id = editor.AddNode(result[0], 120, 80);
        var node = Assert.Single(editor.Document.Nodes); Assert.Equal(id, node.Id); Assert.Equal(120, node.X); Assert.Contains(id, editor.SelectedNodeIds);
    }

    [Fact]
    public void Keyboard_and_clipboard_contract_exposes_direct_editor_semantics()
    {
        var node = Node(10, 10, "One"); var editor = Editor(new NodeEditorDocument([node], [])); editor.SelectNode(node.Id);
        Assert.True(editor.KeyDown(new HavenKeyInput(HavenKey.Right, HavenKeyModifiers.Shift))); Assert.Equal(20, editor.Document.Nodes.Single().X);
        var copied = editor.Copy(); Assert.False(string.IsNullOrWhiteSpace(copied)); Assert.True(editor.Paste(copied)); Assert.Equal(2, editor.Document.Nodes.Count);
        Assert.True(editor.KeyDown(new HavenKeyInput(HavenKey.Delete, HavenKeyModifiers.None))); Assert.Single(editor.Document.Nodes);
    }

    [Fact]
    public void Inspector_updates_are_undoable_and_cannot_replace_stable_node_ids()
    {
        var node = Node(10, 10, "Before"); var editor = Editor(new NodeEditorDocument([node], []));
        Assert.True(editor.UpdateNode(node.Id, current => current with { Id = Guid.NewGuid(), Title = "After" }));
        Assert.Equal(node.Id, editor.Document.Nodes.Single().Id); Assert.Equal("After", editor.Document.Nodes.Single().Title);
        Assert.True(editor.Undo()); Assert.Equal("Before", editor.Document.Nodes.Single().Title);
    }

    private static NodeEditor Editor(NodeEditorDocument document, double width = 960, double height = 640)
    {
        var editor = new NodeEditor { Document = document }; editor.SetValue(HavenProperties.Width, HavenLength.Px(width)); editor.SetValue(HavenProperties.Height, HavenLength.Px(height));
        new HavenLayoutEngine().Layout(editor, new HavenSize(width, height), HavenPlatform.Windows, new FixedMeasure()); return editor;
    }

    private static NodeEditorNode Node(double x, double y, string title) => new(Guid.NewGuid(), "Action", title) { X = x, Y = y, Width = 220, Height = 118, Ports = Ports() };
    private static IReadOnlyList<NodeEditorPort> Ports() => [new NodeEditorPort("in", "In", NodeEditorPortDirection.Input, "flow", false), new NodeEditorPort("out", "Out", NodeEditorPortDirection.Output, "flow", false)];

    private sealed class FixedMeasure : IHavenMeasureContext
    {
        public HavenSize MeasureLeaf(HavenElement element, HavenSize available) => available;
    }
}
