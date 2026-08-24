using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class HavenTabAutomationSelectionTests
{
    [AvaloniaFact]
    public void Haven_tabs_expose_single_selection_and_route_UIA_selection_through_item_invocation()
    {
        var page = new Haven.UI.Components.Page();
        var tabs = new TabStrip { Name = "WorkspaceTabs" };
        SetSelected(tabs, "one");
        string? invoked = null;
        tabs.ItemInvoked += (_, key) =>
        {
            invoked = key;
            SetSelected(tabs, key);
        };
        page.Add(tabs);

        var scene = new HavenSceneControl { Root = page };
        var window = new Window { Width = 620, Height = 220, Content = scene };
        try
        {
            window.Show();
            window.UpdateLayout();
            var rootPeer = ControlAutomationPeer.CreatePeerForElement(scene);
            Assert.NotNull(rootPeer);

            var tabPeer = Assert.Single(Descendants(rootPeer), peer => peer.GetAutomationId() == "WorkspaceTabs");
            Assert.Equal(AutomationControlType.Tab, tabPeer.GetAutomationControlType());
            var selection = Assert.IsAssignableFrom<ISelectionProvider>(tabPeer);
            Assert.False(selection.CanSelectMultiple);
            Assert.True(selection.IsSelectionRequired);

            var selected = Assert.Single(selection.GetSelection());
            Assert.Equal("One", selected.GetName());
            var selectedItem = Assert.IsAssignableFrom<ISelectionItemProvider>(selected);
            Assert.True(selectedItem.IsSelected);
            Assert.Same(selection, selectedItem.SelectionContainer);

            var second = Assert.Single(Descendants(rootPeer), peer => peer.GetAutomationId() == "TabStrip.Item.1.Button");
            Assert.Equal(AutomationControlType.TabItem, second.GetAutomationControlType());
            var secondItem = Assert.IsAssignableFrom<ISelectionItemProvider>(second);
            Assert.False(secondItem.IsSelected);
            Assert.Same(selection, secondItem.SelectionContainer);

            secondItem.Select();

            Assert.Equal("two", invoked);
            Assert.True(tabs.Items[1].IsSelected);
            Assert.Equal("Two", Assert.Single(selection.GetSelection()).GetName());
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static void SetSelected(TabStrip tabs, string selected) => tabs.SetItems(
    [
        new TabStripItem("one", "One", selected == "one", false),
        new TabStripItem("two", "Two", selected == "two", false),
        new TabStripItem("three", "Three", selected == "three", false)
    ]);

    private static IEnumerable<AutomationPeer> Descendants(AutomationPeer peer)
    {
        foreach (var child in peer.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
