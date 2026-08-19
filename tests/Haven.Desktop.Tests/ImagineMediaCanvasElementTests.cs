using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Imagine;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class ImagineMediaCanvasElementTests
{
    [Fact]
    public void Media_canvas_preserves_image_mode_and_exposes_real_audio_video_timelines()
    {
        var session = new ImagineProjectSession(ImagineProjectSession.CreateProject("Media"));
        using var canvas = new ImagineMediaCanvasElement();
        canvas.SetSession(session);
        Assert.IsAssignableFrom<Container>(canvas);
        Assert.Equal(ImagineMediaKind.Image, canvas.Mode);
        Assert.Equal(HavenVisibility.Collapsed, canvas.Timeline.GetValue(HavenProperties.Visibility));
        canvas.SetMode(ImagineMediaKind.Audio);
        Assert.Equal(ImagineMediaKind.Audio, canvas.Timeline.Kind);
        Assert.Equal(HavenVisibility.Visible, canvas.Timeline.GetValue(HavenProperties.Visibility));
        Assert.Contains("playback", canvas.Notice.Content, StringComparison.OrdinalIgnoreCase);
        canvas.SetMode(ImagineMediaKind.Video);
        Assert.Equal(ImagineMediaKind.Video, canvas.Timeline.Kind);
        Assert.Contains("no native video host", canvas.Notice.Content, StringComparison.OrdinalIgnoreCase);
    }
}
