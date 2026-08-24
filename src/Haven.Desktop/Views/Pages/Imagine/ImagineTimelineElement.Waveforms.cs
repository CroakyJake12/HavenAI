using Haven.Core;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Imagine;

internal sealed partial class ImagineTimelineElement
{
    private readonly ImagineAudioWaveformCache _waveforms = new();

    private void DrawRealWaveform(HavenDrawingContext context, ImagineClip clip, HavenRect rect, double opacity)
    {
        if (_session is null || rect.Width < 12 || rect.Height < 12) return;
        var asset = _session.Project.Assets.FirstOrDefault(item => item.Id == clip.AssetId && item.Kind == ImagineMediaKind.Audio);
        if (asset is null) return;
        var waveform = _waveforms.GetOrQueue(asset.ManagedPath, Invalidate);
        if (waveform is null || waveform.DurationSeconds <= 0 || waveform.Peaks.Length == 0) return;

        var start = Math.Clamp(clip.SourceStartSeconds / waveform.DurationSeconds, 0, 1);
        var sourceDuration = clip.DurationSeconds > 0 ? clip.DurationSeconds : waveform.DurationSeconds - clip.SourceStartSeconds;
        var end = Math.Clamp((clip.SourceStartSeconds + Math.Max(0, sourceDuration)) / waveform.DurationSeconds, start, 1);
        var columns = Math.Clamp((int)Math.Floor(rect.Width / 3), 8, 180);
        var centerY = rect.Y + rect.Height / 2;
        var maxAmplitude = Math.Max(2, rect.Height * .34);
        var pen = new HavenPen(new HavenTokenBrush("TextOnAccent"), 1);

        for (var column = 0; column < columns; column++)
        {
            var fraction = columns == 1 ? 0d : column / (double)(columns - 1);
            var sourceFraction = start + (end - start) * fraction;
            var index = Math.Clamp((int)Math.Round(sourceFraction * (waveform.Peaks.Length - 1)), 0, waveform.Peaks.Length - 1);
            var amplitude = waveform.Peaks[index] * maxAmplitude;
            var x = rect.X + 3 + fraction * Math.Max(0, rect.Width - 6);
            context.Add(new HavenLineCommand(
                new HavenPoint(x, centerY - amplitude),
                new HavenPoint(x, centerY + amplitude),
                pen,
                opacity * (clip.IsMuted ? .2 : .42)));
        }
    }
}
