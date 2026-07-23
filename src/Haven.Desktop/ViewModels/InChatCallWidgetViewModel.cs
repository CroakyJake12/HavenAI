using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Application-wide compact voice-call widget state. The shell owns one instance and
/// attaches it to the currently active conversation without interrupting an active call.
/// </summary>
public sealed class InChatCallWidgetViewModel : ObservableObject, IDisposable
{
    private readonly ICallCoordinator _callCoordinator;
    private readonly IConversationRepository _conversations;
    private Guid? _parentConversationId;
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
        StartCallCommand = new AsyncRelayCommand(StartCallAsync, () => !IsActive);
        EndCallCommand = new AsyncRelayCommand(EndCallAsync, () => IsActive);
        ToggleMuteCommand = new AsyncRelayCommand(ToggleMuteAsync, () => IsActive);

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

    public event EventHandler<Guid>? CallLinked;
    public event EventHandler? CallEnded;

    public void AttachConversation(Guid? conversationId)
    {
        _parentConversationId = conversationId;
        RaisePropertyChanged(nameof(ParentConversationId));
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

        IsOpen = true;
        Status = "Connecting…";

        try
        {
            var result = await _callCoordinator.StartAsync(
                new CallStartOptions(
                    Model: new ModelDescriptor(
                        "default",
                        0,
                        "unknown",
                        string.Empty,
                        string.Empty,
                        new HashSet<ToolCapability>(),
                        DateTimeOffset.UtcNow)),
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
            CallSummary = null;
            IsActive = true;
            Status = "Active";
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
            return;
        }

        var next = !IsMuted;
        await _callCoordinator.SetMutedAsync(next, CancellationToken.None);
        IsMuted = next;
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
                    Status = args.State == CallState.Error ? "Error" : "Ended";
                    CallSummary = "Call ended.";
                    CallEnded?.Invoke(this, EventArgs.Empty);
                    break;

                default:
                    Status = args.State.ToString();
                    IsActive = true;
                    break;
            }
        });
    }

    private void OnTranscriptChanged(object? sender, CallTranscriptEventArgs args)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Transcript = args.IsDelta ? Transcript + args.Text : args.Text;
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
