using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class HavenAccessibilityTests
{
    [AvaloniaFact]
    public void Scene_peer_projects_haven_semantics_and_invocation_into_platform_automation()
    {
        var root = new Haven.UI.Components.Page { Name = "Accessibility.Root" };
        var button = new Haven.UI.Components.Button { Name = "Accessibility.Save", Content = "Save privacy choices" };
        var invoked = 0;
        button.Invoked += (_, _) => invoked++;
        root.Add(button);
        var host = new HavenSceneControl { Root = root };
        var window = new Window { Width = 640, Height = 480, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();

            var scenePeer = Assert.IsAssignableFrom<AutomationPeer>(ControlAutomationPeer.CreatePeerForElement(host));
            var savePeer = Descendants(scenePeer).Single(peer => peer.GetAutomationId() == "Accessibility.Save");

            Assert.Equal("Save privacy choices", savePeer.GetName());
            Assert.Equal(AutomationControlType.Button, savePeer.GetAutomationControlType());
            Assert.True(savePeer.IsKeyboardFocusable());
            Assert.True(savePeer.GetBoundingRectangle().Width > 0);
            Assert.IsAssignableFrom<IInvokeProvider>(savePeer.GetProvider<IInvokeProvider>()).Invoke();
            Assert.Equal(1, invoked);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Secret_input_value_is_not_exposed_to_platform_automation()
    {
        var input = new Input
        {
            Name = "Provider.ApiKey",
            Placeholder = "API key",
            Text = "super-secret",
            IsSecret = true
        };
        var root = new Haven.UI.Components.Page();
        root.Add(input);
        var host = new HavenSceneControl { Root = root };
        var window = new Window { Width = 640, Height = 480, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();

            var scenePeer = Assert.IsAssignableFrom<AutomationPeer>(ControlAutomationPeer.CreatePeerForElement(host));
            var inputPeer = Descendants(scenePeer).Single(peer => peer.GetAutomationId() == "Provider.ApiKey");
            var value = Assert.IsAssignableFrom<IValueProvider>(inputPeer.GetProvider<IValueProvider>());

            Assert.Equal("API key", inputPeer.GetName());
            Assert.Equal(string.Empty, value.Value);
            Assert.True(value.IsReadOnly);
            Assert.DoesNotContain("super-secret", inputPeer.GetName(), StringComparison.Ordinal);
            value.SetValue("replacement");
            Assert.Equal("super-secret", input.Text);
            Assert.Equal(string.Empty, value.Value);
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
