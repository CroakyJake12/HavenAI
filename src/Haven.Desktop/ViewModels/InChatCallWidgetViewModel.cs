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
    private EffortLevel _effort = EffortLevel.Low;
    private int _speechSpeedPercent = 100;
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
        ToggleScreenShareCommand = new AsyncRelayCommand(
            ToggleScreenShareAsync,
            () => IsActive && CanShareScreen);
        SubmitTextCommand = new AsyncRelayCommand(
            SubmitTextAsync,
            () => IsActive && !string.IsNullOrWhiteSpace(TypedTranscript));

        _selectedInputDevice = InputDevices.FirstOrDefault(item => item.IsDefault) ?? InputDevices.FirstOrDefault();
        _selectedVoice = Voices.FirstOrDefault(item => item.IsDefault) ?? Voices.FirstOrDefault();

        _callCoordinator.StateChanged += OnCallStateChanged;
        _callCoordinator.TranscriptChanged += OnTranscriptChanged;
        _callCoordinator.AudioLevelChanged += OnAudioLevelChanged;
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
            ToggleScreenShareCommand.RaiseCanExecuteChanged();
            SubmitTextCommand.RaiseCanExecuteChanged();
            CloseWidgetCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        private set => SetProperty(ref _isMuted, value);
    }

    public double AudioLevel
    {
        get => _audioLevel;
        private set => SetProperty(ref _audioLevel, value);
    }

    public IReadOnlyList<VoiceTranscriptTurn> TranscriptTurns => _transcriptTurns;

    public IReadOnlyList<CallAudioDevice> InputDevices => _callCoordinator.Capabilities.InputDevices;
    public IReadOnlyList<CallVoice> Voices => _callCoordinator.Capabilities.Voices;
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

    public int SpeechSpeedPercent
    {
        get => _speechSpeedPercent;
        set => SetProperty(ref _speechSpeedPercent, Math.Clamp(value, 50, 200));
    }

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
                    InputDeviceId: SelectedInputDevice?.Id,
                    VoiceName: SelectedVoice?.Name,
                    Effort: Effort),
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
            Status = "Active";
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

    public void Dispose()
    {
        _callCoordinator.StateChanged -= OnCallStateChanged;
        _callCoordinator.TranscriptChanged -= OnTranscriptChanged;
        _callCoordinator.AudioLevelChanged -= OnAudioLevelChanged;
    }
}
