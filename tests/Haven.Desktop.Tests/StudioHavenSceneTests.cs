using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class StudioHavenSceneTests
{
    [Fact]
    public void Studio_scene_uses_shared_chatbox_prefab_and_dynamic_runtime_collections()
    {
        using var scene = new StudioHavenScene();

        Assert.Equal("Chatbox", scene.Composer.PrefabID);
        Assert.Equal("Start New Chat", scene.ComposerInput.Placeholder);
        Assert.True(scene.ComposerInput.Multiline);
        Assert.True(scene.ComposerInput.SubmitOnEnter);
        Assert.Equal("SidebarChats", scene.SidebarChats.Name);
        Assert.Equal("SidebarFiles", scene.SidebarFiles.Name);
        Assert.Equal("MainChats", scene.MainChats.Name);
        Assert.Equal("MainFiles", scene.MainFiles.Name);
    }

    [Fact]
    public void Studio_scene_projects_and_filters_project_chats_and_files_through_dynamic_ui()
    {
        using var scene = new StudioHavenScene();
        var projectId = Guid.NewGuid();
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Studio, ConversationKind.Chat, "Results Day", projectId, null, false, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var file = new WorkspaceFileItemViewModel(Path.GetTempPath(), "src/ResultsDay.cs");
        scene.Sync(new StudioSceneState(
            "Haven", "Captured", "main", "Working tree clean", "Passed", "Run tests", "abc123 · Project shell", "No recent error found", "Not forecast yet", 0, 0, Path.GetTempPath(), false,
            "Haven", "Context", string.Empty, [conversation], [file]));

        Assert.Single(scene.SidebarChats.Items);
        Assert.Single(scene.SidebarFiles.Items);
        Assert.Single(scene.MainChats.Items);
        Assert.Single(scene.MainFiles.Items);
        var chatItem = scene.MainChats.Items[0];

        scene.Sync(new StudioSceneState(
            "Haven", "Status changed only", "main", "Working tree clean", "Passed", "Run tests", "abc123 · Project shell", "No recent error found", "Not forecast yet", 0, 0, Path.GetTempPath(), false,
            "Haven", "Context", string.Empty, [conversation], [file]));
        Assert.Same(chatItem, scene.MainChats.Items[0]);

        scene.MainSearch.Text = "no-match";

        Assert.Empty(scene.SidebarChats.Items);
        Assert.Empty(scene.SidebarFiles.Items);
        Assert.Empty(scene.MainChats.Items);
        Assert.Empty(scene.MainFiles.Items);
        Assert.Equal(HavenVisibility.Visible, scene.MainFilesEmpty.GetValue(HavenProperties.Visibility));
    }

    [Fact]
    public void Studio_scene_compacts_to_single_column_without_replacing_the_haven_surface()
    {
        using var scene = new StudioHavenScene();

        scene.SetViewportWidth(700);
        Assert.Equal("1fr", scene.Workspace.Columns);
        Assert.Equal(HavenVisibility.Collapsed, scene.Sidebar.GetValue(HavenProperties.Visibility));
        Assert.Equal(0, scene.Main.GetValue(HavenProperties.Column));
        Assert.Equal("1fr 1fr", scene.StateCards.Columns);
        Assert.Equal(0, scene.LastBuildCard.GetValue(HavenProperties.Column));
        Assert.Equal(1, scene.LastBuildCard.GetValue(HavenProperties.Row));

        scene.SetViewportWidth(560);
        Assert.Equal("1fr", scene.StateCards.Columns);
        Assert.Equal(0, scene.RecommendedCard.GetValue(HavenProperties.Column));
        Assert.Equal(3, scene.RecommendedCard.GetValue(HavenProperties.Row));

        scene.SetViewportWidth(1280);
        Assert.Equal("280px 1fr", scene.Workspace.Columns);
        Assert.Equal(HavenVisibility.Visible, scene.Sidebar.GetValue(HavenProperties.Visibility));
        Assert.Equal(1, scene.Main.GetValue(HavenProperties.Column));
    }
}
