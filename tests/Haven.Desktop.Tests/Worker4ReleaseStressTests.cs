using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Imagine;
using Haven.UI;

namespace Haven.Desktop.Tests;

public sealed class Worker4ReleaseStressTests
{
    [Fact]
    public void Imagine_image_keeps_large_layer_stack_without_truncation_and_preserves_canvas_when_narrow()
    {
        using var scene = new ImagineWorkspaceScene();
        var project = ImagineProjectSession.CreateProject("Layer stress") with
        {
            Objects = Enumerable.Range(0, 160)
                .Select(index => new ImagineEditableObject(
                    Guid.NewGuid(),
                    ImagineObjectKind.Rectangle,
                    $"Layer {index}",
                    null,
                    new ImagineTransform(index % 20 * 12, index / 20 * 12, 120, 80),
                    index,
                    string.Empty,
                    "#00A7B3",
                    true,
                    false))
                .ToArray()
        };
        var session = new ImagineProjectSession(project);

        scene.SetSession(session);
        scene.Sync(session.Project, [session.Project]);
        scene.SetViewportWidth(700);

        Assert.Equal(160, scene.Layers.Items.Count);
        Assert.Equal(HavenVisibility.Visible, scene.Canvas.GetValue(HavenProperties.Visibility));
        Assert.Equal(0, scene.Canvas.GetValue(HavenProperties.Column));
        Assert.Equal(HavenVisibility.Collapsed, scene.Left.GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Collapsed, scene.Right.GetValue(HavenProperties.Visibility));
    }

    [Fact]
    public void Imagine_video_keeps_large_mixed_timeline_without_track_or_clip_caps()
    {
        using var scene = new ImagineWorkspaceScene();
        var tracks = Enumerable.Range(0, 24)
            .Select(trackIndex =>
            {
                var kind = trackIndex % 3 == 0 ? ImagineTrackKind.Audio : ImagineTrackKind.Video;
                var clips = Enumerable.Range(0, 80)
                    .Select(clipIndex => new ImagineClip(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        $"Clip {trackIndex}-{clipIndex}",
                        clipIndex * 2d,
                        0,
                        1.5,
                        1,
                        false))
                    .ToArray();
                return new ImagineTrack(Guid.NewGuid(), kind, $"Track {trackIndex}", trackIndex, false, 1, clips);
            })
            .ToArray();
        var session = new ImagineProjectSession(ImagineProjectSession.CreateProject("Timeline stress") with { Tracks = tracks });

        scene.SetSession(session);
        scene.SetMode(ImagineMediaKind.Video);

        Assert.Equal(24, scene.Canvas.Timeline.CurrentTracks.Count);
        Assert.Equal(1_920, scene.Canvas.Timeline.CurrentTracks.Sum(track => track.Clips.Length));
        Assert.Contains(scene.Canvas.Timeline.CurrentTracks, track => track.Kind == ImagineTrackKind.Audio);
        Assert.Contains(scene.Canvas.Timeline.CurrentTracks, track => track.Kind == ImagineTrackKind.Video);
    }

    [Fact]
    public void Vision_large_source_geometry_stays_finite_and_letterbox_correct()
    {
        var source = VisionRegionCropper.MapDisplaySelectionToSource(
            new HavenRect(.125, .25, .75, .5),
            viewportWidth: 900,
            viewportHeight: 900,
            sourceWidth: 12_000,
            sourceHeight: 8_000);
        var oneToOne = VisionPreviewElement.CalculateOneToOneZoom(
            viewportWidth: 876,
            viewportHeight: 876,
            sourceWidth: 12_000,
            sourceHeight: 8_000,
            renderScaling: 1.5);

        Assert.True(double.IsFinite(oneToOne));
        Assert.True(oneToOne > 1);
        Assert.InRange(source.X, 0, 1);
        Assert.InRange(source.Y, 0, 1);
        Assert.InRange(source.Width, 0, 1);
        Assert.InRange(source.Height, 0, 1);
        Assert.True(source.Width > 0);
        Assert.True(source.Height > 0);
    }
}
