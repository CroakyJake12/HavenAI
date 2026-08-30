using Haven.Core;
using Haven.Desktop.Views;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Tests;

public sealed class ProjectSourceControlSceneTests
{
    [Fact]
    public void Project_scene_exposes_real_source_control_workflows_without_legacy_branding()
    {
        using var scene = new ProjectHavenScene();
        var snapshot = new ProjectSourceControlSnapshot(
            true,
            "main",
            [
                new ProjectSourceControlChange("src/A.cs", "None", "Modified", false, true, false, false, "Modified"),
                new ProjectSourceControlChange("src/B.cs", "Modified", "None", true, false, false, false, "Staged modified")
            ],
            [new ProjectGitBranch("main", true, "origin/main", 0, 0), new ProjectGitBranch("feature", false, null, 0, 0)],
            [new ProjectGitStash(0, "stash@{0}", "On main: WIP")],
            [new ProjectGitWorktree("C:/repo", "abc123", "main", true, false, false, null)],
            [new ProjectGitCommit("abc123def", "abc123", "Haven", DateTimeOffset.UtcNow, "Project source control")],
            "diff --git a/src/A.cs b/src/A.cs\n-old\n+new",
            "diff --git a/src/B.cs b/src/B.cs\n-old\n+new",
            "2 changed paths",
            DateTimeOffset.UtcNow);

        scene.SyncSourceControl(snapshot);

        Assert.NotNull(scene.Root.DescendantsAndSelf().Single(element => element.Name == "Project.SourceControl"));
        Assert.NotNull(scene.Root.DescendantsAndSelf().Single(element => element.Name == "Project.SourceControl.Refresh"));
        Assert.NotNull(scene.Root.DescendantsAndSelf().Single(element => element.Name == "Project.SourceControl.CreateStash"));
        var buttons = scene.Root.DescendantsAndSelf().OfType<HavenButton>().Select(button => button.Content).ToArray();
        Assert.Contains("Stage", buttons);
        Assert.Contains("Unstage", buttons);
        Assert.Contains("Checkout", buttons);
        Assert.Contains("Apply", buttons);
        var text = scene.Root.DescendantsAndSelf().OfType<HavenText>().Select(item => item.Content).ToArray();
        Assert.Contains(text, value => value.Contains("src/A.cs", StringComparison.Ordinal));
        Assert.Contains(text, value => value.Contains("abc123 · Project source control", StringComparison.Ordinal));
        Assert.Contains(text, value => value.Contains("Working diff", StringComparison.Ordinal));
        Assert.DoesNotContain(text, value => value.Contains("Studio", StringComparison.OrdinalIgnoreCase));
    }
}
