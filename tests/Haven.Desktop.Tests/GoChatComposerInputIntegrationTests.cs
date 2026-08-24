using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Go;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class GoChatComposerInputIntegrationTests
{
    [Fact]
    public void Go_composer_keeps_plain_enter_multiline_and_ctrl_enter_submits()
    {
        using var page = new GoPage(new HavenEventBus());
        var input = page.Route.Instruction;
        var router = new HavenInputRouter(page.Route.Root);
        Input? submitted = null;
        router.InputSubmitted += value => submitted = value;
        input.Text = "First line";
        router.Focus(input);

        Assert.True(router.KeyDown(HavenKey.Enter, new HavenInputModifiers(Shift: true)));
        Assert.Equal("First line\n", input.Text);
        Assert.Null(submitted);

        Assert.True(router.KeyDown(HavenKey.Enter, new HavenInputModifiers(Control: true)));
        Assert.Same(input, submitted);
    }

    [Fact]
    public void Chat_composer_keeps_plain_enter_multiline_and_ctrl_enter_submits()
    {
        using var scene = new ChatHavenScene();
        var input = scene.Instruction;
        var router = new HavenInputRouter(scene.Root);
        Input? submitted = null;
        router.InputSubmitted += value => submitted = value;
        input.Text = "First line";
        router.Focus(input);

        Assert.True(router.KeyDown(HavenKey.Enter, new HavenInputModifiers(Shift: true)));
        Assert.Equal("First line\n", input.Text);
        Assert.Null(submitted);

        Assert.True(router.KeyDown(HavenKey.Enter, new HavenInputModifiers(Control: true)));
        Assert.Same(input, submitted);
    }
}
