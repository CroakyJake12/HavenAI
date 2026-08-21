using System.Reflection;
using Haven.Desktop.Views.Pages.Tasks;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class TasksHavenSceneTests
{
    [Fact]
    public void Scene_is_haven_ui_and_has_responsive_headers()
    {
        using var scene = new TasksHavenScene();

        Assert.IsType<Page>(scene.Root);
        Assert.NotEmpty(scene.WideHeader.Conditions);
        Assert.NotEmpty(scene.CompactHeader.Conditions);
        Assert.All(scene.Root.DescendantsAndSelf(), element =>
            Assert.False(element.GetType().Namespace?.StartsWith("Avalonia", StringComparison.Ordinal) == true));
    }

    [Fact]
    public void Tabs_switch_sections_and_render_history()
    {
        using var scene = new TasksHavenScene();
        var history = new TasksHavenHistoryItem(Guid.NewGuid(), "Nightly research", DateTimeOffset.UtcNow);
        scene.SetData([], [], [history]);

        Invoke(scene.Root, "Tasks.Tab.History");

        Assert.Equal(TasksHavenSection.History, scene.SelectedSection);
        Assert.Contains(scene.Content.DescendantsAndSelf(), element =>
            element.Name == $"Tasks.History.{history.Id:N}");
    }

    [Fact]
    public void Reusable_search_filters_name_description_and_scheduled_detail()
    {
        using var scene = new TasksHavenScene();
        var report = new TasksHavenReusableItem(Guid.NewGuid(), "Weekly report", "Summarise the week", "Write report", DateTimeOffset.UtcNow);
        var deploy = new TasksHavenReusableItem(Guid.NewGuid(), "Deploy service", "Ship the current build", "Deploy", DateTimeOffset.UtcNow);
        var scheduled = new TasksHavenScheduledItem(Guid.NewGuid(), "Backups", "Run backup", "Nightly backup");
        scene.SetData([report, deploy], [scheduled], []);
        Invoke(scene.Root, "Tasks.Tab.Reusable");

        scene.SearchInput.Text = "deploy";

        var named = scene.Content.DescendantsAndSelf()
            .Where(element => element.Name is not null)
            .Select(element => element.Name!)
            .ToArray();
        Assert.Contains($"Tasks.Reusable.{deploy.Id:N}", named);
        Assert.DoesNotContain($"Tasks.Reusable.{report.Id:N}", named);
        Assert.DoesNotContain($"Tasks.Automatic.{scheduled.Id:N}", named);
    }

    [Fact]
    public void Header_and_task_actions_raise_existing_scene_events()
    {
        using var scene = new TasksHavenScene();
        var reusable = new TasksHavenReusableItem(Guid.NewGuid(), "Review", "Review a project", "Review it", DateTimeOffset.UtcNow);
        var scheduled = new TasksHavenScheduledItem(Guid.NewGuid(), "Morning brief", "Brief me", "Every morning");
        scene.SetData([reusable], [scheduled], []);

        var refreshCount = 0;
        var oneTimeCount = 0;
        var createReusableCount = 0;
        Guid? edited = null;
        string? instruction = null;
        scene.RefreshRequested += (_, _) => refreshCount++;
        scene.StartOneTimeRequested += (_, _) => oneTimeCount++;
        scene.CreateReusableRequested += (_, _) => createReusableCount++;
        scene.EditRequested += (_, args) => edited = args.TaskId;
        scene.RunRequested += (_, args) => instruction = args.Instruction;

        Invoke(scene.Root, "Tasks.Header.Wide.Refresh");
        Invoke(scene.Root, "Tasks.Header.Wide.New");
        Invoke(scene.Root, "Tasks.Header.Wide.Reusable");
        Invoke(scene.Root, "Tasks.Tab.Reusable");
        Invoke(scene.Root, $"Tasks.Reusable.{reusable.Id:N}.Action");
        Invoke(scene.Root, $"Tasks.Automatic.{scheduled.Id:N}.Action");

        Assert.Equal(1, refreshCount);
        Assert.Equal(1, oneTimeCount);
        Assert.Equal(1, createReusableCount);
        Assert.Equal(reusable.Id, edited);
        Assert.Equal(scheduled.Instruction, instruction);
    }

    private static void Invoke(HavenElement root, string name)
    {
        var element = Assert.Single(root.DescendantsAndSelf(), candidate => candidate.Name == name);
        var method = typeof(HavenElement).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(element, null);
    }
}
