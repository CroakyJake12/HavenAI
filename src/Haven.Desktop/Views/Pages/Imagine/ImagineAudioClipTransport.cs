using Avalonia.Threading;
using Haven.Core;
using NAudio.Wave;

namespace Haven.Desktop.Views.Pages.Imagine;

internal sealed record ImagineAudioPreviewPlan(
    string Path,
    double SourceStartSeconds,
    double DurationSeconds,
    double TimelineStartSeconds,
    float Volume);

/// <summary>Previews one selected audio clip using the real local audio device. It does not pretend to be a multitrack mixer.</summary>
internal sealed class ImagineAudioClipTransport : IDisposable
{
    private readonly DispatcherTimer _timer;
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;
    private ImagineAudioPreviewPlan? _plan;
    private Action<double>? _positionChanged;

    public ImagineAudioClipTransport()
    {
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Background, (_, _) => Tick());
        _timer.Stop();
    }

    internal static bool TryCreatePlan(ImagineProject project, Guid clipId, out ImagineAudioPreviewPlan? plan, out string status)
    {
        plan = null;
        status = "Select an audio clip first.";
        foreach (var track in project.Tracks)
        {
            var clip = track.Clips.FirstOrDefault(item => item.Id == clipId);
            if (clip is null) continue;
            if (track.Kind != ImagineTrackKind.Audio) { status = "The selected clip is not audio."; return false; }
            if (track.IsMuted || clip.IsMuted) { status = "The selected audio clip or its track is muted."; return false; }
            var asset = project.Assets.FirstOrDefault(item => item.Id == clip.AssetId && item.Kind == ImagineMediaKind.Audio);
            if (asset is null || string.IsNullOrWhiteSpace(asset.ManagedPath) || !File.Exists(asset.ManagedPath))
            {
                status = "The selected audio source is unavailable.";
                return false;
            }
            if (clip.DurationSeconds <= 0) { status = "The selected clip duration is unknown."; return false; }
            plan = new ImagineAudioPreviewPlan(
                asset.ManagedPath,
                Math.Max(0, clip.SourceStartSeconds),
                clip.DurationSeconds,
                Math.Max(0, clip.TimelineStartSeconds),
                (float)Math.Clamp(track.Gain * clip.Gain, 0, 4));
            status = "Ready to preview selected audio clip.";
            return true;
        }
        return false;
    }

    public bool Play(ImagineProject project, Guid clipId, Action<double> positionChanged, out string status)
    {
        if (!TryCreatePlan(project, clipId, out var plan, out status) || plan is null) return false;
        Stop();
        try
        {
            var reader = new AudioFileReader(plan.Path);
            if (plan.SourceStartSeconds >= reader.TotalTime.TotalSeconds)
            {
                reader.Dispose();
                status = "The clip starts beyond the end of its audio source.";
                return false;
            }
            reader.CurrentTime = TimeSpan.FromSeconds(plan.SourceStartSeconds);
            reader.Volume = plan.Volume;
            var output = new WaveOutEvent();
            output.Init(reader);
            output.PlaybackStopped += OnPlaybackStopped;
            _reader = reader;
            _output = output;
            _plan = plan;
            _positionChanged = positionChanged;
            _positionChanged(plan.TimelineStartSeconds);
            _timer.Start();
            output.Play();
            status = "Previewing selected audio clip.";
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Stop();
            status = "Audio preview is unavailable: " + exception.Message;
            return false;
        }
    }

    public bool PauseOrResume(out string status)
    {
        if (_output is null) { status = "No audio preview is active."; return false; }
        if (_output.PlaybackState == PlaybackState.Playing)
        {
            _output.Pause();
            status = "Audio preview paused.";
            return true;
        }
        if (_output.PlaybackState == PlaybackState.Paused)
        {
            _output.Play();
            status = "Audio preview resumed.";
            return true;
        }
        status = "No audio preview is active.";
        return false;
    }

    public bool Stop(out string status)
    {
        var active = _output is not null || _reader is not null;
        Stop();
        status = active ? "Audio preview stopped." : "No audio preview is active.";
        return active;
    }

    public void Stop()
    {
        _timer.Stop();
        if (_output is not null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            try { _output.Stop(); } catch (Exception exception) when (exception is not OutOfMemoryException) { }
            _output.Dispose();
        }
        _reader?.Dispose();
        _output = null;
        _reader = null;
        _plan = null;
        _positionChanged = null;
    }

    private void Tick()
    {
        if (_reader is null || _plan is null || _output?.PlaybackState != PlaybackState.Playing) return;
        var elapsed = Math.Max(0, _reader.CurrentTime.TotalSeconds - _plan.SourceStartSeconds);
        _positionChanged?.Invoke(_plan.TimelineStartSeconds + Math.Min(elapsed, _plan.DurationSeconds));
        if (elapsed >= _plan.DurationSeconds) Stop();
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (_reader is null || _plan is null) return;
        var elapsed = Math.Max(0, _reader.CurrentTime.TotalSeconds - _plan.SourceStartSeconds);
        if (elapsed + .02 >= _plan.DurationSeconds || _reader.Position >= _reader.Length)
            Dispatcher.UIThread.Post(Stop);
    }

    public void Dispose()
    {
        Stop();
        _timer.Stop();
    }
}
