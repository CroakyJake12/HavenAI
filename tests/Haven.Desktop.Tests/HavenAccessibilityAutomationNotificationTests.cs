using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class HavenAccessibilityAutomationNotificationTests
{
    [AvaloniaFact]
    public void Haven_interactive_peers_raise_live_UIA_property_changes()
    {
        var page = new Haven.UI.Components.Page();
        var toggle = new Toggle { Name = "LiveToggle" };
        var input = new Input { Name = "LiveInput", Text = "Old" };
        var slider = new Haven.UI.Components.Slider { Name = "LiveSlider", Minimum = 0, Maximum = 10, Step = 1, Value = 3 };
        var select = new Select { Name = "LiveSelect", Items = ["One", "Two"], SelectedIndex = 0 };
        page.Add(toggle);
        page.Add(input);
        page.Add(slider);
        page.Add(select);

        var scene = new HavenSceneControl { Root = page };
        var window = new Window { Width = 420, Height = 280, Content = scene };
        try
        {
            window.Show();
            window.UpdateLayout();
            var rootPeer = ControlAutomationPeer.CreatePeerForElement(scene);
            Assert.NotNull(rootPeer);
            var peers = Descendants(rootPeer).ToArray();

            var togglePeer = Assert.Single(peers, peer => peer.GetAutomationId() == "LiveToggle");
            var inputPeer = Assert.Single(peers, peer => peer.GetAutomationId() == "LiveInput");
            var sliderPeer = Assert.Single(peers, peer => peer.GetAutomationId() == "LiveSlider");
            var selectPeer = Assert.Single(peers, peer => peer.GetAutomationId() == "LiveSelect");

            var toggleChanges = 0;
            var inputChanges = 0;
            var sliderChanges = 0;
            var selectChanges = 0;
            togglePeer.PropertyChanged += (_, _) => toggleChanges++;
            inputPeer.PropertyChanged += (_, _) => inputChanges++;
            sliderPeer.PropertyChanged += (_, _) => sliderChanges++;
            selectPeer.PropertyChanged += (_, _) => selectChanges++;

            toggle.IsChecked = true;
            input.Text = "Updated";
            slider.Value = 7;
            select.SelectedIndex = 1;
            select.IsExpanded = true;

            Assert.Equal(1, toggleChanges);
            Assert.Equal(1, inputChanges);
            Assert.Equal(1, sliderChanges);
            Assert.Equal(2, selectChanges);
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
