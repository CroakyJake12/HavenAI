using System.Diagnostics;
using System.Globalization;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Desktop.Views.Pages.Imagine;

internal sealed record ImagineVideoClipExportPlan(string SourcePath, string ClipName, double SourceStartSeconds, double DurationSeconds);
internal sealed record ImagineVideoClipExportResult(bool Succeeded, string Status, string? Path = null);

/// <summary>Exports one selected trimmed video clip through real local ffmpeg. This does not claim to render the full multitrack composition.</summary>
internal sealed class ImagineVideoClipExporter
{
    private readonly ILocalMediaToolLocator _tools;

    public ImagineVideoClipExporter(ILocalMediaToolLocator? tools = null)
    {
        _tools = tools ?? new LocalMediaToolLocator();
    }

    internal static bool TryCreatePlan(ImagineProject project, Guid clipId, out ImagineVideoClipExportPlan? plan, out string status)
    {
        plan = null;
        status = "Select a video clip first.";
        foreach (var track in project.Tracks)
        {
            var clip = track.Clips.FirstOrDefault(item => item.Id == clipId);
            if (clip is null) continue;
            if (track.Kind != ImagineTrackKind.Video) { status = "The selected clip is not video."; return false; }
            if (track.IsMuted || clip.IsMuted) { status = "The selected video clip or its track is muted."; return false; }
            var asset = project.Assets.FirstOrDefault(item => item.Id == clip.AssetId && item.Kind == ImagineMediaKind.Video);
            if (asset is null || string.IsNullOrWhiteSpace(asset.ManagedPath) || !File.Exists(asset.ManagedPath))
            {
                status = "The selected video source is unavailable.";
                return false;
            }
            if (clip.DurationSeconds <= 0) { status = "The selected video clip duration is unknown."; return false; }
            plan = new ImagineVideoClipExportPlan(asset.ManagedPath, clip.Name, Math.Max(0, clip.SourceStartSeconds), clip.DurationSeconds);
            status = "Ready to export the selected video clip.";
            return true;
        }
        return false;
    }

    public async Task<ImagineVideoClipExportResult> ExportAsync(ImagineVideoClipExportPlan plan, string destination, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(destination)) return new ImagineVideoClipExportResult(false, "Choose an MP4 destination first.");
        var output = Path.GetFullPath(destination.Trim());
        var extension = Path.GetExtension(output);
        if (string.IsNullOrWhiteSpace(extension)) output += ".mp4";
        else if (!extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            return new ImagineVideoClipExportResult(false, "Selected video clip export currently supports MP4 only.");

        var ffmpeg = _tools.FindExecutable("ffmpeg");
        if (ffmpeg is null) return new ImagineVideoClipExportResult(false, "Selected video clip export requires local ffmpeg. The Imagine project was not changed.");

        var directory = Path.GetDirectoryName(output);
        if (string.IsNullOrWhiteSpace(directory)) return new ImagineVideoClipExportResult(false, "The export destination is invalid.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".haven-video-" + Guid.NewGuid().ToString("N") + ".mp4");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
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
                     "-ss", plan.SourceStartSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                     "-i", plan.SourcePath, "-t", plan.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                     "-map", "0:v:0", "-map", "0:a?",
                     "-c:v", "libx264", "-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p",
                     "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", "-y", temporary
                 })
            startInfo.ArgumentList.Add(argument);

        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start()) return new ImagineVideoClipExportResult(false, "Could not start local ffmpeg for video export.");
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                _ = await outputTask.ConfigureAwait(false);
                var error = await errorTask.ConfigureAwait(false);
                if (process.ExitCode != 0 || !File.Exists(temporary) || new FileInfo(temporary).Length == 0)
                {
                    var detail = string.IsNullOrWhiteSpace(error) ? string.Empty : error.Trim();
                    if (detail.Length > 500) detail = detail[..500] + "...";
                    return new ImagineVideoClipExportResult(false, "ffmpeg could not export the selected video clip." + (detail.Length == 0 ? string.Empty : " " + detail));
                }
                File.Move(temporary, output, overwrite: true);
                return new ImagineVideoClipExportResult(true, "Exported selected video clip. Separate timeline audio tracks and other video tracks were not rendered.", output);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return new ImagineVideoClipExportResult(false, "Video clip export timed out.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new ImagineVideoClipExportResult(false, "Video clip export is unavailable: " + exception.Message);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
