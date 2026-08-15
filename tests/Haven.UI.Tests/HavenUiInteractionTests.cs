using Haven.UI;
using Haven.UI.Components;
using Xunit;

namespace Haven.UI.Tests;

public sealed class HavenUiInteractionTests
{
    [Fact]
    public void Slider_pointer_and_keyboard_input_stays_in_haven_runtime()
    {
        var root = new Container();
        var slider = new Slider { Minimum = 0, Maximum = 3, Step = 1 };
        slider.SetValue(HavenProperties.Width, HavenLength.Px(300));
        root.Add(slider);
        new HavenLayoutEngine().Layout(root, new HavenSize(300, 80), HavenPlatform.Windows, new FixedMeasure());
        var router = new HavenInputRouter(root);
        var point = new HavenPoint(slider.Bounds.X + 210, slider.Bounds.Y + 10);
        router.PointerPressed(point);
        router.PointerReleased(point);
        Assert.Equal(2, slider.Value);
        router.Focus(slider);
        Assert.True(router.KeyDown(HavenKey.Right));
        Assert.Equal(3, slider.Value);
        Assert.True(router.KeyDown(HavenKey.Left));
        Assert.Equal(2, slider.Value);
    }

    [Fact]
    public void Toggle_pointer_and_keyboard_activation_updates_checked_state()
    {
        var root = new Container();
        var toggle = new Toggle();
        root.Add(toggle);
        new HavenLayoutEngine().Layout(root, new HavenSize(100, 60), HavenPlatform.Windows, new FixedMeasure());
        var router = new HavenInputRouter(root);
        var point = new HavenPoint(toggle.Bounds.X + 10, toggle.Bounds.Y + 10);
        router.PointerPressed(point);
        router.PointerReleased(point);
        Assert.True(toggle.IsChecked);
        router.Focus(toggle);
        Assert.True(router.KeyDown(HavenKey.Space));
        Assert.True(router.KeyUp(HavenKey.Space));
        Assert.False(toggle.IsChecked);
    }

    [Fact]
    public void Select_keyboard_navigation_and_activation_are_haven_owned()
    {
        var select = new Select { Items = ["Super Bright", "Bright", "Dark", "Super Dark"] };
        var router = new HavenInputRouter(select);
        router.Focus(select);
        Assert.True(router.KeyDown(HavenKey.Down));
        Assert.Equal("Super Bright", select.SelectedItem);
        Assert.True(router.KeyDown(HavenKey.End));
        Assert.Equal("Super Dark", select.SelectedItem);
        Assert.True(router.KeyDown(HavenKey.Enter));
        Assert.True(router.KeyUp(HavenKey.Enter));
        Assert.True(select.IsExpanded);
        Assert.True(router.KeyDown(HavenKey.Escape));
        Assert.False(select.IsExpanded);
    }

    [Fact]
    public void Input_text_editing_caret_and_submit_are_haven_owned()
    {
        var input = new Input { Text = "Hi" };
        input.SetValue(HavenProperties.Width, HavenLength.Px(300));
        input.SetValue(HavenProperties.Height, HavenLength.Px(48));
        new HavenLayoutEngine().Layout(input, new HavenSize(300, 48), HavenPlatform.Windows, new FixedMeasure());
        var router = new HavenInputRouter(input);
        Input? submitted = null;
        router.InputSubmitted += value => submitted = value;

        router.Focus(input);
        Assert.Equal(2, input.CaretIndex);
        Assert.True(router.TextInput("!"));
        Assert.Equal("Hi!", input.Text);
        Assert.Equal(3, input.CaretIndex);
        Assert.True(router.KeyDown(HavenKey.Left));
        Assert.Equal(2, input.CaretIndex);
        Assert.True(router.KeyDown(HavenKey.Backspace));
        Assert.Equal("H!", input.Text);
        Assert.Equal(1, input.CaretIndex);
        Assert.True(router.KeyDown(HavenKey.Home));
        Assert.True(router.TextInput("A"));
        Assert.Equal("AH!", input.Text);
        Assert.True(router.KeyDown(HavenKey.Enter));
        Assert.Same(input, submitted);
        Assert.Contains(new HavenSceneRenderer().Render(input), command => command is HavenCaretCommand);
    }

    [Fact]
    public void Markup_supports_slider_step_and_select_items_without_platform_controls()
    {
        var root = new HavenMarkupParser().Parse("<Page><Slider Minimum='0' Maximum='3' Step='1' Value='2'/><Select Items='Bright, Dark, Super Dark' SelectedIndex='1'/></Page>");
        var slider = Assert.IsType<Slider>(root.Children[0]);
        var select = Assert.IsType<Select>(root.Children[1]);
        Assert.Equal(1, slider.Step);
        Assert.Equal(2, slider.Value);
        Assert.Equal("Dark", select.SelectedItem);
    }

    [Fact]
    public void Button_release_requests_the_shared_bounce_animation()
    {
        var button = new Button();
        button.SetState(HavenElementState.Pressed, true);
        button.SetState(HavenElementState.Pressed, false);
        Assert.Equal(ButtonDefaults.ReleaseAnimation, button.GetValue(HavenProperties.Animation));
    }

    [Fact]
    public void Inline_hui_text_maps_to_the_canonical_text_and_button_content()
    {
        var root = new HavenMarkupParser().Parse("<Page><Text>Hello</Text><Button>Continue</Button></Page>");
        Assert.Equal("Hello", Assert.IsType<Text>(root.Children[0]).Content);
        Assert.Equal("Continue", Assert.IsType<Button>(root.Children[1]).Content);
    }

    [Fact]
    public void Embedded_resources_parse_as_an_executable_resource_set()
    {
        var resources = HavenResourceSet.LoadEmbedded();
        Assert.Equal(TimeSpan.FromMilliseconds(170), resources.ResolveAnimation(ButtonDefaults.ReleaseAnimation).Duration);
        Assert.False(resources.TryResolveAnimation("DoesNotExist", out var definition));
        Assert.Null(definition);
    }

    private sealed class FixedMeasure : IHavenMeasureContext
    {
        public HavenSize MeasureLeaf(HavenElement element, HavenSize available) => new(available.Width, 44);
    }
}
