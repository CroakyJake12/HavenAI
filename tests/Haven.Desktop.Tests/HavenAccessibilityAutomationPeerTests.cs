using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class HavenAccessibilityAutomationPeerTests
{
    [AvaloniaFact]
    public void Haven_scene_peer_projects_roles_names_bounds_hierarchy_and_focus()
    {
        var page = new Haven.UI.Components.Page { Name = "SettingsPage" };
        page.Accessibility.AccessibleName = "Settings";
        var group = new Haven.UI.Components.Container();
        var title = new Text("Account") { Name = "AccountTitle" };
        var save = new Haven.UI.Components.Button { Name = "SaveButton", Content = "Save" };
        var sync = new Toggle { Name = "SyncToggle" };
        sync.Accessibility.AccessibleName = "Sync";
        group.Add(title);
        group.Add(save);
        group.Add(sync);
        page.Add(group);

        var scene = new HavenSceneControl { Root = page };
        var window = new Window { Width = 480, Height = 320, Content = scene };
        try
        {
            window.Show();
            window.UpdateLayout();

            var rootPeer = ControlAutomationPeer.CreatePeerForElement(scene);
            Assert.NotNull(rootPeer);
            Assert.Equal(AutomationControlType.Window, rootPeer.GetAutomationControlType());
            Assert.Equal("Settings", rootPeer.GetName());

            var peers = Descendants(rootPeer).ToArray();
            var savePeer = Assert.Single(peers, peer => peer.GetAutomationId() == "SaveButton");
            Assert.Equal(AutomationControlType.Button, savePeer.GetAutomationControlType());
            Assert.Equal("Save", savePeer.GetName());
            Assert.True(savePeer.GetBoundingRectangle().Width > 0d);

            var syncPeer = Assert.Single(peers, peer => peer.GetAutomationId() == "SyncToggle");
            Assert.Equal(AutomationControlType.CheckBox, syncPeer.GetAutomationControlType());
            Assert.Equal("Sync", syncPeer.GetName());
            Assert.True(syncPeer.IsKeyboardFocusable());
            syncPeer.SetFocus();
            Assert.True(sync.State.HasFlag(HavenElementState.Focused));
            Assert.True(syncPeer.HasKeyboardFocus());
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Haven_scene_peer_excludes_collapsed_semantics()
    {
        var page = new Haven.UI.Components.Page();
        var visible = new Haven.UI.Components.Button { Name = "VisibleButton", Content = "Visible" };
        var collapsed = new Haven.UI.Components.Button { Name = "CollapsedButton", Content = "Collapsed" };
        collapsed.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        page.Add(visible);
        page.Add(collapsed);

        var scene = new HavenSceneControl { Root = page };
        var window = new Window { Width = 320, Height = 180, Content = scene };
        try
        {
            window.Show();
            window.UpdateLayout();
            var rootPeer = ControlAutomationPeer.CreatePeerForElement(scene);
            Assert.NotNull(rootPeer);
            var ids = Descendants(rootPeer).Select(peer => peer.GetAutomationId()).ToArray();
            Assert.Contains("VisibleButton", ids);
            Assert.DoesNotContain("CollapsedButton", ids);
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
