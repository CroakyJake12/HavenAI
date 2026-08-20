using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class HavenSecretInputAutomationTests
{
    [AvaloniaFact]
    public void Secret_input_is_password_semantic_and_never_exposes_UIA_value()
    {
        var page = new Haven.UI.Components.Page();
        var input = new Input
        {
            Name = "SecretInput",
            Text = "top-secret",
            IsSecret = true,
            RevealSecret = true,
            Placeholder = "API key"
        };
        page.Add(input);

        var scene = new HavenSceneControl { Root = page };
        var window = new Window { Width = 420, Height = 180, Content = scene };
        try
        {
            window.Show();
            window.UpdateLayout();
            var rootPeer = ControlAutomationPeer.CreatePeerForElement(scene);
            Assert.NotNull(rootPeer);
            var inputPeer = Descendants(rootPeer).Single(peer => peer.GetAutomationId() == "SecretInput");
            var value = Assert.IsAssignableFrom<IValueProvider>(inputPeer);

            Assert.True(input.Accessibility.IsPassword);
            Assert.True(value.IsReadOnly);
            Assert.Equal(string.Empty, value.Value);
            value.SetValue("replacement");
            Assert.Equal("top-secret", input.Text);

            input.Text = "changed-secret";
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
