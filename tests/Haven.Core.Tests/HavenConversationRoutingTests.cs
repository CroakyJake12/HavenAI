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
    }

    [Fact]
    public void Go_RoutesStrongModeIntentToMode()
    {
        var decision = HavenConversationRouter.Route(Request(
            HavenRoutingOrigin.Go, "let's study biology", strongMode: true, modeKey: "teach"));

        Assert.Equal(HavenRoutingDestinationKind.Mode, decision.Destination);
        Assert.Equal("teach", decision.TargetKey);
        Assert.False(decision.KeepChatAsPrimarySurface);
    }

    [Fact]
    public void Chat_KeepsModeCapabilityInCurrentChat()
    {
        var decision = HavenConversationRouter.Route(Request(
            HavenRoutingOrigin.Chat, "help me plan my week", strongMode: true, modeKey: "plan"));

        Assert.Equal(HavenRoutingDestinationKind.Chat, decision.Destination);
        Assert.True(decision.KeepChatAsPrimarySurface);
    }

    [Fact]
    public void Chat_AllowsProjectTransition()
    {
        var decision = HavenConversationRouter.Route(Request(
            HavenRoutingOrigin.Chat, "open my website project", project: true));

        Assert.Equal(HavenRoutingDestinationKind.Project, decision.Destination);
        Assert.False(decision.KeepChatAsPrimarySurface);
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
            new HavenContextTransfer(null, text, [], [], [], [], [], null, null));
}
