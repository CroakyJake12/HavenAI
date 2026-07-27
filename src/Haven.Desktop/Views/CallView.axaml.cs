using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

/// <summary>
/// New Haven's call page. Owns its domain state directly and talks to
/// application services through constructor injection; no ViewModel participates.
/// </summary>
public sealed partial class CallView : UserControl, IDisposable
{
    private readonly ICallCoordinator _coordinator;
    private readonly IOllamaClient _ollama;
    private readonly ISpeechModelManager _speechModels;
    private readonly ObservableCollection<CallTranscriptItem> _transcript = [];
    private readonly Dictionary<Guid, CallTranscriptItem> _transcriptById = [];
    private readonly ObservableCollection<WaveformBar> _waveformBars = [];
    private ModelDescriptor? _selectedModel;
    private SpeechModelInfo? _selectedSpeechModel;
    private CallAudioDevice? _selectedInputDevice;
    private CallVoice? _selectedVoice;
    private CallInputMode _inputMode = CallInputMode.HandsFree;
    private bool _enableSpeechOutput = true;
    private CallState _callState = CallState.Idle;
    private bool _isPushToTalkPressed;
    private Bitmap? _screenPreview;
    private bool _initialized;
    private bool _disposed;

    public CallView()
    {
        _coordinator = App.Services?.GetRequiredService<ICallCoordinator>() ?? throw new InvalidOperationException("Call coordinator not available.");
        _ollama = App.Services?.GetRequiredService<IOllamaClient>() ?? throw new InvalidOperationException("Ollama client not available.");
        _speechModels = App.Services?.GetRequiredService<ISpeechModelManager>() ?? throw new InvalidOperationException("Speech model manager not available.");

        InitializeComponent();

        for (var index = 0; index < 20; index++)
            _waveformBars.Add(new WaveformBar(4));

        WaveformBars.ItemsSource = _waveformBars;
        TranscriptScroller.ItemsSource = _transcript;

        _coordinator.StateChanged += OnStateChanged;
        _coordinator.TranscriptChanged += OnTranscriptChanged;
        _coordinator.AudioLevelChanged += OnAudioLevelChanged;
        _coordinator.ScreenPreviewChanged += OnScreenPreviewChanged;

        WireEvents();
    }

