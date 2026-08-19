using Haven.UI.Components;

namespace Haven.UI;

public enum HavenPointerKind { Mouse, Touch, Pen }
public enum HavenPointerButton { Primary, Secondary, Middle }
public enum HavenKey { Unknown, Enter, Space, Escape, Tab, Left, Right, Up, Down, Home, End, Backspace, Delete, A, C, F, V, X, Y, Z }

public sealed class HavenInputRouter(HavenElement root)
{
    private readonly HavenActionExecutor _actions = new();
    private readonly HashSet<HavenElement> _hoverPath = [];
    private HavenElement? _hovered;
    private HavenElement? _pressed;
    private HavenElement? _focused;
    private IHavenPointerInputTarget? _pointerTarget;
    private HavenElement? _pointerTargetElement;
    private Input? _dragInput;
    private int _dragSelectionAnchor;
    private HavenPointerKind _activePointerKind = HavenPointerKind.Mouse;
    private HavenPointerButton _activePointerButton = HavenPointerButton.Primary;
    private bool _pointerConsumed;

    public HavenElement? Hovered => _hovered;
    public HavenElement? Pressed => _pressed;
    public HavenElement? Focused => _focused;
    public Func<Input, HavenPoint, int>? InputCaretHitTest { get; set; }
    public Func<Input, HavenKey, int>? InputCaretNavigation { get; set; }
    public event Action<Input>? InputSubmitted;
    public event Action<string>? ClipboardCopyRequested;
    public event Action? ClipboardPasteRequested;
    public HavenElement? HitTest(HavenPoint point) => HitTestCore(root, point);

    public void PointerMoved(
        HavenPoint point,
        HavenPointerKind pointerKind = HavenPointerKind.Mouse,
        HavenInputModifiers modifiers = default)
    {
        if (_pointerTarget is not null && _pointerTargetElement is not null)
            _pointerConsumed |= _pointerTarget.PointerMoved(PointerInput(_pointerTargetElement, point, _activePointerKind, modifiers));
        if (_pressed is Slider activeSlider && _activePointerButton == HavenPointerButton.Primary)
            activeSlider.SetFromPointer(point.X);
        if (_dragInput is not null && _activePointerButton == HavenPointerButton.Primary)
            _dragInput.SetSelection(_dragSelectionAnchor, HitInputCaret(_dragInput, point));
        UpdateHover(HitTest(point));
    }

    public void PointerExited() => UpdateHover(null);

    public void PointerPressed(
        HavenPoint point,
        HavenPointerKind pointerKind = HavenPointerKind.Mouse,
        HavenPointerButton pointerButton = HavenPointerButton.Primary,
        HavenInputModifiers modifiers = default)
    {
        PointerMoved(point, pointerKind, modifiers);
        _pressed?.SetState(HavenElementState.Pressed, false);
        _pointerTarget = null;
        _pointerTargetElement = null;
        _dragInput = null;
        _pointerConsumed = false;
        _activePointerKind = pointerKind;
        _activePointerButton = pointerButton;

        if (pointerButton == HavenPointerButton.Primary)
        {
            foreach (var candidate in root.DescendantsAndSelf().OfType<Select>()
                         .Where(select => select.IsExpanded && IsInteractive(select))
                         .OrderByDescending(select => select.GetValue(HavenProperties.ZIndex)))
            {
                var popup = candidate.GetPopupLayout(root.Bounds);
                if (popup is null || !popup.Bounds.Contains(point)) continue;
                foreach (var other in root.DescendantsAndSelf().OfType<Select>().Where(select => !ReferenceEquals(select, candidate) && select.IsExpanded))
                    other.IsExpanded = false;
                _pressed = candidate;
                _pressed.SetState(HavenElementState.Pressed, true);
                Focus(candidate);
                _pointerConsumed = true;
                return;
            }
        }

        var rawHit = HitTest(point);
        var hitSelect = FindAncestor<Select>(rawHit);
        foreach (var expanded in root.DescendantsAndSelf().OfType<Select>().Where(select => select.IsExpanded && !ReferenceEquals(select, hitSelect)))
            expanded.IsExpanded = false;

        var pointerTargetElement = FindPointerTarget(rawHit);
        _pressed = pointerTargetElement ?? ResolveInteractionTarget(rawHit);
        if (_pressed is null) return;

        var input = FindAncestor<Input>(rawHit);
        var inputWasFocused = input is not null && ReferenceEquals(_focused, input);
        var selectionAnchor = input is null ? 0 : input.HasSelection ? input.SelectionAnchor : input.CaretIndex;

        _pressed.SetState(HavenElementState.Pressed, true);
        if (_pressed is Slider slider && pointerButton == HavenPointerButton.Primary) slider.SetFromPointer(point.X);
        if (pointerTargetElement is IHavenPointerInputTarget pointerTarget)
        {
            _pointerTarget = pointerTarget;
            _pointerTargetElement = pointerTargetElement;
            _pointerConsumed = pointerTarget.PointerPressed(PointerInput(pointerTargetElement, point, pointerKind, modifiers));
        }
        if (_pressed.Accessibility.Focusable) Focus(_pressed);

        if (input is not null && pointerButton == HavenPointerButton.Primary)
        {
            var caret = HitInputCaret(input, point);
            if (modifiers.Shift && inputWasFocused)
            {
                input.SetSelection(selectionAnchor, caret);
                _dragSelectionAnchor = selectionAnchor;
            }
            else
            {
                input.SetSelection(caret, caret);
                _dragSelectionAnchor = caret;
            }
            _dragInput = input;
            _pointerConsumed = true;
        }
    }

