using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Imagine;

namespace Haven.Desktop.Tests;

public sealed class ImagineVideoClipExporterTests
{
    [Fact]
    public void Export_plan_uses_selected_video_source_trim_and_duration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HavenVideoExportTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "video.mp4");
        File.WriteAllBytes(source, [0]);
        try
        {
            var assetId = Guid.NewGuid();
            var clipId = Guid.NewGuid();
            var project = ImagineProjectSession.CreateProject("Video export") with
            {
                Assets = [new ImagineMediaAsset(assetId, ImagineMediaKind.Video, "video.mp4", source, source, 1, "hash", DateTimeOffset.UtcNow)],
                Tracks = [new ImagineTrack(Guid.NewGuid(), ImagineTrackKind.Video, "Video 1", 0, false, 1, [new ImagineClip(clipId, assetId, "Scene", 12, 4.5, 8.25, 1, false)])]
            };

            Assert.True(ImagineVideoClipExporter.TryCreatePlan(project, clipId, out var plan, out _));
            Assert.NotNull(plan);
            Assert.Equal(source, plan!.SourcePath);
            Assert.Equal("Scene", plan.ClipName);
            Assert.Equal(4.5, plan.SourceStartSeconds, 6);
            Assert.Equal(8.25, plan.DurationSeconds, 6);

            var muted = project with { Tracks = [project.Tracks[0] with { IsMuted = true }] };
            Assert.False(ImagineVideoClipExporter.TryCreatePlan(muted, clipId, out _, out var mutedStatus));
            Assert.Contains("muted", mutedStatus, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Missing_ffmpeg_refuses_export_without_creating_output()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HavenVideoExportTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "video.mp4");
        var destination = Path.Combine(directory, "export.mp4");
        File.WriteAllBytes(source, [0]);
        try
        {
            var exporter = new ImagineVideoClipExporter(new MissingToolLocator());
            var result = await exporter.ExportAsync(new ImagineVideoClipExportPlan(source, "Scene", 0, 1), destination, TestContext.Current.CancellationToken);
            Assert.False(result.Succeeded);
            Assert.Contains("ffmpeg", result.Status, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(destination));
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
