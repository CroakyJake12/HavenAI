using Haven.UI;
using Haven.UI.Components;
using Xunit;

namespace Haven.UI.Tests;

public sealed class HavenSecretInputTests
{
    [Fact]
    public void Hidden_secret_uses_masked_render_selection_and_caret_layouts()
    {
        var root = new Container();
        var input = new Input { Text = "s3cr3t", IsSecret = true };
        input.SetValue(HavenProperties.Width, HavenLength.Px(180));
        input.SetValue(HavenProperties.Height, HavenLength.Px(48));
        root.Add(input);
        new HavenLayoutEngine().Layout(root, new HavenSize(220, 80), HavenPlatform.Windows, new FixedMeasure());
        input.SetState(HavenElementState.Focused, true);
        input.SetSelection(1, 5);

        var commands = new HavenSceneRenderer().Render(root);
        var text = Assert.Single(commands.OfType<HavenTextCommand>());
        var selection = Assert.Single(commands.OfType<HavenTextSelectionCommand>());
        var caret = Assert.Single(commands.OfType<HavenCaretCommand>());

        Assert.Equal("••••••", text.Layout.Text);
        Assert.Equal("••••••", selection.Layout.Text);
        Assert.Equal("••••••", caret.FullLayout?.Text);
        Assert.DoesNotContain("s3cr3t", caret.PrefixLayout.Text, StringComparison.Ordinal);

        input.RevealSecret = true;
        var revealed = new HavenSceneRenderer().Render(root).OfType<HavenTextCommand>().Single();
        Assert.Equal("s3cr3t", revealed.Layout.Text);
    }

    [Fact]
    public void Secret_copy_and_cut_require_explicit_clipboard_opt_in()
    {
        var root = new Container();
        var input = new Input { Text = "token-123", IsSecret = true };
        input.SetValue(HavenProperties.Width, HavenLength.Px(180));
        input.SetValue(HavenProperties.Height, HavenLength.Px(48));
        root.Add(input);
        new HavenLayoutEngine().Layout(root, new HavenSize(220, 80), HavenPlatform.Windows, new FixedMeasure());
        var router = new HavenInputRouter(root);
        router.Focus(input);
        input.SelectAll();
        string? copied = null;
        router.ClipboardCopyRequested += value => copied = value;
        var control = new HavenInputModifiers(Control: true);

        Assert.True(router.KeyDown(HavenKey.C, control));
        Assert.Null(copied);
        Assert.True(router.KeyDown(HavenKey.X, control));
        Assert.Equal("token-123", input.Text);

        input.AllowSecretClipboard = true;
        Assert.True(router.KeyDown(HavenKey.C, control));
        Assert.Equal("token-123", copied);
        input.SelectAll();
        Assert.True(router.KeyDown(HavenKey.X, control));
        Assert.Equal(string.Empty, input.Text);
    }

    [Fact]
    public void Markup_can_declare_secret_and_clipboard_policy()
    {
        var root = new HavenMarkupParser().Parse("<Page><Input Text='abc' Secret='true' AllowSecretClipboard='true'/></Page>");
        var input = Assert.IsType<Input>(root.Children[0]);

        Assert.True(input.IsSecret);
        Assert.True(input.Accessibility.IsPassword);
        Assert.True(input.AllowSecretClipboard);
        Assert.Equal("•••", input.DisplayText);
    }

    private sealed class FixedMeasure : IHavenMeasureContext
    {
        public HavenSize MeasureLeaf(HavenElement element, HavenSize available) => new(available.Width, 44);
    }
}
