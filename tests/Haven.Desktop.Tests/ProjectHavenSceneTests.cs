using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class ProjectHavenSceneTests
{
    [Fact]
    public void Visible_project_scene_is_an_ide_shell_not_a_dashboard()
    {
        using var scene = new ProjectHavenScene();

        Assert.Equal("Project.Root", scene.Root.Name);
        Assert.Equal("Project integrated workspace", scene.Root.Accessibility.AccessibleName);
        Assert.NotNull(scene.Root.DescendantsAndSelf().Single(x => x.Name == "Project.Explorer"));
        Assert.NotNull(scene.Root.DescendantsAndSelf().Single(x => x.Name == "Project.Editor"));
        Assert.NotNull(scene.Root.DescendantsAndSelf().Single(x => x.Name == "Project.Assistant"));
        Assert.NotNull(scene.Root.DescendantsAndSelf().Single(x => x.Name == "Project.ToolDock"));
        Assert.NotNull(scene.Root.DescendantsAndSelf().Single(x => x.Name == "Project.Intelligence"));
        Assert.NotNull(scene.Root.DescendantsAndSelf().Single(x => x.Name == "Project.Intelligence.ForecastRisk"));
        Assert.NotNull(scene.Root.DescendantsAndSelf().Single(x => x.Name == "Project.Intelligence.AskError"));
        Assert.DoesNotContain(scene.Root.DescendantsAndSelf().OfType<Text>(), text => string.Equals(text.Content, "Project Home", StringComparison.Ordinal));
        Assert.All(scene.Root.DescendantsAndSelf(), element => Assert.False(element.GetType().Namespace?.StartsWith("Avalonia", StringComparison.Ordinal) == true));
    }

    [Fact]
    public void Project_scene_keeps_all_project_files_and_chats_without_ui_caps()
    {
        using var scene = new ProjectHavenScene();
        var projectId = Guid.NewGuid();
        var chats = Enumerable.Range(0, 30).Select(i => new Conversation(Guid.NewGuid(), HavenMode.Studio, ConversationKind.Chat, $"Chat {i}", projectId, null, false, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)).ToArray();
        var files = Enumerable.Range(0, 180).Select(i => new WorkspaceFileItemViewModel(Path.GetTempPath(), $"src/Folder{i / 20}/File{i}.cs")).ToArray();

        scene.Sync(State(chats, files));

        Assert.Equal(30, scene.ExplorerChats.Items.Count);
        Assert.Equal(30, scene.ChatHistory.Items.Count);
        Assert.Equal(180, scene.ExplorerFiles.Items.Count);
        Assert.Equal(180, scene.CompactFiles.Items.Count);
    }

    [Fact]
    public void Project_scene_reflows_panels_while_preserving_editor_and_ai_access()
    {
        using var scene = new ProjectHavenScene();

        scene.SetViewportWidth(700);
        Assert.Equal("1fr", scene.Workspace.Columns);
        Assert.Equal(HavenVisibility.Collapsed, scene.Explorer.GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Visible, scene.CompactExplorer.GetValue(HavenProperties.Visibility));
        Assert.Equal("1fr", scene.WorkArea.Columns);
        Assert.Equal("1fr 280px", scene.WorkArea.Rows);
        Assert.Equal(0, scene.AssistantPanel.GetValue(HavenProperties.Column));
        Assert.Equal(1, scene.AssistantPanel.GetValue(HavenProperties.Row));

        scene.SetViewportWidth(1280);
        Assert.Equal("250px 1fr", scene.Workspace.Columns);
        Assert.Equal(HavenVisibility.Visible, scene.Explorer.GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Collapsed, scene.CompactExplorer.GetValue(HavenProperties.Visibility));
        Assert.Equal("1fr 320px", scene.WorkArea.Columns);
        Assert.Equal(1, scene.AssistantPanel.GetValue(HavenProperties.Column));
        Assert.Equal(0, scene.AssistantPanel.GetValue(HavenProperties.Row));
    }

    [Fact]
    public void Project_scene_uses_persistent_project_chat_and_searches_both_object_types()
    {
        using var scene = new ProjectHavenScene();
        var projectId = Guid.NewGuid();
        var chat = new Conversation(Guid.NewGuid(), HavenMode.Studio, ConversationKind.Chat, "Release review", projectId, null, false, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var file = new WorkspaceFileItemViewModel(Path.GetTempPath(), "src/ReleaseReview.cs");
        scene.Sync(State([chat], [file]));

        Assert.Equal("Chatbox", scene.Composer.PrefabID);
        Assert.Equal("Ask Haven about this project", scene.ComposerInput.Placeholder);
        Assert.Equal("Commit · abc123 · Project shell", scene.LastCommit.Content);
        Assert.Equal("Latest error · No recent error found", scene.LatestError.Content);
        Assert.Equal("Release risk · Low · 12% risk", scene.Risk.Content);
        Assert.Equal("Decisions · 3", scene.DecisionCount.Content);
        Assert.Equal("Automations · 2", scene.AutomationCount.Content);
        scene.ExplorerSearch.Text = "ReleaseReview";
        Assert.Empty(scene.ExplorerChats.Items);
        Assert.Single(scene.ExplorerFiles.Items);
        Assert.Single(scene.CompactFiles.Items);
    }

    private static StudioSceneState State(IReadOnlyList<Conversation> chats, IReadOnlyList<WorkspaceFileItemViewModel> files) =>
        new("Haven", "Captured", "main", "Working tree clean", "Passed", "Run tests", "abc123 · Project shell", "No recent error found", "Low · 12% risk", 3, 2, Path.GetTempPath(), false, "Haven", "Context", string.Empty, chats, files);
}
