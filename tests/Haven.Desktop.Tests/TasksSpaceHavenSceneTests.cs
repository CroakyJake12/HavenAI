using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Tasks;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Tests;

public sealed class TasksSpaceHavenSceneTests
{
    [AvaloniaFact]
    public void Tasks_space_is_haven_native_and_emits_one_off_work_actions()
    {
        using var scene = new TasksSpaceHavenScene();
        string? delegated = null;
        var blankRequested = false;
        Guid? opened = null;
        var recentId = Guid.NewGuid();
        scene.DelegateRequested += (_, value) => delegated = value;
        scene.NewBlankTaskRequested += (_, _) => blankRequested = true;
        scene.RecentTaskRequested += (_, value) => opened = value;
        scene.SetRecent([new TasksSpaceRecentItem(recentId, "Compare documents", "Updated today")]);

        Assert.IsType<Haven.UI.Components.Page>(scene.Root);
        Assert.All(scene.Root.DescendantsAndSelf(), element => Assert.IsAssignableFrom<HavenElement>(element));
        Assert.Contains(scene.Root.DescendantsAndSelf().OfType<HavenText>(), text =>
            text.Content.Contains("Automations", StringComparison.Ordinal));

        scene.Instruction.Text = "Compare these documents and identify the differences";
        Click(scene, scene.DelegateTask);
        Assert.Equal("Compare these documents and identify the differences", delegated);

        Click(scene, scene.NewBlankTask);
        Assert.True(blankRequested);

        var recentButton = scene.RecentRows.DescendantsAndSelf().OfType<HavenButton>()
            .Single(button => button.Content == "Compare documents");
        Click(scene, recentButton);
        Assert.Equal(recentId, opened);
    }

    private static void Click(TasksSpaceHavenScene scene, HavenElement element)
    {
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 760, Height = 720, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();
            var router = new HavenInputRouter(scene.Root);
            var point = new HavenPoint(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);
            router.PointerPressed(point);
            Assert.True(router.PointerReleased(point));
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }
}
