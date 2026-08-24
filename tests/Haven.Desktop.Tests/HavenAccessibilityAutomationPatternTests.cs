using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class HavenAccessibilityAutomationPatternTests
{
    [AvaloniaFact]
    public void Haven_interactive_peers_expose_native_UIA_patterns_and_preserve_Haven_semantics()
    {
        var page = new Haven.UI.Components.Page();
        var button = new Haven.UI.Components.Button { Name = "SaveButton", Content = "Save" };
        var toggle = new Toggle { Name = "SyncToggle" };
        toggle.Accessibility.AccessibleName = "Sync";
        var input = new Input { Name = "NameInput", Text = "Old", Placeholder = "Name" };
        var slider = new Haven.UI.Components.Slider { Name = "ZoomSlider", Minimum = 0, Maximum = 10, Step = 1, Value = 3 };
        slider.Accessibility.AccessibleName = "Zoom";
        var select = new Select
        {
            Name = "ModeSelect",
            Items = ["Alpha", "Beta", "Gamma"],
            SelectedIndex = 1
        };
        page.Add(button);
        page.Add(toggle);
        page.Add(input);
        page.Add(slider);
        page.Add(select);

        var invoked = 0;
        button.Invoked += (_, _) => invoked++;

        var scene = new HavenSceneControl { Root = page };
        var window = new Window { Width = 520, Height = 360, Content = scene };
        try
        {
            window.Show();
            window.UpdateLayout();
            var rootPeer = ControlAutomationPeer.CreatePeerForElement(scene);
            Assert.NotNull(rootPeer);
            var peers = Descendants(rootPeer).ToArray();

            var buttonPeer = Assert.Single(peers, peer => peer.GetAutomationId() == "SaveButton");
            var invoke = Assert.IsAssignableFrom<IInvokeProvider>(buttonPeer);
            Assert.False(buttonPeer is IToggleProvider);
            invoke.Invoke();
            Assert.Equal(1, invoked);

            var togglePeer = Assert.Single(peers, peer => peer.GetAutomationId() == "SyncToggle");
            var toggleProvider = Assert.IsAssignableFrom<IToggleProvider>(togglePeer);
            Assert.Equal(ToggleState.Off, toggleProvider.ToggleState);
            toggleProvider.Toggle();
            Assert.True(toggle.IsChecked);
            Assert.Equal(ToggleState.On, toggleProvider.ToggleState);

            var inputPeer = Assert.Single(peers, peer => peer.GetAutomationId() == "NameInput");
            var valueProvider = Assert.IsAssignableFrom<IValueProvider>(inputPeer);
            Assert.False(valueProvider.IsReadOnly);
            valueProvider.SetValue("Updated");
            Assert.Equal("Updated", input.Text);
            Assert.Equal("Updated", valueProvider.Value);

            var sliderPeer = Assert.Single(peers, peer => peer.GetAutomationId() == "ZoomSlider");
            var rangeProvider = Assert.IsAssignableFrom<IRangeValueProvider>(sliderPeer);
            Assert.Equal(0, rangeProvider.Minimum);
            Assert.Equal(10, rangeProvider.Maximum);
            Assert.Equal(1, rangeProvider.SmallChange);
            rangeProvider.SetValue(7);
            Assert.Equal(7d, (double)slider.GetValue(Haven.UI.Components.Slider.ValueProperty, Haven.UI.HavenValueSource.Explicit)!);
            Assert.Equal(7, rangeProvider.Value);

            var selectPeer = Assert.Single(peers, peer => peer.GetAutomationId() == "ModeSelect");
            Assert.Equal(AutomationControlType.ComboBox, selectPeer.GetAutomationControlType());
            var selectValue = Assert.IsAssignableFrom<IValueProvider>(selectPeer);
            var expand = Assert.IsAssignableFrom<IExpandCollapseProvider>(selectPeer);
            Assert.True(selectValue.IsReadOnly);
            Assert.Equal("Beta", selectValue.Value);
            Assert.Equal(ExpandCollapseState.Collapsed, expand.ExpandCollapseState);
            expand.Expand();
            Assert.True(select.IsExpanded);
            Assert.Equal(ExpandCollapseState.Expanded, expand.ExpandCollapseState);
            expand.Collapse();
            Assert.False(select.IsExpanded);
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
