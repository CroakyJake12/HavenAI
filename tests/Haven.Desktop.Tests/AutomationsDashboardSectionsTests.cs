using System.Reflection;
using Haven.Desktop.Views.Pages.Automations;
using Haven.UI;

namespace Haven.Desktop.Tests;

public sealed class AutomationsDashboardSectionsTests
{
    [Fact]
    public void Scheduled_and_library_sections_keep_real_sources_distinct()
    {
        using var scene = new AutomationsHavenScene();
        var manualId = Guid.NewGuid();
        var scheduledId = Guid.NewGuid();
        var scheduledOnlyId = Guid.NewGuid();

        scene.SetDashboardData(
            [
                new AutomationsWorkflowCard(manualId, "Manual review", "Manual", true, false, string.Empty),
                new AutomationsWorkflowCard(scheduledId, "Scheduled review", "Scheduled", true, true, "Next tomorrow")
            ],
            [new AutomationsScheduledCard(scheduledOnlyId, "Repository schedule", "Next tomorrow", true)],
            [],
            []);

        Invoke(scene.Root, "Automations.Tab.Scheduled");
        Assert.DoesNotContain(scene.Root.DescendantsAndSelf(), element => element.Name == $"Automations.Workflow.{manualId:N}");
        Assert.Contains(scene.Root.DescendantsAndSelf(), element => element.Name == $"Automations.Workflow.{scheduledId:N}");
        Assert.Contains(scene.Root.DescendantsAndSelf(), element => element.Name == $"Automations.Scheduled.{scheduledOnlyId:N}");

        Invoke(scene.Root, "Automations.Tab.Library");
        Assert.Contains(scene.Root.DescendantsAndSelf(), element => element.Name == $"Automations.Workflow.{manualId:N}");
        Assert.Contains(scene.Root.DescendantsAndSelf(), element => element.Name == $"Automations.Workflow.{scheduledId:N}");
        Assert.DoesNotContain(scene.Root.DescendantsAndSelf(), element => element.Name == $"Automations.Scheduled.{scheduledOnlyId:N}");
    }

    private static void Invoke(HavenElement root, string name)
    {
        var element = Assert.Single(root.DescendantsAndSelf(), candidate => candidate.Name == name);
        var method = typeof(HavenElement).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(element, null);
    }
}
