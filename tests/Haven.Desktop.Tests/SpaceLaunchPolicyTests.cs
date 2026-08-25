using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

public sealed class SpaceLaunchPolicyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Study_spaces_route_to_the_native_study_product_even_when_forked(bool builtIn)
    {
        var space = Create(SpaceKind.Study, builtIn);

        var plan = SpaceLaunchPolicy.Resolve(space);

        Assert.Equal(SpaceLaunchDestination.StudyProduct, plan.Destination);
        Assert.Equal(HavenMode.Study, plan.Mode);
    }

    [Theory]
    [InlineData(SpaceKind.General)]
    [InlineData(SpaceKind.Shopping)]
    [InlineData(SpaceKind.Research)]
    public void Non_study_spaces_route_to_configured_workspaces(SpaceKind kind)
    {
        var plan = SpaceLaunchPolicy.Resolve(Create(kind, false));

        Assert.Equal(SpaceLaunchDestination.ConfiguredWorkspace, plan.Destination);
        Assert.Equal(HavenMode.Chat, plan.Mode);
        Assert.Equal("space-model", plan.ModelName);
        Assert.Equal(SpaceThinkingMode.Deep, plan.ThinkingMode);
        Assert.Equal(EffortLevel.High, plan.EffortOverride);
        Assert.Single(plan.Files);
        Assert.NotNull(plan.GeneratedSurface);
        Assert.NotNull(plan.LayoutDocument);
        Assert.Single(plan.LayoutDocument!.Nodes);
    }

    [Fact]
    public void Agent_spaces_route_to_tasks_mode_chat_workspaces_with_high_effort()
    {
        var space = Create(SpaceKind.Agent, true) with
        {
            ThinkingMode = SpaceThinkingMode.Fast,
            Instructions = "Plan before acting."
        };

        var plan = SpaceLaunchPolicy.Resolve(space);

        Assert.Equal(SpaceLaunchDestination.ConfiguredWorkspace, plan.Destination);
        Assert.Equal(HavenMode.Tasks, plan.Mode);
        Assert.Equal(EffortLevel.High, plan.EffortOverride);
        Assert.Contains("Space instructions:\nPlan before acting.", plan.RegisteredContext, StringComparison.Ordinal);
    }

    [Fact]
    public void Registered_context_preserves_space_instructions_examples_and_declared_file_permissions()
    {
        var context = SpaceLaunchPolicy.Resolve(Create(SpaceKind.Research, false)).RegisteredContext;

        Assert.Contains("Active Haven Space: Example Space.", context, StringComparison.Ordinal);
        Assert.Contains("Purpose: Purpose text", context, StringComparison.Ordinal);
        Assert.Contains("Space instructions:\nUse careful sources.", context, StringComparison.Ordinal);
        Assert.Contains("User: Question\nHaven: Answer", context, StringComparison.Ordinal);
        Assert.Contains("source.txt: read-only", context, StringComparison.Ordinal);
    }

    private static SpaceDefinition Create(SpaceKind kind, bool builtIn)
    {
        var now = DateTimeOffset.UtcNow;
        return new SpaceDefinition(
            Guid.NewGuid(),
            "Example Space",
            "Purpose text",
            "sparkles",
            kind,
            builtIn,
            false,
            "space-model",
            "Use careful sources.",
            SpaceThinkingMode.Deep,
            [new SpaceExamplePair("Question", "Answer")],
            [new SpaceFileReference(Path.Combine(Path.GetTempPath(), "source.txt"), "source.txt", SpaceFilePermission.ReadOnly, now)],
            new SpaceGeneratedSurface("checklist", "{}"),
            now,
            now,
            builtIn ? null : SpaceRegistry.StudySpaceId,
            new SpaceLayoutDocument(
                [new SpaceLayoutNode(Guid.NewGuid(), "Surface", "Checklist")],
                []));
    }
}
