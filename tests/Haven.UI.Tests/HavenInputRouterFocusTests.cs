using Haven.UI.Components;
using Xunit;

namespace Haven.UI.Tests;

public sealed class HavenInputRouterFocusTests
{
    [Fact]
    public void Text_input_remaps_focus_to_same_named_replacement_after_dynamic_rebuild()
    {
        var root = new Container();
        var original = new Input { Name = "Editor.Body", Text = "old" };
        root.Add(original);
        var router = new HavenInputRouter(root);
        router.Focus(original);

        root.Remove(original);
        var replacement = new Input { Name = "Editor.Body" };
        root.Add(replacement);

        Assert.True(router.TextInput("x"));
        Assert.Same(replacement, router.Focused);
        Assert.Equal("x", replacement.Text);
        Assert.Equal("old", original.Text);
        Assert.False(original.State.HasFlag(HavenElementState.Focused));
    }
}
