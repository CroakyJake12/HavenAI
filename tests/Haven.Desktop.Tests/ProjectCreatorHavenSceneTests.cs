using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class ProjectCreatorHavenSceneTests
{
    [Fact]
    public void Creator_is_a_focused_haven_native_create_and_connect_surface()
    {
        var scene = new ProjectCreatorHavenScene();

        Assert.Equal("ProjectCreator.Root", scene.Root.Name);
        Assert.Equal("Create or connect Project", scene.Root.Accessibility.AccessibleName);
        Assert.NotNull(scene.Root.DescendantsAndSelf().Single(x => x.Name == "ProjectCreator.Create"));
        Assert.NotNull(scene.Root.DescendantsAndSelf().Single(x => x.Name == "ProjectCreator.OpenExisting"));
        Assert.DoesNotContain(scene.Root.DescendantsAndSelf().OfType<Text>(), x => x.Content.Contains("Installed App", StringComparison.OrdinalIgnoreCase));
        Assert.All(scene.Root.DescendantsAndSelf(), element => Assert.False(element.GetType().Namespace?.StartsWith("Avalonia", StringComparison.Ordinal) == true));
    }

    [Fact]
    public void Proposal_is_hidden_until_review_and_then_shows_exact_files_and_commands()
    {
        var scene = new ProjectCreatorHavenScene();
        scene.Sync(State(null));
        Assert.Equal(HavenVisibility.Collapsed, scene.ProposalCard.GetValue(HavenProperties.Visibility));

        var proposal = new ProjectCreationProposal(
            ProjectCreationKind.DotNetProject,
            "Demo",
            Path.GetTempPath(),
            Path.Combine(Path.GetTempPath(), "Demo"),
            "Console app",
            "Create Demo.",
            [new("Demo.csproj", "Project configuration"), new("Program.cs", "Entry point")],
            [new("dotnet", "new console", Path.GetTempPath())],
            string.Empty,
            "fingerprint");

        scene.Sync(State(proposal));

        Assert.Equal(HavenVisibility.Visible, scene.ProposalCard.GetValue(HavenProperties.Visibility));
        Assert.Equal(2, scene.ProposalFiles.Children.Count);
        Assert.Single(scene.ProposalCommands.Children);
        Assert.Equal("Create Demo.", scene.ProposalSummary.Content);
    }

    [Fact]
    public void Creator_reflows_to_one_column_at_narrow_widths()
    {
        var scene = new ProjectCreatorHavenScene();
        scene.SetViewportWidth(700);
        Assert.Equal("1fr", scene.Body.Columns);
        Assert.Equal(0, scene.ExistingPanel.GetValue(HavenProperties.Column));
        Assert.Equal(1, scene.ExistingPanel.GetValue(HavenProperties.Row));

        scene.SetViewportWidth(1200);
        Assert.Equal("1fr 1fr", scene.Body.Columns);
        Assert.Equal(1, scene.ExistingPanel.GetValue(HavenProperties.Column));
        Assert.Equal(0, scene.ExistingPanel.GetValue(HavenProperties.Row));
    }

    private static ProjectCreatorSceneState State(ProjectCreationProposal? proposal) => new(
        "Demo",
        Path.GetTempPath(),
        "A small console app",
        string.Empty,
        "Console app",
        true,
        false,
        "Ready",
        false,
        true,
        proposal is not null,
        proposal);
}
