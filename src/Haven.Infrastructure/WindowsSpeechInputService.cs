using System.Text;
using System.Threading.Channels;
using Haven.Application;
using Haven.Core;
using NAudio.Wave;
using WebRtcVadSharp;
using Whisper.net;

namespace Haven.Infrastructure;

/// <summary>
/// Windows microphone capture with WebRTC VAD and local Whisper transcription.
/// Raw PCM is held only for the current utterance and discarded immediately after
/// inference; callers receive derived levels and text only.
/// </summary>
public sealed class WindowsSpeechInputService : ISpeechInputService, IAsyncDisposable
{
    private const int SampleRateHz = 16_000;
    private const int BytesPerSample = 2;
    private const int FrameMilliseconds = 30;
    private const int FrameBytes = SampleRateHz * BytesPerSample * FrameMilliseconds / 1000;
    private const int SilenceFramesToFinish = 20;
    private const int MinimumUtteranceBytes = SampleRateHz * BytesPerSample / 4;
    private const int MaximumUtteranceBytes = SampleRateHz * BytesPerSample * 30;

    private readonly object _sync = new();
    private readonly AsyncLocal<bool> _insideWorker = new();
    private WaveInEvent? _capture;
    private WebRtcVad? _vad;
    private Channel<SpeechWorkItem>? _work;
    private Task? _worker;
    private CancellationTokenSource? _captureCts;
    private Func<SpeechInputEvent, CancellationToken, Task>? _callback;
    private SpeechInputOptions? _options;
    private readonly List<byte> _pendingFrames = [];
    private MemoryStream _utterance = new();
    private bool _inSpeech;
    private bool _pushToTalkHeld;
    private bool _stopping;
    private int _silenceFrames;

    public bool IsAvailable => OperatingSystem.IsWindows() && TryGetDeviceCount() > 0;
    public string? UnavailableReason => IsAvailable
        ? null
        : OperatingSystem.IsWindows()
            ? "No Windows recording device was found."
            : "NAudio microphone capture currently requires Windows.";

