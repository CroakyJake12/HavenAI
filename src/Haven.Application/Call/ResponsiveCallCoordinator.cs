/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/ResponsiveCallCoordinator.cs, in the Application layer.
 * What: Decorates CallCoordinator with model and speech warm-up plus a cancellable acknowledgement for genuinely slow turns.
 * How: The selected call model and voice warm in the background; a reliable short phrase is scheduled only after prolonged silence and cancelled on the first real model delta.
 * Why: Prompted filler is unreliable, "Hmm" can be mis-phonemized, and cold local models make ordinary spoken interaction feel unresponsive.
 * Maintenance: Keep cues out of transcript and persistence; they are ephemeral audio feedback only.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Preserves the normal call coordinator contract while reducing cold-start delay
/// and preventing an enabled spoken call from sitting silently on a slow turn.
/// </summary>
public sealed class ResponsiveCallCoordinator : ICallCoordinator, IVoiceReactionSource, IVoiceInputStatusSource
{
    private static readonly TimeSpan CueDelay = TimeSpan.FromMilliseconds(1250);

    private readonly CallCoordinator _inner;
    private readonly ISpeechOutputService _speechOutput;
    private readonly ISpeechOutputWarmup _speechWarmup;
    private readonly CallOptimizedOllamaClient _models;
    private readonly object _cueGate = new();
    private readonly object _warmupGate = new();
    private CancellationTokenSource? _cueCts;
    private CancellationTokenSource? _warmupCts;
    private bool _speechEnabled;
    private string? _voiceName;
    private string? _outputDeviceId;
    private bool _disposed;

    public ResponsiveCallCoordinator(
        CallCoordinator inner,
        ISpeechOutputService speechOutput,
        CallOptimizedOllamaClient models)
        : this(inner, speechOutput, NoOpSpeechOutputWarmup.Instance, models)
    {
    }

    public ResponsiveCallCoordinator(
        CallCoordinator inner,
        ISpeechOutputService speechOutput,
        ISpeechOutputWarmup speechWarmup,
        CallOptimizedOllamaClient models)
    {
        _inner = inner;
        _speechOutput = speechOutput;
        _speechWarmup = speechWarmup;
        _models = models;
        _inner.StateChanged += OnInnerStateChanged;
        _inner.TranscriptChanged += OnInnerTranscriptChanged;
        _inner.AudioLevelChanged += OnInnerAudioLevelChanged;
        _inner.ScreenPreviewChanged += OnInnerScreenPreviewChanged;
        _inner.VoiceReactionChanged += OnInnerVoiceReactionChanged;
        _inner.InputStatusChanged += OnInnerInputStatusChanged;
    }

    public CallState State => _inner.State;
    public CallSession? CurrentSession => _inner.CurrentSession;
    public Conversation? CurrentConversation => _inner.CurrentConversation;
    public CallCapabilities Capabilities => _inner.Capabilities;
    public bool IsActive => _inner.IsActive;
    public bool IsMuted => _inner.IsMuted;
    public bool IsScreenSharing => _inner.IsScreenSharing;
    public VoiceProfile? ActiveVoiceProfile => _inner.ActiveVoiceProfile;
    public VoiceReaction? LatestVoiceReaction => _inner.LatestVoiceReaction;
    public VoiceReaction? CurrentVoiceReaction => _inner.CurrentVoiceReaction;
    public VoiceInputStatus InputStatus => _inner.InputStatus;

    public event EventHandler<CallStateChangedEventArgs>? StateChanged;
    public event EventHandler<CallTranscriptEventArgs>? TranscriptChanged;
    public event EventHandler<CallAudioLevelEventArgs>? AudioLevelChanged;
    public event EventHandler<ScreenShareSnapshotEventArgs>? ScreenPreviewChanged;
    public event EventHandler<VoiceReactionEventArgs>? VoiceReactionChanged;
    public event EventHandler<VoiceInputStatusChangedEventArgs>? InputStatusChanged;

    public async Task<CallSession> StartAsync(
        CallStartOptions options,
        SpeechModelInfo? speechModel,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelCue();
        CancelWarmup();
        _speechEnabled = options.EnableSpeechOutput;
        _voiceName = options.VoiceName;
        _outputDeviceId = options.OutputDeviceId;
        try
        {
            var session = await _inner.StartAsync(options, speechModel, cancellationToken).ConfigureAwait(false);
            StartWarmup(options.Model);
            return session;
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
        CancelWarmup();
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

    private void StartWarmup(ModelDescriptor model)
    {
        CancelWarmup();
        var cts = new CancellationTokenSource();
        lock (_warmupGate) _warmupCts = cts;
        _ = WarmServicesSafelyAsync(model, cts);
    }

    private async Task WarmServicesSafelyAsync(ModelDescriptor model, CancellationTokenSource owner)
    {
        try
        {
            await Task.WhenAll(
                _models.WarmAsync(model, owner.Token),
                _speechWarmup.WarmAsync(_voiceName, owner.Token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested)
        {
            // Ending or replacing a call cancelled an obsolete warm-up.
        }
        catch (Exception)
        {
            // Warm-up is an optimization; the real turn retains normal error handling.
        }
        finally
        {
            lock (_warmupGate)
            {
                if (ReferenceEquals(_warmupCts, owner)) _warmupCts = null;
            }
            owner.Dispose();
        }
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
                "Let me think.",
                _voiceName,
                _outputDeviceId,
                owner.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested)
        {
            // Real output or another state transition won the race, which is expected.
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

    private void CancelWarmup()
    {
        CancellationTokenSource? warmup;
        lock (_warmupGate)
        {
            warmup = _warmupCts;
            _warmupCts = null;
        }
        if (warmup is null) return;
        try { warmup.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void ClearSpeechSettings()
    {
        _speechEnabled = false;
        _voiceName = null;
        _outputDeviceId = null;
    }

    private void OnInnerTranscriptChanged(object? sender, CallTranscriptEventArgs e)
    {
        if (e.Role == MessageRole.Assistant && e.IsDelta && !string.IsNullOrWhiteSpace(e.Text))
            CancelCue();
        TranscriptChanged?.Invoke(this, e);
    }

    private void OnInnerAudioLevelChanged(object? sender, CallAudioLevelEventArgs e) =>
        AudioLevelChanged?.Invoke(this, e);

    private void OnInnerScreenPreviewChanged(object? sender, ScreenShareSnapshotEventArgs e) =>
        ScreenPreviewChanged?.Invoke(this, e);

    private void OnInnerVoiceReactionChanged(object? sender, VoiceReactionEventArgs e) =>
        VoiceReactionChanged?.Invoke(this, e);

    private void OnInnerInputStatusChanged(object? sender, VoiceInputStatusChangedEventArgs e) =>
        InputStatusChanged?.Invoke(this, e);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        CancelCue();
        CancelWarmup();
        ClearSpeechSettings();
        _inner.StateChanged -= OnInnerStateChanged;
        _inner.TranscriptChanged -= OnInnerTranscriptChanged;
        _inner.AudioLevelChanged -= OnInnerAudioLevelChanged;
        _inner.ScreenPreviewChanged -= OnInnerScreenPreviewChanged;
        _inner.VoiceReactionChanged -= OnInnerVoiceReactionChanged;
        _inner.InputStatusChanged -= OnInnerInputStatusChanged;
        await _inner.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
    private sealed class NoOpSpeechOutputWarmup : ISpeechOutputWarmup
    {
        public static NoOpSpeechOutputWarmup Instance { get; } = new();

        public Task WarmAsync(string? voiceName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

}
