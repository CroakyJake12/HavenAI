using Haven.Desktop.Views.Pages.Projects;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class ProjectsHavenSceneTests
{
    [Fact]
    public void Scene_is_haven_ui_and_has_wide_and_compact_layouts()
    {
        using var scene = new ProjectsHavenScene();

        Assert.IsType<Page>(scene.Root);
        Assert.NotEmpty(scene.WideHeader.Conditions);
        Assert.NotEmpty(scene.CompactHeader.Conditions);
        Assert.NotEmpty(scene.WideGroups.Conditions);
        Assert.NotEmpty(scene.CompactGroups.Conditions);
        Assert.All(scene.Root.DescendantsAndSelf(), element =>
            Assert.False(element.GetType().Namespace?.StartsWith("Avalonia", StringComparison.Ordinal) == true));
    }

    [Fact]
    public void Scene_groups_pinned_unread_and_remaining_projects()
    {
        using var scene = new ProjectsHavenScene();
        var pinned = Item("Pinned", isPinned: true);
        var unread = Item("Unread", isUnread: true);
        var normal = Item("Normal");

        scene.SetItems([normal, unread, pinned]);

        Assert.Equal(1, scene.PinnedCount);
        Assert.Equal(1, scene.UnreadCount);
        Assert.Equal(1, scene.ProjectCount);
        Assert.Equal(3, scene.VisibleItemIds.Count);
    }

    [Fact]
    public void Search_matches_project_metadata_not_only_name()
    {
        using var scene = new ProjectsHavenScene();
        var alpha = Item("Alpha", branch: "feature/haven-ui");
        var beta = Item("Beta", branch: "main");
        scene.SetItems([alpha, beta]);

        scene.SearchInput.Text = "haven-ui";

        Assert.Equal(new[] { alpha.Id }, scene.VisibleItemIds);
    }

    [Fact]
    public void Cards_make_the_tile_primary_and_hide_secondary_actions_in_overflow()
    {
        using var scene = new ProjectsHavenScene();
        var item = Item("Actions");
        scene.SetItems([item]);

        var elements = scene.Root.DescendantsAndSelf().Where(element => element.Name is not null).ToArray();
        var tile = Assert.Single(elements, element => element.Name == $"Projects.Card.{item.Id:N}.Wide.Tile");
        Assert.Equal(HavenAccessibleRole.Button, tile.Accessibility.Role);
        Assert.True(tile.Accessibility.Focusable);
        Assert.Equal(HavenCursor.Pointer, tile.GetValue(HavenProperties.Cursor));

        Assert.Contains(elements, element => element.Name == $"Projects.Card.{item.Id:N}.Wide.More");
        var menu = Assert.Single(elements, element => element.Name == $"Projects.Card.{item.Id:N}.Wide.Menu");
        Assert.Equal(HavenVisibility.Collapsed, menu.GetValue(HavenProperties.Visibility));
        Assert.Contains(elements, element => element.Name == $"Projects.Card.{item.Id:N}.Wide.Menu.Branch");
        Assert.Contains(elements, element => element.Name == $"Projects.Card.{item.Id:N}.Wide.Menu.State");
        Assert.Contains(elements, element => element.Name == $"Projects.Card.{item.Id:N}.Wide.Menu.Build");
        Assert.Contains(elements, element => element.Name == $"Projects.Card.{item.Id:N}.Wide.Menu.Pin");
        Assert.Contains(elements, element => element.Name == $"Projects.Card.{item.Id:N}.Wide.Menu.Read");
        Assert.Contains(elements, element => element.Name == $"Projects.Card.{item.Id:N}.Wide.Menu.Archive");
        Assert.DoesNotContain(elements.OfType<Button>(), button => button.Name?.Contains($"Projects.Card.{item.Id:N}.Open.", StringComparison.Ordinal) == true);
    }

    private static ProjectsHavenItem Item(
        string name,
        string branch = "main",
        bool isPinned = false,
        bool isUnread = false) =>
        new(
            Guid.NewGuid(),
            name,
            $"C:/Projects/{name}",
            "Recent work",
            branch,
            "Working tree clean",
            "Build passed",
            "Open the project",
            DateTimeOffset.UtcNow,
            isPinned,
            isUnread);
}
