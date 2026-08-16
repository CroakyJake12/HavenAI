using Haven.Desktop.Views.Shell.NativePresentation;

namespace Haven.Desktop.Tests;

public sealed class NativePresentationRoutePolicyTests
{
    [Theory]
    [InlineData("CallPage", null)]
    [InlineData("CallView", null)]
    [InlineData("StandaloneCallSurface", null)]
    [InlineData("ContentControl", "CallPageViewModel")]
    public void LegacyCallSurfacesRouteToInChatWidget(string surfaceName, string? dataContextName)
    {
        var destination = NativePresentationRoutePolicy.Classify(surfaceName, dataContextName);

        Assert.Equal(NativePresentationDestination.ChatCallWidget, destination);
    }

    [Theory]
    [InlineData("WorkspaceHomeView", null)]
    [InlineData("StudioHomePage", null)]
    [InlineData("ProjectsPage", null)]
    [InlineData("ProjectBrowserView", null)]
    [InlineData("ContentControl", "WorkspaceHomeViewModel")]
    [InlineData("ContentControl", "StudioHomePageViewModel")]
    [InlineData("ContentControl", "ProjectBrowserViewModel")]
    public void LegacyProjectListSurfacesRouteToNativeProjects(string surfaceName, string? dataContextName)
    {
        var destination = NativePresentationRoutePolicy.Classify(surfaceName, dataContextName);

        Assert.Equal(NativePresentationDestination.Projects, destination);
    }

    [Theory]
    [InlineData("ChatView", null)]
    [InlineData("ContentControl", "ChatPageViewModel")]
    [InlineData("ContentControl", "ConversationViewModel")]
    public void LegacyChatSurfacesRouteToNativeChat(string surfaceName, string? dataContextName)
    {
        var destination = NativePresentationRoutePolicy.Classify(surfaceName, dataContextName);

        Assert.Equal(NativePresentationDestination.Chat, destination);
    }

    [Fact]
    public void HavenNativeNewChatPageIsNotRedirectedAgain()
    {
        var destination = NativePresentationRoutePolicy.Classify("NewChatPage", "ConversationViewModel");

        Assert.Equal(NativePresentationDestination.None, destination);
    }

    [Theory]
    [InlineData("StudioProjectPage", null)]
    [InlineData("ProjectHomeView", "ProjectHomeViewModel")]
    [InlineData("ProjectFilesView", "ProjectFilesViewModel")]
    public void ProjectDetailSurfacesAreNotReplaced(string surfaceName, string? dataContextName)
    {
        var destination = NativePresentationRoutePolicy.Classify(surfaceName, dataContextName);

        Assert.Equal(NativePresentationDestination.None, destination);
    }

    [Fact]
    public void UnrelatedSurfaceIsNotReplaced()
    {
        var destination = NativePresentationRoutePolicy.Classify("SettingsView", "SettingsPageViewModel");

        Assert.Equal(NativePresentationDestination.None, destination);
    }
}
