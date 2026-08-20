using Haven.UI;
using Haven.UI.Components;
using Xunit;

namespace Haven.UI.Tests;

public sealed class HavenUiInteractionTests
{
    [Fact]
    public void Secondary_pointer_invocation_is_distinct_from_primary_activation()
    {
        var root = new Container();
        var button = new Button { Content = "Tab" };
        button.SetValue(HavenProperties.Width, HavenLength.Px(120));
        button.SetValue(HavenProperties.Height, HavenLength.Px(48));
        root.Add(button);
        new HavenLayoutEngine().Layout(root, new HavenSize(140, 70), HavenPlatform.Windows, new FixedMeasure());

        var primaryInvoked = 0;
        var secondaryInvoked = 0;
        button.Invoked += (_, _) => primaryInvoked++;
        button.SecondaryInvoked += (_, _) => secondaryInvoked++;
        var router = new HavenInputRouter(root);
        var point = new HavenPoint(button.Bounds.X + 20, button.Bounds.Y + 20);

        router.PointerPressed(point, HavenPointerKind.Mouse, HavenPointerButton.Secondary);
        Assert.True(router.PointerReleased(point));
        Assert.Equal(0, primaryInvoked);
        Assert.Equal(1, secondaryInvoked);

        router.PointerPressed(point);
        Assert.True(router.PointerReleased(point));
        Assert.Equal(1, primaryInvoked);
        Assert.Equal(1, secondaryInvoked);
    }

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
    public void Input_selection_replace_cut_and_undo_redo_are_shared_state()
    {
        var input = new Input { Text = "Alpha Beta" };
        input.SelectAll();
        Assert.True(input.HasSelection);
        Assert.Equal("Alpha Beta", input.SelectedText);

        Assert.True(input.InsertText("Replaced"));
        Assert.Equal("Replaced", input.Text);
        Assert.False(input.HasSelection);

        Assert.True(input.Undo());
        Assert.Equal("Alpha Beta", input.Text);
        Assert.Equal("Alpha Beta", input.SelectedText);
        Assert.True(input.Redo());
        Assert.Equal("Replaced", input.Text);

        input.SetSelection(0, 3);
        Assert.True(input.CutSelection());
        Assert.Equal("laced", input.Text);
        Assert.True(input.Undo());
        Assert.Equal("Replaced", input.Text);
        Assert.Equal("Rep", input.SelectedText);
    }

    [Fact]
    public void Pointer_modifier_payload_is_backend_neutral()
    {
        var modifiers = HavenKeyModifiers.Shift | HavenKeyModifiers.Control;
        var input = new HavenPointerInput(
            new HavenPoint(12, 14),
            new HavenPoint(2, 4),
            HavenPointerKind.Mouse,
            modifiers,
            HavenPointerButton.Secondary);
        Assert.True(input.Modifiers.HasFlag(HavenKeyModifiers.Shift));
        Assert.True(input.Modifiers.HasFlag(HavenKeyModifiers.Control));
        Assert.Equal(HavenPointerButton.Secondary, input.Button);
    }

    [Fact]
    public void Raw_pointer_target_keeps_capture_outside_bounds_and_consumes_click()
    {
        var root = new Container();
        var target = new PointerTarget();
        target.SetValue(HavenProperties.Width, HavenLength.Px(100));
        target.SetValue(HavenProperties.Height, HavenLength.Px(60));
        root.Add(target);
        new HavenLayoutEngine().Layout(root, new HavenSize(120, 80), HavenPlatform.Windows, new FixedMeasure());
        var router = new HavenInputRouter(root);
        var invoked = 0;
        target.Invoked += (_, _) => invoked++;

        router.PointerPressed(new HavenPoint(20, 20), HavenPointerKind.Pen);
        router.PointerMoved(new HavenPoint(180, 140), HavenPointerKind.Pen);
        Assert.True(router.PointerReleased(new HavenPoint(190, 150)));

        Assert.Equal(1, target.PressedCount);
        Assert.Equal(1, target.MovedCount);
        Assert.Equal(1, target.ReleasedCount);
        Assert.Equal(HavenPointerKind.Pen, target.LastPointerKind);
        Assert.True(target.LastLocalPosition.X > target.Bounds.Width);
        Assert.Equal(0, invoked);
    }

