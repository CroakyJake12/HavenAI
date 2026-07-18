/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/CallPageViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns CallPageViewModel, CallTranscriptItemViewModel, WaveformBarViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents call page view model and keeps its related state and behavior together.
/// </summary>
public sealed class CallPageViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Stores coordinator locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ICallCoordinator _coordinator;
    /// <summary>
    /// Stores ollama locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IOllamaClient _ollama;
    /// <summary>
    /// Stores speech models locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ISpeechModelManager _speechModels;
    /// <summary>
    /// Stores initialization gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    /// <summary>
    /// Stores transcript by id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Dictionary<Guid, CallTranscriptItemViewModel> _transcriptById = [];
    /// <summary>
    /// Stores initialized locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _initialized;
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Stores selected model locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ModelDescriptor? _selectedModel;
    public ModelDescriptor? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (!SetProperty(ref _selectedModel, value)) return;
            RaisePropertyChanged(nameof(CanStart));
            StartCallCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Stores selected speech model locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private SpeechModelInfo? _selectedSpeechModel;
    public SpeechModelInfo? SelectedSpeechModel
    {
        get => _selectedSpeechModel;
        set
        {
            if (!SetProperty(ref _selectedSpeechModel, value)) return;
            RaisePropertyChanged(nameof(ShowSpeechModelSetup));
            RaisePropertyChanged(nameof(SpeechModelStatus));
        }
    }

    /// <summary>
    /// Stores selected input device locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CallAudioDevice? _selectedInputDevice;
    public CallAudioDevice? SelectedInputDevice
    {
        get => _selectedInputDevice;
        set => SetProperty(ref _selectedInputDevice, value);
    }

    /// <summary>
    /// Stores selected output device locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CallAudioDevice? _selectedOutputDevice;
    public CallAudioDevice? SelectedOutputDevice
    {
        get => _selectedOutputDevice;
        set => SetProperty(ref _selectedOutputDevice, value);
    }

    /// <summary>
    /// Stores selected voice locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CallVoice? _selectedVoice;
    public CallVoice? SelectedVoice
    {
        get => _selectedVoice;
        set => SetProperty(ref _selectedVoice, value);
    }

    /// <summary>
    /// Stores input mode locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CallInputMode _inputMode = CallInputMode.HandsFree;
    public CallInputMode InputMode
    {
        get => _inputMode;
        set => SetProperty(ref _inputMode, value);
    }

    /// <summary>
    /// Stores enable speech output locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _enableSpeechOutput = true;
    public bool EnableSpeechOutput
    {
        get => _enableSpeechOutput;
        set => SetProperty(ref _enableSpeechOutput, value);
    }

    /// <summary>
    /// Stores typed transcript locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _typedTranscript = string.Empty;
    public string TypedTranscript
    {
        get => _typedTranscript;
        set
        {
            if (!SetProperty(ref _typedTranscript, value)) return;
            RaisePropertyChanged(nameof(CanSendText));
            SendTranscriptCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Stores call state locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CallState _callState = CallState.Idle;
    public CallState CallState
    {
        get => _callState;
        private set
        {
            if (!SetProperty(ref _callState, value)) return;
            RaisePropertyChanged(nameof(StateLabel));
            RaisePropertyChanged(nameof(IsPaused));
        }
    }

    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = "Ready to call";
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>
    /// Stores is downloading speech model locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isDownloadingSpeechModel;
    public bool IsDownloadingSpeechModel
    {
        get => _isDownloadingSpeechModel;
        private set
        {
            if (!SetProperty(ref _isDownloadingSpeechModel, value)) return;
            DownloadSpeechModelCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Stores speech model download progress locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private double _speechModelDownloadProgress;
    public double SpeechModelDownloadProgress
    {
        get => _speechModelDownloadProgress;
        private set => SetProperty(ref _speechModelDownloadProgress, value);
    }

    /// <summary>
    /// Stores is push to talk pressed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isPushToTalkPressed;
    public bool IsPushToTalkPressed
    {
        get => _isPushToTalkPressed;
        private set
        {
            if (!SetProperty(ref _isPushToTalkPressed, value)) return;
            RaisePropertyChanged(nameof(PushToTalkLabel));
        }
    }

    /// <summary>
    /// Stores screen preview locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Bitmap? _screenPreview;
    /// <summary>
    /// Gets or updates screen preview, the bindable or domain state represented by this property.
    /// </summary>
    public Bitmap? ScreenPreview => _screenPreview;
    /// <summary>
    /// Reports whether has screen preview is true for the current state.
    /// </summary>
    public bool HasScreenPreview => _screenPreview is not null;

    /// <summary>
    /// Gets or updates available models, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ModelDescriptor> AvailableModels { get; } = [];
    /// <summary>
    /// Gets or updates available speech models, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<SpeechModelInfo> AvailableSpeechModels { get; } = [];
    /// <summary>
    /// Gets or updates input devices, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<CallAudioDevice> InputDevices { get; } = [];
    /// <summary>
    /// Gets or updates output devices, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<CallAudioDevice> OutputDevices { get; } = [];
    /// <summary>
    /// Gets or updates voices, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<CallVoice> Voices { get; } = [];
    /// <summary>
    /// Gets or updates transcript, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<CallTranscriptItemViewModel> Transcript { get; } = [];
    /// <summary>
    /// Gets or updates waveform bars, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<WaveformBarViewModel> WaveformBars { get; } = [];

    /// <summary>
    /// Gets or updates input modes, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<CallInputMode> InputModes { get; } = Enum.GetValues<CallInputMode>();

    /// <summary>
    /// Reports whether is active is true for the current state.
    /// </summary>
    public bool IsActive => _coordinator.IsActive;
    /// <summary>
    /// Reports whether is not active is true for the current state.
    /// </summary>
    public bool IsNotActive => !IsActive;
    /// <summary>
    /// Reports whether is paused is true for the current state.
    /// </summary>
    public bool IsPaused => CallState == CallState.Paused;
    /// <summary>
    /// Reports whether is muted is true for the current state.
    /// </summary>
    public bool IsMuted => _coordinator.IsMuted;
    /// <summary>
    /// Reports whether is sharing is true for the current state.
    /// </summary>
    public bool IsSharing => _coordinator.IsScreenSharing;
    /// <summary>
    /// Reports whether has speech input is true for the current state.
    /// </summary>
    public bool HasSpeechInput => _coordinator.Capabilities.HasSpeechInput;
    /// <summary>
    /// Reports whether has speech output is true for the current state.
    /// </summary>
    public bool HasSpeechOutput => _coordinator.Capabilities.HasSpeechOutput;
    /// <summary>
    /// Reports whether can share screen is true for the current state.
    /// </summary>
    public bool CanShareScreen => _coordinator.Capabilities.CanShareScreen;
    /// <summary>
    /// Reports whether has transcript is true for the current state.
    /// </summary>
    public bool HasTranscript => Transcript.Count > 0;
    /// <summary>
    /// Reports whether can start is true for the current state.
    /// </summary>
    public bool CanStart => !IsActive && SelectedModel is not null;
    /// <summary>
    /// Reports whether can send text is true for the current state.
    /// </summary>
    public bool CanSendText => IsActive && !string.IsNullOrWhiteSpace(TypedTranscript);
    /// <summary>
    /// Gets or updates state label, the bindable or domain state represented by this property.
    /// </summary>
    public string StateLabel => CallState.ToString();
    /// <summary>
    /// Gets or updates mute label, the bindable or domain state represented by this property.
    /// </summary>
    public string MuteLabel => IsMuted ? "Unmute" : "Mute";
    /// <summary>
    /// Gets or updates pause label, the bindable or domain state represented by this property.
    /// </summary>
    public string PauseLabel => IsPaused ? "Resume" : "Pause";
    /// <summary>
    /// Gets or updates share label, the bindable or domain state represented by this property.
    /// </summary>
    public string ShareLabel => IsSharing ? "Stop sharing" : "Share screen";
    /// <summary>
    /// Gets or updates push to talk label, the bindable or domain state represented by this property.
    /// </summary>
    public string PushToTalkLabel => IsPushToTalkPressed ? "Release to send" : "Hold to talk";
    /// <summary>
    /// Gets or updates speech input status, the bindable or domain state represented by this property.
    /// </summary>
    public string SpeechInputStatus => HasSpeechInput
        ? "Local microphone transcription ready"
        : _coordinator.Capabilities.SpeechInputUnavailableReason ?? "Microphone transcription unavailable";
    /// <summary>
    /// Gets or updates speech output status, the bindable or domain state represented by this property.
    /// </summary>
    public string SpeechOutputStatus => HasSpeechOutput
        ? "Local speech output ready"
        : _coordinator.Capabilities.SpeechOutputUnavailableReason ?? "Speech output unavailable";
    /// <summary>
    /// Gets or updates screen share status, the bindable or domain state represented by this property.
    /// </summary>
    public string ScreenShareStatus => CanShareScreen
        ? "Windows screen picker ready"
        : _coordinator.Capabilities.ScreenShareUnavailableReason ?? "Screen sharing unavailable";
    /// <summary>
    /// Gets or updates show speech model setup, the bindable or domain state represented by this property.
    /// </summary>
    public bool ShowSpeechModelSetup => SelectedSpeechModel?.IsInstalled == false;
    /// <summary>
    /// Gets or updates speech model status, the bindable or domain state represented by this property.
    /// </summary>
    public string SpeechModelStatus => SelectedSpeechModel is null
        ? "Choose a local speech model"
        : SelectedSpeechModel.IsInstalled
            ? $"Installed · {FormatBytes(SelectedSpeechModel.ApproximateSizeBytes)}"
            : $"Download required · about {FormatBytes(SelectedSpeechModel.ApproximateSizeBytes)}";

    /// <summary>
    /// Gets or updates start call command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand StartCallCommand { get; }
    /// <summary>
    /// Gets or updates end call command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand EndCallCommand { get; }
    /// <summary>
    /// Gets or updates pause resume command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand PauseResumeCommand { get; }
    /// <summary>
    /// Gets or updates toggle mute command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ToggleMuteCommand { get; }
    /// <summary>
    /// Gets or updates toggle screen share command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ToggleScreenShareCommand { get; }
    /// <summary>
    /// Gets or updates send transcript command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand SendTranscriptCommand { get; }
    /// <summary>
    /// Gets or updates download speech model command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand DownloadSpeechModelCommand { get; }
    /// <summary>
    /// Gets or updates interrupt command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand InterruptCommand { get; }

    public CallPageViewModel(
        ICallCoordinator coordinator,
        IOllamaClient ollama,
        ISpeechModelManager speechModels)
    {
        _coordinator = coordinator;
        _ollama = ollama;
        _speechModels = speechModels;

        StartCallCommand = new AsyncRelayCommand(StartCallAsync, () => CanStart);
        EndCallCommand = new AsyncRelayCommand(EndCallAsync, () => IsActive);
        PauseResumeCommand = new AsyncRelayCommand(PauseResumeAsync, () => IsActive);
        ToggleMuteCommand = new AsyncRelayCommand(ToggleMuteAsync, () => IsActive);
        ToggleScreenShareCommand = new AsyncRelayCommand(ToggleScreenShareAsync, () => IsActive);
        SendTranscriptCommand = new AsyncRelayCommand(SendTranscriptAsync, () => CanSendText);
        DownloadSpeechModelCommand = new AsyncRelayCommand(
            DownloadSpeechModelAsync,
            () => SelectedSpeechModel is not null && !IsDownloadingSpeechModel);
        InterruptCommand = new AsyncRelayCommand(
            () => _coordinator.InterruptAsync(CancellationToken.None),
            () => IsActive && CallState is CallState.Thinking or CallState.Speaking);

        for (var index = 0; index < 20; index++)
            WaveformBars.Add(new WaveformBarViewModel(4));

        _coordinator.StateChanged += OnStateChanged;
        _coordinator.TranscriptChanged += OnTranscriptChanged;
        _coordinator.AudioLevelChanged += OnAudioLevelChanged;
        _coordinator.ScreenPreviewChanged += OnScreenPreviewChanged;
    }

    /// <summary>
    /// Performs initialize async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized || _disposed) return;
        await _initializationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized || _disposed) return;
            var modelsTask = LoadOllamaModelsAsync();
            var speechModelsTask = _speechModels.GetModelsAsync(CancellationToken.None);
            await Task.WhenAll(modelsTask, speechModelsTask).ConfigureAwait(false);

            await RunOnUiThreadAsync(() =>
            {
                Replace(InputDevices, _coordinator.Capabilities.InputDevices);
                Replace(OutputDevices, _coordinator.Capabilities.OutputDevices);
                Replace(Voices, _coordinator.Capabilities.Voices);
                Replace(AvailableSpeechModels, speechModelsTask.Result);
                SelectedInputDevice = InputDevices.FirstOrDefault(item => item.IsDefault) ?? InputDevices.FirstOrDefault();
                SelectedOutputDevice = OutputDevices.FirstOrDefault(item => item.IsDefault) ?? OutputDevices.FirstOrDefault();
                SelectedVoice = Voices.FirstOrDefault(item => item.IsDefault) ?? Voices.FirstOrDefault();
                SelectedSpeechModel = AvailableSpeechModels.FirstOrDefault(item => item.Size == SpeechModelSize.Base)
                    ?? AvailableSpeechModels.FirstOrDefault();
                _initialized = true;
                RaiseCapabilityProperties();
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RunOnUiThreadAsync(() => Status = $"Call setup could not load: {ex.Message}").ConfigureAwait(false);
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    /// <summary>
    /// Performs begin push to talk async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task BeginPushToTalkAsync()
    {
        if (!IsActive || IsPushToTalkPressed) return;
        try
        {
            IsPushToTalkPressed = true;
            await _coordinator.BeginPushToTalkAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RunOnUiThreadAsync(() => Status = ex.Message).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Performs end push to talk async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task EndPushToTalkAsync()
    {
        if (!IsPushToTalkPressed) return;
        try
        {
            IsPushToTalkPressed = false;
            await _coordinator.EndPushToTalkAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RunOnUiThreadAsync(() => Status = ex.Message).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Performs load ollama models async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<IReadOnlyList<ModelDescriptor>> LoadOllamaModelsAsync()
    {
        var models = await _ollama.GetModelsAsync(CancellationToken.None).ConfigureAwait(false);
        await RunOnUiThreadAsync(() =>
        {
            Replace(AvailableModels, models);
            SelectedModel ??= AvailableModels.FirstOrDefault();
            if (AvailableModels.Count == 0) Status = "No local Ollama models are installed.";
        }).ConfigureAwait(false);
        return models;
    }

    /// <summary>
    /// Performs start call async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task StartCallAsync()
    {
        if (SelectedModel is null) return;
        try
        {
            Transcript.Clear();
            _transcriptById.Clear();
            RaisePropertyChanged(nameof(HasTranscript));
            var options = new CallStartOptions(
                SelectedModel,
                InputMode,
                SelectedInputDevice?.Id,
                SelectedOutputDevice?.Id,
                SelectedVoice?.Id,
                EnableSpeechOutput);
            await _coordinator.StartAsync(
                options,
                SelectedSpeechModel?.IsInstalled == true ? SelectedSpeechModel : null,
                CancellationToken.None).ConfigureAwait(false);
            await RunOnUiThreadAsync(RaiseCallProperties).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RunOnUiThreadAsync(() => Status = $"Call could not start: {ex.Message}").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Performs end call async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task EndCallAsync()
    {
        try { await _coordinator.EndAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { await RunOnUiThreadAsync(() => Status = $"Call cleanup failed: {ex.Message}").ConfigureAwait(false); }
        await RunOnUiThreadAsync(RaiseCallProperties).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs pause resume async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private Task PauseResumeAsync() => RunCoordinatorActionAsync(
        IsPaused
            ? () => _coordinator.ResumeAsync(CancellationToken.None)
            : () => _coordinator.PauseAsync(CancellationToken.None));

    /// <summary>
    /// Performs toggle mute async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private Task ToggleMuteAsync() => RunCoordinatorActionAsync(
        () => _coordinator.SetMutedAsync(!IsMuted, CancellationToken.None));

    /// <summary>
    /// Performs toggle screen share async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private Task ToggleScreenShareAsync() => RunCoordinatorActionAsync(
        IsSharing
            ? () => _coordinator.StopScreenShareAsync(CancellationToken.None)
            : () => _coordinator.StartScreenShareAsync(CancellationToken.None));

    /// <summary>
    /// Runs run coordinator action async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private async Task RunCoordinatorActionAsync(Func<Task> action)
    {
        try { await action().ConfigureAwait(false); }
        catch (Exception ex) { await RunOnUiThreadAsync(() => Status = ex.Message).ConfigureAwait(false); }
        await RunOnUiThreadAsync(RaiseCallProperties).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs send transcript async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task SendTranscriptAsync()
    {
        var text = TypedTranscript.Trim();
        if (text.Length == 0) return;
        TypedTranscript = string.Empty;
        try { await _coordinator.SubmitTextAsync(text, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { await RunOnUiThreadAsync(() => Status = ex.Message).ConfigureAwait(false); }
    }

    /// <summary>
    /// Performs download speech model async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task DownloadSpeechModelAsync()
    {
        if (SelectedSpeechModel is null) return;
        var size = SelectedSpeechModel.Size;
        IsDownloadingSpeechModel = true;
        SpeechModelDownloadProgress = 0;
        try
        {
            var progress = new Progress<double>(value => SpeechModelDownloadProgress = value * 100);
            await _speechModels.DownloadAsync(size, progress, CancellationToken.None).ConfigureAwait(false);
            var models = await _speechModels.GetModelsAsync(CancellationToken.None).ConfigureAwait(false);
            await RunOnUiThreadAsync(() =>
            {
                Replace(AvailableSpeechModels, models);
                SelectedSpeechModel = AvailableSpeechModels.First(item => item.Size == size);
                Status = $"{SelectedSpeechModel.DisplayName} is ready.";
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RunOnUiThreadAsync(() => Status = $"Speech model download failed: {ex.Message}").ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiThreadAsync(() => IsDownloadingSpeechModel = false).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Handles the state changed event raised by the UI or runtime.
    /// </summary>
    private void OnStateChanged(object? sender, CallStateChangedEventArgs e) => RunOnUiThread(() =>
    {
        CallState = e.State;
        Status = e.Status;
        UpdateWaveformForState(e.State);
        RaiseCallProperties();
    });

    /// <summary>
    /// Handles the transcript changed event raised by the UI or runtime.
    /// </summary>
    private void OnTranscriptChanged(object? sender, CallTranscriptEventArgs e) => RunOnUiThread(() =>
    {
        if (!_transcriptById.TryGetValue(e.MessageId, out var item))
        {
            item = new CallTranscriptItemViewModel(
                e.MessageId,
                e.Role,
                e.Text,
                !e.IsFinal,
                DateTimeOffset.Now);
            _transcriptById.Add(e.MessageId, item);
            Transcript.Add(item);
        }
        else if (e.IsDelta)
        {
            item.Text += e.Text;
        }
        else
        {
            item.Text = e.Text;
        }
        item.IsPartial = !e.IsFinal;
        item.WasInterrupted = e.WasInterrupted;
        RaisePropertyChanged(nameof(HasTranscript));
    });

    /// <summary>
    /// Handles the audio level changed event raised by the UI or runtime.
    /// </summary>
    private void OnAudioLevelChanged(object? sender, CallAudioLevelEventArgs e) =>
        RunOnUiThread(() => UpdateWaveform(e.Level));

    /// <summary>
    /// Handles the screen preview changed event raised by the UI or runtime.
    /// </summary>
    private void OnScreenPreviewChanged(object? sender, ScreenShareSnapshotEventArgs e)
    {
        try
        {
            var bytes = Convert.FromBase64String(e.Snapshot.Base64Jpeg);
            using var stream = new MemoryStream(bytes, writable: false);
            var preview = new Bitmap(stream);
            RunOnUiThread(() => SetScreenPreview(preview));
        }
        catch (Exception)
        {
            // A malformed or incomplete preview must not affect the active call.
        }
    }

    /// <summary>
    /// Performs the raise call properties step owned by this component.
    /// </summary>
    private void RaiseCallProperties()
    {
        RaisePropertyChanged(nameof(IsActive));
        RaisePropertyChanged(nameof(IsNotActive));
        RaisePropertyChanged(nameof(IsMuted));
        RaisePropertyChanged(nameof(IsSharing));
        RaisePropertyChanged(nameof(CanStart));
        RaisePropertyChanged(nameof(CanSendText));
        RaisePropertyChanged(nameof(MuteLabel));
        RaisePropertyChanged(nameof(PauseLabel));
        RaisePropertyChanged(nameof(ShareLabel));
        if (!IsSharing) SetScreenPreview(null);
        StartCallCommand.RaiseCanExecuteChanged();
        EndCallCommand.RaiseCanExecuteChanged();
        PauseResumeCommand.RaiseCanExecuteChanged();
        ToggleMuteCommand.RaiseCanExecuteChanged();
        ToggleScreenShareCommand.RaiseCanExecuteChanged();
        SendTranscriptCommand.RaiseCanExecuteChanged();
        InterruptCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Performs the raise capability properties step owned by this component.
    /// </summary>
    private void RaiseCapabilityProperties()
    {
        RaisePropertyChanged(nameof(HasSpeechInput));
        RaisePropertyChanged(nameof(HasSpeechOutput));
        RaisePropertyChanged(nameof(CanShareScreen));
        RaisePropertyChanged(nameof(SpeechInputStatus));
        RaisePropertyChanged(nameof(SpeechOutputStatus));
        RaisePropertyChanged(nameof(ScreenShareStatus));
    }

    /// <summary>
    /// Performs the update waveform for state step owned by this component.
    /// </summary>
    private void UpdateWaveformForState(CallState state) =>
        UpdateWaveform(state is CallState.Listening or CallState.Transcribing or CallState.Speaking ? 0.35 : 0);

    /// <summary>
    /// Performs the update waveform step owned by this component.
    /// </summary>
    private void UpdateWaveform(double level)
    {
        for (var index = 0; index < WaveformBars.Count; index++)
        {
            var shape = 0.35 + Math.Abs(Math.Sin((index + 1) * 1.47)) * 0.65;
            WaveformBars[index].Height = 4 + (Math.Clamp(level, 0, 1) * 32 * shape);
        }
    }

    /// <summary>
    /// Performs the set screen preview step owned by this component.
    /// </summary>
    private void SetScreenPreview(Bitmap? preview)
    {
        if (_disposed)
        {
            preview?.Dispose();
            return;
        }
        if (ReferenceEquals(_screenPreview, preview)) return;
        var previous = _screenPreview;
        _screenPreview = preview;
        RaisePropertyChanged(nameof(ScreenPreview));
        RaisePropertyChanged(nameof(HasScreenPreview));
        previous?.Dispose();
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    /// <summary>
    /// Runs run on ui thread while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    /// <summary>
    /// Runs run on ui thread async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private static Task RunOnUiThreadAsync(Action action) =>
        Dispatcher.UIThread.CheckAccess()
            ? RunImmediately(action)
            : Dispatcher.UIThread.InvokeAsync(action).GetTask();

    /// <summary>
    /// Runs run immediately while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private static Task RunImmediately(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs the format bytes step owned by this component.
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        var megabytes = bytes / (1024d * 1024d);
        return megabytes >= 1024 ? $"{megabytes / 1024:0.0} GB" : $"{megabytes:0} MB";
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _coordinator.StateChanged -= OnStateChanged;
        _coordinator.TranscriptChanged -= OnTranscriptChanged;
        _coordinator.AudioLevelChanged -= OnAudioLevelChanged;
        _coordinator.ScreenPreviewChanged -= OnScreenPreviewChanged;
        var preview = _screenPreview;
        _screenPreview = null;
        preview?.Dispose();
        _initializationGate.Dispose();
    }
}

/// <summary>
/// Represents call transcript item view model and keeps its related state and behavior together.
/// </summary>
public sealed class CallTranscriptItemViewModel(
    Guid id,
    MessageRole role,
    string text,
    bool isPartial,
    DateTimeOffset timestamp) : ObservableObject
{
    /// <summary>
    /// Stores text locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _text = text;
    /// <summary>
    /// Stores is partial locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isPartial = isPartial;
    /// <summary>
    /// Stores was interrupted locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _wasInterrupted;

    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; } = id;
    /// <summary>
    /// Gets or updates role, the bindable or domain state represented by this property.
    /// </summary>
    public MessageRole Role { get; } = role;
    /// <summary>
    /// Reports whether is user is true for the current state.
    /// </summary>
    public bool IsUser => Role == MessageRole.User;
    /// <summary>
    /// Gets or updates speaker, the bindable or domain state represented by this property.
    /// </summary>
    public string Speaker => IsUser ? "You" : "Haven";
    /// <summary>
    /// Gets or updates time label, the bindable or domain state represented by this property.
    /// </summary>
    public string TimeLabel { get; } = timestamp.ToString("HH:mm");
    /// <summary>
    /// Gets or updates text, the bindable or domain state represented by this property.
    /// </summary>
    public string Text { get => _text; set => SetProperty(ref _text, value); }
    /// <summary>
    /// Reports whether is partial is true for the current state.
    /// </summary>
    public bool IsPartial { get => _isPartial; set => SetProperty(ref _isPartial, value); }
    /// <summary>
    /// Gets or updates was interrupted, the bindable or domain state represented by this property.
    /// </summary>
    public bool WasInterrupted { get => _wasInterrupted; set => SetProperty(ref _wasInterrupted, value); }
}

/// <summary>
/// Represents waveform bar view model and keeps its related state and behavior together.
/// </summary>
public sealed class WaveformBarViewModel(double height) : ObservableObject
{
    /// <summary>
    /// Stores height locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private double _height = height;
    /// <summary>
    /// Gets or updates height, the bindable or domain state represented by this property.
    /// </summary>
    public double Height { get => _height; set => SetProperty(ref _height, value); }
}
