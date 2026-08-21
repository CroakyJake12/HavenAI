using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.HavenUI.Backend;

/// <summary>UIA ListItem peer for one logical Haven Select option.</summary>
internal sealed class HavenSelectItemAutomationPeer : ControlAutomationPeer, ISelectionItemProvider
{
    private readonly HavenSceneControl _owner;
    private readonly HavenSceneAutomationPeer _rootPeer;
    private readonly HavenSelectAutomationPeer _parent;
    private readonly Select _select;
    private readonly int _index;
    private bool _lastSelected;

    public HavenSelectItemAutomationPeer(
        HavenSceneControl owner,
        HavenSceneAutomationPeer rootPeer,
        HavenSelectAutomationPeer parent,
        Select select,
        int index)
        : base(owner)
    {
        _owner = owner;
        _rootPeer = rootPeer;
        _parent = parent;
        _select = select;
        _index = index;
        _lastSelected = IsSelected;
        _select.Invalidated += OnSelectInvalidated;
    }

    public bool IsSelected => _select.SelectedIndex == _index;
    public ISelectionProvider SelectionContainer => _parent;

    public void AddToSelection() => Select();

    public void RemoveFromSelection()
    {
        if (IsEnabledCore() && IsSelected) _select.SelectedIndex = -1;
    }

    public void Select()
    {
        if (!IsEnabledCore() || _index < 0 || _index >= _select.Items.Count) return;
        _select.SelectedIndex = _index;
        _select.IsExpanded = false;
        _owner.FocusElement(_select);
    }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
    protected override string GetClassNameCore() => "HavenSelectItem";
    protected override string? GetAutomationIdCore() => $"{_select.Name ?? "Select"}.Item.{_index}";
    protected override string? GetNameCore() => _index >= 0 && _index < _select.Items.Count ? _select.Items[_index] : string.Empty;
    protected override AutomationPeer? GetParentCore() => _parent;
    protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() => [];
    protected override bool IsControlElementCore() => true;
    protected override bool IsContentElementCore() => true;
    protected override bool IsEnabledCore() =>
        _select.Accessibility.Enabled
        && _select.GetValue(HavenProperties.Enabled)
        && !_select.State.HasFlag(HavenElementState.Disabled);
    protected override bool IsKeyboardFocusableCore() => IsEnabledCore();
    protected override bool HasKeyboardFocusCore() => IsSelected && _select.State.HasFlag(HavenElementState.Focused);

    protected override Rect GetBoundingRectangleCore()
    {
        var item = PopupItem();
        return item is null ? default : _rootPeer.BoundsInTopLevel(item.Bounds);
    }

    protected override bool IsOffscreenCore() => PopupItem() is null;

    protected override void SetFocusCore()
    {
        if (IsEnabledCore()) _owner.FocusElement(_select);
    }

    private HavenSelectPopupItemLayout? PopupItem()
    {
        if (!_select.IsExpanded || _owner.Root is not { } root) return null;
        return _select.GetPopupLayout(root.Bounds)?.Items.FirstOrDefault(item => item.Index == _index);
    }

    private void OnSelectInvalidated(object? sender, EventArgs e)
    {
        var next = IsSelected;
        if (next == _lastSelected) return;
        var previous = _lastSelected;
        _lastSelected = next;
        RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, previous, next);
    }
}