    public IReadOnlyList<CallAudioDevice> Devices
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return [];
            try
            {
                return Enumerable.Range(0, WaveInEvent.DeviceCount)
                    .Select(index => new CallAudioDevice(
                        index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        WaveInEvent.GetCapabilities(index).ProductName,
                        index == 0))
                    .ToArray();
            }
            catch (Exception)
            {
                return [];
            }
        }
    }

    public async Task StartAsync(
        SpeechInputOptions options,
        Func<SpeechInputEvent, CancellationToken, Task> onEvent,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(UnavailableReason);
        if (options.Model is null || !options.Model.IsInstalled || !File.Exists(options.Model.LocalPath))
            throw new InvalidOperationException("Download and select a local Whisper speech model first.");
        if (!IsAvailable) throw new InvalidOperationException(UnavailableReason);

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var deviceNumber = 0;
        if (!string.IsNullOrWhiteSpace(options.DeviceId)
            && int.TryParse(options.DeviceId, out var parsedDevice)
            && parsedDevice >= 0
            && parsedDevice < WaveInEvent.DeviceCount)
            deviceNumber = parsedDevice;

        var captureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channel = Channel.CreateUnbounded<SpeechWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        var vad = new WebRtcVad
        {
            OperatingMode = OperatingMode.Aggressive,
            SampleRate = SampleRate.Is16kHz,
            FrameLength = FrameLength.Is30ms
        };
        var capture = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(SampleRateHz, 16, 1),
            BufferMilliseconds = FrameMilliseconds,
            NumberOfBuffers = 3
        };

        lock (_sync)
        {
            _options = options;
            _callback = onEvent;
            _captureCts = captureCts;
            _work = channel;
            _vad = vad;
            _capture = capture;
            _stopping = false;
            ResetAudioStateLocked();
            _worker = Task.Run(
                () => ProcessWorkAsync(channel.Reader, onEvent, captureCts.Token),
                CancellationToken.None);
        }

        capture.DataAvailable += OnDataAvailable;
        capture.RecordingStopped += OnRecordingStopped;
        try
        {
            capture.StartRecording();
        }
        catch
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public Task BeginPushToTalkAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            EnsureRunningLocked();
            if (_pushToTalkHeld) return Task.CompletedTask;
            _pushToTalkHeld = true;
            _inSpeech = true;
            _silenceFrames = 0;
            _utterance.Dispose();
            _utterance = new MemoryStream();
            QueueEventLocked(new SpeechInputEvent(SpeechInputEventKind.SpeechStarted));
        }
        return Task.CompletedTask;
    }

    public Task EndPushToTalkAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            EnsureRunningLocked();
            if (!_pushToTalkHeld) return Task.CompletedTask;
            _pushToTalkHeld = false;
            FinishUtteranceLocked();
        }
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        WaveInEvent? capture;
        WebRtcVad? vad;
        Channel<SpeechWorkItem>? work;
        Task? worker;
        CancellationTokenSource? captureCts;
        lock (_sync)
        {
            _stopping = true;
            capture = _capture;
            vad = _vad;
            work = _work;
            worker = _worker;
            captureCts = _captureCts;
            _capture = null;
            _vad = null;
            _work = null;
            _worker = null;
            _captureCts = null;
            _callback = null;
            _options = null;
            ResetAudioStateLocked();
        }

        if (capture is not null)
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            try { capture.StopRecording(); }
            catch (Exception) { }
            capture.Dispose();
        }
        work?.Writer.TryComplete();
        captureCts?.Cancel();
        if (worker is not null && !_insideWorker.Value)
        {
            try { await worker.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        vad?.Dispose();
        captureCts?.Dispose();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_sync)
        {
            if (_capture is null || _work is null || _vad is null || e.BytesRecorded <= 0) return;
            _pendingFrames.AddRange(e.Buffer.AsSpan(0, e.BytesRecorded).ToArray());
            while (_pendingFrames.Count >= FrameBytes)
            {
                var frame = _pendingFrames.GetRange(0, FrameBytes).ToArray();
                _pendingFrames.RemoveRange(0, FrameBytes);
                ProcessFrameLocked(frame);
            }
        }
    }

    private void ProcessFrameLocked(byte[] frame)
    {
        QueueEventLocked(new SpeechInputEvent(
            SpeechInputEventKind.AudioLevel,
            AudioLevel: CalculateLevel(frame)));

        if (_options?.InputMode == CallInputMode.PushToTalk)
        {
            if (_pushToTalkHeld) _utterance.Write(frame);
            if (_utterance.Length >= MaximumUtteranceBytes) FinishUtteranceLocked();
            return;
        }

        bool hasSpeech;
        try { hasSpeech = _vad!.HasSpeech(frame); }
        catch (Exception ex)
        {
            QueueEventLocked(new SpeechInputEvent(SpeechInputEventKind.Error, Error: ex.Message));
            return;
        }

        if (hasSpeech)
        {
            if (!_inSpeech)
            {
                _inSpeech = true;
                _silenceFrames = 0;
                _utterance.Dispose();
                _utterance = new MemoryStream();
                QueueEventLocked(new SpeechInputEvent(SpeechInputEventKind.SpeechStarted));
            }
            _silenceFrames = 0;
            _utterance.Write(frame);
        }
        else if (_inSpeech)
        {
            _utterance.Write(frame);
            _silenceFrames++;
            if (_silenceFrames >= SilenceFramesToFinish) FinishUtteranceLocked();
        }

        if (_utterance.Length >= MaximumUtteranceBytes) FinishUtteranceLocked();
    }

    private void FinishUtteranceLocked()
    {
        var audio = _utterance.ToArray();
        _utterance.Dispose();
        _utterance = new MemoryStream();
        _inSpeech = false;
        _silenceFrames = 0;
        if (audio.Length < MinimumUtteranceBytes)
            QueueEventLocked(new SpeechInputEvent(SpeechInputEventKind.SpeechEnded));
        else
            _work?.Writer.TryWrite(new SpeechWorkItem(Audio: audio));
    }

    private async Task ProcessWorkAsync(
        ChannelReader<SpeechWorkItem> reader,
        Func<SpeechInputEvent, CancellationToken, Task> callback,
        CancellationToken cancellationToken)
    {
        _insideWorker.Value = true;
        try
        {
            await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (item.Event is not null)
                {
                    await callback(item.Event, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                if (item.Audio is not null)
                    await TranscribeAsync(item.Audio, callback, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            try
            {
                await callback(
                    new SpeechInputEvent(SpeechInputEventKind.Error, Error: ex.Message),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) { }
        }
        finally
        {
            _insideWorker.Value = false;
        }
    }

    private async Task TranscribeAsync(
        byte[] pcm,
        Func<SpeechInputEvent, CancellationToken, Task> callback,
        CancellationToken cancellationToken)
    {
        var modelPath = _options?.Model?.LocalPath;
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            await callback(
                new SpeechInputEvent(SpeechInputEventKind.Error, Error: "The selected Whisper model is no longer available."),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        using var waveStream = CreateWaveStream(pcm);
        using var factory = WhisperFactory.FromPath(modelPath);
        using var processor = factory.CreateBuilder()
            .WithLanguage("auto")
            .Build();
        var transcript = new StringBuilder();
        await foreach (var segment in processor.ProcessAsync(waveStream, cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(segment.Text)) continue;
            transcript.Append(segment.Text);
            await callback(
                new SpeechInputEvent(SpeechInputEventKind.PartialTranscript, transcript.ToString().Trim()),
                cancellationToken).ConfigureAwait(false);
        }

        var text = transcript.ToString().Trim();
        if (text.Length == 0)
        {
            await callback(new SpeechInputEvent(SpeechInputEventKind.SpeechEnded), cancellationToken).ConfigureAwait(false);
            return;
        }

        // Do not await the full model turn here: the worker must stay free to
        // detect barge-in speech while Ollama is generating or TTS is speaking.
        _ = ObserveFinalCallbackAsync(
            callback(new SpeechInputEvent(SpeechInputEventKind.FinalTranscript, text), cancellationToken));
    }

    private static async Task ObserveFinalCallbackAsync(Task callbackTask)
    {
        try { await callbackTask.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception) { /* coordinator publishes its own fatal status */ }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        lock (_sync)
        {
            if (_stopping || _work is null) return;
            _work.Writer.TryWrite(new SpeechWorkItem(Event: e.Exception is null
                ? new SpeechInputEvent(SpeechInputEventKind.SourceClosed)
                : new SpeechInputEvent(SpeechInputEventKind.Error, Error: e.Exception.Message)));
        }
    }

    private void QueueEventLocked(SpeechInputEvent inputEvent) =>
        _work?.Writer.TryWrite(new SpeechWorkItem(Event: inputEvent));

    private void EnsureRunningLocked()
    {
        if (_capture is null || _work is null)
            throw new InvalidOperationException("Microphone capture is not running.");
    }

    private void ResetAudioStateLocked()
    {
        _pendingFrames.Clear();
        _utterance.Dispose();
        _utterance = new MemoryStream();
        _inSpeech = false;
        _pushToTalkHeld = false;
        _silenceFrames = 0;
    }

    private static MemoryStream CreateWaveStream(byte[] pcm)
    {
        var stream = new MemoryStream(44 + pcm.Length);
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + pcm.Length);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(SampleRateHz);
            writer.Write(SampleRateHz * BytesPerSample);
            writer.Write((short)BytesPerSample);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(pcm.Length);
            writer.Write(pcm);
        }
        stream.Position = 0;
        return stream;
    }

    private static double CalculateLevel(byte[] frame)
    {
        double sum = 0;
        var samples = frame.Length / BytesPerSample;
        for (var index = 0; index < frame.Length - 1; index += 2)
        {
            var sample = BitConverter.ToInt16(frame, index) / 32768d;
            sum += sample * sample;
        }
        return samples == 0 ? 0 : Math.Clamp(Math.Sqrt(sum / samples) * 3.5, 0, 1);
    }

    private static int TryGetDeviceCount()
    {
        try { return WaveInEvent.DeviceCount; }
        catch (Exception) { return 0; }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _utterance.Dispose();
    }

    private sealed record SpeechWorkItem(SpeechInputEvent? Event = null, byte[]? Audio = null);
}
