/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/ResponsiveCallCoordinator.cs, in the Application layer.
 * What: Decorates CallCoordinator with a cancellable audible acknowledgement when a model is slow to begin speaking.
 * How: A short cue is scheduled when Call enters Thinking and cancelled as soon as real speech, listening, interruption or teardown begins.
 * Why: Prompting a model to say "Hmm" is not reliable and does not improve perceived latency when model startup is slow.
 * Maintenance: Keep cues out of the transcript and persistence; they are ephemeral audio feedback only.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Preserves the normal call coordinator contract while guaranteeing that an
/// enabled spoken call does not sit silently during a slow model warm-up.
/// </summary>
public sealed class ResponsiveCallCoordinator : ICallCoordinator
{
    private static readonly TimeSpan CueDelay = TimeSpan.FromMilliseconds(550);

    private readonly CallCoordinator _inner;
    private readonly ISpeechOutputService _speechOutput;
    private readonly object _cueGate = new();
    private CancellationTokenSource? _cueCts;
    private bool _speechEnabled;
    private string? _voiceName;
    private string? _outputDeviceId;
    private bool _disposed;

    public ResponsiveCallCoordinator(CallCoordinator inner, ISpeechOutputService speechOutput)
    {
        _inner = inner;
        _speechOutput = speechOutput;
        _inner.StateChanged += OnInnerStateChanged;
        _inner.TranscriptChanged += OnInnerTranscriptChanged;
        _inner.AudioLevelChanged += OnInnerAudioLevelChanged;
        _inner.ScreenPreviewChanged += OnInnerScreenPreviewChanged;
    }

    public CallState State => _inner.State;
    public CallSession? CurrentSession => _inner.CurrentSession;
    public Conversation? CurrentConversation => _inner.CurrentConversation;
    public CallCapabilities Capabilities => _inner.Capabilities;
    public bool IsActive => _inner.IsActive;
    public bool IsMuted => _inner.IsMuted;
    public bool IsScreenSharing => _inner.IsScreenSharing;

    public event EventHandler<CallStateChangedEventArgs>? StateChanged;
    public event EventHandler<CallTranscriptEventArgs>? TranscriptChanged;
    public event EventHandler<CallAudioLevelEventArgs>? AudioLevelChanged;
    public event EventHandler<ScreenShareSnapshotEventArgs>? ScreenPreviewChanged;

    public async Task<CallSession> StartAsync(
        CallStartOptions options,
        SpeechModelInfo? speechModel,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelCue();
        _speechEnabled = options.EnableSpeechOutput;
        _voiceName = options.VoiceName;
        _outputDeviceId = options.OutputDeviceId;
        try
        {
            return await _inner.StartAsync(options, speechModel, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ClearSpeechSettings();
            throw;
        }
    }

    public Task SubmitTextAsync(string text, CancellationToken cancellationToken) =>
        _inner.SubmitTextAsync(text, cancellationToken);

    public Task BeginPushToTalkAsync(CancellationToken cancellationToken) =>
        _inner.BeginPushToTalkAsync(cancellationToken);

    public Task EndPushToTalkAsync(CancellationToken cancellationToken) =>
        _inner.EndPushToTalkAsync(cancellationToken);

    public Task SetMutedAsync(bool muted, CancellationToken cancellationToken) =>
        _inner.SetMutedAsync(muted, cancellationToken);

    public async Task PauseAsync(CancellationToken cancellationToken)
    {
        CancelCue();
        await _inner.PauseAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task ResumeAsync(CancellationToken cancellationToken) =>
        _inner.ResumeAsync(cancellationToken);

    public Task StartScreenShareAsync(CancellationToken cancellationToken) =>
        _inner.StartScreenShareAsync(cancellationToken);

    public Task StopScreenShareAsync(CancellationToken cancellationToken) =>
        _inner.StopScreenShareAsync(cancellationToken);

    public async Task InterruptAsync(CancellationToken cancellationToken)
    {
        CancelCue();
        await _inner.InterruptAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task EndAsync(CancellationToken cancellationToken)
    {
        CancelCue();
        try
        {
            await _inner.EndAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ClearSpeechSettings();
        }
    }

    private void OnInnerStateChanged(object? sender, CallStateChangedEventArgs e)
    {
        if (e.State == CallState.Thinking && _speechEnabled && _speechOutput.IsAvailable && _inner.IsActive)
            ScheduleCue();
        else
            CancelCue();

        StateChanged?.Invoke(this, e);
    }

    private void ScheduleCue()
    {
        CancelCue();
        var cts = new CancellationTokenSource();
        lock (_cueGate) _cueCts = cts;
        _ = SpeakCueAfterDelayAsync(cts);
    }

    private async Task SpeakCueAfterDelayAsync(CancellationTokenSource owner)
    {
        try
        {
            await Task.Delay(CueDelay, owner.Token).ConfigureAwait(false);
            if (!_inner.IsActive || _inner.State != CallState.Thinking) return;

            await _speechOutput.SpeakAsync(
                "Hmm…",
                _voiceName,
                _outputDeviceId,
                owner.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested)
        {
            // Real speech or another state transition won the race, which is expected.
        }
        catch (Exception)
        {
            // The cue is optional feedback. Never fail or interrupt the real turn.
        }
        finally
        {
            lock (_cueGate)
            {
                if (ReferenceEquals(_cueCts, owner)) _cueCts = null;
            }
            owner.Dispose();
        }
    }

    private void CancelCue()
    {
        CancellationTokenSource? cue;
        lock (_cueGate)
        {
            cue = _cueCts;
            _cueCts = null;
        }
        if (cue is null) return;
        try { cue.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void ClearSpeechSettings()
    {
        _speechEnabled = false;
        _voiceName = null;
        _outputDeviceId = null;
    }

    private void OnInnerTranscriptChanged(object? sender, CallTranscriptEventArgs e) =>
        TranscriptChanged?.Invoke(this, e);

    private void OnInnerAudioLevelChanged(object? sender, CallAudioLevelEventArgs e) =>
        AudioLevelChanged?.Invoke(this, e);

    private void OnInnerScreenPreviewChanged(object? sender, ScreenShareSnapshotEventArgs e) =>
        ScreenPreviewChanged?.Invoke(this, e);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        CancelCue();
        ClearSpeechSettings();
        _inner.StateChanged -= OnInnerStateChanged;
        _inner.TranscriptChanged -= OnInnerTranscriptChanged;
        _inner.AudioLevelChanged -= OnInnerAudioLevelChanged;
        _inner.ScreenPreviewChanged -= OnInnerScreenPreviewChanged;
        await _inner.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
