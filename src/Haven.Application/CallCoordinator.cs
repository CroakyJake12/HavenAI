/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/CallCoordinator.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns CallCoordinator. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text;
using System.Threading.Channels;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Coordinates one process-wide local call. Media services are replaceable and
/// only expose transcripts/snapshots, which keeps raw audio and video out of
/// persistence by construction.
/// </summary>
public sealed class CallCoordinator : ICallCoordinator
{
    /// <summary>
    /// Stores default system prompt locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const string DefaultSystemPrompt =
        "You are Haven in a private, local live call. Respond conversationally and concisely. " +
        "Prefer short paragraphs that sound natural when spoken. Do not claim to see a shared " +
        "screen unless an image is attached to the current turn.";

    /// <summary>
    /// Stores calls locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ICallRepository _calls;
    /// <summary>
    /// Stores conversations locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IConversationRepository _conversations;
    /// <summary>
    /// Stores ollama locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IOllamaClient _ollama;
    /// <summary>
    /// Stores speech input locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ISpeechInputService _speechInput;
    /// <summary>
    /// Stores speech output locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ISpeechOutputService _speechOutput;
    /// <summary>
    /// Stores screen share locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IScreenShareService _screenShare;
    /// <summary>
    /// Stores time provider locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TimeProvider _timeProvider;
    /// <summary>
    /// Stores lifecycle gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    /// <summary>
    /// Stores turn gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SemaphoreSlim _turnGate = new(1, 1);

    /// <summary>
    /// Stores lifetime cts locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CancellationTokenSource? _lifetimeCts;
    /// <summary>
    /// Stores turn cts locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CancellationTokenSource? _turnCts;
    /// <summary>
    /// Stores options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CallStartOptions? _options;
    /// <summary>
    /// Stores speech model locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private SpeechModelInfo? _speechModel;
    /// <summary>
    /// Stores partial user message id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Guid _partialUserMessageId;
    /// <summary>
    /// Stores ending locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _ending;
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _disposed;

    public CallCoordinator(
        ICallRepository calls,
        IConversationRepository conversations,
        IOllamaClient ollama,
        ISpeechInputService speechInput,
        ISpeechOutputService speechOutput,
        IScreenShareService screenShare,
        TimeProvider? timeProvider = null)
    {
        _calls = calls;
        _conversations = conversations;
        _ollama = ollama;
        _speechInput = speechInput;
        _speechOutput = speechOutput;
        _screenShare = screenShare;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _screenShare.SourceClosed += OnScreenShareSourceClosed;
        _screenShare.SnapshotAvailable += OnScreenShareSnapshotAvailable;
    }

    /// <summary>
    /// Gets or updates state, the bindable or domain state represented by this property.
    /// </summary>
    public CallState State { get; private set; } = CallState.Idle;
    /// <summary>
    /// Gets or updates current session, the bindable or domain state represented by this property.
    /// </summary>
    public CallSession? CurrentSession { get; private set; }
    /// <summary>
    /// Gets or updates current conversation, the bindable or domain state represented by this property.
    /// </summary>
    public Conversation? CurrentConversation { get; private set; }
    /// <summary>
    /// Reports whether is active is true for the current state.
    /// </summary>
    public bool IsActive => CurrentSession?.Status == CallSessionStatus.Active;
    /// <summary>
    /// Reports whether is muted is true for the current state.
    /// </summary>
    public bool IsMuted { get; private set; }
    /// <summary>
    /// Reports whether is screen sharing is true for the current state.
    /// </summary>
    public bool IsScreenSharing => _screenShare.IsSharing;

    /// <summary>
    /// Gets or updates capabilities, the bindable or domain state represented by this property.
    /// </summary>
    public CallCapabilities Capabilities => new(
        _speechInput.IsAvailable,
        _speechOutput.IsAvailable,
        _screenShare.IsSupported,
        _speechInput.UnavailableReason,
        _speechOutput.UnavailableReason,
        _screenShare.UnavailableReason,
        _speechInput.Devices,
        _speechOutput.Devices,
        _speechOutput.Voices);

