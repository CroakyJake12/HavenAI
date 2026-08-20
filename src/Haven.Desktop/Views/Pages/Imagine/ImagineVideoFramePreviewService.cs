using System.Diagnostics;
using System.Globalization;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Imagine;

internal sealed record ImagineVideoFramePlan(string Path, double SourceSeconds, double TimelineSeconds);
internal sealed record ImagineVideoFrameResult(bool Succeeded, string Status, string? Path = null, double SourceSeconds = 0);

/// <summary>Extracts one real video frame at the selected timeline position. This is a still-frame monitor, not simulated playback.</summary>
internal sealed class ImagineVideoFramePreviewService(ILocalMediaToolLocator tools)
{
    internal static bool TryCreatePlan(ImagineProject project, Guid clipId, double playheadSeconds, out ImagineVideoFramePlan? plan, out string status)
    {
        plan = null;
        status = "Select a video clip first.";
        foreach (var track in project.Tracks)
        {
            var clip = track.Clips.FirstOrDefault(item => item.Id == clipId);
            if (clip is null) continue;
            if (track.Kind != ImagineTrackKind.Video) { status = "The selected clip is not video."; return false; }
            var asset = project.Assets.FirstOrDefault(item => item.Id == clip.AssetId && item.Kind == ImagineMediaKind.Video);
            if (asset is null || string.IsNullOrWhiteSpace(asset.ManagedPath) || !File.Exists(asset.ManagedPath))
            {
                status = "The selected video source is unavailable.";
                return false;
            }
            if (clip.DurationSeconds <= 0) { status = "The selected video clip duration is unknown."; return false; }
            var clipEnd = clip.TimelineStartSeconds + clip.DurationSeconds;
            if (playheadSeconds < clip.TimelineStartSeconds || playheadSeconds > clipEnd)
            {
                status = "Move the playhead over the selected video clip before previewing a frame.";
                return false;
            }
            var relative = Math.Clamp(playheadSeconds - clip.TimelineStartSeconds, 0, Math.Max(0, clip.DurationSeconds - .001));
            plan = new ImagineVideoFramePlan(asset.ManagedPath, Math.Max(0, clip.SourceStartSeconds + relative), playheadSeconds);
            status = "Ready to decode the selected video frame.";
            return true;
        }
        return false;
    }

    public async Task<ImagineVideoFrameResult> CreateFrameAsync(ImagineProject project, Guid clipId, double playheadSeconds, CancellationToken cancellationToken)
    {
        if (!TryCreatePlan(project, clipId, playheadSeconds, out var plan, out var status) || plan is null)
            return new ImagineVideoFrameResult(false, status);
        var ffmpeg = tools.FindExecutable("ffmpeg");
        if (ffmpeg is null)
            return new ImagineVideoFrameResult(false, "Video frame preview requires local ffmpeg. Timeline editing remains available.");

        var directory = Path.Combine(Path.GetTempPath(), "Haven", "Imagine", "video-preview");
        Directory.CreateDirectory(directory);
        var output = Path.Combine(directory, "frame-" + Guid.NewGuid().ToString("N") + ".jpg");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
                 {
                     "-hide_banner", "-loglevel", "error",
                     "-ss", plan.SourceSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                     "-i", plan.Path, "-frames:v", "1",
                     "-vf", "scale='min(1280,iw)':-2", "-q:v", "2", "-y", output
                 })
            startInfo.ArgumentList.Add(argument);

        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start()) return new ImagineVideoFrameResult(false, "Could not start local ffmpeg for video preview.");
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                _ = await outputTask.ConfigureAwait(false);
                var error = await errorTask.ConfigureAwait(false);
                if (process.ExitCode != 0 || !File.Exists(output) || new FileInfo(output).Length == 0)
                {
                    DeleteTemporary(output);
                    var detail = string.IsNullOrWhiteSpace(error) ? string.Empty : " " + error.Trim();
                    return new ImagineVideoFrameResult(false, "ffmpeg could not decode that video frame." + detail);
                }
                return new ImagineVideoFrameResult(true, $"Previewing source frame at {plan.SourceSeconds:0.###}s.", output, plan.SourceSeconds);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                DeleteTemporary(output);
                return new ImagineVideoFrameResult(false, "Video frame decoding timed out.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DeleteTemporary(output);
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            DeleteTemporary(output);
            return new ImagineVideoFrameResult(false, "Video frame preview is unavailable: " + exception.Message);
        }
    }

    internal static void DeleteTemporary(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
