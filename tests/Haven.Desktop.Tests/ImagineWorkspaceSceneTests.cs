using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Imagine;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class ImagineWorkspaceSceneTests
{
    [Fact]
    public void Visible_scene_exposes_distinct_image_audio_and_video_workspaces()
    {
        using var scene = new ImagineWorkspaceScene();
        var session = new ImagineProjectSession(ImagineProjectSession.CreateProject("Creative"));
        scene.SetSession(session);

        Assert.Equal(ImagineMediaKind.Image, scene.Mode);
        var globalExport = scene.Root.DescendantsAndSelf().OfType<Button>().Single(button => button.Name == "Export");
        Assert.Equal("Export image", globalExport.Content);
        Assert.Equal(HavenVisibility.Visible, scene.ImageTools.GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Collapsed, scene.TimelineTools.GetValue(HavenProperties.Visibility));
        scene.SetMode(ImagineMediaKind.Audio);
        Assert.Equal(ImagineMediaKind.Audio, scene.Canvas.Timeline.Kind);
        Assert.Equal(HavenVisibility.Collapsed, scene.ImageTools.GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Visible, scene.TimelineTools.GetValue(HavenProperties.Visibility));
        Assert.Contains(scene.Root.DescendantsAndSelf().OfType<Button>(), button => button.Name == "TimelinePreviewAudio" && button.Content == "Preview audio clip");
        Assert.Contains(scene.Root.DescendantsAndSelf().OfType<Button>(), button => button.Name == "TimelinePauseAudio");
        Assert.Contains(scene.Root.DescendantsAndSelf().OfType<Button>(), button => button.Name == "TimelineStopAudio");
        Assert.Contains("previewed", scene.Canvas.Notice.Content, StringComparison.OrdinalIgnoreCase);
        scene.SetMode(ImagineMediaKind.Video);
        Assert.Equal(ImagineMediaKind.Video, scene.Canvas.Timeline.Kind);
        var frameButton = scene.Root.DescendantsAndSelf().OfType<Button>().Single(button => button.Name == "TimelinePreviewFrame");
        var exportClip = scene.Root.DescendantsAndSelf().OfType<Button>().Single(button => button.Name == "TimelineExportVideoClip");
        Assert.Equal(HavenVisibility.Visible, frameButton.GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Visible, exportClip.GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Visible, scene.Canvas.VideoPreview.GetValue(HavenProperties.Visibility));
        Assert.Equal("Decoded video frame preview", scene.Canvas.VideoPreview.Accessibility.AccessibleName);
        Assert.Contains("ffmpeg-decoded", scene.Canvas.Notice.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not yet available", scene.Canvas.Notice.Content, StringComparison.OrdinalIgnoreCase);
        scene.SetMode(ImagineMediaKind.Audio);
        Assert.Equal(HavenVisibility.Collapsed, frameButton.GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Collapsed, exportClip.GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Collapsed, scene.Canvas.VideoPreview.GetValue(HavenProperties.Visibility));
    }

    [Fact]
    public void Timeline_mode_preserves_central_workspace_at_narrow_width()
    {
        using var scene = new ImagineWorkspaceScene();
        scene.SetMode(ImagineMediaKind.Audio);
        scene.SetViewportWidth(700);
        Assert.Equal("1fr", scene.Body.Columns);
        Assert.Equal(0, scene.Canvas.GetValue(HavenProperties.Column));
        Assert.Equal(HavenVisibility.Collapsed, scene.Left.GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Collapsed, scene.Right.GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Visible, scene.Canvas.Timeline.GetValue(HavenProperties.Visibility));
    }

    [Fact]
    public void Timeline_toolbar_adds_real_tracks_to_active_mode()
    {
        using var scene = new ImagineWorkspaceScene();
        var session = new ImagineProjectSession(ImagineProjectSession.CreateProject("Tracks"));
        scene.SetSession(session); scene.SetMode(ImagineMediaKind.Video);
        var addAudio = scene.Root.DescendantsAndSelf().OfType<Button>().Single(button => button.Name == "TimelineAddAudioTrack");
        Assert.Equal(HavenVisibility.Visible, addAudio.GetValue(HavenProperties.Visibility));
        Assert.NotEqual(Guid.Empty, scene.Canvas.Timeline.AddTrack());
        Assert.NotEqual(Guid.Empty, scene.Canvas.Timeline.AddTrack(ImagineTrackKind.Audio));
        Assert.Contains(session.Project.Tracks, track => track.Kind == ImagineTrackKind.Video);
        Assert.Contains(session.Project.Tracks, track => track.Kind == ImagineTrackKind.Audio);

        scene.SetMode(ImagineMediaKind.Audio);
        Assert.Equal(HavenVisibility.Collapsed, addAudio.GetValue(HavenProperties.Visibility));
        Assert.Equal(Guid.Empty, scene.Canvas.Timeline.AddTrack(ImagineTrackKind.Video));
    }

    [Fact]
    public void Video_timeline_shows_video_and_audio_tracks_while_audio_mode_stays_audio_only()
    {
        using var scene = new ImagineWorkspaceScene();
        var session = new ImagineProjectSession(ImagineProjectSession.CreateProject("Mixed timeline"));
        session.AddTrack(ImagineTrackKind.Video, "Picture");
        session.AddTrack(ImagineTrackKind.Audio, "Dialogue");
        scene.SetSession(session);

        scene.SetMode(ImagineMediaKind.Video);
        Assert.Contains(scene.Canvas.Timeline.CurrentTracks, track => track.Kind == ImagineTrackKind.Video);
        Assert.Contains(scene.Canvas.Timeline.CurrentTracks, track => track.Kind == ImagineTrackKind.Audio);

        scene.SetMode(ImagineMediaKind.Audio);
        Assert.NotEmpty(scene.Canvas.Timeline.CurrentTracks);
        Assert.All(scene.Canvas.Timeline.CurrentTracks, track => Assert.Equal(ImagineTrackKind.Audio, track.Kind));
    }

    [Fact]
    public void Image_mode_projects_real_layer_stack_and_hides_it_in_timeline_modes()
    {
        using var scene = new ImagineWorkspaceScene();
        var session = new ImagineProjectSession(ImagineProjectSession.CreateProject("Layers"));
        var backId = session.AddRectangle(30, 30, 200, 100);
        var frontId = session.AddRectangle(70, 60, 200, 100);
        Assert.True(session.ToggleObjectVisibility(backId));
        Assert.True(session.ToggleObjectLock(frontId));
        scene.SetSession(session);
        scene.Sync(session.Project, [session.Project]);

        Assert.Equal(2, scene.Layers.Items.Count);
        Assert.Equal(HavenVisibility.Visible, scene.LayerPanel.GetValue(HavenProperties.Visibility));
        var front = scene.Layers.Items[0];
        var back = scene.Layers.Items[1];
        Assert.Equal("Unlock", front.GetComponent<Button>("Lock").Content);
        Assert.Equal("Hide", front.GetComponent<Button>("Visibility").Content);
        Assert.Equal("Show", back.GetComponent<Button>("Visibility").Content);

        scene.SetMode(ImagineMediaKind.Audio);
        Assert.Equal(HavenVisibility.Collapsed, scene.LayerPanel.GetValue(HavenProperties.Visibility));
        scene.SetMode(ImagineMediaKind.Video);
        Assert.Equal(HavenVisibility.Collapsed, scene.LayerPanel.GetValue(HavenProperties.Visibility));
    }

    [Fact]
    public void Unknown_duration_clip_hit_area_matches_its_rendered_placeholder_width()
    {
        using var timeline = new ImagineTimelineElement();
        timeline.ZoomBy(.01);
        var clip = new ImagineClip(Guid.NewGuid(), Guid.NewGuid(), "unknown.wav", 0, 0, 0);

        Assert.True(timeline.HitClipAtTime(clip, 4));
        Assert.False(timeline.HitClipAtTime(clip, 9));
    }
}
