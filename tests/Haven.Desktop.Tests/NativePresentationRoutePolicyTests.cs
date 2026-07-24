using Haven.Desktop.Views.Shell.NativePresentation;

namespace Haven.Desktop.Tests;

public sealed class NativePresentationRoutePolicyTests
{
    [Theory]
    [InlineData("CallPage", null)]
    [InlineData("CallView", null)]
    [InlineData("StandaloneCallSurface", null)]
    [InlineData("ContentControl", "CallPageViewModel")]
    [InlineData("CallPageViewModel", "CallPageViewModel")]
    public void LegacyCallSurfacesRouteToInChatWidget(string surfaceName, string? dataContextName)
    {
        var destination = NativePresentationRoutePolicy.Classify(surfaceName, dataContextName);

        Assert.Equal(NativePresentationDestination.ChatCallWidget, destination);
    }

    [Theory]
    [InlineData("StudioProjectPage", null)]
    [InlineData("WorkspaceHomeView", null)]
    [InlineData("ProjectBrowserView", null)]
    [InlineData("ContentControl", "WorkspaceHomeViewModel")]
    [InlineData("ContentControl", "StudioHomePageViewModel")]
    [InlineData("ContentControl", "ProjectBrowserViewModel")]
    public void LegacyProjectSurfacesRouteToNativeProjects(string surfaceName, string? dataContextName)
    {
        var destination = NativePresentationRoutePolicy.Classify(surfaceName, dataContextName);

        Assert.Equal(NativePresentationDestination.Projects, destination);
    }

    [Fact]
    public void UnrelatedSurfaceIsNotReplaced()
    {
        var destination = NativePresentationRoutePolicy.Classify("SettingsView", "SettingsPageViewModel");

        Assert.Equal(NativePresentationDestination.None, destination);
    }
}
