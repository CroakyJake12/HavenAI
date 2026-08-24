using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Best-effort local metadata probing for Imagine media. Missing ffprobe never invents media facts.</summary>
internal static class ImagineMediaProbe
{
    public static async Task<string> CreateMetadataJsonAsync(
        string path,
        ImagineMediaKind kind,
        string extension,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["extension"] = extension.ToLowerInvariant()
        };

        if (kind == ImagineMediaKind.Image || new LocalMediaToolLocator().FindExecutable("ffprobe") is not { } ffprobe)
            return JsonSerializer.Serialize(metadata);

        var output = await RunProbeAsync(ffprobe, path, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(output)) return JsonSerializer.Serialize(metadata);

        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            if (root.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var duration) &&
                TryPositiveDouble(duration, out var durationSeconds))
                metadata["durationSeconds"] = durationSeconds;

            if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
            {
                var desiredType = kind == ImagineMediaKind.Audio ? "audio" : "video";
                foreach (var stream in streams.EnumerateArray())
                {
                    if (!stream.TryGetProperty("codec_type", out var codecType) ||
                        !string.Equals(codecType.GetString(), desiredType, StringComparison.OrdinalIgnoreCase)) continue;
                    if (stream.TryGetProperty("codec_name", out var codec) && codec.GetString() is { Length: > 0 } codecName) metadata["codec"] = codecName;
                    if (stream.TryGetProperty("width", out var width) && width.TryGetInt32(out var widthValue) && widthValue > 0) metadata["width"] = widthValue;
                    if (stream.TryGetProperty("height", out var height) && height.TryGetInt32(out var heightValue) && heightValue > 0) metadata["height"] = heightValue;
                    if (stream.TryGetProperty("sample_rate", out var sampleRate) && TryPositiveDouble(sampleRate, out var sampleRateValue)) metadata["sampleRateHz"] = sampleRateValue;
                    if (stream.TryGetProperty("channels", out var channels) && channels.TryGetInt32(out var channelCount) && channelCount > 0) metadata["channels"] = channelCount;
                    break;
                }
            }
        }
        catch (JsonException)
        {
            // Preserve truthful extension-only metadata if the local probe returns malformed output.
        }

        return JsonSerializer.Serialize(metadata);
    }

    private static async Task<string?> RunProbeAsync(string executable, string path, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
                 {
                     "-v", "error", "-show_entries",
                     "format=duration:stream=codec_type,codec_name,width,height,sample_rate,channels",
                     "-of", "json", path
                 })
            startInfo.ArgumentList.Add(argument);

        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start()) return null;
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                _ = await errorTask.ConfigureAwait(false);
                return process.ExitCode == 0 ? await outputTask.ConfigureAwait(false) : null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return null;
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static bool TryPositiveDouble(JsonElement element, out double value)
    {
        value = 0;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value)) return double.IsFinite(value) && value > 0;
        if (element.ValueKind == JsonValueKind.String && double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return double.IsFinite(value) && value > 0;
        return false;
    }
}
