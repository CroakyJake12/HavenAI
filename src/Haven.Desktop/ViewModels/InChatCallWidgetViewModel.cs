using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed record VoiceTranscriptTurn(
    Guid MessageId,
    MessageRole Role,
    string Content,
    bool IsFinal,
    bool WasInterrupted);

/// <summary>
/// Application-wide compact voice-call widget state. The shell owns one instance and
/// attaches it to the currently active conversation without interrupting an active call.
/// </summary>
public sealed class InChatCallWidgetViewModel : ObservableObject, IDisposable
{
    private readonly ICallCoordinator _callCoordinator;
    private readonly IConversationRepository _conversations;
    private Guid? _parentConversationId;
    private ModelDescriptor? _selectedModel;
    private CallAudioDevice? _selectedInputDevice;
    private CallVoice? _selectedVoice;
    private VoiceProfile? _selectedVoiceProfile = VoiceProfileCatalog.BuiltIns.FirstOrDefault(profile => profile.Id == "general")
        ?? VoiceProfileCatalog.BuiltIns.FirstOrDefault();
    private string _liveReaction = "Live reactions ready";
    private EffortLevel _effort = EffortLevel.Low;
    private CallInputMode _inputMode = CallInputMode.HandsFree;
    private bool _isPushToTalkRecording;
    private string _typedTranscript = string.Empty;
    private readonly List<VoiceTranscriptTurn> _transcriptTurns = [];
    private Conversation? _linkedCallConversation;
    private string _status = "Ready";
    private string _transcript = string.Empty;
    private string? _callSummary;
    private bool _isOpen;
    private bool _isActive;
    private bool _isMuted;
    private double _audioLevel;
    private VoiceInputStatus _inputStatus = new(VoiceInputState.Ready, "Microphone ready.");

    public InChatCallWidgetViewModel(
        ICallCoordinator callCoordinator,
        IConversationRepository conversations,
        Guid parentConversationId)
        : this(callCoordinator, conversations, (Guid?)parentConversationId)
    {
    }

