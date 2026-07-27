/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/WindowsSpeechInputService.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns WindowsSpeechInputService, SpeechWorkItem. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

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
    /// <summary>
    /// Stores sample rate hz locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int SampleRateHz = 16_000;
    /// <summary>
    /// Stores bytes per sample locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int BytesPerSample = 2;
    /// <summary>
    /// Stores frame milliseconds locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int FrameMilliseconds = 30;
    /// <summary>
    /// Stores frame bytes locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int FrameBytes = SampleRateHz * BytesPerSample * FrameMilliseconds / 1000;
    /// <summary>
    /// Stores silence frames to finish locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int SilenceFramesToFinish = 20;
    /// <summary>
    /// Stores minimum utterance bytes locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int MinimumUtteranceBytes = SampleRateHz * BytesPerSample / 4;
    /// <summary>
    /// Stores maximum utterance bytes locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int MaximumUtteranceBytes = SampleRateHz * BytesPerSample * 30;
    /// <summary>
    /// Stores audio level interval locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly TimeSpan AudioLevelInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Stores sync locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly object _sync = new();
    /// <summary>
    /// Stores inside worker locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly AsyncLocal<bool> _insideWorker = new();
    /// <summary>
    /// Stores capture locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private WaveInEvent? _capture;
    /// <summary>
    /// Stores vad locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private WebRtcVad? _vad;
    /// <summary>
    /// Stores work locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Channel<SpeechWorkItem>? _work;
    /// <summary>
    /// Stores worker locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Task? _worker;
    /// <summary>
    /// Stores capture cts locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CancellationTokenSource? _captureCts;
    /// <summary>
    /// Stores options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private SpeechInputOptions? _options;
    /// <summary>
    /// Stores pending frames locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly List<byte> _pendingFrames = [];
    /// <summary>
    /// Stores utterance locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private MemoryStream _utterance = new();
    /// <summary>
    /// Stores in speech locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _inSpeech;
    /// <summary>
    /// Stores push to talk held locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _pushToTalkHeld;
    /// <summary>
    /// Stores stopping locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _stopping;
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _disposed;
    /// <summary>
    /// Stores silence frames locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _silenceFrames;
    /// <summary>
    /// Stores last audio level timestamp locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private long _lastAudioLevelTimestamp;

    /// <summary>
    /// Reports whether available applies to the current state.
    /// </summary>
    public bool IsAvailable => !_disposed && OperatingSystem.IsWindows() && TryGetDeviceCount() > 0;
    /// <summary>
    /// Gets or updates unavailable reason, the bindable or domain state represented by this property.
    /// </summary>
    public string? UnavailableReason => IsAvailable
        ? null
        : _disposed
            ? "The Windows speech input service has been disposed."
            : OperatingSystem.IsWindows()
                ? "No Windows recording device was found."
                : "NAudio microphone capture currently requires Windows.";

    public IReadOnlyList<CallAudioDevice> Devices
    {
        get
        {
            if (_disposed || !OperatingSystem.IsWindows()) return [];
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

    /// <summary>
    /// Performs start asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task StartAsync(
        SpeechInputOptions options,
        Func<SpeechInputEvent, CancellationToken, Task> onEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onEvent);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(UnavailableReason);
        if (options.Model is null || !options.Model.IsInstalled || !File.Exists(options.Model.LocalPath))
            throw new InvalidOperationException("Download and select a local Whisper speech model first.");
        if (!IsAvailable) throw new InvalidOperationException(UnavailableReason);

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        ObjectDisposedException.ThrowIf(_disposed, this);
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

        var registered = false;
        lock (_sync)
        {
            if (!_disposed)
            {
                _options = options;
                _captureCts = captureCts;
                _work = channel;
                _vad = vad;
                _capture = capture;
                _stopping = false;
                ResetAudioStateLocked();
                _worker = Task.Run(
                    () => ProcessWorkAsync(channel.Reader, onEvent, captureCts.Token),
                    CancellationToken.None);
                registered = true;
            }
        }

        if (!registered)
        {
            capture.Dispose();
            vad.Dispose();
            captureCts.Dispose();
            throw new ObjectDisposedException(nameof(WindowsSpeechInputService));
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

    /// <summary>
    /// Performs begin push to talk asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task BeginPushToTalkAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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

    /// <summary>
    /// Performs end push to talk asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task EndPushToTalkAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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

    /// <summary>
    /// Performs stop asynchronously so I/O does not block the caller's thread.
    /// </summary>
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
            _options = null;
            ResetAudioStateLocked();
        }

        try
        {
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
                try
                {
                    await worker.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // The capture token was cancelled as part of this normal stop.
                }
            }
        }
        finally
        {
            vad?.Dispose();
            captureCts?.Dispose();
        }
    }

    /// <summary>
    /// Handles the data available event raised by the UI or runtime.
    /// </summary>
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

    /// <summary>
    /// Performs the process frame locked step owned by this component.
    /// </summary>
    private void ProcessFrameLocked(byte[] frame)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_lastAudioLevelTimestamp == 0
            || System.Diagnostics.Stopwatch.GetElapsedTime(_lastAudioLevelTimestamp, now) >= AudioLevelInterval)
        {
            _lastAudioLevelTimestamp = now;
            QueueEventLocked(new SpeechInputEvent(
                SpeechInputEventKind.AudioLevel,
                AudioLevel: CalculateLevel(frame)));
        }

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

    /// <summary>
    /// Performs the finish utterance locked step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs process work asynchronously so I/O does not block the caller's thread.
    /// </summary>
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
            try
            {
                // If the worker exits because transcription or a callback failed,
                // detach native capture immediately instead of leaving a producer
                // writing into a channel with no reader. The AsyncLocal guard keeps
                // StopAsync from waiting on this worker from inside itself.
                await StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception) { }
            _insideWorker.Value = false;
        }
    }

    /// <summary>
    /// Performs transcribe asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs observe final callback asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task ObserveFinalCallbackAsync(Task callbackTask)
    {
        try { await callbackTask.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception) { /* coordinator publishes its own fatal status */ }
    }

    /// <summary>
    /// Handles the recording stopped event raised by the UI or runtime.
    /// </summary>
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

    /// <summary>
    /// Performs the queue event locked step owned by this component.
    /// </summary>
    private void QueueEventLocked(SpeechInputEvent inputEvent) =>
        _work?.Writer.TryWrite(new SpeechWorkItem(Event: inputEvent));

    /// <summary>
    /// Performs the ensure running locked step owned by this component.
    /// </summary>
    private void EnsureRunningLocked()
    {
        if (_capture is null || _work is null)
            throw new InvalidOperationException("Microphone capture is not running.");
    }

    /// <summary>
    /// Performs the reset audio state locked step owned by this component.
    /// </summary>
    private void ResetAudioStateLocked()
    {
        _pendingFrames.Clear();
        _utterance.Dispose();
        _utterance = new MemoryStream();
        _inSpeech = false;
        _pushToTalkHeld = false;
        _silenceFrames = 0;
        _lastAudioLevelTimestamp = 0;
    }

    /// <summary>
    /// Creates wave stream with the invariants required by its callers.
    /// </summary>
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

    /// <summary>
    /// Performs the calculate level step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Attempts to get device count and reports the result without using failure for normal control flow.
    /// </summary>
    private static int TryGetDeviceCount()
    {
        try { return WaveInEvent.DeviceCount; }
        catch (Exception) { return 0; }
    }

    /// <summary>
    /// Performs dispose asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        lock (_sync)
        {
            _utterance.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Represents speech work item and keeps its related state and behavior together.
    /// </summary>
    private sealed record SpeechWorkItem(SpeechInputEvent? Event = null, byte[]? Audio = null);
}
