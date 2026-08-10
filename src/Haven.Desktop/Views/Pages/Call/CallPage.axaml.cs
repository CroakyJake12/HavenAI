using System.Collections.ObjectModel;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;
using Haven.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Pages.Call;

/// <summary>
/// Call page. Directly accesses ICallCoordinator and speech services.
/// All pointer events are wired through the HavenEventBus.
/// </summary>
public sealed partial class CallPage : UserControl
{
    private readonly HavenEventBus _bus;
    private readonly ICallCoordinator _coordinator;
    private readonly IOllamaClient _ollama;
    private readonly ISpeechModelManager _speechModels;
    private readonly VoiceProfileCatalog _voiceProfiles;
    private readonly UserPreferencesService _preferences;

    private readonly ObservableCollection<ModelDescriptor> _models = [];
    private readonly ObservableCollection<SpeechModelInfo> _speechModelsList = [];
    private readonly ObservableCollection<CallAudioDevice> _inputDevices = [];
    private readonly ObservableCollection<CallAudioDevice> _outputDevices = [];
    private readonly ObservableCollection<CallVoice> _voices = [];
    private readonly List<TranscriptEntry> _transcript = [];
    private readonly Dictionary<Guid, TranscriptEntry> _transcriptById = [];
    private readonly List<Border> _waveformBars = [];

    private ModelDescriptor? _selectedModel;
    private SpeechModelInfo? _selectedSpeechModel;
    private CallAudioDevice? _selectedInputDevice;
    private CallAudioDevice? _selectedOutputDevice;
    private CallVoice? _selectedVoice;
    private VoiceProfile? _selectedVoiceProfile;
    private CallInputMode _inputMode = CallInputMode.HandsFree;
    private bool _enableSpeechOutput = true;
    private bool _initialized;
    private bool _isActive;
    private bool _isMuted;
    private bool _isPaused;