    public InChatCallWidgetViewModel(
        ICallCoordinator callCoordinator,
        IConversationRepository conversations,
        Guid? parentConversationId = null)
    {
        _callCoordinator = callCoordinator;
        _conversations = conversations;
        _parentConversationId = parentConversationId;

        OpenWidgetCommand = new AsyncRelayCommand(OpenWidgetAsync);
        CloseWidgetCommand = new AsyncRelayCommand(CloseWidgetAsync, () => CanClose);
        StartCallCommand = new AsyncRelayCommand(StartCallAsync, () => !IsActive && _selectedModel is not null);
        EndCallCommand = new AsyncRelayCommand(EndCallAsync, () => IsActive);
        ToggleMuteCommand = new AsyncRelayCommand(ToggleMuteAsync);
        RetryMicrophoneCommand = new AsyncRelayCommand(RetryMicrophoneAsync, () => CanRetryVoiceInput);
        TogglePauseCommand = new AsyncRelayCommand(TogglePauseAsync, () => IsActive);
        InterruptCommand = new AsyncRelayCommand(InterruptAsync, () => IsActive && !IsPaused);
        TogglePushToTalkCommand = new AsyncRelayCommand(TogglePushToTalkAsync, () => CanPushToTalk || IsPushToTalkRecording);
        ToggleScreenShareCommand = new AsyncRelayCommand(
            ToggleScreenShareAsync,
            () => IsActive && CanShareScreen);
        SubmitTextCommand = new AsyncRelayCommand(
            SubmitTextAsync,
            () => IsActive && !string.IsNullOrWhiteSpace(TypedTranscript));

        _selectedInputDevice = InputDevices.FirstOrDefault(item => item.IsDefault) ?? InputDevices.FirstOrDefault();
        _selectedVoice = Voices.FirstOrDefault(item => item.IsDefault) ?? Voices.FirstOrDefault();

        if (_callCoordinator is IVoiceInputStatusSource inputStatusSource)
        {
            _inputStatus = inputStatusSource.InputStatus;
            inputStatusSource.InputStatusChanged += OnVoiceInputStatusChanged;
        }
        else if (!_callCoordinator.Capabilities.HasSpeechInput)
        {
            _inputStatus = new VoiceInputStatus(
                VoiceInputState.Unavailable,
                _callCoordinator.Capabilities.SpeechInputUnavailableReason ?? "Microphone transcription is unavailable.");
        }

        _callCoordinator.StateChanged += OnCallStateChanged;
        _callCoordinator.TranscriptChanged += OnTranscriptChanged;
        _callCoordinator.AudioLevelChanged += OnAudioLevelChanged;
        if (_callCoordinator is IVoiceReactionSource reactionSource)
            reactionSource.VoiceReactionChanged += OnVoiceReactionChanged;

        // Presentation hosts may be recreated while the singleton call coordinator keeps
        // the same live session. Hydrate immediately so normal/floating/Overlay Voice
        // surfaces never flash a second Start action or lose the linked call identity.
        if (_callCoordinator.IsActive)
        {
            _linkedCallConversation = _callCoordinator.CurrentConversation;
            _inputMode = _callCoordinator.CurrentSession?.InputMode ?? CallInputMode.HandsFree;
            _isOpen = true;
            _isActive = true;
            _isMuted = _callCoordinator.IsMuted;
            _status = _callCoordinator.State switch
            {
                CallState.Listening => "Listening",
                CallState.Transcribing => "Transcribing",
                CallState.Thinking => "Thinking",
                CallState.Speaking => "Speaking",
                CallState.Paused => "Paused",
                _ => "Active"
            };
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>
    /// The unified call transcript supplied by the coordinator. It includes spoken user turns,
    /// typed turns written to the call conversation, and Haven's spoken responses.
    /// </summary>
    public string Transcript
    {
        get => _transcript;
        private set => SetProperty(ref _transcript, value);
    }

    public string? CallSummary
    {
        get => _callSummary;
        private set => SetProperty(ref _callSummary, value);
    }

    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (SetProperty(ref _isOpen, value))
            {
                RaisePropertyChanged(nameof(IsVisible));
            }
        }
    }

    public bool IsVisible => IsOpen || IsActive;

