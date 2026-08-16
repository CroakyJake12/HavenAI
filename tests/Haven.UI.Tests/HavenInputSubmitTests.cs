using Haven.UI;
using Haven.UI.Components;
using Xunit;

namespace Haven.UI.Tests;

public sealed class HavenInputSubmitTests
{
    [Fact]
    public void Configured_multiline_input_submits_on_enter_and_keeps_shift_enter_for_newline()
    {
        var input = new Input { Multiline = true, SubmitOnEnter = true };
        var router = new HavenInputRouter(input);
        Input? submitted = null;
        router.InputSubmitted += value => submitted = value;
        router.Focus(input);

        Assert.True(router.KeyDown(HavenKey.Enter));
        Assert.Same(input, submitted);
        Assert.Equal(string.Empty, input.Text);

        submitted = null;
        Assert.True(router.KeyDown(HavenKey.Enter, shift: true));
        Assert.Null(submitted);
        Assert.Equal("\n", input.Text);
    }

    [Fact]
    public void Submit_on_enter_is_configurable_from_hui_markup()
    {
        var root = new HavenMarkupParser().Parse("<Page><Input Multiline='true' SubmitOnEnter='true'/></Page>");
        var input = Assert.IsType<Input>(Assert.Single(root.Children));
        Assert.True(input.Multiline);
        Assert.True(input.SubmitOnEnter);
    }
}
