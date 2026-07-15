using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed class CallPageViewModel : ObservableObject, IDisposable
{
    private readonly ICallCoordinator _coordinator;
    private readonly IOllamaClient _ollama;
    private readonly ISpeechModelManager _speechModels;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly Dictionary<Guid, CallTranscriptItemViewModel> _transcriptById = [];
    private bool _initialized;
    private bool _disposed;

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

    private CallAudioDevice? _selectedInputDevice;
    public CallAudioDevice? SelectedInputDevice
    {
        get => _selectedInputDevice;
        set => SetProperty(ref _selectedInputDevice, value);
    }

    private CallAudioDevice? _selectedOutputDevice;
    public CallAudioDevice? SelectedOutputDevice
    {
        get => _selectedOutputDevice;
        set => SetProperty(ref _selectedOutputDevice, value);
    }

    private CallVoice? _selectedVoice;
    public CallVoice? SelectedVoice
    {
        get => _selectedVoice;
        set => SetProperty(ref _selectedVoice, value);
    }

    private CallInputMode _inputMode = CallInputMode.HandsFree;
    public CallInputMode InputMode
    {
        get => _inputMode;
        set => SetProperty(ref _inputMode, value);
    }

    private bool _enableSpeechOutput = true;
    public bool EnableSpeechOutput
    {
        get => _enableSpeechOutput;
        set => SetProperty(ref _enableSpeechOutput, value);
    }

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

    private string _status = "Ready to call";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

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

    private double _speechModelDownloadProgress;
    public double SpeechModelDownloadProgress
    {
        get => _speechModelDownloadProgress;
        private set => SetProperty(ref _speechModelDownloadProgress, value);
    }

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

    private Bitmap? _screenPreview;
    public Bitmap? ScreenPreview => _screenPreview;
    public bool HasScreenPreview => _screenPreview is not null;

    public ObservableCollection<ModelDescriptor> AvailableModels { get; } = [];
    public ObservableCollection<SpeechModelInfo> AvailableSpeechModels { get; } = [];
    public ObservableCollection<CallAudioDevice> InputDevices { get; } = [];
    public ObservableCollection<CallAudioDevice> OutputDevices { get; } = [];
    public ObservableCollection<CallVoice> Voices { get; } = [];
    public ObservableCollection<CallTranscriptItemViewModel> Transcript { get; } = [];
    public ObservableCollection<WaveformBarViewModel> WaveformBars { get; } = [];

    public IReadOnlyList<CallInputMode> InputModes { get; } = Enum.GetValues<CallInputMode>();

    public bool IsActive => _coordinator.IsActive;
    public bool IsNotActive => !IsActive;
    public bool IsPaused => CallState == CallState.Paused;
    public bool IsMuted => _coordinator.IsMuted;
    public bool IsSharing => _coordinator.IsScreenSharing;
    public bool HasSpeechInput => _coordinator.Capabilities.HasSpeechInput;
    public bool HasSpeechOutput => _coordinator.Capabilities.HasSpeechOutput;
    public bool CanShareScreen => _coordinator.Capabilities.CanShareScreen;
    public bool HasTranscript => Transcript.Count > 0;
    public bool CanStart => !IsActive && SelectedModel is not null;
    public bool CanSendText => IsActive && !string.IsNullOrWhiteSpace(TypedTranscript);
    public string StateLabel => CallState.ToString();
    public string MuteLabel => IsMuted ? "Unmute" : "Mute";
    public string PauseLabel => IsPaused ? "Resume" : "Pause";
    public string ShareLabel => IsSharing ? "Stop sharing" : "Share screen";
    public string PushToTalkLabel => IsPushToTalkPressed ? "Release to send" : "Hold to talk";
    public string SpeechInputStatus => HasSpeechInput
        ? "Local microphone transcription ready"
        : _coordinator.Capabilities.SpeechInputUnavailableReason ?? "Microphone transcription unavailable";
    public string SpeechOutputStatus => HasSpeechOutput
        ? "Local speech output ready"
        : _coordinator.Capabilities.SpeechOutputUnavailableReason ?? "Speech output unavailable";
    public string ScreenShareStatus => CanShareScreen
        ? "Windows screen picker ready"
        : _coordinator.Capabilities.ScreenShareUnavailableReason ?? "Screen sharing unavailable";
    public bool ShowSpeechModelSetup => SelectedSpeechModel?.IsInstalled == false;
    public string SpeechModelStatus => SelectedSpeechModel is null
        ? "Choose a local speech model"
        : SelectedSpeechModel.IsInstalled
            ? $"Installed · {FormatBytes(SelectedSpeechModel.ApproximateSizeBytes)}"
            : $"Download required · about {FormatBytes(SelectedSpeechModel.ApproximateSizeBytes)}";

    public AsyncRelayCommand StartCallCommand { get; }
    public AsyncRelayCommand EndCallCommand { get; }
    public AsyncRelayCommand PauseResumeCommand { get; }
    public AsyncRelayCommand ToggleMuteCommand { get; }
    public AsyncRelayCommand ToggleScreenShareCommand { get; }
    public AsyncRelayCommand SendTranscriptCommand { get; }
    public AsyncRelayCommand DownloadSpeechModelCommand { get; }
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

    private async Task EndCallAsync()
    {
        try { await _coordinator.EndAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { await RunOnUiThreadAsync(() => Status = $"Call cleanup failed: {ex.Message}").ConfigureAwait(false); }
        await RunOnUiThreadAsync(RaiseCallProperties).ConfigureAwait(false);
    }

    private Task PauseResumeAsync() => RunCoordinatorActionAsync(
        IsPaused
            ? () => _coordinator.ResumeAsync(CancellationToken.None)
            : () => _coordinator.PauseAsync(CancellationToken.None));

    private Task ToggleMuteAsync() => RunCoordinatorActionAsync(
        () => _coordinator.SetMutedAsync(!IsMuted, CancellationToken.None));

    private Task ToggleScreenShareAsync() => RunCoordinatorActionAsync(
        IsSharing
            ? () => _coordinator.StopScreenShareAsync(CancellationToken.None)
            : () => _coordinator.StartScreenShareAsync(CancellationToken.None));

    private async Task RunCoordinatorActionAsync(Func<Task> action)
    {
        try { await action().ConfigureAwait(false); }
        catch (Exception ex) { await RunOnUiThreadAsync(() => Status = ex.Message).ConfigureAwait(false); }
        await RunOnUiThreadAsync(RaiseCallProperties).ConfigureAwait(false);
    }

    private async Task SendTranscriptAsync()
    {
        var text = TypedTranscript.Trim();
        if (text.Length == 0) return;
        TypedTranscript = string.Empty;
        try { await _coordinator.SubmitTextAsync(text, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { await RunOnUiThreadAsync(() => Status = ex.Message).ConfigureAwait(false); }
    }

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

    private void OnStateChanged(object? sender, CallStateChangedEventArgs e) => RunOnUiThread(() =>
    {
        CallState = e.State;
        Status = e.Status;
        UpdateWaveformForState(e.State);
        RaiseCallProperties();
    });

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

    private void OnAudioLevelChanged(object? sender, CallAudioLevelEventArgs e) =>
        RunOnUiThread(() => UpdateWaveform(e.Level));

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

    private void RaiseCapabilityProperties()
    {
        RaisePropertyChanged(nameof(HasSpeechInput));
        RaisePropertyChanged(nameof(HasSpeechOutput));
        RaisePropertyChanged(nameof(CanShareScreen));
        RaisePropertyChanged(nameof(SpeechInputStatus));
        RaisePropertyChanged(nameof(SpeechOutputStatus));
        RaisePropertyChanged(nameof(ScreenShareStatus));
    }

    private void UpdateWaveformForState(CallState state) =>
        UpdateWaveform(state is CallState.Listening or CallState.Transcribing or CallState.Speaking ? 0.35 : 0);

    private void UpdateWaveform(double level)
    {
        for (var index = 0; index < WaveformBars.Count; index++)
        {
            var shape = 0.35 + Math.Abs(Math.Sin((index + 1) * 1.47)) * 0.65;
            WaveformBars[index].Height = 4 + (Math.Clamp(level, 0, 1) * 32 * shape);
        }
    }

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

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    private static Task RunOnUiThreadAsync(Action action) =>
        Dispatcher.UIThread.CheckAccess()
            ? RunImmediately(action)
            : Dispatcher.UIThread.InvokeAsync(action).GetTask();

    private static Task RunImmediately(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private static string FormatBytes(long bytes)
    {
        var megabytes = bytes / (1024d * 1024d);
        return megabytes >= 1024 ? $"{megabytes / 1024:0.0} GB" : $"{megabytes:0} MB";
    }

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

public sealed class CallTranscriptItemViewModel(
    Guid id,
    MessageRole role,
    string text,
    bool isPartial,
    DateTimeOffset timestamp) : ObservableObject
{
    private string _text = text;
    private bool _isPartial = isPartial;
    private bool _wasInterrupted;

    public Guid Id { get; } = id;
    public MessageRole Role { get; } = role;
    public bool IsUser => Role == MessageRole.User;
    public string Speaker => IsUser ? "You" : "Haven";
    public string TimeLabel { get; } = timestamp.ToString("HH:mm");
    public string Text { get => _text; set => SetProperty(ref _text, value); }
    public bool IsPartial { get => _isPartial; set => SetProperty(ref _isPartial, value); }
    public bool WasInterrupted { get => _wasInterrupted; set => SetProperty(ref _wasInterrupted, value); }
}

public sealed class WaveformBarViewModel(double height) : ObservableObject
{
    private double _height = height;
    public double Height { get => _height; set => SetProperty(ref _height, value); }
}
