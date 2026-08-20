using Haven.Desktop.Views.Pages.Imagine;

namespace Haven.Desktop.Tests;

public sealed class ImagineAudioWaveformTests
{
    [Fact]
    public void Decoder_builds_bounded_real_peaks_from_pcm_wave()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HavenWaveformTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "tone.wav");
        try
        {
            WriteTone(path, sampleRate: 8000, seconds: 1);
            var waveform = ImagineAudioWaveformCache.Decode(path, TestContext.Current.CancellationToken);
            Assert.NotNull(waveform);
            Assert.Equal(512, waveform!.Peaks.Length);
            Assert.InRange(waveform.DurationSeconds, .99, 1.01);
            Assert.Contains(waveform.Peaks, peak => peak > .2f);
            Assert.All(waveform.Peaks, peak => Assert.InRange(peak, 0f, 1f));
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }

    private static void WriteTone(string path, int sampleRate, int seconds)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        var sampleCount = sampleRate * seconds;
        var dataSize = sampleCount * channels * bitsPerSample / 8;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        for (var index = 0; index < sampleCount; index++)
        {
            var value = Math.Sin(2 * Math.PI * 440 * index / sampleRate);
            writer.Write((short)(value * short.MaxValue * .65));
        }
    }
}