    public CallPage(
        HavenEventBus bus,
        ICallCoordinator coordinator,
        IOllamaClient ollama,
        ISpeechModelManager speechModels,
        VoiceProfileCatalog voiceProfiles,
        UserPreferencesService preferences)
    {
        _bus = bus;
        _coordinator = coordinator;
        _ollama = ollama;
        _speechModels = speechModels;
        _voiceProfiles = voiceProfiles;
        _preferences = preferences;

        InitializeComponent();
        CreateWaveformBars();
        WireEvents();

        _coordinator.StateChanged += OnStateChanged;
        _coordinator.TranscriptChanged += OnTranscriptChanged;
        _coordinator.AudioLevelChanged += OnAudioLevelChanged;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (_initialized) return;
        try
        {
            var modelsTask = _ollama.GetModelsAsync(CancellationToken.None);
            var speechModelsTask = _speechModels.GetModelsAsync(CancellationToken.None);
            await Task.WhenAll(modelsTask, speechModelsTask);

            foreach (var model in modelsTask.Result)
                _models.Add(model);
            foreach (var sm in speechModelsTask.Result)
                _speechModelsList.Add(sm);

            foreach (var device in _coordinator.Capabilities.InputDevices)
                _inputDevices.Add(device);
            foreach (var device in _coordinator.Capabilities.OutputDevices)
                _outputDevices.Add(device);
            foreach (var voice in _coordinator.Capabilities.Voices)
                _voices.Add(voice);

            _selectedModel = _models.FirstOrDefault();
            _selectedSpeechModel = _speechModelsList.FirstOrDefault(s => s.Size == SpeechModelSize.Base)
                ?? _speechModelsList.FirstOrDefault();
            _selectedInputDevice = _inputDevices.FirstOrDefault(d => d.IsDefault) ?? _inputDevices.FirstOrDefault();
            _selectedOutputDevice = _outputDevices.FirstOrDefault(d => d.IsDefault) ?? _outputDevices.FirstOrDefault();
            _selectedVoice = _voices.FirstOrDefault(v => v.IsDefault) ?? _voices.FirstOrDefault();
            var profiles = _voiceProfiles.GetAll()
                .Concat(_preferences.CustomVoiceProfiles)
                .GroupBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .OrderBy(profile => profile.Name)
                .ToArray();
            _selectedVoiceProfile = profiles.FirstOrDefault(profile => profile.Id == "general") ?? profiles.FirstOrDefault();

            ModelCombo.ItemsSource = _models;
            ModelCombo.ItemTemplate = new FuncDataTemplate<ModelDescriptor>((m, _) => new TextBlock { Text = m?.Name });
            ModelCombo.SelectionChanged += (_, _) =>
            {
                _selectedModel = ModelCombo.SelectedItem as ModelDescriptor;
                UpdateStartButton();
            };
            ModelCombo.SelectedItem = _selectedModel;

            InputModeCombo.ItemsSource = Enum.GetValues<CallInputMode>();
            InputModeCombo.SelectedItem = _inputMode;
            InputModeCombo.SelectionChanged += (_, _) =>
            {
                if (InputModeCombo.SelectedItem is CallInputMode mode) _inputMode = mode;
            };

            VoiceProfileCombo.ItemsSource = profiles;
            VoiceProfileCombo.ItemTemplate = new FuncDataTemplate<VoiceProfile>((profile, _) => new TextBlock { Text = profile?.Name });
            VoiceProfileCombo.SelectionChanged += (_, _) =>
            {
                _selectedVoiceProfile = VoiceProfileCombo.SelectedItem as VoiceProfile;
                VoiceProfileDescription.Text = _selectedVoiceProfile?.Description ?? string.Empty;
                UpdateVoiceProfileButtons();
            };
            VoiceProfileCombo.SelectedItem = _selectedVoiceProfile;

            NewVoiceProfileButton.Click += (_, _) => BeginVoiceProfileEdit(null);
            EditVoiceProfileButton.Click += (_, _) =>
            {
                if (_selectedVoiceProfile is { IsBuiltIn: false } profile) BeginVoiceProfileEdit(profile);
            };
            DeleteVoiceProfileButton.Click += (_, _) =>
            {
                if (_selectedVoiceProfile is { IsBuiltIn: false } profile)
                {
                    _preferences.RemoveCustomVoiceProfile(profile.Id);
                    RefreshVoiceProfiles();
                }
            };
            SaveVoiceProfileButton.Click += (_, _) => SaveVoiceProfile();
            CancelVoiceProfileButton.Click += (_, _) => VoiceProfileEditor.IsVisible = false;
            UpdateVoiceProfileButtons();

            InputDeviceCombo.ItemsSource = _inputDevices;
            InputDeviceCombo.ItemTemplate = new FuncDataTemplate<CallAudioDevice>((d, _) => new TextBlock { Text = d?.Name });
            InputDeviceCombo.SelectionChanged += (_, _) => _selectedInputDevice = InputDeviceCombo.SelectedItem as CallAudioDevice;
            InputDeviceCombo.SelectedItem = _selectedInputDevice;

            SpeechModelCombo.ItemsSource = _speechModelsList;
            SpeechModelCombo.ItemTemplate = new FuncDataTemplate<SpeechModelInfo>((s, _) => new TextBlock { Text = s?.DisplayName });
            SpeechModelCombo.SelectionChanged += (_, _) =>
            {
                _selectedSpeechModel = SpeechModelCombo.SelectedItem as SpeechModelInfo;
                UpdateSpeechModelStatus();
            };
            SpeechModelCombo.SelectedItem = _selectedSpeechModel;

            VoiceCombo.ItemsSource = _voices;
            VoiceCombo.ItemTemplate = new FuncDataTemplate<CallVoice>((v, _) => new TextBlock { Text = v?.Name });
            VoiceCombo.SelectionChanged += (_, _) =>
            {
                _selectedVoice = VoiceCombo.SelectedItem as CallVoice;
                UpdateVoiceDescription();
            };
            VoiceCombo.SelectedItem = _selectedVoice;

            SpeechOutputCheck.IsChecked = _enableSpeechOutput;
            SpeechOutputCheck.PointerReleased += (_, _) => _enableSpeechOutput = SpeechOutputCheck.IsChecked == true;

            SpeechInputStatus.Text = _coordinator.Capabilities.HasSpeechInput
                ? "Local microphone transcription ready"
                : _coordinator.Capabilities.SpeechInputUnavailableReason ?? "Unavailable";
            SpeechOutputStatus.Text = _coordinator.Capabilities.HasSpeechOutput
                ? "Haven Neural and Windows voicebanks ready"
                : _coordinator.Capabilities.SpeechOutputUnavailableReason ?? "Unavailable";
            ScreenShareStatus.Text = _coordinator.Capabilities.CanShareScreen
                ? "Windows screen picker ready"
                : _coordinator.Capabilities.ScreenShareUnavailableReason ?? "Unavailable";

            UpdateSpeechModelStatus();
            UpdateVoiceDescription();
            _initialized = true;
            UpdateStartButton();
        }
        catch (Exception ex)
        {
            TranscriptStatus.Text = $"Setup failed: {ex.Message}";
        }
    }

