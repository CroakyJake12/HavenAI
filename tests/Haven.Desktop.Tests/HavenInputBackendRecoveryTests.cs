using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class HavenInputBackendRecoveryTests
{
    [AvaloniaFact]
    public void Backend_translates_all_shared_shortcut_modifiers_and_keys()
    {
        var modifiers = HavenSceneControl.ToHavenModifiers(
            KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta);

        Assert.True(modifiers.Shift);
        Assert.True(modifiers.Control);
        Assert.True(modifiers.Alt);
        Assert.True(modifiers.Meta);
        Assert.Equal(HavenKey.A, HavenSceneControl.MapInputKey(Key.A));
        Assert.Equal(HavenKey.C, HavenSceneControl.MapInputKey(Key.C));
        Assert.Equal(HavenKey.D, HavenSceneControl.MapInputKey(Key.D));
        Assert.Equal(HavenKey.F, HavenSceneControl.MapInputKey(Key.F));
        Assert.Equal(HavenKey.V, HavenSceneControl.MapInputKey(Key.V));
        Assert.Equal(HavenKey.X, HavenSceneControl.MapInputKey(Key.X));
        Assert.Equal(HavenKey.Y, HavenSceneControl.MapInputKey(Key.Y));
        Assert.Equal(HavenKey.Z, HavenSceneControl.MapInputKey(Key.Z));
    }

    [AvaloniaFact]
    public void Input_caret_hit_testing_uses_real_platform_glyph_metrics()
    {
        var root = new Haven.UI.Components.Page { Layout = HavenLayout.Vertical };
        var wide = CreateInput("WWWW");
        var narrow = CreateInput("iiii");
        root.Add(wide);
        root.Add(narrow);

        var scene = new HavenSceneControl { Root = root };
        var window = new Window { Width = 320, Height = 180, Content = scene };
        try
        {
            window.Show();
            window.UpdateLayout();

            var sameVisualX = 58d;
            var wideCaret = scene.HitTestInputCaret(wide, new HavenPoint(sameVisualX, 32));
            var narrowCaret = scene.HitTestInputCaret(narrow, new HavenPoint(sameVisualX, 32));

            Assert.True(
                narrowCaret > wideCaret,
                $"Expected proportional glyph metrics to advance farther through narrow glyphs, but wide={wideCaret} and narrow={narrowCaret}.");
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Multiline_selection_and_caret_render_from_full_platform_layout()
    {
        var input = CreateInput("Alpha\nBeta");
        input.Multiline = true;
        input.SetValue(HavenProperties.Height, HavenLength.Px(120));
        var router = new HavenInputRouter(input);
        router.Focus(input);
        input.SetSelection(0, 7);

        var scene = new HavenSceneControl { Root = input };
        var window = new Window { Width = 320, Height = 180, Content = scene };
        try
        {
            window.Show();
            window.UpdateLayout();

            var commands = new HavenSceneRenderer().Render(input);
            var selection = Assert.Single(commands.OfType<HavenTextSelectionCommand>());
            var caret = Assert.Single(commands.OfType<HavenCaretCommand>());
            var text = Assert.Single(commands.OfType<HavenTextCommand>());

            Assert.Equal(input.Text, selection.Layout.Text);
            Assert.Equal(0, selection.SelectionStart);
            Assert.Equal(7, selection.SelectionLength);
            Assert.False(selection.Layout.CenterVertically);
            Assert.False(text.Layout.CenterVertically);
            Assert.Equal(input.Text, caret.FullLayout?.Text);
            Assert.Equal(7, caret.CaretIndex);
            Assert.False(caret.FullLayout!.CenterVertically);

            var selectionRects = HavenSceneControl.ResolveSelectionRects(selection);
            Assert.True(selectionRects.Count >= 2);
            Assert.True(selectionRects[1].Y > selectionRects[0].Y);

            var firstLineCaret = HavenSceneControl.ResolveCaretRect(caret with { CaretIndex = 2 });
            var secondLineCaret = HavenSceneControl.ResolveCaretRect(caret with { CaretIndex = 7 });
            Assert.True(secondLineCaret.Y > firstLineCaret.Y);
            Assert.InRange(Math.Abs(firstLineCaret.Y - caret.Rect.Y), 0d, .75d);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Single_line_caret_remains_vertically_centered()
    {
        var input = CreateInput("Alpha");
        input.SetValue(HavenProperties.Height, HavenLength.Px(96));
        var router = new HavenInputRouter(input);
        router.Focus(input);

        var scene = new HavenSceneControl { Root = input };
        var window = new Window { Width = 320, Height = 160, Content = scene };
        try
        {
            window.Show();
            window.UpdateLayout();
            var caret = Assert.Single(new HavenSceneRenderer().Render(input).OfType<HavenCaretCommand>());
            Assert.True(caret.FullLayout!.CenterVertically);
            var rect = HavenSceneControl.ResolveCaretRect(caret);
            Assert.True(rect.Y > caret.Rect.Y);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Shared_router_uses_visual_line_navigation_and_preserves_shift_selection()
    {
        var input = CreateInput("Alpha\nBeta\nGamma");
        input.Multiline = true;
        var router = new HavenInputRouter(input)
        {
            InputCaretNavigation = (_, key) => key switch
            {
                HavenKey.Home => 6,
                HavenKey.End => 10,
                HavenKey.Up => 2,
                HavenKey.Down => 13,
                _ => input.CaretIndex
            }
        };
        router.Focus(input);

        input.SetSelection(8, 8);
        Assert.True(router.KeyDown(HavenKey.Home));
        Assert.Equal(6, input.CaretIndex);

        input.SetSelection(8, 8);
        Assert.True(router.KeyDown(HavenKey.Home, new HavenInputModifiers(Shift: true)));
        Assert.Equal("Be", input.SelectedText);

        input.SetSelection(8, 8);
        Assert.True(router.KeyDown(HavenKey.Up));
        Assert.Equal(2, input.CaretIndex);
        input.SetSelection(8, 8);
        Assert.True(router.KeyDown(HavenKey.Down));
        Assert.Equal(13, input.CaretIndex);

        input.SetSelection(8, 8);
        Assert.True(router.KeyDown(HavenKey.Home, new HavenInputModifiers(Control: true)));
        Assert.Equal(0, input.CaretIndex);
        Assert.True(router.KeyDown(HavenKey.End, new HavenInputModifiers(Control: true)));
        Assert.Equal(input.Text.Length, input.CaretIndex);
    }

    [AvaloniaFact]
    public void Backend_navigation_uses_wrapped_visual_lines_not_only_newlines()
    {
        var input = CreateInput("MMMM MMMM MMMM");
        input.Multiline = true;
        input.SetValue(HavenProperties.Width, HavenLength.Px(120));
        input.SetValue(HavenProperties.Height, HavenLength.Px(180));
        var scene = new HavenSceneControl { Root = input };
        var window = new Window { Width = 180, Height = 220, Content = scene };
        try
        {
            window.Show();
            window.UpdateLayout();
            input.SetSelection(input.Text.Length, input.Text.Length);

            var lineStart = scene.NavigateInputCaret(input, HavenKey.Home);
            var up = scene.NavigateInputCaret(input, HavenKey.Up);

            Assert.InRange(lineStart, 1, input.Text.Length - 1);
            Assert.InRange(up, 0, input.Text.Length - 1);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Expanded_select_popup_is_viewport_aware_and_flips_above_near_bottom()
    {
        var root = new Haven.UI.Components.Page { Layout = HavenLayout.Canvas };
        root.SetValue(HavenProperties.Width, HavenLength.Px(240));
        root.SetValue(HavenProperties.Height, HavenLength.Px(220));
        var select = new Select
        {
            Items = ["Alpha", "Beta", "Gamma", "Delta"],
            SelectedIndex = 2,
            IsExpanded = true
        };
        select.SetValue(HavenProperties.Left, HavenLength.Px(24));
        select.SetValue(HavenProperties.Top, HavenLength.Px(164));
        select.SetValue(HavenProperties.Width, HavenLength.Px(180));
        select.SetValue(HavenProperties.Height, HavenLength.Px(48));
        root.Add(select);
        new HavenLayoutEngine().Layout(root, new HavenSize(240, 220), HavenPlatform.Windows, new FixedMeasure());
        var viewport = root.Bounds;

        var popup = select.GetPopupLayout(viewport);
        Assert.NotNull(popup);
        Assert.True(popup!.OpensAbove);
        Assert.True(popup.Bounds.Bottom <= select.Bounds.Y);
        Assert.True(popup.Bounds.X >= viewport.X);
        Assert.True(popup.Bounds.Right <= viewport.Right);
        Assert.All(popup.Items, item => Assert.True(viewport.Contains(new HavenPoint(item.Bounds.X + 1, item.Bounds.Y + 1))));
    }

    [AvaloniaFact]
    public void Expanded_select_renders_as_a_scene_overlay_after_parent_clips()
    {
        var root = new Haven.UI.Components.Page { Layout = HavenLayout.Canvas };
        root.SetValue(HavenProperties.Width, HavenLength.Px(320));
        root.SetValue(HavenProperties.Height, HavenLength.Px(240));
        var clippedParent = new Container { Layout = HavenLayout.Canvas };
        clippedParent.SetValue(HavenProperties.Clip, true);
        clippedParent.SetValue(HavenProperties.Left, HavenLength.Px(20));
        clippedParent.SetValue(HavenProperties.Top, HavenLength.Px(20));
        clippedParent.SetValue(HavenProperties.Width, HavenLength.Px(220));
        clippedParent.SetValue(HavenProperties.Height, HavenLength.Px(70));
        var select = new Select
        {
            Items = ["One", "Two", "Three"],
            SelectedIndex = 1,
            IsExpanded = true
        };
        select.SetValue(HavenProperties.Left, HavenLength.Px(10));
        select.SetValue(HavenProperties.Top, HavenLength.Px(16));
        select.SetValue(HavenProperties.Width, HavenLength.Px(180));
        select.SetValue(HavenProperties.Height, HavenLength.Px(48));
        root.Add(clippedParent);
        clippedParent.Add(select);
        new HavenLayoutEngine().Layout(root, new HavenSize(320, 240), HavenPlatform.Windows, new FixedMeasure());

        var popup = select.GetPopupLayout(root.Bounds);
        Assert.NotNull(popup);
        var commands = new HavenSceneRenderer().Render(root).ToList();
        var parentPop = commands.FindLastIndex(command => command is HavenPopClipCommand pop && pop.Rect == clippedParent.Bounds);
        var popupFill = commands.FindIndex(command => command is HavenFillRoundedRectCommand fill
            && fill.Rect == popup!.Bounds
            && fill.Brush is HavenTokenBrush { Token: "SurfaceRaised" });

        Assert.True(parentPop >= 0);
        Assert.True(popupFill > parentPop);
        Assert.Contains(commands, command => command is HavenFillRoundedRectCommand fill
            && fill.Rect == popup!.Items.Single(item => item.Index == 1).Bounds
            && fill.Brush is HavenTokenBrush { Token: "AccentMuted" });
        Assert.All(popup!.Items, item => Assert.Contains(commands, command => command is HavenTextCommand text
            && text.Layout.Text == item.Text
            && text.Rect == new HavenRect(item.Bounds.X + 12d, item.Bounds.Y, Math.Max(0d, item.Bounds.Width - 24d), item.Bounds.Height)));
    }

    [AvaloniaFact]
    public void Expanded_select_popup_accepts_pointer_selection_outside_scene_tree()
    {
        var root = new Haven.UI.Components.Page { Layout = HavenLayout.Canvas };
        root.SetValue(HavenProperties.Width, HavenLength.Px(320));
        root.SetValue(HavenProperties.Height, HavenLength.Px(260));
        var select = new Select
        {
            Items = ["One", "Two", "Three"],
            SelectedIndex = 0,
            IsExpanded = true
        };
        select.SetValue(HavenProperties.Left, HavenLength.Px(20));
        select.SetValue(HavenProperties.Top, HavenLength.Px(20));
        select.SetValue(HavenProperties.Width, HavenLength.Px(180));
        select.SetValue(HavenProperties.Height, HavenLength.Px(48));
        root.Add(select);
        new HavenLayoutEngine().Layout(root, new HavenSize(320, 260), HavenPlatform.Windows, new FixedMeasure());

        var popup = select.GetPopupLayout(root.Bounds);
        Assert.NotNull(popup);
        var row = popup!.Items.Single(item => item.Index == 2);
        var point = new HavenPoint(row.Bounds.X + row.Bounds.Width / 2d, row.Bounds.Y + row.Bounds.Height / 2d);
        var router = new HavenInputRouter(root);

        router.PointerPressed(point, HavenPointerKind.Mouse, HavenPointerButton.Primary);
        Assert.True(router.PointerReleased(point));
        Assert.Equal(2, select.SelectedIndex);
        Assert.False(select.IsExpanded);
    }

    [AvaloniaFact]
    public void Outside_click_dismisses_expanded_select_and_still_activates_underlying_control()
    {
        var root = new Haven.UI.Components.Page { Layout = HavenLayout.Canvas };
        root.SetValue(HavenProperties.Width, HavenLength.Px(380));
        root.SetValue(HavenProperties.Height, HavenLength.Px(260));
        var select = new Select
        {
            Items = ["One", "Two", "Three"],
            SelectedIndex = 1,
            IsExpanded = true
        };
        select.SetValue(HavenProperties.Left, HavenLength.Px(20));
        select.SetValue(HavenProperties.Top, HavenLength.Px(20));
        select.SetValue(HavenProperties.Width, HavenLength.Px(180));
        select.SetValue(HavenProperties.Height, HavenLength.Px(48));
        var button = new Haven.UI.Components.Button { Content = "Apply" };
        button.SetValue(HavenProperties.Left, HavenLength.Px(240));
        button.SetValue(HavenProperties.Top, HavenLength.Px(20));
        button.SetValue(HavenProperties.Width, HavenLength.Px(100));
        button.SetValue(HavenProperties.Height, HavenLength.Px(48));
        var invoked = 0;
        button.Invoked += (_, _) => invoked++;
        root.Add(select);
        root.Add(button);
        new HavenLayoutEngine().Layout(root, new HavenSize(380, 260), HavenPlatform.Windows, new FixedMeasure());
        var point = new HavenPoint(button.Bounds.X + button.Bounds.Width / 2d, button.Bounds.Y + button.Bounds.Height / 2d);
        var router = new HavenInputRouter(root);

        router.PointerPressed(point, HavenPointerKind.Mouse, HavenPointerButton.Primary);
        Assert.False(select.IsExpanded);
        Assert.True(router.PointerReleased(point));
        Assert.Equal(1, invoked);
    }

    [AvaloniaFact]
    public void Select_keyboard_toggles_popup_navigates_and_escapes()
    {
        var select = new Select
        {
            Items = ["One", "Two", "Three"],
            SelectedIndex = 0
        };
        var router = new HavenInputRouter(select);
        router.Focus(select);

        Assert.True(router.KeyDown(HavenKey.Enter));
        Assert.True(router.KeyUp(HavenKey.Enter));
        Assert.True(select.IsExpanded);
        Assert.True(router.KeyDown(HavenKey.Down));
        Assert.Equal(1, select.SelectedIndex);
        Assert.True(router.KeyDown(HavenKey.Escape));
        Assert.False(select.IsExpanded);

        Assert.True(router.KeyDown(HavenKey.Space));
        Assert.True(router.KeyUp(HavenKey.Space));
        Assert.True(select.IsExpanded);
    }

    [AvaloniaFact]
    public void Tab_focus_change_closes_expanded_select()
    {
        var root = new Haven.UI.Components.Page();
        var first = new Select
        {
            Items = ["One", "Two"],
            SelectedIndex = 0,
            IsExpanded = true
        };
        var second = new Select
        {
            Items = ["Alpha", "Beta"],
            SelectedIndex = 0
        };
        root.Add(first);
        root.Add(second);
        var router = new HavenInputRouter(root);
        router.Focus(first);

        Assert.True(router.KeyDown(HavenKey.Tab));
        Assert.False(first.IsExpanded);
        Assert.Same(second, router.Focused);
    }

    [AvaloniaFact]
    public void Outside_scene_pointer_dismisses_select_and_preserves_notification()
    {
        var root = new Haven.UI.Components.Page();
        var select = new Select
        {
            Items = ["One", "Two"],
            SelectedIndex = 0,
            IsExpanded = true
        };
        root.Add(select);
        var host = new HavenSceneControl { Root = root };
        var notifications = 0;
        host.PointerPressedOutside += () => notifications++;

        host.NotifyPointerPressedOutside();

        Assert.False(select.IsExpanded);
        Assert.Equal(1, notifications);
    }

    [AvaloniaFact]
    public void Long_select_popup_wheel_scrolls_visible_window_without_changing_selection()
    {
        var root = new Haven.UI.Components.Page { Layout = HavenLayout.Canvas };
        root.SetValue(HavenProperties.Width, HavenLength.Px(320));
        root.SetValue(HavenProperties.Height, HavenLength.Px(220));
        var select = new Select
        {
            Items = Enumerable.Range(0, 10).Select(index => $"Option {index}").ToArray(),
            SelectedIndex = 0,
            IsExpanded = true
        };
        select.SetValue(HavenProperties.Left, HavenLength.Px(20));
        select.SetValue(HavenProperties.Top, HavenLength.Px(20));
        select.SetValue(HavenProperties.Width, HavenLength.Px(180));
        select.SetValue(HavenProperties.Height, HavenLength.Px(48));
        root.Add(select);
        new HavenLayoutEngine().Layout(root, new HavenSize(320, 220), HavenPlatform.Windows, new FixedMeasure());

        var before = select.GetPopupLayout(root.Bounds);
        Assert.NotNull(before);
        Assert.Equal(0, before!.Items[0].Index);
        var point = new HavenPoint(before.Bounds.X + before.Bounds.Width / 2d, before.Bounds.Y + before.Bounds.Height / 2d);
        var router = new HavenInputRouter(root);

        Assert.True(router.Scroll(point, 0, -48d));
        Assert.Equal(0, select.GetPopupLayout(root.Bounds)!.Items[0].Index);
        Assert.True(router.Scroll(point, 0, 48d));
        var after = select.GetPopupLayout(root.Bounds);

        Assert.NotNull(after);
        Assert.True(after!.Items[0].Index > 0);
        Assert.Equal(0, select.SelectedIndex);
        Assert.True(select.IsExpanded);
    }

    [AvaloniaFact]
    public void Custom_keyboard_target_gets_first_refusal_for_tab_then_falls_back_to_focus_traversal()
    {
        var root = new Haven.UI.Components.Page();
        var custom = new KeyboardTarget { ConsumeTab = true };
        var next = new Haven.UI.Components.Button { Content = "Next" };
        root.Add(custom);
        root.Add(next);
        var router = new HavenInputRouter(root);
        router.Focus(custom);

        Assert.True(router.KeyDown(HavenKey.Tab, new HavenInputModifiers(Shift: true, Control: true)));
        Assert.Same(custom, router.Focused);
        Assert.Equal(HavenKey.Tab, custom.LastKeyDown?.Key);
        Assert.True(custom.LastKeyDown.HasValue && custom.LastKeyDown.Value.Shift);
        Assert.True(custom.LastKeyDown.HasValue && custom.LastKeyDown.Value.Control);
        Assert.True(router.KeyUp(HavenKey.Tab));
        Assert.Equal(HavenKey.Tab, custom.LastKeyUp?.Key);

        custom.ConsumeTab = false;
        Assert.True(router.KeyDown(HavenKey.Tab));
        Assert.Same(next, router.Focused);
    }

    [AvaloniaFact]
    public void Custom_clipboard_and_text_targets_use_shared_platform_bridge()
    {
        var target = new ClipboardTarget();
        var router = new HavenInputRouter(target);
        router.Focus(target);
        string? copied = null;
        var pasteRequests = 0;
        router.ClipboardCopyRequested += text => copied = text;
        router.ClipboardPasteRequested += () => pasteRequests++;

        Assert.True(router.KeyDown(HavenKey.C, new HavenInputModifiers(Control: true)));
        Assert.Equal("copy-value", copied);

        copied = null;
        Assert.True(router.KeyDown(HavenKey.X, new HavenInputModifiers(Meta: true)));
        Assert.Equal("cut-value", copied);

        Assert.True(router.KeyDown(HavenKey.V, new HavenInputModifiers(Control: true)));
        Assert.Equal(1, pasteRequests);
        Assert.True(router.PasteText("pasted-value"));
        Assert.Equal("pasted-value", target.PastedText);

        Assert.True(router.TextInput("typed-value"));
        Assert.Equal("typed-value", target.TypedText);
    }

    private sealed class ClipboardTarget : Container, IHavenClipboardInputTarget, IHavenTextInputTarget
    {
        public ClipboardTarget() => Accessibility.Focusable = true;
        public string? PastedText { get; private set; }
        public string? TypedText { get; private set; }

        public string? Copy() => "copy-value";
        public string? Cut() => "cut-value";
        public bool Paste(string? text)
        {
            PastedText = text;
            return true;
        }
        public bool TextInput(string? text)
        {
            TypedText = text;
            return true;
        }
    }

    private sealed class KeyboardTarget : Container, IHavenKeyboardInputTarget
    {
        public KeyboardTarget() => Accessibility.Focusable = true;
        public bool ConsumeTab { get; set; }
        public HavenKeyInput? LastKeyDown { get; private set; }
        public HavenKeyInput? LastKeyUp { get; private set; }

        public bool KeyDown(HavenKeyInput input)
        {
            LastKeyDown = input;
            return ConsumeTab && input.Key == HavenKey.Tab;
        }

        public bool KeyUp(HavenKeyInput input)
        {
            LastKeyUp = input;
            return ConsumeTab && input.Key == HavenKey.Tab;
        }
    }

    private sealed class FixedMeasure : IHavenMeasureContext
    {
        public HavenSize MeasureLeaf(HavenElement element, HavenSize available) => HavenSize.Zero;
    }

    private static Input CreateInput(string text)
    {
        var input = new Input { Text = text };
        input.SetValue(HavenProperties.Width, HavenLength.Px(280));
        input.SetValue(HavenProperties.Height, HavenLength.Px(64));
        input.SetValue(HavenProperties.FontSize, 32d);
        return input;
    }
}