    public bool IsActive
    {
        get => _isActive;
        private set
        {
            if (!SetProperty(ref _isActive, value))
            {
                return;
            }

            if (value)
            {
                IsOpen = true;
            }

            RaisePropertyChanged(nameof(IsVisible));
            RaisePropertyChanged(nameof(CanClose));
            RaisePropertyChanged(nameof(IsStartButtonVisible));
            RaisePropertyChanged(nameof(IsEndButtonVisible));
            RaisePropertyChanged(nameof(CallButtonLabel));
            StartCallCommand.RaiseCanExecuteChanged();
            EndCallCommand.RaiseCanExecuteChanged();
            ToggleMuteCommand.RaiseCanExecuteChanged();
            RaisePropertyChanged(nameof(CanRetryVoiceInput));
            RetryMicrophoneCommand.RaiseCanExecuteChanged();
            RaiseVoiceControlCanExecuteChanged();
            ToggleScreenShareCommand.RaiseCanExecuteChanged();
            SubmitTextCommand.RaiseCanExecuteChanged();
            CloseWidgetCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        private set
        {
            if (!SetProperty(ref _isMuted, value)) return;
            RaisePropertyChanged(nameof(CanPushToTalk));
            TogglePushToTalkCommand.RaiseCanExecuteChanged();
        }
    }

    public double AudioLevel
    {
        get => _audioLevel;
        private set => SetProperty(ref _audioLevel, value);
    }

    public VoiceInputStatus InputStatus
    {
        get => _inputStatus;
        private set
        {
            if (!SetProperty(ref _inputStatus, value)) return;
            RaisePropertyChanged(nameof(IsVoiceInputDegraded));
            RaisePropertyChanged(nameof(CanRetryVoiceInput));
            RaisePropertyChanged(nameof(CanPushToTalk));
            if (IsVoiceInputDegraded)
            {
                AudioLevel = 0;
                if (IsPushToTalkRecording) IsPushToTalkRecording = false;
            }
            RetryMicrophoneCommand.RaiseCanExecuteChanged();
            TogglePushToTalkCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsVoiceInputDegraded => InputStatus.State is
        VoiceInputState.PermissionDenied or VoiceInputState.Unavailable or VoiceInputState.Error;
    public bool CanRetryVoiceInput => IsActive && IsVoiceInputDegraded && InputStatus.CanRetry;

    public IReadOnlyList<VoiceTranscriptTurn> TranscriptTurns => _transcriptTurns;

    public IReadOnlyList<CallAudioDevice> InputDevices => _callCoordinator.Capabilities.InputDevices;
    public IReadOnlyList<CallVoice> Voices => _callCoordinator.Capabilities.Voices;
    public IReadOnlyList<VoiceProfile> VoiceProfiles => VoiceProfileCatalog.BuiltIns;

    public IReadOnlyList<CallInputMode> InputModes { get; } = Enum.GetValues<CallInputMode>();

    public CallInputMode InputMode
    {
        get => _inputMode;
        set
        {
            if (IsActive) return;
            if (SetProperty(ref _inputMode, value))
                RaisePropertyChanged(nameof(CanPushToTalk));
        }
    }

    public bool IsPaused => IsActive && _callCoordinator.State == CallState.Paused;
    public bool IsPushToTalkRecording
    {
        get => _isPushToTalkRecording;
        private set
        {
            if (!SetProperty(ref _isPushToTalkRecording, value)) return;
            RaisePropertyChanged(nameof(CanPushToTalk));
            TogglePushToTalkCommand.RaiseCanExecuteChanged();
        }
    }
    public bool CanPushToTalk => IsActive && InputMode == CallInputMode.PushToTalk && _callCoordinator.Capabilities.HasSpeechInput && !IsVoiceInputDegraded && !IsMuted && !IsPaused;

    public VoiceProfile? SelectedVoiceProfile
    {
        get => _selectedVoiceProfile;
        set
        {
            if (IsActive) return;
            if (SetProperty(ref _selectedVoiceProfile, value))
            {
                RaisePropertyChanged(nameof(ActiveVoiceModeName));
            }
        }
    }

    public string ActiveVoiceModeName =>
        (_callCoordinator as IVoiceReactionSource)?.ActiveVoiceProfile?.Name
        ?? SelectedVoiceProfile?.Name
        ?? "Voice";

    public string LiveReaction
    {
        get => _liveReaction;
        private set => SetProperty(ref _liveReaction, value);
    }
    public bool CanShareScreen => _callCoordinator.Capabilities.CanShareScreen;
    public bool IsScreenSharing => _callCoordinator.IsScreenSharing;
    public string ScreenShareStatus => IsScreenSharing
        ? "Screen or app is being shared"
        : CanShareScreen
            ? "Choose a screen or app to share"
            : _callCoordinator.Capabilities.ScreenShareUnavailableReason ?? "Screen sharing is unavailable";
    public string SelectedModelName => _selectedModel?.Name ?? "No model available";

    public CallAudioDevice? SelectedInputDevice
    {
        get => _selectedInputDevice;
        set => SetProperty(ref _selectedInputDevice, value);
    }

    public CallVoice? SelectedVoice
    {
        get => _selectedVoice;
        set => SetProperty(ref _selectedVoice, value);
    }

    public EffortLevel Effort
    {
        get => _effort;
        set
        {
            if (SetProperty(ref _effort, value))
            {
                RaisePropertyChanged(nameof(ReasoningPercent));
            }
        }
    }

    public int ReasoningPercent => Effort switch
    {
        EffortLevel.Low => 25,
        EffortLevel.Medium => 50,
        EffortLevel.High => 75,
        _ => 100
    };

    public string TypedTranscript
    {
        get => _typedTranscript;
        set
        {
            if (SetProperty(ref _typedTranscript, value))
            {
                SubmitTextCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanClose => !IsActive;
    public bool IsStartButtonVisible => !IsActive;
    public bool IsEndButtonVisible => IsActive;
    public string CallButtonLabel => IsActive ? "End" : "Start";
    public Guid? ParentConversationId => _parentConversationId;
    public Guid? LinkedCallConversationId => _linkedCallConversation?.Id;

    public AsyncRelayCommand OpenWidgetCommand { get; }
    public AsyncRelayCommand CloseWidgetCommand { get; }
    public AsyncRelayCommand StartCallCommand { get; }
    public AsyncRelayCommand EndCallCommand { get; }
    public AsyncRelayCommand ToggleMuteCommand { get; }
    public AsyncRelayCommand RetryMicrophoneCommand { get; }
    public AsyncRelayCommand TogglePauseCommand { get; }
    public AsyncRelayCommand InterruptCommand { get; }
    public AsyncRelayCommand TogglePushToTalkCommand { get; }
    public AsyncRelayCommand ToggleScreenShareCommand { get; }
    public AsyncRelayCommand SubmitTextCommand { get; }

    public event EventHandler<Guid>? CallLinked;
    public event EventHandler? CallEnded;

    public void AttachConversation(Guid? conversationId)
    {
        _parentConversationId = conversationId;
        RaisePropertyChanged(nameof(ParentConversationId));
    }

    public void AttachConversation(Guid? conversationId, ModelDescriptor? selectedModel)
    {
        _selectedModel = selectedModel;
        RaisePropertyChanged(nameof(SelectedModelName));
        StartCallCommand.RaiseCanExecuteChanged();
        AttachConversation(conversationId);
    }

    public void Open()
    {
        IsOpen = true;
        if (!IsActive)
        {
            Status = "Ready";
        }
    }

    public void Close()
    {
        if (CanClose)
        {
            IsOpen = false;
        }
    }

    private Task OpenWidgetAsync()
    {
        Open();
        return Task.CompletedTask;
    }

    private Task CloseWidgetAsync()
    {
        Close();
        return Task.CompletedTask;
    }

    private async Task StartCallAsync()
    {
        if (IsActive)
        {
            return;
        }

        if (_selectedModel is null)
        {
            Status = "No compatible model is available";
            return;
        }

        IsOpen = true;
        var startMuted = IsMuted;
        Status = "Connecting…";

        try
        {
            var result = await _callCoordinator.StartAsync(
                new CallStartOptions(
                    Model: _selectedModel,
                    InputMode: InputMode,
                    InputDeviceId: SelectedInputDevice?.Id,
                    VoiceName: SelectedVoice?.Name,
                    Effort: Effort,
                    VoiceProfileId: SelectedVoiceProfile?.Id,
                    VoiceProfile: SelectedVoiceProfile),
                null,
                CancellationToken.None);

            if (result is null)
            {
                Status = "Failed to start";
                return;
            }

            _linkedCallConversation = await _conversations.GetAsync(
                result.ConversationId,
                CancellationToken.None);

            Transcript = string.Empty;
            _transcriptTurns.Clear();
            RaisePropertyChanged(nameof(TranscriptTurns));
            CallSummary = null;
            IsActive = true;
            if (_callCoordinator is IVoiceInputStatusSource inputStatusSource)
                InputStatus = inputStatusSource.InputStatus;
            Status = _callCoordinator.State switch
            {
                CallState.Listening => "Listening",
                CallState.Transcribing => "Transcribing",
                CallState.Thinking => "Thinking",
                CallState.Speaking => "Speaking",
                CallState.Paused => "Paused",
                _ => "Active"
            };
            if (startMuted)
            {
                await _callCoordinator.SetMutedAsync(true, CancellationToken.None);
                IsMuted = true;
            }
            CallLinked?.Invoke(this, result.ConversationId);
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled";
        }
        catch (Exception exception)
        {
            Status = $"Error: {exception.Message}";
        }
    }

    private async Task EndCallAsync()
    {
        if (!IsActive)
        {
            return;
        }

        Status = "Ending…";

        try
        {
            await _callCoordinator.EndAsync(CancellationToken.None);
            IsActive = false;
            IsPushToTalkRecording = false;
            IsMuted = false;
            AudioLevel = 0;
            RaisePropertyChanged(nameof(IsScreenSharing));
            RaisePropertyChanged(nameof(ScreenShareStatus));
            Status = "Ended";
            CallSummary = "Call ended.";
            CallEnded?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            Status = "Active";
        }
        catch (Exception exception)
        {
            Status = $"Error: {exception.Message}";
        }
    }

    private async Task ToggleMuteAsync()
    {
        if (!IsActive)
        {
            IsMuted = !IsMuted;
            return;
        }

        var next = !IsMuted;
        await _callCoordinator.SetMutedAsync(next, CancellationToken.None);
        IsMuted = next;
    }

    private async Task RetryMicrophoneAsync()
    {
        if (!CanRetryVoiceInput) return;
        try
        {
            if (!_callCoordinator.IsMuted)
                await _callCoordinator.SetMutedAsync(true, CancellationToken.None);
            await _callCoordinator.SetMutedAsync(false, CancellationToken.None);
            IsMuted = _callCoordinator.IsMuted;
            if (_callCoordinator is IVoiceInputStatusSource inputStatusSource)
                InputStatus = inputStatusSource.InputStatus;
        }
        catch (Exception exception)
        {
            Status = $"Microphone retry failed: {exception.Message}";
        }
    }

    private async Task TogglePauseAsync()
    {
        if (!IsActive) return;
        try
        {
            if (IsPaused)
                await _callCoordinator.ResumeAsync(CancellationToken.None);
            else
            {
                IsPushToTalkRecording = false;
                await _callCoordinator.PauseAsync(CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            Status = $"Voice control error: {exception.Message}";
        }
    }

    private async Task InterruptAsync()
    {
        if (!IsActive || IsPaused) return;
        try { await _callCoordinator.InterruptAsync(CancellationToken.None); }
        catch (Exception exception) { Status = $"Voice control error: {exception.Message}"; }
    }

    private async Task TogglePushToTalkAsync()
    {
        if (!IsActive) return;
        try
        {
            if (IsPushToTalkRecording)
            {
                await _callCoordinator.EndPushToTalkAsync(CancellationToken.None);
                IsPushToTalkRecording = false;
                return;
            }
            if (!CanPushToTalk) return;
            await _callCoordinator.BeginPushToTalkAsync(CancellationToken.None);
            IsPushToTalkRecording = true;
        }
        catch (Exception exception)
        {
            IsPushToTalkRecording = false;
            Status = $"Push-to-talk error: {exception.Message}";
        }
    }

    private void RaiseVoiceControlCanExecuteChanged()
    {
        RaisePropertyChanged(nameof(IsPaused));
        RaisePropertyChanged(nameof(CanPushToTalk));
        TogglePauseCommand.RaiseCanExecuteChanged();
        InterruptCommand.RaiseCanExecuteChanged();
        TogglePushToTalkCommand.RaiseCanExecuteChanged();
    }

    private async Task ToggleScreenShareAsync()
    {
        if (!IsActive || !CanShareScreen)
        {
            return;
        }

        Status = IsScreenSharing ? "Stopping share…" : "Choosing what to share…";
        try
        {
            if (IsScreenSharing)
            {
                await _callCoordinator.StopScreenShareAsync(CancellationToken.None);
            }
            else
            {
                await _callCoordinator.StartScreenShareAsync(CancellationToken.None);
            }

            RaisePropertyChanged(nameof(IsScreenSharing));
            RaisePropertyChanged(nameof(ScreenShareStatus));
            ToggleScreenShareCommand.RaiseCanExecuteChanged();
            Status = "Active";
        }
        catch (OperationCanceledException)
        {
            Status = "Active";
        }
        catch (Exception exception)
        {
            Status = $"Share error: {exception.Message}";
        }
    }

    private async Task SubmitTextAsync()
    {
        var text = TypedTranscript.Trim();
        if (!IsActive || text.Length == 0)
        {
            return;
        }

        TypedTranscript = string.Empty;
        try
        {
            await _callCoordinator.SubmitTextAsync(text, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            TypedTranscript = text;
        }
        catch (Exception exception)
        {
            TypedTranscript = text;
            Status = $"Message error: {exception.Message}";
        }
    }

    private void OnCallStateChanged(object? sender, CallStateChangedEventArgs args)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            switch (args.State)
            {
                case CallState.Idle:
                case CallState.Error:
                    IsActive = false;
                    IsPushToTalkRecording = false;
                    IsMuted = false;
                    AudioLevel = 0;
                    RaisePropertyChanged(nameof(IsScreenSharing));
                    RaisePropertyChanged(nameof(ScreenShareStatus));
                    Status = args.State == CallState.Error ? "Error" : "Ended";
                    CallSummary = "Call ended.";
                    CallEnded?.Invoke(this, EventArgs.Empty);
                    break;

                default:
                    Status = args.Status;
                    IsActive = true;
                    IsMuted = _callCoordinator.IsMuted;
                    if (args.State == CallState.Paused) IsPushToTalkRecording = false;
                    RaiseVoiceControlCanExecuteChanged();
                    RaisePropertyChanged(nameof(IsScreenSharing));
                    RaisePropertyChanged(nameof(ScreenShareStatus));
                    ToggleScreenShareCommand.RaiseCanExecuteChanged();
                    break;
            }
        });
    }

    private void OnTranscriptChanged(object? sender, CallTranscriptEventArgs args)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var index = _transcriptTurns.FindIndex(turn => turn.MessageId == args.MessageId);
            if (index < 0)
            {
                _transcriptTurns.Add(new VoiceTranscriptTurn(
                    args.MessageId,
                    args.Role,
                    args.Text,
                    args.IsFinal,
                    args.WasInterrupted));
            }
            else
            {
                var current = _transcriptTurns[index];
                var content = args.IsDelta ? current.Content + args.Text : args.Text;
                _transcriptTurns[index] = current with
                {
                    Content = content,
                    IsFinal = args.IsFinal,
                    WasInterrupted = args.WasInterrupted
                };
            }

            Transcript = string.Join(
                Environment.NewLine,
                _transcriptTurns.Select(turn => turn.Content));
            RaisePropertyChanged(nameof(TranscriptTurns));
        });
    }

    private void OnAudioLevelChanged(object? sender, CallAudioLevelEventArgs args)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => AudioLevel = args.Level);
    }

    private void OnVoiceInputStatusChanged(object? sender, VoiceInputStatusChangedEventArgs args)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => InputStatus = args.Status);
    }

    private void OnVoiceReactionChanged(object? sender, VoiceReactionEventArgs args)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LiveReaction = args.Reaction.Summary;
            RaisePropertyChanged(nameof(ActiveVoiceModeName));
        });
    }

    public void Dispose()
    {
        _callCoordinator.StateChanged -= OnCallStateChanged;
        _callCoordinator.TranscriptChanged -= OnTranscriptChanged;
        _callCoordinator.AudioLevelChanged -= OnAudioLevelChanged;
        if (_callCoordinator is IVoiceInputStatusSource inputStatusSource)
            inputStatusSource.InputStatusChanged -= OnVoiceInputStatusChanged;
        if (_callCoordinator is IVoiceReactionSource reactionSource)
            reactionSource.VoiceReactionChanged -= OnVoiceReactionChanged;
    }
}