    private void WireEvents()
    {
        StartCallButton.Click += async (_, _) => await StartCallAsync();
        EndCallButton.Click += async (_, _) => await EndCallAsync();
        PauseButton.Click += async (_, _) => await PauseResumeAsync();
        MuteButton.Click += async (_, _) => await ToggleMuteAsync();
        ShareButton.Click += async (_, _) => await ToggleScreenShareAsync();
        SendTranscriptButton.Click += async (_, _) => await SendTranscriptAsync();
        InterruptButton.Click += async (_, _) => await InterruptAsync();
        DownloadSpeechModelButton.Click += async (_, _) => await DownloadSpeechModelAsync();

        ModelSelector.SelectionChanged += (_, e) =>
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is ModelDescriptor model)
                _selectedModel = model;
            UpdateButtonStates();
        };

        InputModeSelector.SelectionChanged += (_, e) =>
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is CallInputMode mode)
                _inputMode = mode;
        };

        InputDeviceSelector.SelectionChanged += (_, e) =>
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is CallAudioDevice device)
                _selectedInputDevice = device;
        };

        SpeechModelSelector.SelectionChanged += (_, e) =>
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is SpeechModelInfo model)
            {
                _selectedSpeechModel = model;
                SpeechModelSetupPanel.IsVisible = !model.IsInstalled;
                SpeechModelStatus.Text = model.IsInstalled
                    ? $"Installed · {FormatBytes(model.ApproximateSizeBytes)}"
                    : $"Download required · about {FormatBytes(model.ApproximateSizeBytes)}";
            }
        };

        VoiceSelector.SelectionChanged += (_, e) =>
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is CallVoice voice)
            {
                _selectedVoice = voice;
                SelectedVoiceDescription.Text = voice.Id.StartsWith("kokoro:", StringComparison.OrdinalIgnoreCase)
                    ? "Neural, expressive and fully local. The compact voice model downloads once on first preview."
                    : "Windows system voice. Instant and offline, but less conversational than Haven Neural.";
            }
        };

        EnableSpeechOutputCheckBox.IsCheckedChanged += (_, _) =>
            _enableSpeechOutput = EnableSpeechOutputCheckBox.IsChecked == true;

        TypedTranscriptBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                e.Handled = true;
                _ = SendTranscriptAsync();
            }
        };
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var modelsTask = _ollama.GetModelsAsync(CancellationToken.None);
            var speechModelsTask = _speechModels.GetModelsAsync(CancellationToken.None);
            await Task.WhenAll(modelsTask, speechModelsTask);

            var models = await modelsTask;
            var speechModels = await speechModelsTask;

            ModelSelector.ItemsSource = models;
            _selectedModel = models.FirstOrDefault();
            if (_selectedModel is not null) ModelSelector.SelectedItem = _selectedModel;

            InputModeSelector.ItemsSource = Enum.GetValues<CallInputMode>();
            InputModeSelector.SelectedItem = _inputMode;

            InputDeviceSelector.ItemsSource = _coordinator.Capabilities.InputDevices;
            _selectedInputDevice = _coordinator.Capabilities.InputDevices.FirstOrDefault(d => d.IsDefault)
                ?? _coordinator.Capabilities.InputDevices.FirstOrDefault();
            if (_selectedInputDevice is not null) InputDeviceSelector.SelectedItem = _selectedInputDevice;

            SpeechInputStatus.Text = _coordinator.Capabilities.HasSpeechInput
                ? "Local microphone transcription ready"
                : _coordinator.Capabilities.SpeechInputUnavailableReason ?? "Microphone transcription unavailable";

            SpeechModelSelector.ItemsSource = speechModels;
            _selectedSpeechModel = speechModels.FirstOrDefault(m => m.Size == SpeechModelSize.Base)
                ?? speechModels.FirstOrDefault();
            if (_selectedSpeechModel is not null)
            {
                SpeechModelSelector.SelectedItem = _selectedSpeechModel;
                SpeechModelSetupPanel.IsVisible = !_selectedSpeechModel.IsInstalled;
                SpeechModelStatus.Text = _selectedSpeechModel.IsInstalled
                    ? $"Installed · {FormatBytes(_selectedSpeechModel.ApproximateSizeBytes)}"
                    : $"Download required · about {FormatBytes(_selectedSpeechModel.ApproximateSizeBytes)}";
            }

            VoiceSelector.ItemsSource = _coordinator.Capabilities.Voices;
            _selectedVoice = _coordinator.Capabilities.Voices.FirstOrDefault(v => v.IsDefault)
                ?? _coordinator.Capabilities.Voices.FirstOrDefault();
            if (_selectedVoice is not null) VoiceSelector.SelectedItem = _selectedVoice;

            SpeechOutputStatus.Text = _coordinator.Capabilities.HasSpeechOutput
                ? "Haven Neural and Windows voicebanks ready"
                : _coordinator.Capabilities.SpeechOutputUnavailableReason ?? "Speech output unavailable";

            ScreenShareStatus.Text = _coordinator.Capabilities.CanShareScreen
                ? "Windows screen picker ready"
                : _coordinator.Capabilities.ScreenShareUnavailableReason ?? "Screen sharing unavailable";

            _initialized = true;
            UpdateButtonStates();
        }
        catch (Exception ex)
        {
            Status.Text = $"Call setup could not load: {ex.Message}";
        }
    }

    private async Task StartCallAsync()
    {
        if (_selectedModel is null) return;
        try
        {
            _transcript.Clear();
            _transcriptById.Clear();
            UpdateTranscriptVisibility();
            var options = new CallStartOptions(
                _selectedModel,
                _inputMode,
                _selectedInputDevice?.Id,
                null,
                _selectedVoice?.Id,
                _enableSpeechOutput);
            await _coordinator.StartAsync(
                options,
                _selectedSpeechModel?.IsInstalled == true ? _selectedSpeechModel : null,
                CancellationToken.None);
            UpdateButtonStates();
        }
        catch (Exception ex)
        {
            Status.Text = $"Call could not start: {ex.Message}";
        }
    }

    private async Task EndCallAsync()
    {
        try { await _coordinator.EndAsync(CancellationToken.None); }
        catch (Exception ex) { Status.Text = $"Call cleanup failed: {ex.Message}"; }
        UpdateButtonStates();
    }

    private async Task PauseResumeAsync()
    {
        try
        {
            if (_callState == CallState.Paused)
                await _coordinator.ResumeAsync(CancellationToken.None);
            else
                await _coordinator.PauseAsync(CancellationToken.None);
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        UpdateButtonStates();
    }

    private async Task ToggleMuteAsync()
    {
        try { await _coordinator.SetMutedAsync(!_coordinator.IsMuted, CancellationToken.None); }
        catch (Exception ex) { Status.Text = ex.Message; }
        UpdateButtonStates();
    }

    private async Task ToggleScreenShareAsync()
    {
        try
        {
            if (_coordinator.IsScreenSharing)
                await _coordinator.StopScreenShareAsync(CancellationToken.None);
            else
                await _coordinator.StartScreenShareAsync(CancellationToken.None);
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        UpdateButtonStates();
    }

    private async Task SendTranscriptAsync()
    {
        var text = TypedTranscriptBox.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        TypedTranscriptBox.Text = string.Empty;
        try { await _coordinator.SubmitTextAsync(text, CancellationToken.None); }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private async Task InterruptAsync()
    {
        try { await _coordinator.InterruptAsync(CancellationToken.None); }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private async Task DownloadSpeechModelAsync()
    {
        if (_selectedSpeechModel is null) return;
        var size = _selectedSpeechModel.Size;
        SpeechModelProgress.IsVisible = true;
        SpeechModelProgress.Value = 0;
        DownloadSpeechModelButton.IsEnabled = false;
        try
        {
            var progress = new Progress<double>(value =>
                Dispatcher.UIThread.Post(() => SpeechModelProgress.Value = value * 100));
            await _speechModels.DownloadAsync(size, progress, CancellationToken.None);
            var models = await _speechModels.GetModelsAsync(CancellationToken.None);
            SpeechModelSelector.ItemsSource = models;
            _selectedSpeechModel = models.First(m => m.Size == size);
            SpeechModelSelector.SelectedItem = _selectedSpeechModel;
            SpeechModelSetupPanel.IsVisible = false;
            Status.Text = $"{_selectedSpeechModel.DisplayName} is ready.";
        }
        catch (Exception ex)
        {
            Status.Text = $"Speech model download failed: {ex.Message}";
        }
        finally
        {
            SpeechModelProgress.IsVisible = false;
            DownloadSpeechModelButton.IsEnabled = true;
        }
    }

    public async void OnPushToTalkPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_coordinator.IsActive || _isPushToTalkPressed) return;
        _isPushToTalkPressed = true;
        PushToTalkButton.Content = "Release to send";
        try { await _coordinator.BeginPushToTalkAsync(CancellationToken.None); }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    public async void OnPushToTalkReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPushToTalkPressed) return;
        _isPushToTalkPressed = false;
        PushToTalkButton.Content = "Hold to talk";
        try { await _coordinator.EndPushToTalkAsync(CancellationToken.None); }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    public void OnPushToTalkCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isPushToTalkPressed) return;
        _isPushToTalkPressed = false;
        PushToTalkButton.Content = "Hold to talk";
        _ = _coordinator.EndPushToTalkAsync(CancellationToken.None);
    }

    private void OnStateChanged(object? sender, CallStateChangedEventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            _callState = e.State;
            Status.Text = e.Status;
            StateLabel.Text = e.State.ToString();
            UpdateWaveformForState(e.State);
            UpdateButtonStates();
        });

    private void OnTranscriptChanged(object? sender, CallTranscriptEventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!_transcriptById.TryGetValue(e.MessageId, out var item))
            {
                item = new CallTranscriptItem(e.MessageId, e.Role, e.Text, !e.IsFinal, DateTimeOffset.Now);
                _transcriptById.Add(e.MessageId, item);
                _transcript.Add(item);
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
            UpdateTranscriptVisibility();
        });

    private void OnAudioLevelChanged(object? sender, CallAudioLevelEventArgs e) =>
        Dispatcher.UIThread.Post(() => UpdateWaveform(e.Level));

    private void OnScreenPreviewChanged(object? sender, ScreenShareSnapshotEventArgs e)
    {
        try
        {
            var bytes = Convert.FromBase64String(e.Snapshot.Base64Jpeg);
            using var stream = new MemoryStream(bytes, writable: false);
            var preview = new Bitmap(stream);
            Dispatcher.UIThread.Post(() => SetScreenPreview(preview));
        }
        catch { }
    }

    private void UpdateButtonStates()
    {
        var isActive = _coordinator.IsActive;
        var isMuted = _coordinator.IsMuted;
        var isSharing = _coordinator.IsScreenSharing;
        var isPaused = _callState == CallState.Paused;

        StartCallButton.IsEnabled = !isActive && _selectedModel is not null;
        EndCallButton.IsEnabled = isActive;
        PauseButton.IsEnabled = isActive;
        PauseButton.Content = isPaused ? "Resume" : "Pause";
        MuteButton.IsEnabled = isActive;
        MuteButton.Content = isMuted ? "Unmute" : "Mute";
        ShareButton.IsEnabled = isActive;
        ShareButton.Content = isSharing ? "Stop sharing" : "Share screen";
        PushToTalkButton.IsEnabled = isActive;
        SendTranscriptButton.IsEnabled = isActive && !string.IsNullOrWhiteSpace(TypedTranscriptBox.Text);
        InterruptButton.IsEnabled = isActive && _callState is CallState.Thinking or CallState.Speaking;

        ModelSelector.IsEnabled = !isActive;
        InputModeSelector.IsEnabled = !isActive;
        InputDeviceSelector.IsEnabled = !isActive;
        SpeechModelSelector.IsEnabled = !isActive;
        VoiceSelector.IsEnabled = !isActive;
        EnableSpeechOutputCheckBox.IsEnabled = !isActive;

        ScreenSharePanel.IsVisible = isSharing;
        if (!isSharing) SetScreenPreview(null);
    }

    private void UpdateTranscriptVisibility()
    {
        var hasTranscript = _transcript.Count > 0;
        EmptyTranscriptPanel.IsVisible = !hasTranscript;
        TranscriptScroller.IsVisible = hasTranscript;
    }

    private void UpdateWaveformForState(CallState state) =>
        UpdateWaveform(state is CallState.Listening or CallState.Transcribing or CallState.Speaking ? 0.35 : 0);

    private void UpdateWaveform(double level)
    {
        for (var index = 0; index < _waveformBars.Count; index++)
        {
            var shape = 0.35 + Math.Abs(Math.Sin((index + 1) * 1.47)) * 0.65;
            _waveformBars[index].Height = 4 + (Math.Clamp(level, 0, 1) * 32 * shape);
        }
    }

    private void SetScreenPreview(Bitmap? preview)
    {
        if (_disposed) { preview?.Dispose(); return; }
        if (ReferenceEquals(_screenPreview, preview)) return;
        var previous = _screenPreview;
        _screenPreview = preview;
        ScreenPreview.Source = preview;
        ScreenPreviewBorder.IsVisible = preview is not null;
        previous?.Dispose();
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
    }
}

/// <summary>
/// Represents a transcript item in the call.
/// </summary>
public sealed class CallTranscriptItem(
    Guid id,
    MessageRole role,
    string text,
    bool isPartial,
    DateTimeOffset timestamp)
{
    public Guid Id { get; } = id;
    public MessageRole Role { get; } = role;
    public bool IsUser => Role == MessageRole.User;
    public string Speaker => IsUser ? "You" : "Haven";
    public string TimeLabel { get; } = timestamp.ToString("HH:mm");
    public string Text { get; set; } = text;
    public bool IsPartial { get; set; } = isPartial;
    public bool WasInterrupted { get; set; }
}

/// <summary>
/// Represents a waveform bar in the call visualization.
/// </summary>
public sealed class WaveformBar(double height)
{
    public double Height { get; set; } = height;
}
