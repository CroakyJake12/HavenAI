using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

public sealed class GoRouteIntentPolicyTests
{
    public static TheoryData<string, GoRouteDestination, string?> ClearRoutes => new()
    {
        { "manage spaces", GoRouteDestination.App, "spaces" },
        { "open my spaces", GoRouteDestination.App, "spaces" },
        { "make me a presentation", GoRouteDestination.App, "present" },
        { "build a slide deck for results day", GoRouteDestination.App, "present" },
        { "write a letter to my teacher", GoRouteDestination.App, "write" },
        { "draft an email about the timetable", GoRouteDestination.App, "write" },
        { "open my maths revision", GoRouteDestination.App, "study" },
        { "help me revise integration", GoRouteDestination.App, "study" },
        { "show my dashboard", GoRouteDestination.App, "dashboard" },
        { "open my personal overview", GoRouteDestination.App, "dashboard" },
        { "search the web for Avalonia docs", GoRouteDestination.App, "browse" },
        { "look this up online", GoRouteDestination.App, "browse" },
        { "plan my week", GoRouteDestination.App, "plan" },
        { "open my calendar", GoRouteDestination.App, "plan" },
        { "set a reminder for tomorrow", GoRouteDestination.App, "automations" },
        { "automate this every week", GoRouteDestination.App, "automations" },
        { "analyse this csv", GoRouteDestination.App, "data" },
        { "open the spreadsheet", GoRouteDestination.App, "data" },
        { "translate this into Spanish", GoRouteDestination.App, "translate" },
        { "Translate this sentence into French: Good morning, everyone.", GoRouteDestination.App, "translate" },
        { "make a whiteboard for this topic", GoRouteDestination.App, "canvas" },
        { "play a game", GoRouteDestination.App, "play" },
        { "open calculator", GoRouteDestination.App, "launcher" },
        { "generate an image of a castle", GoRouteDestination.App, "imagine" },
        { "fix code in this module", GoRouteDestination.App, "studio" },
        { "run tests for this module", GoRouteDestination.App, "studio" },
        { "delegate this task", GoRouteDestination.App, "tasks" },
        { "explain recursion simply", GoRouteDestination.Chat, "chat" },
        { "why is the sky blue", GoRouteDestination.Chat, "chat" }
    };

    [Theory]
    [MemberData(nameof(ClearRoutes))]
    public void Clear_intents_route_to_expected_destination(string instruction, GoRouteDestination destination, string? targetKey)
    {
        var decision = GoRouteIntentPolicy.Resolve(instruction);

        Assert.Equal(destination, decision.Destination);
        Assert.Equal(targetKey, decision.TargetKey);
        Assert.Equal(instruction, decision.Instruction);
    }

    [Theory]
    [InlineData("work on Haven")]
    [InlineData("fix bug in Haven")]
    [InlineData("open the Haven project")]
    public void Project_intents_resolve_named_project_when_context_contains_it(string instruction)
    {
        var context = new GoRoutingContext([], ["Haven", "CAKE Bot"]);

        var decision = GoRouteIntentPolicy.Resolve(instruction, context);

        Assert.Equal(GoRouteDestination.Project, decision.Destination);
        Assert.Equal("Haven", decision.ProjectName);
        Assert.Same(context, decision.Context);
    }

    [Theory]
    [InlineData("edit this image")]
    [InlineData("remove the background")]
    public void Image_edit_routes_to_imagine_only_when_image_context_exists(string instruction)
    {
        var context = new GoRoutingContext(["C:/Temp/reference.png"], []);

        var decision = GoRouteIntentPolicy.Resolve(instruction, context);

        Assert.Equal(GoRouteDestination.App, decision.Destination);
        Assert.Equal("imagine", decision.TargetKey);
        Assert.Same(context, decision.Context);
    }

    [Fact]
    public void Image_edit_without_image_context_clarifies_instead_of_guessing()
    {
        var decision = GoRouteIntentPolicy.Resolve("edit this image");

        Assert.Equal(GoRouteDestination.Clarify, decision.Destination);
        Assert.Contains("Attach", decision.Clarification);
    }

    [Fact]
    public void Vision_intent_preserves_attached_image_context()
    {
        var context = new GoRoutingContext(["C:/Temp/photo.webp"], []);

        var decision = GoRouteIntentPolicy.Resolve("what is in this image", context);

        Assert.Equal(GoRouteDestination.App, decision.Destination);
        Assert.Equal("vision", decision.TargetKey);
        Assert.Same(context, decision.Context);
    }

    [Fact]
    public void Vision_without_image_context_clarifies()
    {
        var decision = GoRouteIntentPolicy.Resolve("inspect this image");

        Assert.Equal(GoRouteDestination.Clarify, decision.Destination);
        Assert.Contains("Attach", decision.Clarification);
    }

    [Fact]
    public void Generic_project_intent_with_one_project_uses_that_project()
    {
        var decision = GoRouteIntentPolicy.Resolve("work on the project", new GoRoutingContext([], ["Haven"]));

        Assert.Equal(GoRouteDestination.Project, decision.Destination);
        Assert.Equal("Haven", decision.ProjectName);
    }

    [Fact]
    public void Generic_project_intent_with_multiple_projects_clarifies()
    {
        var decision = GoRouteIntentPolicy.Resolve("work on the project", new GoRoutingContext([], ["Haven", "CAKE Bot"]));

        Assert.Equal(GoRouteDestination.Clarify, decision.Destination);
        Assert.Contains("Which project", decision.Clarification);
    }

    [Theory]
    [InlineData("open it")]
    [InlineData("open this")]
    [InlineData("go there")]
    public void Ambiguous_navigation_requests_clarify(string instruction)
    {
        var decision = GoRouteIntentPolicy.Resolve(instruction);

        Assert.Equal(GoRouteDestination.Clarify, decision.Destination);
        Assert.NotNull(decision.Clarification);
    }

    [Fact]
    public void Whitespace_instruction_clarifies()
    {
        var decision = GoRouteIntentPolicy.Resolve("   " );

        Assert.Equal(GoRouteDestination.Clarify, decision.Destination);
    }
}
