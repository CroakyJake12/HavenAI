using Haven.UI;
using Haven.UI.Components;
using Xunit;

namespace Haven.UI.Tests;

public sealed class HavenInputRouterRecoveryTests
{
    [Fact]
    public void Pointer_drag_shortcuts_clipboard_and_tab_are_router_owned()
    {
        var root = new Container { Layout = HavenLayout.Vertical };
        var first = new Input { Text = "Alpha" };
        var second = new Input { Text = "Beta" };
        first.SetValue(HavenProperties.Width, HavenLength.Px(240));
        first.SetValue(HavenProperties.Height, HavenLength.Px(48));
        second.SetValue(HavenProperties.Width, HavenLength.Px(240));
        second.SetValue(HavenProperties.Height, HavenLength.Px(48));
        root.Add(first);
        root.Add(second);
        new HavenLayoutEngine().Layout(root, new HavenSize(260, 120), HavenPlatform.Windows, new FixedMeasure());

        var router = new HavenInputRouter(root)
        {
            InputCaretHitTest = (_, local) => local.X < 50 ? 1 : 4
        };
        string? copied = null;
        var pasteRequests = 0;
        router.ClipboardCopyRequested += text => copied = text;
        router.ClipboardPasteRequested += () => pasteRequests++;

        var start = new HavenPoint(first.Bounds.X + 20, first.Bounds.Y + 20);
        var end = new HavenPoint(first.Bounds.X + 120, first.Bounds.Y + 20);
        router.PointerPressed(start);
        router.PointerMoved(end);
        Assert.True(router.PointerReleased(end));
        Assert.Equal("lph", first.SelectedText);

        Assert.True(router.KeyDown(HavenKey.C, new HavenInputModifiers(Control: true)));
        Assert.Equal("lph", copied);
        Assert.True(router.KeyDown(HavenKey.X, new HavenInputModifiers(Control: true)));
        Assert.Equal("Aa", first.Text);
        Assert.True(router.KeyDown(HavenKey.Z, new HavenInputModifiers(Control: true)));
        Assert.Equal("Alpha", first.Text);
        Assert.True(router.KeyDown(HavenKey.Y, new HavenInputModifiers(Control: true)));
        Assert.Equal("Aa", first.Text);
        Assert.True(router.KeyDown(HavenKey.V, new HavenInputModifiers(Control: true)));
        Assert.Equal(1, pasteRequests);
        Assert.True(router.PasteText("ZZ"));
        Assert.Equal("AZZa", first.Text);

        Assert.True(router.KeyDown(HavenKey.Tab));
        Assert.Same(second, router.Focused);
        Assert.True(router.KeyDown(HavenKey.Tab, new HavenInputModifiers(Shift: true)));
        Assert.Same(first, router.Focused);
    }

    [Fact]
    public void Shift_navigation_extends_selection_and_command_enter_submits()
    {
        var input = new Input { Text = "Alpha", Multiline = true };
        var router = new HavenInputRouter(input);
        Input? submitted = null;
        router.InputSubmitted += value => submitted = value;
        router.Focus(input);

        Assert.True(router.KeyDown(HavenKey.Left, new HavenInputModifiers(Shift: true)));
        Assert.Equal("a", input.SelectedText);
        Assert.True(router.KeyDown(HavenKey.Home, new HavenInputModifiers(Shift: true)));
        Assert.Equal("Alpha", input.SelectedText);
        Assert.True(router.KeyDown(HavenKey.Enter, new HavenInputModifiers(Control: true)));
        Assert.Same(input, submitted);
    }

    [Fact]
    public void Child_hit_bubbles_hover_press_and_activation_to_focusable_parent()
    {
        var root = new Container();
        var button = new Button { Content = "Parent" };
        button.SetValue(HavenProperties.Width, HavenLength.Px(160));
        button.SetValue(HavenProperties.Height, HavenLength.Px(60));
        var child = new Container();
        child.SetValue(HavenProperties.Width, HavenLength.Px(40));
        child.SetValue(HavenProperties.Height, HavenLength.Px(30));
        button.Add(child);
        root.Add(button);
        new HavenLayoutEngine().Layout(root, new HavenSize(180, 80), HavenPlatform.Windows, new FixedMeasure());
        var router = new HavenInputRouter(root);
        var invoked = 0;
        button.Invoked += (_, _) => invoked++;
        var point = new HavenPoint(child.Bounds.X + 5, child.Bounds.Y + 5);

        router.PointerMoved(point);
        Assert.Same(button, router.Hovered);
        Assert.True(button.State.HasFlag(HavenElementState.Hover));
        router.PointerPressed(point);
        Assert.Same(button, router.Pressed);
        Assert.True(button.State.HasFlag(HavenElementState.Pressed));
        Assert.True(router.PointerReleased(point));
        Assert.Equal(1, invoked);
        router.PointerExited();
        Assert.False(button.State.HasFlag(HavenElementState.Hover));
    }

    private sealed class FixedMeasure : IHavenMeasureContext
    {
        public HavenSize MeasureLeaf(HavenElement element, HavenSize available) => new(available.Width, 44);
    }
}
