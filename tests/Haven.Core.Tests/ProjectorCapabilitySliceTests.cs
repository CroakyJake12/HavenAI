using Haven.Application;

namespace Haven.Core.Tests;

public sealed class ProjectorCapabilitySliceTests
{
    [Fact]
    public void Slice_is_disabled_by_default_even_for_a_capable_display()
    {
        var display = Display(ProjectorCapabilityState.Available, ProjectorCapabilityState.Available);

        Assert.False(ProjectorCapabilitySlice.IsEnabledByDefault);
        Assert.False(ProjectorCapabilitySlice.CanRender(display));
        Assert.True(ProjectorCapabilitySlice.CanRender(display, enabled: true));
    }

    [Theory]
    [InlineData(ProjectorCapabilityState.Unknown, ProjectorCapabilityState.Available)]
    [InlineData(ProjectorCapabilityState.Available, ProjectorCapabilityState.Unknown)]
    [InlineData(ProjectorCapabilityState.Unavailable, ProjectorCapabilityState.Available)]
    [InlineData(ProjectorCapabilityState.Available, ProjectorCapabilityState.Unavailable)]
    public void Missing_or_unproven_display_capability_fails_closed(
        ProjectorCapabilityState presentation,
        ProjectorCapabilityState render)
    {
        var display = Display(presentation, render);

        Assert.False(ProjectorCapabilitySlice.CanRender(display, enabled: true));
    }

    private static ProjectorDisplay Display(
        ProjectorCapabilityState presentation,
        ProjectorCapabilityState render) => new(
            "display:test",
            "stable:test",
            "Test display",
            1920,
            1080,
            null,
            60,
            0,
            false,
            ProjectorTransportKind.NativeDisplay,
            ProjectorConnectionKind.Virtual,
            ProjectorDisplayTrust.Private,
            ProjectorCapabilities.Unknown with
            {
                PresentationDisplay = presentation,
                RenderHavenSurface = render
            },
            DateTimeOffset.UtcNow);
}
