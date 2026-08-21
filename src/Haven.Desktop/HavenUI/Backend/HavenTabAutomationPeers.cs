using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Haven.UI;
using Haven.UI.Components;
using HavenTabStrip = Haven.UI.Components.TabStrip;

namespace Haven.Desktop.HavenUI.Backend;

internal sealed class HavenTabAutomationPeer(
    HavenSceneControl owner,
    HavenSceneAutomationPeer rootPeer,
    HavenTabStrip tabs)
    : HavenElementAutomationPeer(owner, rootPeer, tabs), ISelectionProvider
{
    private HavenTabStrip OwnerTabs => (HavenTabStrip)Element;

    public bool CanSelectMultiple => false;
    public bool IsSelectionRequired => OwnerTabs.ItemButtons.Count > 0;

    public IReadOnlyList<AutomationPeer> GetSelection() => OwnerTabs.ItemButtons
        .Where(button => button.Accessibility.Selected || button.State.HasFlag(HavenElementState.Selected))
        .Select(button => (AutomationPeer)RootPeer.GetOrCreateElementPeer(button))
        .ToArray();
}

internal sealed class HavenTabItemAutomationPeer(
    HavenSceneControl owner,
    HavenSceneAutomationPeer rootPeer,
    Button button)
    : HavenElementAutomationPeer(owner, rootPeer, button), IInvokeProvider, ISelectionItemProvider
{
    private bool _lastSelected = button.Accessibility.Selected || button.State.HasFlag(HavenElementState.Selected);
    private Button OwnerButton => (Button)Element;

    public bool IsSelected => OwnerButton.Accessibility.Selected || OwnerButton.State.HasFlag(HavenElementState.Selected);

    public ISelectionProvider SelectionContainer
    {
        get
        {
            for (var current = Element.Parent; current is not null; current = current.Parent)
                if (current is HavenTabStrip tabs)
                    return (ISelectionProvider)RootPeer.GetOrCreateElementPeer(tabs);
            throw new InvalidOperationException("A Haven TabItem must belong to a TabStrip.");
        }
    }

    public void Invoke() => Select();
    public void AddToSelection() => Select();

    public void RemoveFromSelection()
    {
        // Haven tabs are single-selection and selection is owned by the consuming route.
    }

    public void Select()
    {
        if (!CanInteract) return;
        SceneOwner.ActivateElementForAutomation(Element);
    }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TabItem;

    protected override void OnSemanticStateInvalidated()
    {
        var next = IsSelected;
        if (next == _lastSelected) return;
        var previous = _lastSelected;
        _lastSelected = next;
        RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, previous, next);
    }
}
