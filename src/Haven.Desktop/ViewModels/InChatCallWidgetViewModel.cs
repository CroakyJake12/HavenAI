using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed class InChatCallWidgetViewModel : ObservableObject, IDisposable
{
    private readonly ICallCoordinator _callCoordinator;
    private readonly IConversationRepository _conversations;
    private readonly Guid _parentConversationId;
    private string _status = "Ready";
    private string _transcript = string.Empty;
    private bool _isActive;
    private bool _isMuted;
    private double _audioLevel;
    private Conversation? _linkedCallConversation;
    private string? _callSummary;

    public InChatCallWidgetViewModel(
        ICallCoordinator callCoordinator,
        IConversationRepository conversations,
        Guid parentConversationId)
    {
        _callCoordinator = callCoordinator;
        _conversations = conversations;
        _parentConversationId = parentConversationId;
        StartCallCommand = new AsyncRelayCommand(StartCallAsync, () => !IsActive);
        EndCallCommand = new AsyncRelayCommand(EndCallAsync, () => IsActive);
        ToggleMuteCommand = new AsyncRelayCommand(ToggleMuteAsync);
        _callCoordinator.StateChanged += OnCallStateChanged;
        _callCoordinator.TranscriptChanged += OnTranscriptChanged;
        _callCoordinator.AudioLevelChanged += OnAudioLevelChanged;
    }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Transcript { get => _transcript; private set => SetProperty(ref _transcript, value); }
    public bool IsActive { get => _isActive; private set { if (SetProperty(ref _isActive, value)) { RaisePropertyChanged(nameof(CallButtonLabel)); RaisePropertyChanged(nameof(IsReady)); } } }
    public bool IsMuted { get => _isMuted; private set => SetProperty(ref _isMuted, value); }
    public bool IsReady => !IsActive;
    public double AudioLevel { get => _audioLevel; private set => SetProperty(ref _audioLevel, value); }
    public string CallButtonLabel => IsActive ? "End call" : "Start call";
    public string? CallSummary { get => _callSummary; private set => SetProperty(ref _callSummary, value); }
    public Guid? LinkedCallConversationId => _linkedCallConversation?.Id;

    public AsyncRelayCommand StartCallCommand { get; }
    public AsyncRelayCommand EndCallCommand { get; }
    public AsyncRelayCommand ToggleMuteCommand { get; }

    public event EventHandler<Guid>? CallLinked;
    public event EventHandler? CallEnded;

    private async Task StartCallAsync()
    {
        if (IsActive) return;
        Status = "Connecting…";
        try
        {
            var result = await _callCoordinator.StartAsync(
                new CallStartOptions(Model: new ModelDescriptor("default", 0, "unknown", "", "", new HashSet<ToolCapability>(), DateTimeOffset.UtcNow)),
                null,
                CancellationToken.None);
            if (result is not null)
            {
                _linkedCallConversation = await _conversations.GetAsync(result.ConversationId, CancellationToken.None);
                IsActive = true;
                Status = "Active";
                CallLinked?.Invoke(this, result.ConversationId);
            }
            else
            {
                Status = "Failed to start";
            }
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
    }

    private async Task EndCallAsync()
    {
        if (!IsActive) return;
        Status = "Ending…";
        try
        {
            await _callCoordinator.EndAsync(CancellationToken.None);
            IsActive = false;
            Status = "Ended";
            CallSummary = "Call ended.";
            CallEnded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
    }

    private async Task ToggleMuteAsync()
    {
        if (!IsActive) return;
        await _callCoordinator.SetMutedAsync(!IsMuted, CancellationToken.None);
        IsMuted = !IsMuted;
    }

    private void OnCallStateChanged(object? sender, CallStateChangedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (e.State == CallState.Idle || e.State == CallState.Error)
            {
                IsActive = false;
                Status = e.State == CallState.Error ? "Error" : "Ended";
                CallSummary = "Call ended.";
                CallEnded?.Invoke(this, EventArgs.Empty);
            }
            else if (e.State == CallState.Listening || e.State == CallState.Speaking)
            {
                Status = e.State.ToString();
                IsActive = true;
            }
        });
    }

    private void OnTranscriptChanged(object? sender, CallTranscriptEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (e.IsDelta)
                Transcript += e.Text;
            else
                Transcript = e.Text;
        });
    }

    private void OnAudioLevelChanged(object? sender, CallAudioLevelEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => AudioLevel = e.Level);
    }

    public void Dispose()
    {
        _callCoordinator.StateChanged -= OnCallStateChanged;
        _callCoordinator.TranscriptChanged -= OnTranscriptChanged;
        _callCoordinator.AudioLevelChanged -= OnAudioLevelChanged;
    }
}
