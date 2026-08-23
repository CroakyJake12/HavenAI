using Avalonia.Headless.XUnit;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class HavenSceneAutomationPeerLifecycleTests
{
    [AvaloniaFact]
    public void Automation_peer_cache_prunes_semantic_elements_removed_by_rebuild()
    {
        var root = new Page();
        var first = new Button { Name = "Dynamic.First", Content = "First" };
        root.Add(first);
        var scene = new HavenSceneControl { Root = root };
        var peer = new HavenSceneAutomationPeer(scene);

        Assert.Single(peer.GetChildrenFor(root, peer));
        Assert.Equal(1, peer.CachedElementPeerCount);

        root.Remove(first);
        root.Add(new Button { Name = "Dynamic.Second", Content = "Second" });

        Assert.Single(peer.GetChildrenFor(root, peer));
        Assert.Equal(1, peer.CachedElementPeerCount);
    }
}