    [Fact]
    public void Raw_pointer_capture_loss_releases_target_without_click_activation()
    {
        var root = new Container();
        var target = new PointerTarget();
        target.SetValue(HavenProperties.Width, HavenLength.Px(100));
        target.SetValue(HavenProperties.Height, HavenLength.Px(60));
        root.Add(target);
        new HavenLayoutEngine().Layout(root, new HavenSize(120, 80), HavenPlatform.Windows, new FixedMeasure());
        var router = new HavenInputRouter(root);
        var invoked = 0;
        target.Invoked += (_, _) => invoked++;

        router.PointerPressed(new HavenPoint(20, 20), HavenPointerKind.Pen);
        router.PointerMoved(new HavenPoint(180, 140), HavenPointerKind.Pen, new HavenInputModifiers(Shift: true));

        Assert.True(router.CancelPointer());
        Assert.Equal(1, target.PressedCount);
        Assert.Equal(1, target.MovedCount);
        Assert.Equal(1, target.CancelledCount);
        Assert.Equal(0, target.ReleasedCount);
        Assert.Equal(HavenPointerKind.Pen, target.LastPointerKind);
        Assert.True(target.LastLocalPosition.X > target.Bounds.Width);
        Assert.Null(router.Pressed);
        Assert.Equal(0, invoked);
        Assert.False(router.CancelPointer());
        Assert.False(router.PointerReleased(new HavenPoint(20, 20)));
    }

    [Fact]
    public void Raw_scroll_target_consumes_wheel_before_container_scroll()
    {
        var root = new Container();
        var target = new PointerTarget();
        target.SetValue(HavenProperties.Width, HavenLength.Px(100));
        target.SetValue(HavenProperties.Height, HavenLength.Px(60));
        root.Add(target);
        new HavenLayoutEngine().Layout(root, new HavenSize(120, 80), HavenPlatform.Windows, new FixedMeasure());
        var router = new HavenInputRouter(root);

        Assert.True(router.Scroll(new HavenPoint(20, 20), 0, -48));
        Assert.Equal(1, target.WheelCount);
        Assert.Equal(-48, target.LastWheelDeltaY);
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


    private sealed class PointerTarget : HavenElement, IHavenPointerInputTarget, IHavenScrollInputTarget
    {
        public int PressedCount { get; private set; }
        public int MovedCount { get; private set; }
        public int ReleasedCount { get; private set; }
        public int CancelledCount { get; private set; }
        public int WheelCount { get; private set; }
        public HavenPointerKind LastPointerKind { get; private set; }
        public HavenPoint LastLocalPosition { get; private set; }
        public double LastWheelDeltaY { get; private set; }

        public bool PointerPressed(HavenPointerInput input)
        {
            PressedCount++;
            LastPointerKind = input.PointerKind;
            LastLocalPosition = input.LocalPosition;
            return true;
        }

        public bool PointerMoved(HavenPointerInput input)
        {
            MovedCount++;
            LastPointerKind = input.PointerKind;
            LastLocalPosition = input.LocalPosition;
            return true;
        }

        public bool PointerReleased(HavenPointerInput input)
        {
            ReleasedCount++;
            LastPointerKind = input.PointerKind;
            LastLocalPosition = input.LocalPosition;
            return true;
        }

        public bool PointerCancelled(HavenPointerInput input)
        {
            CancelledCount++;
            LastPointerKind = input.PointerKind;
            LastLocalPosition = input.LocalPosition;
            return true;
        }

        public bool PointerWheel(HavenPoint localPosition, double deltaX, double deltaY)
        {
            WheelCount++;
            LastLocalPosition = localPosition;
            LastWheelDeltaY = deltaY;
            return true;
        }
    }

    private sealed class FixedMeasure : IHavenMeasureContext
    {
        public HavenSize MeasureLeaf(HavenElement element, HavenSize available) => new(available.Width, 44);
    }
}
