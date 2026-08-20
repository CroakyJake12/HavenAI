using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Imagine;

namespace Haven.Desktop.Tests;

public sealed class ImagineAudioClipTransportTests
{
    [Fact]
    public void Preview_plan_uses_real_clip_trim_timing_and_gain_state()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HavenAudioPreviewTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "audio.wav");
        File.WriteAllBytes(source, [0]);
        try
        {
            var assetId = Guid.NewGuid();
            var clipId = Guid.NewGuid();
            var trackId = Guid.NewGuid();
            var project = ImagineProjectSession.CreateProject("Preview") with
            {
                Assets = [new ImagineMediaAsset(assetId, ImagineMediaKind.Audio, "audio.wav", source, source, 1, "hash", DateTimeOffset.UtcNow)],
                Tracks = [new ImagineTrack(trackId, ImagineTrackKind.Audio, "Audio 1", 0, false, 1.5, [new ImagineClip(clipId, assetId, "Clip", 12, 3, 5, 1.2, false)])]
            };

            Assert.True(ImagineAudioClipTransport.TryCreatePlan(project, clipId, out var plan, out _));
            Assert.NotNull(plan);
            Assert.Equal(source, plan!.Path);
            Assert.Equal(3, plan.SourceStartSeconds);
            Assert.Equal(5, plan.DurationSeconds);
            Assert.Equal(12, plan.TimelineStartSeconds);
            Assert.Equal(1.8f, plan.Volume, 3);

            var muted = project with { Tracks = [project.Tracks[0] with { IsMuted = true }] };
            Assert.False(ImagineAudioClipTransport.TryCreatePlan(muted, clipId, out _, out var mutedStatus));
            Assert.Contains("muted", mutedStatus, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }
}
