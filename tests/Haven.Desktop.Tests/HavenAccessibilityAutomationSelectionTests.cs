using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class HavenAccessibilityAutomationSelectionTests
{
    [AvaloniaFact]
    public void Haven_select_exposes_single_selection_and_rendered_list_items()
    {
        var page = new Haven.UI.Components.Page();
        var select = new Select
        {
            Name = "ModeSelect",
            Items = ["Alpha", "Beta", "Gamma"],
            SelectedIndex = 1,
            IsExpanded = true
        };
        page.Add(select);

        var scene = new HavenSceneControl { Root = page };
        var window = new Window { Width = 420, Height = 320, Content = scene };
        try
        {
            window.Show();
            window.UpdateLayout();
            var rootPeer = ControlAutomationPeer.CreatePeerForElement(scene);
            Assert.NotNull(rootPeer);
            var selectPeer = Assert.Single(Descendants(rootPeer), peer => peer.GetAutomationId() == "ModeSelect");
            var selection = Assert.IsAssignableFrom<ISelectionProvider>(selectPeer);
            Assert.False(selection.CanSelectMultiple);
            Assert.False(selection.IsSelectionRequired);

            var selected = Assert.Single(selection.GetSelection());
            Assert.Equal("ModeSelect.Item.1", selected.GetAutomationId());
            Assert.Equal("Beta", selected.GetName());

            var items = selectPeer.GetChildren();
            Assert.Equal(3, items.Count);
            Assert.All(items, item => Assert.Equal(AutomationControlType.ListItem, item.GetAutomationControlType()));
            Assert.All(items, item => Assert.True(item.GetBoundingRectangle().Width > 0));

            var first = Assert.Single(items, item => item.GetAutomationId() == "ModeSelect.Item.0");
            var firstSelection = Assert.IsAssignableFrom<ISelectionItemProvider>(first);
            Assert.False(firstSelection.IsSelected);
            Assert.Same(selection, firstSelection.SelectionContainer);

            var selectedChanges = 0;
            first.PropertyChanged += (_, _) => selectedChanges++;
            firstSelection.Select();
            Assert.Equal(0, select.SelectedIndex);
            Assert.False(select.IsExpanded);
            Assert.True(firstSelection.IsSelected);
            Assert.Equal(1, selectedChanges);
            Assert.Equal("ModeSelect.Item.0", Assert.Single(selection.GetSelection()).GetAutomationId());

            firstSelection.RemoveFromSelection();
            Assert.Equal(-1, select.SelectedIndex);
            Assert.Empty(selection.GetSelection());
            Assert.False(firstSelection.IsSelected);
            Assert.Equal(2, selectedChanges);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static IEnumerable<AutomationPeer> Descendants(AutomationPeer peer)
    {
        foreach (var child in peer.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