    public bool PointerReleased(HavenPoint point, HavenInputModifiers modifiers = default)
    {
        var released = _pressed;
        if (released is null) return false;
        var pointerButton = _activePointerButton;

        if (released is Select popupSelect && pointerButton == HavenPointerButton.Primary && popupSelect.IsExpanded)
        {
            var popup = popupSelect.GetPopupLayout(root.Bounds);
            if (popup is not null && popup.Bounds.Contains(point))
            {
                var item = popup.Items.FirstOrDefault(item => item.Bounds.Contains(point));
                released.SetState(HavenElementState.Pressed, false);
                _pressed = null;
                _pointerTarget = null;
                _pointerTargetElement = null;
                _dragInput = null;
                _pointerConsumed = false;
                _activePointerButton = HavenPointerButton.Primary;
                if (item is not null)
                {
                    popupSelect.SelectedIndex = item.Index;
                    popupSelect.IsExpanded = false;
                }
                return true;
            }
        }

        if (released is Slider slider && pointerButton == HavenPointerButton.Primary) slider.SetFromPointer(point.X);
        if (_dragInput is not null && _activePointerButton == HavenPointerButton.Primary)
            _dragInput.SetSelection(_dragSelectionAnchor, HitInputCaret(_dragInput, point));

        var consumed = _pointerConsumed;
        if (_pointerTarget is not null && _pointerTargetElement is not null)
            consumed |= _pointerTarget.PointerReleased(PointerInput(_pointerTargetElement, point, _activePointerKind, modifiers));

        released.SetState(HavenElementState.Pressed, false);
        _pressed = null;
        _pointerTarget = null;
        _pointerTargetElement = null;
        _dragInput = null;
        _pointerConsumed = false;
        _activePointerButton = HavenPointerButton.Primary;
        if (consumed) return true;
        if (!ReferenceEquals(ResolveInteractionTarget(HitTest(point)), released)) return false;
        if (pointerButton == HavenPointerButton.Secondary)
            released.InvokeSecondary();
        else if (pointerButton == HavenPointerButton.Primary)
            Activate(released);
        return true;
    }

    public bool Scroll(HavenPoint point, double deltaX, double deltaY)
    {
        foreach (var select in root.DescendantsAndSelf().OfType<Select>()
                     .Where(select => select.IsExpanded && IsInteractive(select))
                     .OrderByDescending(select => select.GetValue(HavenProperties.ZIndex)))
        {
            var popup = select.GetPopupLayout(root.Bounds);
            if (popup is null || !popup.Bounds.Contains(point)) continue;
            select.ScrollPopup(deltaY, root.Bounds);
            return true;
        }

        for (var element = HitTest(point); element is not null; element = element.Parent)
        {
            if (element is IHavenScrollInputTarget scrollTarget
                && scrollTarget.PointerWheel(new HavenPoint(point.X - element.Bounds.X, point.Y - element.Bounds.Y), deltaX, deltaY))
                return true;
            if (element is not Container container || container.GetValue(HavenProperties.Overflow) != HavenOverflow.Scroll) continue;
            if (container.ScrollBy(deltaX, deltaY)) return true;
        }
        return false;
    }

