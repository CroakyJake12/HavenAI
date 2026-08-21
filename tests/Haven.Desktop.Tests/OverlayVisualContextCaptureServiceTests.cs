using Haven.Application;
using Haven.Desktop.Overlay;
namespace Haven.Desktop.Tests;
public sealed class OverlayVisualContextCaptureServiceTests
{
    [Fact]
    public void Unknown_picker_source_remains_image_context()
    {
        var at = DateTimeOffset.Parse("2026-08-20T06:00:00Z");
        var source = new ScreenShareSource("capture", "Selected display", ScreenShareSourceKind.Unknown);
        var snapshot = new ScreenShareSnapshot("AA==", 1920, 1080, at);
        var context = OverlayVisualContextCaptureService.BuildContext(source, snapshot, @"C:\tmp\capture.jpg");
        Assert.Equal(OverlayContextKind.Image, context.Kind);
        Assert.Equal(OverlayContextPermissionState.Granted, context.Provenance.PermissionState);
        Assert.Null(context.Provenance.SourceWindow);
        Assert.Equal(at.AddMinutes(2), context.Provenance.ExpiresAt);
        var item = Assert.Single(context.SelectedItems);
        Assert.Equal(OverlaySelectionKind.Image, item.Kind);
        Assert.Equal("image/jpeg", item.Attachment?.MimeType);
        Assert.Contains("Unknown", item.Attachment?.MetadataJson);
    }
    [Fact]
    public void Known_window_source_maps_to_window_context()
    {
        var source = new ScreenShareSource("window", "Calculator", ScreenShareSourceKind.Window);
        var snapshot = new ScreenShareSnapshot("AA==", 800, 600, DateTimeOffset.UtcNow);
        var context = OverlayVisualContextCaptureService.BuildContext(source, snapshot, @"C:\tmp\window.jpg");
        Assert.Equal(OverlayContextKind.Window, context.Kind);
        Assert.Equal("Calculator", context.Provenance.SourceWindow);
        Assert.Equal(OverlaySelectionKind.Window, Assert.Single(context.SelectedItems).Kind);
    }
}
