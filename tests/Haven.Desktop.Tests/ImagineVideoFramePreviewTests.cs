using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Imagine;

namespace Haven.Desktop.Tests;

public sealed class ImagineVideoFramePreviewTests
{
    [Fact]
    public void Preview_plan_maps_timeline_playhead_to_real_video_source_time()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HavenVideoFrameTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "video.mp4");
        File.WriteAllBytes(source, [0]);
        try
        {
            var assetId = Guid.NewGuid();
            var clipId = Guid.NewGuid();
            var project = ImagineProjectSession.CreateProject("Video") with
            {
                Assets = [new ImagineMediaAsset(assetId, ImagineMediaKind.Video, "video.mp4", source, source, 1, "hash", DateTimeOffset.UtcNow)],
                Tracks = [new ImagineTrack(Guid.NewGuid(), ImagineTrackKind.Video, "Video 1", 0, false, 1, [new ImagineClip(clipId, assetId, "Clip", 10, 4, 8, 1, false)])]
            };

            Assert.True(ImagineVideoFramePreviewService.TryCreatePlan(project, clipId, 12.5, out var plan, out _));
            Assert.NotNull(plan);
            Assert.Equal(6.5, plan!.SourceSeconds, 6);
            Assert.Equal(12.5, plan.TimelineSeconds, 6);
            Assert.False(ImagineVideoFramePreviewService.TryCreatePlan(project, clipId, 19, out _, out var outside));
            Assert.Contains("playhead", outside, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Missing_ffmpeg_returns_honest_no_preview_result()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HavenVideoFrameTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "video.mp4");
        File.WriteAllBytes(source, [0]);
        try
        {
            var assetId = Guid.NewGuid();
            var clipId = Guid.NewGuid();
            var project = ImagineProjectSession.CreateProject("Video") with
            {
                Assets = [new ImagineMediaAsset(assetId, ImagineMediaKind.Video, "video.mp4", source, source, 1, "hash", DateTimeOffset.UtcNow)],
                Tracks = [new ImagineTrack(Guid.NewGuid(), ImagineTrackKind.Video, "Video 1", 0, false, 1, [new ImagineClip(clipId, assetId, "Clip", 0, 0, 5, 1, false)])]
            };
            var service = new ImagineVideoFramePreviewService(new MissingToolLocator());
            var result = await service.CreateFrameAsync(project, clipId, 1, TestContext.Current.CancellationToken);
            Assert.False(result.Succeeded);
            Assert.Contains("ffmpeg", result.Status, StringComparison.OrdinalIgnoreCase);
            Assert.Null(result.Path);
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }

    private sealed class MissingToolLocator : ILocalMediaToolLocator
    {
        public string? FindExecutable(string name) => null;
    }
}
