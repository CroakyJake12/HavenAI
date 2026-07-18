/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/InChatCallWidgetViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns InChatCallWidgetViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents in chat call widget view model and keeps its related state and behavior together.
/// </summary>
public sealed class InChatCallWidgetViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Stores call coordinator locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ICallCoordinator _callCoordinator;
    /// <summary>
    /// Stores conversations locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IConversationRepository _conversations;
    /// <summary>
    /// Stores parent conversation id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Guid _parentConversationId;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = "Ready";
    /// <summary>
    /// Stores transcript locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _transcript = string.Empty;
    /// <summary>
    /// Stores is active locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isActive;
    /// <summary>
    /// Stores is muted locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isMuted;
    /// <summary>
    /// Stores audio level locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private double _audioLevel;
    /// <summary>
    /// Stores linked call conversation locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Conversation? _linkedCallConversation;
    /// <summary>
    /// Stores call summary locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Gets or updates transcript, the bindable or domain state represented by this property.
    /// </summary>
    public string Transcript { get => _transcript; private set => SetProperty(ref _transcript, value); }
    /// <summary>
    /// Reports whether is active is true for the current state.
    /// </summary>
    public bool IsActive { get => _isActive; private set { if (SetProperty(ref _isActive, value)) { RaisePropertyChanged(nameof(CallButtonLabel)); RaisePropertyChanged(nameof(IsReady)); } } }
    /// <summary>
    /// Reports whether is muted is true for the current state.
    /// </summary>
    public bool IsMuted { get => _isMuted; private set => SetProperty(ref _isMuted, value); }
    /// <summary>
    /// Reports whether is ready is true for the current state.
    /// </summary>
    public bool IsReady => !IsActive;
    /// <summary>
    /// Gets or updates audio level, the bindable or domain state represented by this property.
    /// </summary>
    public double AudioLevel { get => _audioLevel; private set => SetProperty(ref _audioLevel, value); }
    /// <summary>
    /// Gets or updates call button label, the bindable or domain state represented by this property.
    /// </summary>
    public string CallButtonLabel => IsActive ? "End call" : "Start call";
    /// <summary>
    /// Gets or updates call summary, the bindable or domain state represented by this property.
    /// </summary>
    public string? CallSummary { get => _callSummary; private set => SetProperty(ref _callSummary, value); }
    /// <summary>
    /// Gets or updates linked call conversation id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid? LinkedCallConversationId => _linkedCallConversation?.Id;

    /// <summary>
    /// Gets or updates start call command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand StartCallCommand { get; }
    /// <summary>
    /// Gets or updates end call command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand EndCallCommand { get; }
    /// <summary>
    /// Gets or updates toggle mute command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ToggleMuteCommand { get; }

    /// <summary>
    /// Stores call linked locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler<Guid>? CallLinked;
    /// <summary>
    /// Stores call ended locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler? CallEnded;

    /// <summary>
    /// Performs start call async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs end call async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs toggle mute async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ToggleMuteAsync()
    {
        if (!IsActive) return;
        await _callCoordinator.SetMutedAsync(!IsMuted, CancellationToken.None);
        IsMuted = !IsMuted;
    }

    /// <summary>
    /// Handles the call state changed event raised by the UI or runtime.
    /// </summary>
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

    /// <summary>
    /// Handles the transcript changed event raised by the UI or runtime.
    /// </summary>
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

    /// <summary>
    /// Handles the audio level changed event raised by the UI or runtime.
    /// </summary>
    private void OnAudioLevelChanged(object? sender, CallAudioLevelEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => AudioLevel = e.Level);
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        _callCoordinator.StateChanged -= OnCallStateChanged;
        _callCoordinator.TranscriptChanged -= OnTranscriptChanged;
        _callCoordinator.AudioLevelChanged -= OnAudioLevelChanged;
    }
}