    /// <summary>
    /// Stores state changed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler<CallStateChangedEventArgs>? StateChanged;
    /// <summary>
    /// Stores transcript changed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler<CallTranscriptEventArgs>? TranscriptChanged;
    /// <summary>
    /// Stores audio level changed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler<CallAudioLevelEventArgs>? AudioLevelChanged;
    /// <summary>
    /// Stores screen preview changed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler<ScreenShareSnapshotEventArgs>? ScreenPreviewChanged;

    /// <summary>
    /// Performs start async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<CallSession> StartAsync(
        CallStartOptions options,
        SpeechModelInfo? speechModel,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Model.Name))
            throw new ArgumentException("A local Ollama model must be selected.", nameof(options));

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsActive) throw new InvalidOperationException("A Haven call is already active.");

            _lifetimeCts?.Dispose();
            _lifetimeCts = new CancellationTokenSource();
            _options = options;
            _speechModel = speechModel;
            IsMuted = false;

            var now = _timeProvider.GetUtcNow();
            CurrentConversation = new Conversation(
                Guid.NewGuid(), HavenMode.Chat, ConversationKind.Call,
                $"Call · {now.ToLocalTime():dd MMM HH:mm}",
                null, null, false, false, now, now);
            CurrentSession = new CallSession(
                Guid.NewGuid(), CurrentConversation.Id, options.Model.Name,
                options.InputDeviceId, options.OutputDeviceId, options.VoiceName,
                options.InputMode, false, CallSessionStatus.Active, now);

            await _conversations.UpsertConversationAsync(CurrentConversation, cancellationToken).ConfigureAwait(false);
            await _calls.UpsertAsync(CurrentSession, cancellationToken).ConfigureAwait(false);

            SetState(CallState.Listening, _speechInput.IsAvailable
                ? "Listening"
                : _speechInput.UnavailableReason ?? "Microphone transcription is unavailable; type a transcript to continue.");

            if (_speechInput.IsAvailable)
            {
                try
                {
                    await StartSpeechInputAsync(_lifetimeCts.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    SetState(CallState.Listening, $"Microphone unavailable ({ex.Message}); typed transcript mode is still ready.");
                }
            }

            return CurrentSession;
        }
        catch
        {
            _lifetimeCts?.Cancel();
            _lifetimeCts?.Dispose();
            _lifetimeCts = null;
            CurrentSession = null;
            CurrentConversation = null;
            _options = null;
            _speechModel = null;
            SetState(CallState.Idle, "Call could not start");
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Performs submit text async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task SubmitTextAsync(string text, CancellationToken cancellationToken) =>
        RunTurnAsync(text, fromSpeech: false, Guid.NewGuid(), cancellationToken);

    /// <summary>
    /// Performs begin push to talk async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task BeginPushToTalkAsync(CancellationToken cancellationToken)
    {
        EnsureActive();
        if (State == CallState.Paused) return;
        if (IsMuted)
        {
            SetState(State, "Microphone is muted.");
            return;
        }
        if (!_speechInput.IsAvailable)
        {
            SetState(State, _speechInput.UnavailableReason ?? "Push-to-talk is unavailable; use the transcript box.");
            return;
        }

        await InterruptAsync(cancellationToken).ConfigureAwait(false);
        SetState(CallState.Transcribing, "Push-to-talk recording… release to send");
        await _speechInput.BeginPushToTalkAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs end push to talk async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task EndPushToTalkAsync(CancellationToken cancellationToken)
    {
        EnsureActive();
        if (!_speechInput.IsAvailable || IsMuted || State == CallState.Paused) return;
        SetState(CallState.Transcribing, "Transcribing…");
        await _speechInput.EndPushToTalkAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs set muted async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task SetMutedAsync(bool muted, CancellationToken cancellationToken)
    {
        EnsureActive();
        if (IsMuted == muted) return;
        IsMuted = muted;
        if (muted)
        {
            await _speechInput.StopAsync(cancellationToken).ConfigureAwait(false);
            SetState(State, "Microphone muted");
        }
        else if (State != CallState.Paused && _speechInput.IsAvailable && _lifetimeCts is not null)
        {
            await StartSpeechInputAsync(_lifetimeCts.Token).ConfigureAwait(false);
            SetState(CallState.Listening, "Listening");
        }
    }

    /// <summary>
    /// Performs pause async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task PauseAsync(CancellationToken cancellationToken)
    {
        EnsureActive();
        _turnCts?.Cancel();
        await StopMediaInputAndOutputAsync(cancellationToken).ConfigureAwait(false);
        SetState(CallState.Paused, "Call paused");
    }

    /// <summary>
    /// Performs resume async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task ResumeAsync(CancellationToken cancellationToken)
    {
        EnsureActive();
        if (State != CallState.Paused) return;
        if (!IsMuted && _speechInput.IsAvailable && _lifetimeCts is not null)
            await StartSpeechInputAsync(_lifetimeCts.Token).ConfigureAwait(false);
        SetState(CallState.Listening, IsMuted ? "Call resumed · microphone muted" : "Listening");
    }

    /// <summary>
    /// Performs start screen share async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task StartScreenShareAsync(CancellationToken cancellationToken)
    {
        EnsureActive();
        if (!_screenShare.IsSupported)
        {
            SetState(State, _screenShare.UnavailableReason ?? "Screen sharing is unavailable on this device.");
            return;
        }
        if (_screenShare.IsSharing) return;

        try
        {
            var source = await _screenShare.StartWithSystemPickerAsync(cancellationToken).ConfigureAwait(false);
            CurrentSession = CurrentSession! with { UsedScreenShare = true };
            await _calls.UpsertAsync(CurrentSession, cancellationToken).ConfigureAwait(false);
            var suffix = _options?.Model.Supports(ToolCapability.Vision) == true
                ? "The latest frame will be included with each completed turn."
                : "The selected model is not vision-capable, so frames will not be sent.";
            SetState(State, $"Sharing {source.Name}. {suffix}");
        }
        catch (OperationCanceledException)
        {
            SetState(State, "Screen-share selection cancelled.");
        }
        catch (Exception ex)
        {
            SetState(State, $"Screen sharing could not start: {ex.Message}");
        }
    }

    /// <summary>
    /// Performs stop screen share async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task StopScreenShareAsync(CancellationToken cancellationToken)
    {
        if (!_screenShare.IsSharing) return;
        await _screenShare.StopAsync(cancellationToken).ConfigureAwait(false);
        SetState(State, "Screen sharing stopped.");
    }

    /// <summary>
    /// Performs interrupt async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task InterruptAsync(CancellationToken cancellationToken)
    {
        if (!IsActive) return;
        _turnCts?.Cancel();
        try { await _speechOutput.StopAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception) { /* media cleanup is best effort */ }
        if (State != CallState.Paused)
            SetState(CallState.Listening, "Interrupted · listening");
    }

    /// <summary>
    /// Performs end async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task EndAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsActive)
            {
                SetState(CallState.Idle, "Ready to call");
                return;
            }
            await EndCoreAsync(CallSessionStatus.Completed, null, cancellationToken).ConfigureAwait(false);
            SetState(CallState.Idle, "Call ended");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Runs run turn async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private async Task RunTurnAsync(
        string text,
        bool fromSpeech,
        Guid userMessageId,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        text = text.Trim();
        if (text.Length == 0) return;
        EnsureActive();
        if (State == CallState.Paused) throw new InvalidOperationException("Resume the call before sending a turn.");

        await _turnGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Exception? fatalError = null;
        try
        {
            EnsureActive();
            var options = _options!;
            var conversation = CurrentConversation!;
            _turnCts?.Dispose();
            _turnCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCts?.Token ?? CancellationToken.None);
            var turnToken = _turnCts.Token;

            if (fromSpeech) SetState(CallState.Transcribing, "Transcript ready");
            var now = _timeProvider.GetUtcNow();
            var userMessage = new ChatMessage(
                userMessageId,
                conversation.Id,
                MessageRole.User,
                text,
                null,
                null,
                fromSpeech ? "{\"call\":{\"source\":\"speech\"}}" : "{\"call\":{\"source\":\"typed\"}}",
                now);
            CurrentConversation = conversation = conversation with { UpdatedAt = now };
            await _conversations.UpsertConversationAsync(CurrentConversation, turnToken).ConfigureAwait(false);
            await _conversations.AddMessageAsync(userMessage, turnToken).ConfigureAwait(false);
            TranscriptChanged?.Invoke(this, new(userMessage.Id, MessageRole.User, userMessage.Content, false, true));

            SetState(CallState.Thinking, "Haven is thinking…");
            var history = await _conversations.GetContextMessagesAsync(CurrentConversation.Id, turnToken).ConfigureAwait(false);
            var requestMessages = history
                .Where(message => message.Role is MessageRole.User or MessageRole.Assistant)
                .Select(message => new OllamaMessage(
                    message.Role == MessageRole.User ? "user" : "assistant",
                    message.Content))
                .ToList();

            ScreenShareSnapshot? snapshot = null;
            if (_screenShare.IsSharing && options.Model.Supports(ToolCapability.Vision))
            {
                try { snapshot = await _screenShare.GetLatestSnapshotAsync(turnToken).ConfigureAwait(false); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    SetState(CallState.Thinking, $"Screen frame unavailable ({ex.Message}); continuing with voice only.");
                }
            }
            if (snapshot is not null && requestMessages.Count > 0)
                requestMessages[^1] = requestMessages[^1] with { Images = [snapshot.Base64Jpeg] };

            var assistantId = Guid.NewGuid();
            var assistantText = new StringBuilder();
            var chunker = new SentenceChunker();
            var canSpeak = options.EnableSpeechOutput && _speechOutput.IsAvailable;
            Channel<string>? speechChannel = canSpeak
                ? Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true })
                : null;
            var speechTask = speechChannel is null
                ? Task.CompletedTask
                : SpeakQueuedAsync(speechChannel.Reader, turnToken);
            var interrupted = false;

            try
            {
                var request = new OllamaChatRequest(
                    options.Model.Name,
                    requestMessages,
                    options.Effort,
                    string.IsNullOrWhiteSpace(options.SystemPrompt) ? DefaultSystemPrompt : options.SystemPrompt,
                    Options: new GenerationOptions(Temperature: 0.65, ContextLimit: 32768, ActionLimit: 0));

                await foreach (var delta in _ollama.StreamChatAsync(request, turnToken).ConfigureAwait(false))
                {
                    if (string.IsNullOrEmpty(delta)) continue;
                    assistantText.Append(delta);
                    TranscriptChanged?.Invoke(this, new(assistantId, MessageRole.Assistant, delta, true, false));
                    if (speechChannel is null) continue;
                    foreach (var sentence in chunker.Append(delta))
                        await speechChannel.Writer.WriteAsync(sentence, turnToken).ConfigureAwait(false);
                }

                if (speechChannel is not null && chunker.Flush() is { Length: > 0 } remainder)
                    await speechChannel.Writer.WriteAsync(remainder, turnToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (turnToken.IsCancellationRequested)
            {
                interrupted = true;
            }
            catch (Exception ex)
            {
                fatalError = ex;
            }
            finally
            {
                speechChannel?.Writer.TryComplete();
                try { await speechTask.ConfigureAwait(false); }
                catch (OperationCanceledException) when (turnToken.IsCancellationRequested) { interrupted = true; }
                catch (Exception ex) { SetState(CallState.Thinking, $"Speech output stopped ({ex.Message}); transcript preserved."); }

                if (assistantText.Length > 0)
                {
                    var content = assistantText.ToString();
                    var metadata = interrupted
                        ? "{\"call\":{\"interrupted\":true}}"
                        : "{\"call\":{\"interrupted\":false}}";
                    var assistant = new ChatMessage(
                        assistantId, conversation.Id, MessageRole.Assistant, content,
                        "Haven", options.Model.Name, metadata, _timeProvider.GetUtcNow());
                    await _conversations.AddMessageAsync(assistant, CancellationToken.None).ConfigureAwait(false);
                    TranscriptChanged?.Invoke(this, new(assistantId, MessageRole.Assistant, content, false, true, interrupted));
                }
            }

            if (fatalError is null && IsActive && State != CallState.Paused)
                SetState(CallState.Listening, interrupted ? "Interrupted · listening" : "Listening");
        }
        finally
        {
            _turnCts?.Dispose();
            _turnCts = null;
            _turnGate.Release();
        }

        if (fatalError is not null)
            await FailAsync(fatalError.Message).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs speak queued async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task SpeakQueuedAsync(ChannelReader<string> reader, CancellationToken cancellationToken)
    {
        var failed = false;
        await foreach (var sentence in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (failed) continue;
            try
            {
                SetState(CallState.Speaking, "Haven is speaking…");
                await _speechOutput.SpeakAsync(
                    sentence,
                    _options?.VoiceName,
                    _options?.OutputDeviceId,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                failed = true;
                SetState(CallState.Thinking, $"Speech output unavailable ({ex.Message}); continuing with transcript.");
            }
        }
    }

    /// <summary>
    /// Performs handle speech input event async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task HandleSpeechInputEventAsync(SpeechInputEvent inputEvent, CancellationToken cancellationToken)
    {
        if (!IsActive || _ending || IsMuted || State == CallState.Paused) return;
        switch (inputEvent.Kind)
        {
            case SpeechInputEventKind.SpeechStarted:
                if (State is CallState.Speaking or CallState.Thinking)
                    await InterruptAsync(cancellationToken).ConfigureAwait(false);
                _partialUserMessageId = Guid.NewGuid();
                SetState(CallState.Transcribing, "Listening to you…");
                break;
            case SpeechInputEventKind.PartialTranscript when !string.IsNullOrWhiteSpace(inputEvent.Text):
                if (_partialUserMessageId == Guid.Empty) _partialUserMessageId = Guid.NewGuid();
                TranscriptChanged?.Invoke(this, new(
                    _partialUserMessageId, MessageRole.User, inputEvent.Text!, false, false));
                break;
            case SpeechInputEventKind.FinalTranscript when !string.IsNullOrWhiteSpace(inputEvent.Text):
                if (_partialUserMessageId == Guid.Empty) _partialUserMessageId = Guid.NewGuid();
                var messageId = _partialUserMessageId;
                _partialUserMessageId = Guid.Empty;
                await RunTurnAsync(inputEvent.Text!, true, messageId, cancellationToken).ConfigureAwait(false);
                break;
            case SpeechInputEventKind.AudioLevel:
                AudioLevelChanged?.Invoke(this, new(Math.Clamp(inputEvent.AudioLevel, 0, 1)));
                break;
            case SpeechInputEventKind.SpeechEnded:
                _partialUserMessageId = Guid.Empty;
                SetState(CallState.Listening, "No speech was detected · listening");
                break;
            case SpeechInputEventKind.Error:
                await FailAsync(inputEvent.Error ?? "Speech input failed.").ConfigureAwait(false);
                break;
            case SpeechInputEventKind.SourceClosed:
                await FailAsync("The microphone source closed unexpectedly.").ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// Performs start speech input async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private Task StartSpeechInputAsync(CancellationToken cancellationToken) =>
        _speechInput.StartAsync(
            new SpeechInputOptions(_options?.InputDeviceId, _speechModel, _options?.InputMode ?? CallInputMode.HandsFree),
            HandleSpeechInputEventAsync,
            cancellationToken);

    /// <summary>
    /// Performs fail async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task FailAsync(string error)
    {
        await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (IsActive)
                await EndCoreAsync(CallSessionStatus.Failed, error, CancellationToken.None).ConfigureAwait(false);
            SetState(CallState.Error, error);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Performs end core async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task EndCoreAsync(
        CallSessionStatus status,
        string? error,
        CancellationToken cancellationToken)
    {
        _ending = true;
        try
        {
            _lifetimeCts?.Cancel();
            _turnCts?.Cancel();
            await CleanupMediaAsync(cancellationToken).ConfigureAwait(false);

            if (CurrentSession is not null)
            {
                CurrentSession = CurrentSession with
                {
                    Status = status,
                    EndedAt = _timeProvider.GetUtcNow(),
                    Error = error
                };
                await _calls.UpsertAsync(CurrentSession, CancellationToken.None).ConfigureAwait(false);
            }

            _lifetimeCts?.Dispose();
            _lifetimeCts = null;
        }
        finally
        {
            _ending = false;
        }
    }

    /// <summary>
    /// Performs stop media input and output async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task StopMediaInputAndOutputAsync(CancellationToken cancellationToken)
    {
        await BestEffortAsync(() => _speechInput.StopAsync(cancellationToken)).ConfigureAwait(false);
        await BestEffortAsync(() => _speechOutput.StopAsync(cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs cleanup media async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task CleanupMediaAsync(CancellationToken cancellationToken)
    {
        await StopMediaInputAndOutputAsync(cancellationToken).ConfigureAwait(false);
        await BestEffortAsync(() => _screenShare.StopAsync(cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs best effort async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task BestEffortAsync(Func<Task> action)
    {
        try { await action().ConfigureAwait(false); }
        catch (Exception) { /* cleanup must continue across adapter failures */ }
    }

    /// <summary>
    /// Performs the ensure active step owned by this component.
    /// </summary>
    private void EnsureActive()
    {
        if (!IsActive || CurrentConversation is null || _options is null)
            throw new InvalidOperationException("Start a Haven call first.");
    }

    /// <summary>
    /// Performs the set state step owned by this component.
    /// </summary>
    private void SetState(CallState state, string status)
    {
        State = state;
        StateChanged?.Invoke(this, new(state, status));
    }

    /// <summary>
    /// Handles the screen share snapshot available event raised by the UI or runtime.
    /// </summary>
    private void OnScreenShareSnapshotAvailable(object? sender, ScreenShareSnapshotEventArgs e) =>
        ScreenPreviewChanged?.Invoke(this, e);

    /// <summary>
    /// Handles the screen share source closed event raised by the UI or runtime.
    /// </summary>
    private void OnScreenShareSourceClosed(object? sender, EventArgs e) =>
        _ = HandleScreenShareSourceClosedAsync();

    /// <summary>
    /// Performs handle screen share source closed async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task HandleScreenShareSourceClosedAsync()
    {
        if (!IsActive || _ending) return;
        await FailAsync("The shared screen or window was closed.").ConfigureAwait(false);
    }

    /// <summary>
    /// Performs dispose async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (IsActive)
        {
            await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await EndCoreAsync(CallSessionStatus.Cancelled, null, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }
        _disposed = true;
        _screenShare.SourceClosed -= OnScreenShareSourceClosed;
        _screenShare.SnapshotAvailable -= OnScreenShareSnapshotAvailable;
        _turnCts?.Dispose();
        _lifetimeCts?.Dispose();
        _turnGate.Dispose();
        _lifecycleGate.Dispose();
    }
}