    public bool TextInput(string? text) => _focused is Input input && input.InsertText(text);
    public bool PasteText(string? text) => _focused is Input input && input.InsertText(text);

    public bool KeyDown(HavenKey key, bool shift) => KeyDown(key, new HavenInputModifiers(Shift: shift));

    public bool KeyDown(HavenKey key, HavenInputModifiers modifiers = default)
    {
        if (key == HavenKey.Tab) return MoveFocus(modifiers.Shift ? -1 : 1);

        if (_focused is Input input)
        {
            var command = modifiers.Control || modifiers.Meta;
            if (command)
            {
                switch (key)
                {
                    case HavenKey.A:
                        input.SelectAll();
                        return true;
                    case HavenKey.C:
                        if (input.HasSelection) ClipboardCopyRequested?.Invoke(input.SelectedText);
                        return true;
                    case HavenKey.X:
                        if (input.HasSelection)
                        {
                            ClipboardCopyRequested?.Invoke(input.SelectedText);
                            input.CutSelection();
                        }
                        return true;
                    case HavenKey.V:
                        ClipboardPasteRequested?.Invoke();
                        return true;
                    case HavenKey.Z:
                        if (modifiers.Shift) input.Redo(); else input.Undo();
                        return true;
                    case HavenKey.Y:
                        input.Redo();
                        return true;
                    case HavenKey.Enter:
                        InputSubmitted?.Invoke(input);
                        return true;
                    case HavenKey.Home:
                        input.PlaceCaretAtStart(modifiers.Shift);
                        return true;
                    case HavenKey.End:
                        input.PlaceCaretAtEnd(modifiers.Shift);
                        return true;
                }
            }

            switch (key)
            {
                case HavenKey.Left: return input.MoveCaret(-1, modifiers.Shift);
                case HavenKey.Right: return input.MoveCaret(1, modifiers.Shift);
                case HavenKey.Up when input.Multiline && InputCaretNavigation is not null: return NavigateInputCaret(input, HavenKey.Up, modifiers.Shift);
                case HavenKey.Down when input.Multiline && InputCaretNavigation is not null: return NavigateInputCaret(input, HavenKey.Down, modifiers.Shift);
                case HavenKey.Home when input.Multiline && InputCaretNavigation is not null: return NavigateInputCaret(input, HavenKey.Home, modifiers.Shift);
                case HavenKey.End when input.Multiline && InputCaretNavigation is not null: return NavigateInputCaret(input, HavenKey.End, modifiers.Shift);
                case HavenKey.Home: input.PlaceCaretAtStart(modifiers.Shift); return true;
                case HavenKey.End: input.PlaceCaretAtEnd(modifiers.Shift); return true;
                case HavenKey.Backspace: return input.Backspace();
                case HavenKey.Delete: return input.Delete();
                case HavenKey.Enter when input.Multiline && (!input.SubmitOnEnter || modifiers.Shift): return input.InsertText("\n");
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
        if (key == HavenKey.Tab) return true;
        if (_focused is Input && key is HavenKey.Enter or HavenKey.Space or HavenKey.Left or HavenKey.Right or HavenKey.Up or HavenKey.Down or HavenKey.Home or HavenKey.End or HavenKey.Backspace or HavenKey.Delete or HavenKey.A or HavenKey.C or HavenKey.V or HavenKey.X or HavenKey.Y or HavenKey.Z) return true;
        if (_focused is Slider && key is HavenKey.Left or HavenKey.Right or HavenKey.Up or HavenKey.Down) return true;
        if (_focused is Select && key is HavenKey.Left or HavenKey.Right or HavenKey.Up or HavenKey.Down or HavenKey.Home or HavenKey.End or HavenKey.Escape) return true;
        if (_focused is null || key is not (HavenKey.Enter or HavenKey.Space)) return false;
        _focused.SetState(HavenElementState.Pressed, false);
        Activate(_focused);
        return true;
    }

    private bool NavigateInputCaret(Input input, HavenKey key, bool extendSelection)
    {
        var resolver = InputCaretNavigation;
        if (resolver is null) return false;
        var target = Math.Clamp(resolver(input, key), 0, input.Text.Length);
        if (extendSelection)
        {
            var anchor = input.HasSelection ? input.SelectionAnchor : input.CaretIndex;
            input.SetSelection(anchor, target);
        }
        else
        {
            input.SetSelection(target, target);
        }
        return true;
    }

    public bool DismissPopups(Select? except = null)
    {
        var changed = false;
        foreach (var select in root.DescendantsAndSelf().OfType<Select>().Where(select => select.IsExpanded && !ReferenceEquals(select, except)))
        {
            select.IsExpanded = false;
            changed = true;
        }
        return changed;
    }

    public void Focus(HavenElement? element)
    {
        if (ReferenceEquals(_focused, element)) return;
        var next = element is { Accessibility.Focusable: true } && IsInteractive(element) ? element : null;
        DismissPopups(next as Select);
        _focused?.SetState(HavenElementState.Focused, false);
        _focused = next;
        if (_focused is Input input) input.PlaceCaretAtEnd();
        _focused?.SetState(HavenElementState.Focused, true);
    }

    private bool MoveFocus(int direction)
    {
        var focusable = root.DescendantsAndSelf().Where(element => element.Accessibility.Focusable && IsInteractive(element)).ToArray();
        if (focusable.Length == 0) return false;
        var current = Array.IndexOf(focusable, _focused);
        var next = current < 0
            ? direction < 0 ? focusable.Length - 1 : 0
            : (current + direction + focusable.Length) % focusable.Length;
        Focus(focusable[next]);
        return true;
    }

    private int HitInputCaret(Input input, HavenPoint globalPoint)
    {
        var local = new HavenPoint(globalPoint.X - input.Bounds.X, globalPoint.Y - input.Bounds.Y);
        var value = InputCaretHitTest?.Invoke(input, local) ?? input.CaretIndex;
        return Math.Clamp(value, 0, input.Text.Length);
    }

    private void UpdateHover(HavenElement? rawHit)
    {
        var nextPath = new HashSet<HavenElement>();
        for (var element = rawHit; element is not null; element = element.Parent)
            if (element.GetValue(HavenProperties.Hover) == true) nextPath.Add(element);

        foreach (var element in _hoverPath.Where(element => !nextPath.Contains(element)).ToArray())
            element.SetState(HavenElementState.Hover, false);
        foreach (var element in nextPath.Where(element => !_hoverPath.Contains(element)))
            element.SetState(HavenElementState.Hover, true);

        _hoverPath.Clear();
        _hoverPath.UnionWith(nextPath);
        _hovered = ResolveInteractionTarget(rawHit);
    }

    private static HavenElement? ResolveInteractionTarget(HavenElement? element)
    {
        for (var current = element; current is not null; current = current.Parent)
            if (current is IHavenPointerInputTarget || current.Accessibility.Focusable) return current;
        return element;
    }

    private static HavenElement? FindPointerTarget(HavenElement? element)
    {
        for (var current = element; current is not null; current = current.Parent)
            if (current is IHavenPointerInputTarget) return current;
        return null;
    }

    private static T? FindAncestor<T>(HavenElement? element) where T : HavenElement
    {
        for (var current = element; current is not null; current = current.Parent)
            if (current is T match) return match;
        return null;
    }

    private static bool IsInteractive(HavenElement element) =>
        element.IsIncluded
        && element.GetValue(HavenProperties.Visibility) == HavenVisibility.Visible
        && element.GetValue(HavenProperties.Enabled)
        && element.GetValue(HavenProperties.PointerEvents) != HavenPointerEvents.None;

    private static HavenPointerInput PointerInput(
        HavenElement element,
        HavenPoint point,
        HavenPointerKind pointerKind,
        HavenInputModifiers modifiers) =>
        new(point, new HavenPoint(point.X - element.Bounds.X, point.Y - element.Bounds.Y), pointerKind, modifiers);

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
        if (element.GetValue(HavenProperties.PointerEvents) == HavenPointerEvents.ChildrenOnly) return null;
        return element.Bounds.Contains(point) ? element : null;
    }
}
