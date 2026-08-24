using Avalonia.Threading;
using NAudio.Wave;

namespace Haven.Desktop.Views.Pages.Imagine;

internal sealed record ImagineAudioWaveform(double DurationSeconds, float[] Peaks);

/// <summary>Builds a bounded sampled peak envelope from real local audio. Failed decodes stay absent rather than fabricated.</summary>
internal sealed class ImagineAudioWaveformCache : IDisposable
{
    private const int PeakCount = 512;
    private const int FramesPerProbe = 1024;
    private readonly object _gate = new();
    private readonly Dictionary<string, ImagineAudioWaveform?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _dispose = new();

    public ImagineAudioWaveform? GetOrQueue(string? path, Action invalidate)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        var fullPath = Path.GetFullPath(path);
        lock (_gate)
        {
            if (_cache.TryGetValue(fullPath, out var cached)) return cached;
            if (!_pending.Add(fullPath)) return null;
        }

        _ = LoadAsync(fullPath, invalidate, _dispose.Token);
        return null;
    }

    internal static ImagineAudioWaveform? Decode(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            using var reader = new AudioFileReader(path);
            if (reader.TotalTime <= TimeSpan.Zero || reader.Length <= 0 || reader.WaveFormat.Channels <= 0) return null;
            var blockAlign = Math.Max(1, reader.WaveFormat.BlockAlign);
            var totalFrames = Math.Max(1L, reader.Length / blockAlign);
            var peaks = new float[PeakCount];
            var channels = reader.WaveFormat.Channels;
            var buffer = new float[FramesPerProbe * channels];

            for (var bucket = 0; bucket < PeakCount; bucket++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bucketStart = totalFrames * bucket / PeakCount;
                var bucketEnd = Math.Max(bucketStart + 1, totalFrames * (bucket + 1) / PeakCount);
                var probeFrames = Math.Min(FramesPerProbe, bucketEnd - bucketStart);
                var probeStart = bucketStart + Math.Max(0, (bucketEnd - bucketStart - probeFrames) / 2);
                reader.Position = Math.Clamp(probeStart * blockAlign, 0, Math.Max(0, reader.Length - blockAlign));
                var wantedSamples = (int)Math.Min(buffer.Length, probeFrames * channels);
                var read = reader.Read(buffer, 0, wantedSamples);
                var peak = 0f;
                for (var sample = 0; sample < read; sample++)
                    peak = Math.Max(peak, Math.Abs(buffer[sample]));
                peaks[bucket] = Math.Clamp(peak, 0f, 1f);
            }

            return new ImagineAudioWaveform(reader.TotalTime.TotalSeconds, peaks);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    private async Task LoadAsync(string path, Action invalidate, CancellationToken cancellationToken)
    {
        ImagineAudioWaveform? waveform = null;
        try { waveform = await Task.Run(() => Decode(path, cancellationToken), cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            waveform = null;
        }
        finally
        {
            lock (_gate)
            {
                _pending.Remove(path);
                if (!cancellationToken.IsCancellationRequested) _cache[path] = waveform;
            }
        }

        if (!cancellationToken.IsCancellationRequested) Dispatcher.UIThread.Post(invalidate);
    }

    public void Dispose()
    {
        _dispose.Cancel();
        _dispose.Dispose();
        lock (_gate)
        {
            _pending.Clear();
            _cache.Clear();
        }
    }
}
