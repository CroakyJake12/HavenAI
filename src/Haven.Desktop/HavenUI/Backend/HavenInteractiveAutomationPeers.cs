using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Haven.UI.Components;

namespace Haven.Desktop.HavenUI.Backend;

internal sealed class HavenButtonAutomationPeer(
    HavenSceneControl owner,
    HavenSceneAutomationPeer rootPeer,
    Button button)
    : HavenElementAutomationPeer(owner, rootPeer, button), IInvokeProvider
{
    public void Invoke()
    {
        if (CanInteract) SceneOwner.ActivateElementForAutomation(Element);
    }
}

internal sealed class HavenToggleAutomationPeer(
    HavenSceneControl owner,
    HavenSceneAutomationPeer rootPeer,
    Toggle toggle)
    : HavenElementAutomationPeer(owner, rootPeer, toggle), IToggleProvider
{
    private ToggleState _lastToggleState = toggle.IsChecked ? ToggleState.On : ToggleState.Off;
    private Toggle OwnerToggle => (Toggle)Element;

    public ToggleState ToggleState => OwnerToggle.IsChecked ? ToggleState.On : ToggleState.Off;

    public void Toggle()
    {
        if (CanInteract) SceneOwner.ActivateElementForAutomation(Element);
    }

    protected override void OnSemanticStateInvalidated()
    {
        var next = ToggleState;
        if (next == _lastToggleState) return;
        var previous = _lastToggleState;
        _lastToggleState = next;
        RaisePropertyChangedEvent(TogglePatternIdentifiers.ToggleStateProperty, previous, next);
    }
}

internal sealed class HavenInputAutomationPeer(
    HavenSceneControl owner,
    HavenSceneAutomationPeer rootPeer,
    Input input)
    : HavenElementAutomationPeer(owner, rootPeer, input), IValueProvider
{
    private string _lastValue = input.IsSecret ? string.Empty : input.Text;
    private Input OwnerInput => (Input)Element;
    private string SemanticValue => OwnerInput.IsSecret ? string.Empty : OwnerInput.Text;

    public bool IsReadOnly => OwnerInput.IsSecret;
    public string? Value => SemanticValue;

    public void SetValue(string? value)
    {
        if (!CanInteract || OwnerInput.IsSecret) return;
        OwnerInput.Text = value ?? string.Empty;
        OwnerInput.SetSelection(OwnerInput.Text.Length, OwnerInput.Text.Length);
    }

    protected override void OnSemanticStateInvalidated()
    {
        var next = SemanticValue;
        if (string.Equals(next, _lastValue, StringComparison.Ordinal)) return;
        var previous = _lastValue;
        _lastValue = next;
        RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, previous, next);
    }
}

internal sealed class HavenSliderAutomationPeer(
    HavenSceneControl owner,
    HavenSceneAutomationPeer rootPeer,
    Slider slider)
    : HavenElementAutomationPeer(owner, rootPeer, slider), IRangeValueProvider
{
    private double _lastValue = (double)slider.GetValue(Slider.ValueProperty, Haven.UI.HavenValueSource.Explicit)!;
    private Slider OwnerSlider => (Slider)Element;
    private double DefaultStep => OwnerSlider.Step > 0
        ? OwnerSlider.Step
        : Math.Max((OwnerSlider.Maximum - OwnerSlider.Minimum) / 100d, .01d);

    public bool IsReadOnly => false;
    public double LargeChange => DefaultStep * 10d;
    public double Maximum => OwnerSlider.Maximum;
    public double Minimum => OwnerSlider.Minimum;
    public double SmallChange => DefaultStep;
    public double Value => (double)OwnerSlider.GetValue(Slider.ValueProperty, Haven.UI.HavenValueSource.Explicit)!;

    public void SetValue(double value)
    {
        if (CanInteract) OwnerSlider.Value = value;
    }

    protected override void OnSemanticStateInvalidated()
    {
        var next = Value;
        if (Math.Abs(next - _lastValue) < .000001d) return;
        var previous = _lastValue;
        _lastValue = next;
        RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, previous, next);
    }
}

internal sealed class HavenSelectAutomationPeer(
    HavenSceneControl owner,
    HavenSceneAutomationPeer rootPeer,
    Select select)
    : HavenElementAutomationPeer(owner, rootPeer, select), IExpandCollapseProvider, IValueProvider, ISelectionProvider
{
    private readonly Dictionary<int, HavenSelectItemAutomationPeer> _itemPeers = [];
    private string _lastValue = select.SelectedItem ?? string.Empty;
    private ExpandCollapseState _lastExpandCollapseState = select.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
    private Select OwnerSelect => (Select)Element;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ComboBox;

    protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore()
    {
        if (!OwnerSelect.IsExpanded || SceneOwner.Root is not { } root) return [];
        var popup = OwnerSelect.GetPopupLayout(root.Bounds);
        return popup is null
            ? []
            : popup.Items.Select(item => (AutomationPeer)GetItemPeer(item.Index)).ToArray();
    }

    public ExpandCollapseState ExpandCollapseState =>
        OwnerSelect.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
    public bool ShowsMenu => true;

    public bool IsReadOnly => true;
    public string? Value => OwnerSelect.SelectedItem ?? string.Empty;

    public bool CanSelectMultiple => false;
    public bool IsSelectionRequired => false;

    public IReadOnlyList<AutomationPeer> GetSelection() =>
        OwnerSelect.SelectedIndex >= 0 && OwnerSelect.SelectedIndex < OwnerSelect.Items.Count
            ? [GetItemPeer(OwnerSelect.SelectedIndex)]
            : [];

    public void Expand()
    {
        if (CanInteract) OwnerSelect.IsExpanded = true;
    }

    public void Collapse()
    {
        if (CanInteract) OwnerSelect.IsExpanded = false;
    }

    public void SetValue(string? value)
    {
        // Haven Select is non-editable; selection changes through its list interaction semantics.
    }

    internal HavenSelectItemAutomationPeer GetItemPeer(int index)
    {
        if (_itemPeers.TryGetValue(index, out var peer)) return peer;
        peer = new HavenSelectItemAutomationPeer(SceneOwner, RootPeer, this, OwnerSelect, index);
        _itemPeers[index] = peer;
        return peer;
    }

    protected override void OnSemanticStateInvalidated()
    {
        var nextValue = OwnerSelect.SelectedItem ?? string.Empty;
        if (!string.Equals(nextValue, _lastValue, StringComparison.Ordinal))
        {
            var previous = _lastValue;
            _lastValue = nextValue;
            RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, previous, nextValue);
        }

        var nextExpandCollapse = ExpandCollapseState;
        if (nextExpandCollapse == _lastExpandCollapseState) return;
        var previousExpandCollapse = _lastExpandCollapseState;
        _lastExpandCollapseState = nextExpandCollapse;
        RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty, previousExpandCollapse, nextExpandCollapse);
    }
}
