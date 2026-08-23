using Haven.UI.Components;
using Xunit;

namespace Haven.UI.Tests;

public sealed class NodeEditorKeyboardAccessibilityTests
{
    [Fact]
    public void Primary_arrow_keys_traverse_nodes_without_pointer_selection()
    {
        var first = Node("First");
        var second = Node("Second");
        var third = Node("Third");
        var editor = new NodeEditor { Document = new NodeEditorDocument([first, second, third], []) };

        Assert.True(editor.KeyDown(new HavenKeyInput(HavenKey.Right, HavenKeyModifiers.Control)));
        Assert.Equal(first.Id, Assert.Single(editor.SelectedNodeIds));
        Assert.True(editor.KeyDown(new HavenKeyInput(HavenKey.Right, HavenKeyModifiers.Control)));
        Assert.Equal(second.Id, Assert.Single(editor.SelectedNodeIds));
        Assert.True(editor.KeyDown(new HavenKeyInput(HavenKey.Left, HavenKeyModifiers.Control)));
        Assert.Equal(first.Id, Assert.Single(editor.SelectedNodeIds));
        Assert.True(editor.KeyDown(new HavenKeyInput(HavenKey.End, HavenKeyModifiers.Control)));
        Assert.Equal(third.Id, Assert.Single(editor.SelectedNodeIds));
    }

    private static NodeEditorNode Node(string title) => new(Guid.NewGuid(), "Action", title);
}
