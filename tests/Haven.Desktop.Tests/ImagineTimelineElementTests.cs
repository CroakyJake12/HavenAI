using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Imagine;

namespace Haven.Desktop.Tests;

public sealed class ImagineTimelineElementTests
{
    [Fact]
    public void Timeline_exposes_real_audio_and_video_modes()
    {
        using var timeline = new ImagineTimelineElement();
        timeline.SetKind(ImagineMediaKind.Audio);
        Assert.Equal(ImagineMediaKind.Audio, timeline.Kind);
        Assert.Equal("Imagine audio timeline", timeline.Accessibility.AccessibleName);
        timeline.SetKind(ImagineMediaKind.Video);
        Assert.Equal(ImagineMediaKind.Video, timeline.Kind);
        Assert.Equal("Imagine video timeline", timeline.Accessibility.AccessibleName);
    }

    [Fact]
    public void Timeline_toolbar_operations_mutate_the_real_session()
    {
        var assetId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var clipId = Guid.NewGuid();
        var project = ImagineProjectSession.CreateProject("Audio") with
        {
            Assets = [new ImagineMediaAsset(assetId, ImagineMediaKind.Audio, "voice.wav", "C:/voice.wav", "C:/managed.wav", 1, "a", DateTimeOffset.UtcNow, "{\"durationSeconds\":10}")],
            Tracks = [new ImagineTrack(trackId, ImagineTrackKind.Audio, "Dialogue", 0, false, 1, [new ImagineClip(clipId, assetId, "voice.wav", 0, 0, 10)])]
        };
        var session = new ImagineProjectSession(project);
        using var timeline = new ImagineTimelineElement();
        timeline.SetSession(session);
        timeline.SetKind(ImagineMediaKind.Audio);
        Assert.True(session.SelectClip(clipId));
        timeline.SetPlayhead(4);
        Assert.True(timeline.SplitSelected());
        Assert.Equal(2, Assert.Single(session.Project.Tracks).Clips.Length);
        var selected = session.Project.Selection.TargetId;
        Assert.NotNull(selected);
        Assert.True(timeline.ToggleMuteSelected());
        Assert.True(session.Project.Tracks.SelectMany(track => track.Clips).Single(clip => clip.Id == selected).IsMuted);
    }

    [Fact]
    public void Timeline_add_track_uses_the_active_media_kind()
    {
        var session = new ImagineProjectSession(ImagineProjectSession.CreateProject("Tracks"));
        using var timeline = new ImagineTimelineElement();
        timeline.SetSession(session);
        timeline.SetKind(ImagineMediaKind.Video);
        var id = timeline.AddTrack();
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(ImagineTrackKind.Video, Assert.Single(session.Project.Tracks).Kind);
    }
}
