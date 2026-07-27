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
