using Haven.UI.Components;

namespace Haven.UI;

public enum HavenPointerKind { Mouse, Touch, Pen }
public enum HavenKey { Unknown, Enter, Space, Escape, Tab, Left, Right, Up, Down, Home, End, Backspace, Delete }

public sealed class HavenInputRouter(HavenElement root)
{
    private readonly HavenActionExecutor _actions = new();
    private HavenElement? _hovered;
    private HavenElement? _pressed;
    private HavenElement? _focused;

    public HavenElement? Hovered => _hovered;
    public HavenElement? Pressed => _pressed;
    public HavenElement? Focused => _focused;
    public event Action<Input>? InputSubmitted;
    public HavenElement? HitTest(HavenPoint point) => HitTestCore(root, point);

    public void PointerMoved(HavenPoint point)
    {
        if (_pressed is Slider activeSlider) activeSlider.SetFromPointer(point.X);
        var next = HitTest(point);
        if (ReferenceEquals(next, _hovered)) return;
        _hovered?.SetState(HavenElementState.Hover, false);
        _hovered = next;
        if (_hovered?.GetValue(HavenProperties.Hover) == true) _hovered.SetState(HavenElementState.Hover, true);
    }

    public void PointerPressed(HavenPoint point, HavenPointerKind pointerKind = HavenPointerKind.Mouse)
    {
        PointerMoved(point);
        _pressed?.SetState(HavenElementState.Pressed, false);
        _pressed = HitTest(point);
        if (_pressed is null) return;
        _pressed.SetState(HavenElementState.Pressed, true);
        if (_pressed is Slider slider) slider.SetFromPointer(point.X);
        if (_pressed.Accessibility.Focusable) Focus(_pressed);
    }

    public bool PointerReleased(HavenPoint point)
    {
        var released = _pressed;
        if (released is null) return false;
        if (released is Slider slider) slider.SetFromPointer(point.X);
        released.SetState(HavenElementState.Pressed, false);
        _pressed = null;
        if (!ReferenceEquals(HitTest(point), released)) return false;
        Activate(released);
        return true;
    }

    public bool Scroll(HavenPoint point, double deltaX, double deltaY)
    {
        for (var element = HitTest(point); element is not null; element = element.Parent)
        {
            if (element is not Container container || container.GetValue(HavenProperties.Overflow) != HavenOverflow.Scroll) continue;
            if (container.ScrollBy(deltaX, deltaY)) return true;
        }
        return false;
    }

    public bool TextInput(string? text)
    {
        return _focused is Input input && input.InsertText(text);
    }

    public bool KeyDown(HavenKey key)
    {
        if (_focused is Input input)
        {
            switch (key)
            {
                case HavenKey.Left: return input.MoveCaret(-1);
                case HavenKey.Right: return input.MoveCaret(1);
                case HavenKey.Home: input.PlaceCaretAtStart(); return true;
                case HavenKey.End: input.PlaceCaretAtEnd(); return true;
                case HavenKey.Backspace: return input.Backspace();
                case HavenKey.Delete: return input.Delete();
                case HavenKey.Enter when input.Multiline: return input.InsertText("\n");
                case HavenKey.Enter: InputSubmitted?.Invoke(input); return true;
                case HavenKey.Space: return false;
            }
        }
        if (_focused is Slider slider)
        {
            if (key == HavenKey.Left || key == HavenKey.Down) { slider.Nudge(-1); return true; }
            if (key == HavenKey.Right || key == HavenKey.Up) { slider.Nudge(1); return true; }
        }
        if (_focused is Select select)
        {
            if (key == HavenKey.Left || key == HavenKey.Up) { select.MoveSelection(-1); return true; }
            if (key == HavenKey.Right || key == HavenKey.Down) { select.MoveSelection(1); return true; }
            if (key == HavenKey.Home) { select.SelectBoundary(false); return true; }
            if (key == HavenKey.End) { select.SelectBoundary(true); return true; }
            if (key == HavenKey.Escape && select.IsExpanded) { select.IsExpanded = false; return true; }
        }
        if (_focused is null || key is not (HavenKey.Enter or HavenKey.Space)) return false;
        _focused.SetState(HavenElementState.Pressed, true);
        return true;
    }

    public bool KeyUp(HavenKey key)
    {
        if (_focused is Input && key is HavenKey.Enter or HavenKey.Space or HavenKey.Left or HavenKey.Right or HavenKey.Home or HavenKey.End or HavenKey.Backspace or HavenKey.Delete) return true;
        if (_focused is Slider && key is HavenKey.Left or HavenKey.Right or HavenKey.Up or HavenKey.Down) return true;
        if (_focused is Select && key is HavenKey.Left or HavenKey.Right or HavenKey.Up or HavenKey.Down or HavenKey.Home or HavenKey.End or HavenKey.Escape) return true;
        if (_focused is null || key is not (HavenKey.Enter or HavenKey.Space)) return false;
        _focused.SetState(HavenElementState.Pressed, false);
        Activate(_focused);
        return true;
    }

    public void Focus(HavenElement? element)
    {
        if (ReferenceEquals(_focused, element)) return;
        _focused?.SetState(HavenElementState.Focused, false);
        _focused = element is { Accessibility.Focusable: true } ? element : null;
        if (_focused is Input input) input.PlaceCaretAtEnd();
        _focused?.SetState(HavenElementState.Focused, true);
    }

    private void Activate(HavenElement element)
    {
        switch (element)
        {
            case Toggle toggle: toggle.ToggleValue(); break;
            case Select select: select.IsExpanded = !select.IsExpanded; break;
        }
        element.Invoke();
        _actions.ExecuteClick(root, element);
    }

    private static HavenElement? HitTestCore(HavenElement element, HavenPoint point)
    {
        if (!element.IsIncluded
            || element.GetValue(HavenProperties.Visibility) != HavenVisibility.Visible
            || !element.GetValue(HavenProperties.Enabled)
            || element.GetValue(HavenProperties.PointerEvents) == HavenPointerEvents.None) return null;
        var clipsChildren = element.GetValue(HavenProperties.Clip)
            || element.GetValue(HavenProperties.Overflow) is HavenOverflow.Clip or HavenOverflow.Scroll;
        if (clipsChildren && !element.Bounds.Contains(point)) return null;
        foreach (var child in element.Children.Select((value, index) => (value, index)).OrderByDescending(item => item.value.GetValue(HavenProperties.ZIndex)).ThenByDescending(item => item.index).Select(item => item.value))
        {
            if (!child.IsIncluded) continue;
            var hit = HitTestCore(child, point);
            if (hit is not null) return hit;
        }
        return element.Bounds.Contains(point) ? element : null;
    }
}