    private void WireEvents()
    {
        // Start call
        _bus.RegisterElement("Call.Controls.StartClick", StartCallButton);
        _bus.WirePointerEvents("Call.Controls.StartClick", StartCallButton);
        StartCallButton.Click += async (_, _) =>
        {
            _bus.Fire("Call.Controls.StartClick");
            await StartCallAsync();
        };

        // End call
        _bus.RegisterElement("Call.Controls.EndClick", EndCallButton);
        _bus.WirePointerEvents("Call.Controls.EndClick", EndCallButton);
        EndCallButton.Click += async (_, _) =>
        {
            _bus.Fire("Call.Controls.EndClick");
            await EndCallAsync();
        };

        // Mute
        _bus.RegisterElement("Call.Controls.MuteClick", MuteButton);
        _bus.WirePointerEvents("Call.Controls.MuteClick", MuteButton);
        MuteButton.Click += async (_, _) =>
        {
            _bus.Fire("Call.Controls.MuteClick");
            await ToggleMuteAsync();
        };

        // Pause
        _bus.RegisterElement("Call.Controls.PauseClick", PauseButton);
        _bus.WirePointerEvents("Call.Controls.PauseClick", PauseButton);
        PauseButton.Click += async (_, _) =>
        {
            _bus.Fire("Call.Controls.PauseClick");
            await PauseResumeAsync();
        };

        // Interrupt
        _bus.RegisterElement("Call.Controls.InterruptClick", InterruptButton);
        _bus.WirePointerEvents("Call.Controls.InterruptClick", InterruptButton);
        InterruptButton.Click += async (_, _) =>
        {
            _bus.Fire("Call.Controls.InterruptClick");
            await _coordinator.InterruptAsync(CancellationToken.None);
        };

        // Push to talk
        _bus.RegisterElement("Call.Controls.PushToTalk", PushToTalkButton);
        _bus.WirePointerEvents("Call.Controls.PushToTalk", PushToTalkButton);
        PushToTalkButton.PointerPressed += async (_, e) =>
        {
            if (!_isActive) return;
            e.Pointer.Capture(PushToTalkButton);
            e.Handled = true;
            _bus.Fire("Call.Controls.PushToTalk.Press");
            await _coordinator.BeginPushToTalkAsync(CancellationToken.None);
        };
        PushToTalkButton.PointerReleased += async (_, e) =>
        {
            e.Pointer.Capture(null);
            e.Handled = true;
            _bus.Fire("Call.Controls.PushToTalk.Release");
            await _coordinator.EndPushToTalkAsync(CancellationToken.None);
        };

        // Send transcript
        _bus.RegisterElement("Call.Composer.SendClick", SendTranscriptButton);
        _bus.WirePointerEvents("Call.Composer.SendClick", SendTranscriptButton);
        SendTranscriptButton.Click += async (_, _) =>
        {
            _bus.Fire("Call.Composer.SendClick");
            await SendTranscriptAsync();
        };

        // Preview voice
        _bus.RegisterElement("Call.Controls.VoicePreview", PreviewVoiceButton);
        _bus.WirePointerEvents("Call.Controls.VoicePreview", PreviewVoiceButton);
        PreviewVoiceButton.Click += async (_, _) =>
        {
            _bus.Fire("Call.Controls.VoicePreview");
            await PreviewVoiceAsync();
        };

        // Download speech model
        _bus.RegisterElement("Call.Controls.DownloadSpeech", DownloadSpeechButton);
        _bus.WirePointerEvents("Call.Controls.DownloadSpeech", DownloadSpeechButton);
        DownloadSpeechButton.Click += async (_, _) =>
        {
            _bus.Fire("Call.Controls.DownloadSpeech");
            await DownloadSpeechModelAsync();
        };

        // Export transcript
        _bus.RegisterElement("Call.Controls.ExportTranscript", ExportTranscriptButton);
        _bus.WirePointerEvents("Call.Controls.ExportTranscript", ExportTranscriptButton);
        ExportTranscriptButton.Click += async (_, _) =>
        {
            _bus.Fire("Call.Controls.ExportTranscript");
            await ExportTranscriptAsync();
        };

        // Transcript input
        TranscriptInput.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && _isActive)
            {
                await SendTranscriptAsync();
                e.Handled = true;
            }
        };
    }

    private void CreateWaveformBars()
    {
        for (int i = 0; i < 20; i++)
        {
            var bar = new HavenAdaptiveSurface
            {
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(Color.Parse("#0078D4")),
                MinHeight = 4,
                Height = 4,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            _waveformBars.Add(bar);
            Grid.SetColumn(bar, i);
            WaveformGrid.Children.Add(bar);
        }
    }

    private async Task StartCallAsync()
    {
        if (_selectedModel is null) return;
        try
        {
            TranscriptPanel.Children.Clear();
            _transcript.Clear();
            _transcriptById.Clear();
            EmptyState.IsVisible = false;
            TranscriptScroller.IsVisible = false;
            ExportTranscriptButton.IsEnabled = false;

            var options = new CallStartOptions(
                _selectedModel, _inputMode,
                _selectedInputDevice?.Id, _selectedOutputDevice?.Id,
                _selectedVoice?.Id, _enableSpeechOutput);
            options = options with
            {
                SystemPrompt = _selectedVoiceProfile is null
                    ? options.SystemPrompt
                    : $"{options.SystemPrompt}\n\nVoice Profile: {_selectedVoiceProfile.Name}.\n{_selectedVoiceProfile.Instructions}",
                VoiceProfileId = _selectedVoiceProfile?.Id
            };
            await _coordinator.StartAsync(
                options,
                _selectedSpeechModel?.IsInstalled == true ? _selectedSpeechModel : null,
                CancellationToken.None);
            _isActive = true;
            UpdateControlStates();
            _bus.Fire("Call.Status.Active");
        }
        catch (Exception ex)
        {
            TranscriptStatus.Text = $"Call could not start: {ex.Message}";
        }
    }

    private void BeginVoiceProfileEdit(VoiceProfile? profile)
    {
        VoiceProfileNameBox.Text = profile?.Name ?? string.Empty;
        VoiceProfileDescriptionBox.Text = profile?.Description ?? string.Empty;
        VoiceProfileInstructionsBox.Text = profile?.Instructions ?? string.Empty;
        VoiceProfileEditor.Tag = profile?.Id;
        VoiceProfileEditor.IsVisible = true;
    }

    private void SaveVoiceProfile()
    {
        var name = VoiceProfileNameBox.Text?.Trim() ?? string.Empty;
        var instructions = VoiceProfileInstructionsBox.Text?.Trim() ?? string.Empty;
        if (name.Length == 0 || instructions.Length == 0)
        {
            VoiceProfileDescription.Text = "A custom profile needs a name and instructions.";
            return;
        }
        var id = VoiceProfileEditor.Tag as string;
        if (string.IsNullOrWhiteSpace(id))
            id = "user.voice." + Guid.NewGuid().ToString("N");
        _preferences.UpsertCustomVoiceProfile(new VoiceProfile(
            id,
            name,
            VoiceProfileDescriptionBox.Text?.Trim() ?? string.Empty,
            instructions,
            IsBuiltIn: false));
        VoiceProfileEditor.IsVisible = false;
        RefreshVoiceProfiles(id);
    }

    private void RefreshVoiceProfiles(string? selectId = null)
    {
        var profiles = _voiceProfiles.GetAll()
            .Concat(_preferences.CustomVoiceProfiles)
            .GroupBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(profile => profile.Name)
            .ToArray();
        VoiceProfileCombo.ItemsSource = profiles;
        _selectedVoiceProfile = profiles.FirstOrDefault(profile => profile.Id.Equals(selectId, StringComparison.OrdinalIgnoreCase))
            ?? profiles.FirstOrDefault(profile => profile.Id == "general")
            ?? profiles.FirstOrDefault();
        VoiceProfileCombo.SelectedItem = _selectedVoiceProfile;
        VoiceProfileDescription.Text = _selectedVoiceProfile?.Description ?? string.Empty;
        UpdateVoiceProfileButtons();
    }

    private void UpdateVoiceProfileButtons()
    {
        var isCustom = _selectedVoiceProfile is { IsBuiltIn: false };
        EditVoiceProfileButton.IsEnabled = isCustom;
        DeleteVoiceProfileButton.IsEnabled = isCustom;
    }

    private async Task EndCallAsync()
    {
        try { await _coordinator.EndAsync(CancellationToken.None); }
        catch (Exception ex) { TranscriptStatus.Text = $"Call cleanup failed: {ex.Message}"; }
        _isActive = false;
        _isPaused = false;
        UpdateControlStates();
        _bus.Fire("Call.Status.Ended");
    }

    private async Task ToggleMuteAsync()
    {
        try
        {
            await _coordinator.SetMutedAsync(!_isMuted, CancellationToken.None);
            _isMuted = !_isMuted;
            UpdateControlStates();
        }
        catch (Exception ex) { TranscriptStatus.Text = ex.Message; }
    }

    private async Task PauseResumeAsync()
    {
        try
        {
            if (_isPaused)
                await _coordinator.ResumeAsync(CancellationToken.None);
            else
                await _coordinator.PauseAsync(CancellationToken.None);
            _isPaused = !_isPaused;
            UpdateControlStates();
        }
        catch (Exception ex) { TranscriptStatus.Text = ex.Message; }
    }

    private async Task SendTranscriptAsync()
    {
        var text = TranscriptInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text) || !_isActive) return;
        TranscriptInput.Text = string.Empty;
        try { await _coordinator.SubmitTextAsync(text, CancellationToken.None); }
        catch (Exception ex) { TranscriptStatus.Text = ex.Message; }
    }

    private async Task DownloadSpeechModelAsync()
    {
        if (_selectedSpeechModel is null) return;
        DownloadSpeechButton.IsEnabled = false;
        SpeechDownloadProgress.IsVisible = true;
        try
        {
            var progress = new Progress<double>(v => SpeechDownloadProgress.Value = v * 100);
            await _speechModels.DownloadAsync(_selectedSpeechModel.Size, progress, CancellationToken.None);
            var models = await _speechModels.GetModelsAsync(CancellationToken.None);
            _speechModelsList.Clear();
            foreach (var m in models) _speechModelsList.Add(m);
            _selectedSpeechModel = _speechModelsList.FirstOrDefault(s => s.Size == _selectedSpeechModel.Size);
            SpeechModelCombo.SelectedItem = _selectedSpeechModel;
            UpdateSpeechModelStatus();
        }
        catch (Exception ex) { TranscriptStatus.Text = $"Download failed: {ex.Message}"; }
        finally
        {
            DownloadSpeechButton.IsEnabled = true;
            SpeechDownloadProgress.IsVisible = false;
        }
    }

    private async Task PreviewVoiceAsync()
    {
        var services = App.Services;
        if (services is null) return;
        PreviewVoiceButton.IsEnabled = false;
        PreviewVoiceButton.Content = "Playing preview\u2026";
        try
        {
            await services.GetRequiredService<CallVoicePreviewController>().PreviewAsync(
                _selectedVoice, _selectedOutputDevice?.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            services.GetRequiredService<NotificationService>().Show(
                "Voice preview unavailable", ex.Message, ToastKind.Warning, TimeSpan.FromSeconds(8));
        }
        finally
        {
            PreviewVoiceButton.IsEnabled = true;
            PreviewVoiceButton.Content = "Preview selected voice";
        }
    }

    private async Task ExportTranscriptAsync()
    {
        if (_transcript.Count == 0) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        try
        {
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Haven Call transcript",
                SuggestedFileName = $"haven-call-{DateTime.Now:yyyy-MM-dd-HHmm}.md",
                FileTypeChoices =
                [
                    new FilePickerFileType("Markdown") { Patterns = ["*.md"] },
                    new FilePickerFileType("Text") { Patterns = ["*.txt"] }
                ]
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(CallTranscriptExportFormatter.ToMarkdown(
                _transcript.Select(t => new TranscriptExportEntry(t.Role, t.Text, t.Timestamp)).ToList(),
                DateTimeOffset.Now));
            await writer.FlushAsync();
            App.Services?.GetService<NotificationService>()?.Show(
                "Transcript exported", file.Name, ToastKind.Info, TimeSpan.FromSeconds(6));
        }
        catch (Exception ex)
        {
            App.Services?.GetService<NotificationService>()?.Show(
                "Transcript export failed", ex.Message, ToastKind.Warning, TimeSpan.FromSeconds(8));
        }
    }

    private void OnStateChanged(object? sender, CallStateChangedEventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        StateLabel.Text = e.State.ToString();
        TranscriptStatus.Text = e.Status;
        _isActive = _coordinator.IsActive;
        _isMuted = _coordinator.IsMuted;
        UpdateControlStates();
        UpdateWaveform(e.State is CallState.Listening or CallState.Transcribing or CallState.Speaking ? 0.35 : 0);
    });

    private void OnTranscriptChanged(object? sender, CallTranscriptEventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (!_transcriptById.TryGetValue(e.MessageId, out var entry))
        {
            entry = new TranscriptEntry(e.MessageId, e.Role, e.Text, DateTimeOffset.Now);
            _transcriptById[e.MessageId] = entry;
            _transcript.Add(entry);
            AddTranscriptBubble(entry);
        }
        else if (e.IsDelta)
        {
            entry.Text += e.Text;
            UpdateTranscriptBubble(entry);
        }
        else
        {
            entry.Text = e.Text;
            UpdateTranscriptBubble(entry);
        }
        entry.IsPartial = !e.IsFinal;
        ExportTranscriptButton.IsEnabled = _transcript.Count > 0;
        TranscriptScroller.ScrollToEnd();
    });

    private void OnAudioLevelChanged(object? sender, CallAudioLevelEventArgs e) =>
        Dispatcher.UIThread.Post(() => UpdateWaveform(e.Level));

    private void AddTranscriptBubble(TranscriptEntry entry)
    {
        var speaker = entry.Role == MessageRole.User ? "You" : "Haven";
        var timeLabel = entry.Timestamp.ToString("HH:mm");

        var speakerText = new TextBlock { Text = speaker, FontWeight = FontWeight.SemiBold };
        var timeText = new TextBlock { Text = timeLabel, FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#A0A0A0")) };
        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        headerGrid.Children.Add(speakerText);
        Grid.SetColumn(timeText, 1);
        headerGrid.Children.Add(timeText);

        var bodyText = new TextBlock { Text = entry.Text, TextWrapping = TextWrapping.Wrap };
        entry.BodyTextBlock = bodyText;

        var liveLabel = new TextBlock { Text = "Live\u2026", FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#0078D4")), IsVisible = entry.IsPartial };
        entry.LiveLabel = liveLabel;

        var statusPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { liveLabel } };

        var stack = new StackPanel { Spacing = 6, Children = { headerGrid, bodyText, statusPanel } };
        var bubble = new HavenAdaptiveSurface
        {
            Margin = new Avalonia.Thickness(0, 0, 0, 14), Padding = new Avalonia.Thickness(14),
            Background = new SolidColorBrush(Color.Parse("#33FFFFFF")),
            BorderBrush = new SolidColorBrush(Color.Parse("#44FFFFFF")),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = stack
        };
        entry.Bubble = bubble;
        TranscriptPanel.Children.Add(bubble);
        TranscriptScroller.IsVisible = true;
        EmptyState.IsVisible = false;
    }

    private void UpdateTranscriptBubble(TranscriptEntry entry)
    {
        if (entry.BodyTextBlock is { } tb) tb.Text = entry.Text;
        if (entry.LiveLabel is { } ll) ll.IsVisible = entry.IsPartial;
    }

    private void UpdateControlStates()
    {
        StartCallButton.IsEnabled = !_isActive && _selectedModel is not null;
        EndCallButton.IsEnabled = _isActive;
        PauseButton.IsEnabled = _isActive;
        MuteButton.IsEnabled = _isActive;
        InterruptButton.IsEnabled = _isActive;
        SendTranscriptButton.IsEnabled = _isActive;
        TranscriptInput.IsEnabled = _isActive;

        StateLabel.Text = _isActive ? (_isPaused ? "Paused" : "Active") : "Idle";
        var brush = _isActive
            ? new SolidColorBrush(Color.Parse("#22C55E"))
            : new SolidColorBrush(Color.Parse("#0078D4"));
        StateDot.Fill = brush;
    }

    private void UpdateStartButton()
    {
        if (!_isActive)
            StartCallButton.IsEnabled = _selectedModel is not null;
    }

    private void UpdateSpeechModelStatus()
    {
        if (_selectedSpeechModel is null)
        {
            SpeechModelStatus.Text = "Choose a local speech model";
            SpeechModelSetup.IsVisible = false;
        }
        else if (_selectedSpeechModel.IsInstalled)
        {
            SpeechModelStatus.Text = $"Installed \u00b7 {FormatBytes(_selectedSpeechModel.ApproximateSizeBytes)}";
            SpeechModelSetup.IsVisible = false;
        }
        else
        {
            SpeechModelStatus.Text = $"Download required \u00b7 about {FormatBytes(_selectedSpeechModel.ApproximateSizeBytes)}";
            SpeechModelSetup.IsVisible = true;
        }
    }

    private void UpdateVoiceDescription()
    {
        VoiceDescription.Text = _selectedVoice?.Id.StartsWith("kokoro:", StringComparison.OrdinalIgnoreCase) == true
            ? "Neural, expressive and fully local. The compact voice model downloads once on first preview."
            : "Windows system voice. Instant and offline, but less conversational than Haven Neural.";
    }

    private void UpdateWaveform(double level)
    {
        for (int i = 0; i < _waveformBars.Count; i++)
        {
            var shape = 0.35 + Math.Abs(Math.Sin((i + 1) * 1.47)) * 0.65;
            _waveformBars[i].Height = 4 + (Math.Clamp(level, 0, 1) * 32 * shape);
        }
    }

    private static string FormatBytes(long bytes)
    {
        var mb = bytes / (1024d * 1024d);
        return mb >= 1024 ? $"{mb / 1024:0.0} GB" : $"{mb:0} MB";
    }

    private sealed class TranscriptEntry(Guid id, MessageRole role, string text, DateTimeOffset timestamp)
    {
        public Guid Id { get; } = id;
        public MessageRole Role { get; } = role;
        public string Text { get; set; } = text;
        public DateTimeOffset Timestamp { get; } = timestamp;
        public bool IsPartial { get; set; }
        public Border? Bubble { get; set; }
        public TextBlock? BodyTextBlock { get; set; }
        public TextBlock? LiveLabel { get; set; }
    }
}
