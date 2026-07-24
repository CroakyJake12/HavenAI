using Haven.Core;

namespace Haven.Core.Tests;

public sealed class HavenConversationRoutingTests
{
    [Fact]
    public void Go_RoutesGenericConversationToChat()
    {
        var decision = HavenConversationRouter.Route(Request(HavenRoutingOrigin.Go, "hello"));

        Assert.Equal(HavenRoutingDestinationKind.Chat, decision.Destination);
        Assert.True(decision.KeepChatAsPrimarySurface);
        AssertTransfer(decision, HavenRoutingDestinationKind.Chat, "generic Go request");
    }

    [Fact]
    public void Go_RoutesStrongModeIntentToMode()
    {
        var decision = HavenConversationRouter.Route(Request(
            HavenRoutingOrigin.Go, "let's study biology", strongMode: true, modeKey: "teach"));

        Assert.Equal(HavenRoutingDestinationKind.Mode, decision.Destination);
        Assert.Equal("teach", decision.TargetKey);
        Assert.False(decision.KeepChatAsPrimarySurface);
        AssertTransfer(decision, HavenRoutingDestinationKind.Mode, "strong mode intent");
    }

    [Fact]
    public void Chat_KeepsModeCapabilityInCurrentChat()
    {
        var decision = HavenConversationRouter.Route(Request(
            HavenRoutingOrigin.Chat, "help me plan my week", strongMode: true, modeKey: "plan"));

        Assert.Equal(HavenRoutingDestinationKind.Chat, decision.Destination);
        Assert.True(decision.KeepChatAsPrimarySurface);
        AssertTransfer(decision, HavenRoutingDestinationKind.Chat, "current conversation");
    }

    [Fact]
    public void Chat_AllowsProjectTransition()
    {
        var decision = HavenConversationRouter.Route(Request(
            HavenRoutingOrigin.Chat, "open my website project", project: true));

        Assert.Equal(HavenRoutingDestinationKind.Project, decision.Destination);
        Assert.False(decision.KeepChatAsPrimarySurface);
        AssertTransfer(decision, HavenRoutingDestinationKind.Project, "existing project");
    }

    [Fact]
    public void ProjectCreation_CarriesCreatorDestinationAndReason()
    {
        var decision = HavenConversationRouter.Route(Request(
            HavenRoutingOrigin.Go, "create a project for my notes", project: true));

        Assert.Equal(HavenRoutingDestinationKind.ProjectCreator, decision.Destination);
        AssertTransfer(decision, HavenRoutingDestinationKind.ProjectCreator, "creates a project");
    }

    private static void AssertTransfer(
        HavenRoutingDecision decision,
        HavenRoutingDestinationKind expectedDestination,
        string expectedReasonFragment)
    {
        Assert.Equal(expectedDestination, decision.Transfer.Destination);
        Assert.Contains(expectedReasonFragment, decision.Transfer.TransferReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("return-to-source", decision.Transfer.ReturnRoute);
        Assert.Single(decision.Transfer.AttachmentIds);
    }

    private static HavenRoutingRequest Request(
        HavenRoutingOrigin origin,
        string text,
        bool project = false,
        bool strongMode = false,
        string? modeKey = null) =>
        new(
            origin,
            text,
            false,
            project,
            strongMode,
            modeKey,
            false,
            new HavenContextTransfer(
                null,
                text,
                [],
                [Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")],
                [],
                [],
                [],
                null,
                "return-to-source"));
}
