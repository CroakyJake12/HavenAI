using System.ComponentModel;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenSlider = Haven.UI.Components.Slider;
using HavenText = Haven.UI.Components.Text;
using Container = Haven.UI.Components.Container;

namespace Haven.Desktop.Views.Shell.Overlays;

/// <summary>
/// Haven.UI presentation for the shell-owned Voice session. Runtime state and actions
/// remain in InChatCallWidgetViewModel/ICallCoordinator; this class owns only product UI.
/// </summary>
internal sealed class GlobalCallHavenScene : IDisposable
{
    private readonly InChatCallWidgetViewModel _viewModel;
    private readonly Dictionary<string, Container> _panels = [];
    private readonly List<string> _contextItems = [];
    private string? _openPanel;
    private DateTimeOffset? _startedAt;
    private bool _disposed;

    public GlobalCallHavenScene(
        InChatCallWidgetViewModel viewModel,
        Action<HavenPoint>? dragDelta = null)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        Root = new Page { Name = "Voice.Root", Layout = HavenLayout.Vertical };
        Set(Root, HavenProperties.Width, HavenLength.Px(620));
        Set(Root, HavenProperties.MaxWidth, HavenLength.Px(620));
        Set(Root, HavenProperties.Background, "SurfaceRaised");
        Set(Root, HavenProperties.BorderColor, "Border");
        Set(Root, HavenProperties.BorderWidth, HavenLength.Px(1));
        Set(Root, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(24)));
        Set(Root, HavenProperties.Padding, HavenThickness.Parse("16px"));
        Set(Root, HavenProperties.Gap, HavenLength.Px(12));
        Set(Root, HavenProperties.Shadow, "Card");

        var header = new VoiceDragHandle { Name = "Voice.Header", Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "auto" };
        if (dragDelta is not null) header.DragDelta += dragDelta;
        var title = new Container { Layout = HavenLayout.Vertical };
        Set(title, HavenProperties.PointerEvents, HavenPointerEvents.None);
        title.Add(new HavenText("Voice") { Name = "Voice.Title", Level = TextLevel.H4 });
        StatusText = Secondary("Ready", "Voice.Status");
        title.Add(StatusText);
        header.Add(title);
        DurationText = new HavenText("Ready") { Name = "Voice.Duration", Level = TextLevel.Paragraph };
        Set(DurationText, HavenProperties.Column, 1);
        Set(DurationText, HavenProperties.FontWeight, 800);
        Set(DurationText, HavenProperties.PointerEvents, HavenPointerEvents.None);
        header.Add(DurationText);
        Root.Add(header);

        AudioLevel = new Progress { Name = "Voice.AudioLevel", Minimum = 0, Maximum = 1, Value = 0 };
        AudioLevel.Accessibility.AccessibleName = "Microphone level";
        Set(AudioLevel, HavenProperties.Width, HavenLength.Percent(100));
        Root.Add(AudioLevel);

        var modeRow = new Container { Name = "Voice.Mode.Row", Layout = HavenLayout.Grid, Columns = "220px 1fr", Rows = "auto" };
        Set(modeRow, HavenProperties.Gap, HavenLength.Px(10));
        VoiceMode = new Select { Name = "Voice.InputMode" };
        VoiceMode.Accessibility.AccessibleName = "Voice input mode";
        modeRow.Add(VoiceMode);
        ReactionText = Secondary("Choose Hands-free or Push to talk", "Voice.Reaction");
        Set(ReactionText, HavenProperties.Column, 1);
        modeRow.Add(ReactionText);
        Root.Add(modeRow);

        SummaryText = Secondary(string.Empty, "Voice.Summary");
        Visible(SummaryText, false);
        Root.Add(SummaryText);

        var toolbar = new Container { Name = "Voice.Toolbar", Layout = HavenLayout.Horizontal };
        Set(toolbar, HavenProperties.Gap, HavenLength.Px(7));
        Set(toolbar, HavenProperties.Overflow, HavenOverflow.Scroll);
        CallButton = Action("Voice.Call", "Start", ButtonVariant.Primary);
        MuteButton = Action("Voice.Mute", "Mic on", ButtonVariant.Tertiary);
        PauseButton = Action("Voice.Pause", "Pause", ButtonVariant.Tertiary);
        InterruptButton = Action("Voice.Interrupt", "Interrupt", ButtonVariant.Ghost);
        PushToTalkButton = Action("Voice.PushToTalk", "Talk", ButtonVariant.Secondary);
        TranscriptButton = Action("Voice.Transcript.Toggle", "Transcript", ButtonVariant.Ghost);
        ModelButton = Action("Voice.Model.Toggle", "Model", ButtonVariant.Ghost);
        ShareButton = Action("Voice.Share.Toggle", "Share", ButtonVariant.Ghost);
        SettingsButton = Action("Voice.Settings.Toggle", "Settings", ButtonVariant.Ghost);
        toolbar.Add(CallButton);
        toolbar.Add(MuteButton);
        toolbar.Add(PauseButton);
        toolbar.Add(InterruptButton);
        toolbar.Add(PushToTalkButton);
        toolbar.Add(TranscriptButton);
        toolbar.Add(ModelButton);
        toolbar.Add(ShareButton);
        toolbar.Add(SettingsButton);
        Root.Add(toolbar);

        Detail = new Container { Name = "Voice.Detail", Layout = HavenLayout.Vertical };
        Set(Detail, HavenProperties.Width, HavenLength.Percent(100));
        Set(Detail, HavenProperties.Background, "Surface");
        Set(Detail, HavenProperties.BorderColor, "Border");
        Set(Detail, HavenProperties.BorderWidth, HavenLength.Px(1));
        Set(Detail, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        Set(Detail, HavenProperties.Padding, HavenThickness.Parse("14px"));
        Visible(Detail, false);
        Root.Add(Detail);

        AddPanel("transcript", BuildTranscriptPanel());
        AddPanel("model", BuildModelPanel());
        AddPanel("share", BuildSharePanel());
        AddPanel("settings", BuildSettingsPanel());
        AddPanel("context", BuildContextPanel());

        CallButton.Invoked += OnCallInvoked;
        MuteButton.Invoked += OnMuteInvoked;
        PauseButton.Invoked += OnPauseInvoked;
        InterruptButton.Invoked += OnInterruptInvoked;
        PushToTalkButton.Invoked += OnPushToTalkInvoked;
        TranscriptButton.Invoked += (_, _) => TogglePanel("transcript");
        ModelButton.Invoked += (_, _) => TogglePanel("model");
        ShareButton.Invoked += (_, _) => TogglePanel("share");
        SettingsButton.Invoked += (_, _) => TogglePanel("settings");
        ShareAction.Invoked += OnShareInvoked;
        SendButton.Invoked += OnSendInvoked;
        AddFileButton.Invoked += (_, _) => AddFilesRequested?.Invoke(this, EventArgs.Empty);
        VoiceMode.SelectionChanged += OnVoiceModeChanged;
        VoiceProfile.SelectionChanged += OnVoiceProfileChanged;
        VoiceChoice.SelectionChanged += OnVoiceChanged;
        Microphone.SelectionChanged += OnMicrophoneChanged;
        Reasoning.ValueChanged += OnReasoningChanged;
        TranscriptInput.Invalidated += OnTranscriptInputInvalidated;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.CallEnded += OnCallEnded;
        Refresh();
    }

    public event EventHandler? AddFilesRequested;

    public Page Root { get; }
    public HavenText StatusText { get; }
    public HavenText DurationText { get; }
    public Progress AudioLevel { get; }
    public Select VoiceMode { get; }
    public HavenText ReactionText { get; }
    public HavenText SummaryText { get; }
    public HavenButton CallButton { get; }
    public HavenButton MuteButton { get; }
    public HavenButton PauseButton { get; }
    public HavenButton InterruptButton { get; }
    public HavenButton PushToTalkButton { get; }
    public HavenButton TranscriptButton { get; }
    public HavenButton ModelButton { get; }
    public HavenButton ShareButton { get; }
    public HavenButton SettingsButton { get; }
    public Container Detail { get; }
    public Container TranscriptTurns { get; private set; } = null!;
    public Input TranscriptInput { get; private set; } = null!;
    public HavenButton SendButton { get; private set; } = null!;
    public HavenButton AddFileButton { get; private set; } = null!;
    public HavenText ModelName { get; private set; } = null!;
    public Select VoiceProfile { get; private set; } = null!;
    public Select VoiceChoice { get; private set; } = null!;
    public HavenSlider Reasoning { get; private set; } = null!;
    public HavenText ReasoningValue { get; private set; } = null!;
    public HavenText ShareStatus { get; private set; } = null!;
    public HavenButton ShareAction { get; private set; } = null!;
    public Select Microphone { get; private set; } = null!;
    public HavenText ContextItems { get; private set; } = null!;

    public void SubmitFocusedInput(Input input)
    {
        if (!ReferenceEquals(input, TranscriptInput)) return;
        SyncTypedTranscript();
        Execute(_viewModel.SubmitTextCommand);
    }

    public void AddContextFiles(IEnumerable<string> fileNames)
    {
        foreach (var fileName in fileNames.Where(name => !string.IsNullOrWhiteSpace(name)))
            if (!_contextItems.Contains(fileName, StringComparer.OrdinalIgnoreCase)) _contextItems.Add(fileName);
        RefreshContext();
    }

    public void Tick(DateTimeOffset now)
    {
        if (_viewModel.IsActive && _startedAt is null) _startedAt = now;
        if (!_viewModel.IsActive) _startedAt = null;
        if (_startedAt is null)
        {
            DurationText.Content = "Ready";
            return;
        }

        var elapsed = now - _startedAt.Value;
        DurationText.Content = elapsed.TotalHours >= 1
            ? elapsed.ToString(@"hh\:mm\:ss")
            : elapsed.ToString(@"mm\:ss");
    }

    public void Refresh()
    {
        SyncCollections();
        StatusText.Content = _viewModel.Status;
        AudioLevel.Value = Math.Clamp(_viewModel.AudioLevel, 0, 1);
        CallButton.Content = _viewModel.IsActive ? "End" : "Start";
        CallButton.Variant = _viewModel.IsActive ? ButtonVariant.Danger : ButtonVariant.Primary;
        Enabled(CallButton, _viewModel.IsActive || _viewModel.StartCallCommand.CanExecute(null));
        MuteButton.Content = _viewModel.IsMuted ? "Mic off" : "Mic on";
        ReactionText.Content = _viewModel.IsActive ? _viewModel.LiveReaction : _viewModel.InputMode == CallInputMode.PushToTalk ? "Tap Talk to record; tap Stop & send when finished." : "Hands-free listens continuously while the call is active.";
        Enabled(VoiceMode, !_viewModel.IsActive);
        PauseButton.Content = _viewModel.IsPaused ? "Resume" : "Pause";
        Enabled(PauseButton, _viewModel.TogglePauseCommand.CanExecute(null));
        Visible(PauseButton, _viewModel.IsActive);
        Enabled(InterruptButton, _viewModel.InterruptCommand.CanExecute(null));
        Visible(InterruptButton, _viewModel.IsActive);
        PushToTalkButton.Content = _viewModel.IsPushToTalkRecording ? "Stop & send" : "Talk";
        Enabled(PushToTalkButton, _viewModel.TogglePushToTalkCommand.CanExecute(null));
        Visible(PushToTalkButton, _viewModel.IsActive && _viewModel.InputMode == CallInputMode.PushToTalk);
        SummaryText.Content = _viewModel.CallSummary ?? string.Empty;
        Visible(SummaryText, !string.IsNullOrWhiteSpace(_viewModel.CallSummary));

        ModelName.Content = _viewModel.SelectedModelName;
        if (Math.Abs(Reasoning.Value - _viewModel.ReasoningPercent) > .001) Reasoning.Value = _viewModel.ReasoningPercent;
        ReasoningValue.Content = $"{_viewModel.ReasoningPercent}%";
        Enabled(VoiceProfile, !_viewModel.IsActive);
        Enabled(VoiceChoice, !_viewModel.IsActive);
        Enabled(Microphone, !_viewModel.IsActive);
        Enabled(Reasoning, !_viewModel.IsActive);

        ShareStatus.Content = _viewModel.ScreenShareStatus;
        ShareAction.Content = _viewModel.IsScreenSharing ? "Stop sharing" : "Share screen or app";
        ShareAction.Variant = _viewModel.IsScreenSharing ? ButtonVariant.Danger : ButtonVariant.Secondary;
        Enabled(ShareAction, _viewModel.IsActive && _viewModel.CanShareScreen);

        RebuildTranscript();
        if (!string.Equals(TranscriptInput.Text, _viewModel.TypedTranscript, StringComparison.Ordinal))
            TranscriptInput.Text = _viewModel.TypedTranscript;
        Enabled(TranscriptInput, _viewModel.IsActive);
        Enabled(SendButton, _viewModel.SubmitTextCommand.CanExecute(null));
        RefreshContext();
        Tick(DateTimeOffset.Now);
    }

    private Container BuildTranscriptPanel()
    {
        var panel = Panel("Voice.Transcript");
        panel.Add(Heading("Live transcript"));
        TranscriptTurns = new Container { Name = "Voice.Transcript.Turns", Layout = HavenLayout.Vertical };
        Set(TranscriptTurns, HavenProperties.Width, HavenLength.Percent(100));
        Set(TranscriptTurns, HavenProperties.MaxHeight, HavenLength.Px(300));
        Set(TranscriptTurns, HavenProperties.Overflow, HavenOverflow.Scroll);
        Set(TranscriptTurns, HavenProperties.Gap, HavenLength.Px(8));
        panel.Add(TranscriptTurns);

        var composer = new Container { Layout = HavenLayout.Grid, Columns = "Auto 1fr Auto", Rows = "auto" };
        Set(composer, HavenProperties.Gap, HavenLength.Px(8));
        var context = Action("Voice.Context.Toggle", "+", ButtonVariant.Icon);
        context.Accessibility.AccessibleName = "Session context";
        context.Invoked += (_, _) => ShowPanel("context");
        composer.Add(context);
        TranscriptInput = new Input { Name = "Voice.Transcript.Input", Placeholder = "Talk to Haven Voice" };
        TranscriptInput.Accessibility.AccessibleName = "Voice message";
        Set(TranscriptInput, HavenProperties.Column, 1);
        composer.Add(TranscriptInput);
        SendButton = Action("Voice.Transcript.Send", "Send", ButtonVariant.Primary);
        Set(SendButton, HavenProperties.Column, 2);
        composer.Add(SendButton);
        panel.Add(composer);
        return panel;
    }

    private Container BuildModelPanel()
    {
        var panel = Panel("Voice.Model");
        panel.Add(Heading("Model and voice"));
        ModelName = new HavenText("No model available") { Level = TextLevel.Paragraph };
        Set(ModelName, HavenProperties.FontWeight, 800);
        panel.Add(Field("Current conversation model", ModelName));
        panel.Add(Secondary("Voice uses the model selected for this conversation. Change it from the main model picker before starting.", "Voice.Model.Help"));
        VoiceProfile = new Select { Name = "Voice.Model.Style" };
        VoiceProfile.Accessibility.AccessibleName = "Voice style";
        panel.Add(Field("Voice style", VoiceProfile));
        VoiceChoice = new Select { Name = "Voice.Model.Voice" };
        VoiceChoice.Accessibility.AccessibleName = "Voice";
        panel.Add(Field("Voice", VoiceChoice));
        ReasoningValue = Secondary("25%", "Voice.Reasoning.Value");
        panel.Add(ValueHeader("Reasoning", ReasoningValue));
        Reasoning = new HavenSlider { Name = "Voice.Reasoning", Minimum = 25, Maximum = 100, Step = 25, Value = 25 };
        Reasoning.Accessibility.AccessibleName = "Reasoning";
        Set(Reasoning, HavenProperties.Width, HavenLength.Percent(100));
        panel.Add(Reasoning);
        return panel;
    }

    private Container BuildSharePanel()
    {
        var panel = Panel("Voice.Share");
        panel.Add(Heading("Share"));
        ShareStatus = Secondary("Choose a screen or app to share", "Voice.Share.Status");
        panel.Add(ShareStatus);
        ShareAction = Action("Voice.Share.Action", "Share screen or app", ButtonVariant.Secondary);
        panel.Add(ShareAction);
        return panel;
    }

    private Container BuildSettingsPanel()
    {
        var panel = Panel("Voice.Settings");
        panel.Add(Heading("Session settings"));
        Microphone = new Select { Name = "Voice.Settings.Microphone" };
        Microphone.Accessibility.AccessibleName = "Microphone";
        panel.Add(Field("Microphone", Microphone));
        return panel;
    }

    private Container BuildContextPanel()
    {
        var panel = Panel("Voice.Context");
        panel.Add(Heading("Session context"));
        ContextItems = Secondary("No files selected.", "Voice.Context.Items");
        panel.Add(ContextItems);
        AddFileButton = Action("Voice.Context.AddFile", "Add file(s)", ButtonVariant.Secondary);
        panel.Add(AddFileButton);
        panel.Add(Secondary(
            "Agents, plugins and instructions stay attached through Chat and the shared runtime; Voice does not create a parallel tool context.",
            "Voice.Context.Help"));
        return panel;
    }

    private void RebuildTranscript()
    {
        foreach (var child in TranscriptTurns.Children.ToArray()) TranscriptTurns.Remove(child);
        if (_viewModel.TranscriptTurns.Count == 0)
        {
            TranscriptTurns.Add(Secondary("Transcript will appear here while the session is active.", "Voice.Transcript.Empty"));
            return;
        }

        foreach (var turn in _viewModel.TranscriptTurns)
        {
            var bubble = Panel($"Voice.Transcript.{turn.MessageId:N}");
            Set(bubble, HavenProperties.Background, turn.Role == MessageRole.User ? "AccentMuted" : "SurfaceRaised");
            Set(bubble, HavenProperties.BorderColor, "Border");
            Set(bubble, HavenProperties.BorderWidth, HavenLength.Px(1));
            Set(bubble, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
            Set(bubble, HavenProperties.Padding, HavenThickness.Parse("10px 12px"));
            var speaker = new HavenText(turn.Role == MessageRole.User ? "You" : "Haven") { Level = TextLevel.Caption };
            Set(speaker, HavenProperties.FontWeight, 800);
            bubble.Add(speaker);
            bubble.Add(new HavenText(turn.Content) { Level = TextLevel.Paragraph });
            if (turn.WasInterrupted)
                bubble.Add(Secondary("Interrupted", $"Voice.Transcript.{turn.MessageId:N}.Interrupted"));
            TranscriptTurns.Add(bubble);
        }
    }

    private void SyncCollections()
    {
        VoiceMode.Items = _viewModel.InputModes.Select(item => item == CallInputMode.HandsFree ? "Hands-free" : "Push to talk").ToArray();
        VoiceMode.SelectedIndex = IndexOf(_viewModel.InputModes, _viewModel.InputMode, EqualityComparer<CallInputMode>.Default.Equals);

        VoiceProfile.Items = _viewModel.VoiceProfiles.Select(item => item.Name).ToArray();
        VoiceProfile.SelectedIndex = _viewModel.SelectedVoiceProfile is null ? -1 : IndexOf(
            _viewModel.VoiceProfiles,
            _viewModel.SelectedVoiceProfile,
            (left, right) => left.Id.Equals(right.Id, StringComparison.OrdinalIgnoreCase));

        var voices = _viewModel.Voices.Select(item => item.Name).ToArray();
        VoiceChoice.Items = voices.Length == 0 ? ["System voice"] : voices;
        VoiceChoice.SelectedIndex = voices.Length == 0 ? 0 : IndexOf(_viewModel.Voices, _viewModel.SelectedVoice, EqualityComparer<CallVoice>.Default.Equals);

        var microphones = _viewModel.InputDevices.Select(item => item.Name).ToArray();
        Microphone.Items = microphones.Length == 0 ? ["System default"] : microphones;
        Microphone.SelectedIndex = microphones.Length == 0 ? 0 : IndexOf(_viewModel.InputDevices, _viewModel.SelectedInputDevice, EqualityComparer<CallAudioDevice>.Default.Equals);
    }

    private void RefreshContext() => ContextItems.Content = _contextItems.Count == 0
        ? "No files selected."
        : string.Join("  •  ", _contextItems);

    private void OnCallInvoked(object? sender, EventArgs e) => Execute(_viewModel.IsActive ? _viewModel.EndCallCommand : _viewModel.StartCallCommand);
    private void OnMuteInvoked(object? sender, EventArgs e) => Execute(_viewModel.ToggleMuteCommand);
    private void OnPauseInvoked(object? sender, EventArgs e) => Execute(_viewModel.TogglePauseCommand);
    private void OnInterruptInvoked(object? sender, EventArgs e) => Execute(_viewModel.InterruptCommand);
    private void OnPushToTalkInvoked(object? sender, EventArgs e) => Execute(_viewModel.TogglePushToTalkCommand);
    private void OnShareInvoked(object? sender, EventArgs e) => Execute(_viewModel.ToggleScreenShareCommand);
    private void OnSendInvoked(object? sender, EventArgs e) => SubmitFocusedInput(TranscriptInput);

    private void OnVoiceModeChanged(object? sender, EventArgs e)
    {
        if (_viewModel.IsActive) return;
        var index = VoiceMode.SelectedIndex;
        if (index >= 0 && index < _viewModel.InputModes.Count) _viewModel.InputMode = _viewModel.InputModes[index];
    }

    private void OnVoiceProfileChanged(object? sender, EventArgs e)
    {
        if (_viewModel.IsActive) return;
        var index = VoiceProfile.SelectedIndex;
        if (index >= 0 && index < _viewModel.VoiceProfiles.Count) _viewModel.SelectedVoiceProfile = _viewModel.VoiceProfiles[index];
    }

    private void OnVoiceChanged(object? sender, EventArgs e)
    {
        var index = VoiceChoice.SelectedIndex;
        if (index >= 0 && index < _viewModel.Voices.Count) _viewModel.SelectedVoice = _viewModel.Voices[index];
    }

    private void OnMicrophoneChanged(object? sender, EventArgs e)
    {
        var index = Microphone.SelectedIndex;
        if (index >= 0 && index < _viewModel.InputDevices.Count) _viewModel.SelectedInputDevice = _viewModel.InputDevices[index];
    }

    private void OnReasoningChanged(object? sender, EventArgs e)
    {
        var percent = (int)Math.Round(Reasoning.Value / 25d) * 25;
        _viewModel.Effort = percent switch
        {
            <= 25 => EffortLevel.Low,
            <= 50 => EffortLevel.Medium,
            <= 75 => EffortLevel.High,
            _ => EffortLevel.Max
        };
        ReasoningValue.Content = $"{_viewModel.ReasoningPercent}%";
    }

    private void OnTranscriptInputInvalidated(object? sender, EventArgs e) => SyncTypedTranscript();

    private void SyncTypedTranscript()
    {
        if (!string.Equals(_viewModel.TypedTranscript, TranscriptInput.Text, StringComparison.Ordinal))
            _viewModel.TypedTranscript = TranscriptInput.Text;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => Refresh();
    private void OnCallEnded(object? sender, EventArgs e) { _startedAt = null; Refresh(); }

    private void AddPanel(string key, Container panel)
    {
        _panels[key] = panel;
        Visible(panel, false);
        Detail.Add(panel);
    }

    private void TogglePanel(string key) => ShowPanel(_openPanel == key ? null : key);

    private void ShowPanel(string? key)
    {
        _openPanel = key;
        foreach (var pair in _panels) Visible(pair.Value, pair.Key == key);
        Visible(Detail, key is not null);
    }

    private static Container Panel(string name)
    {
        var panel = new Container { Name = name, Layout = HavenLayout.Vertical };
        Set(panel, HavenProperties.Width, HavenLength.Percent(100));
        Set(panel, HavenProperties.Gap, HavenLength.Px(8));
        return panel;
    }

    private static Container Field(string label, HavenElement control)
    {
        var field = new Container { Layout = HavenLayout.Vertical };
        Set(field, HavenProperties.Gap, HavenLength.Px(5));
        field.Add(Secondary(label, string.Empty));
        field.Add(control);
        return field;
    }

    private static Container ValueHeader(string label, HavenElement value)
    {
        var row = new Container { Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "auto" };
        row.Add(new HavenText(label) { Level = TextLevel.Paragraph });
        Set(value, HavenProperties.Column, 1);
        row.Add(value);
        return row;
    }

    private static HavenText Heading(string value)
    {
        var text = new HavenText(value) { Level = TextLevel.H4 };
        Set(text, HavenProperties.FontWeight, 800);
        return text;
    }

    private static HavenText Secondary(string value, string name)
    {
        var text = new HavenText(value) { Name = string.IsNullOrWhiteSpace(name) ? null : name, Level = TextLevel.Caption };
        Set(text, HavenProperties.Foreground, "TextSecondary");
        return text;
    }

    private static HavenButton Action(string name, string content, ButtonVariant variant) =>
        new() { Name = name, Content = content, Variant = variant };

    private static void Execute(System.Windows.Input.ICommand command)
    {
        if (command.CanExecute(null)) command.Execute(null);
    }

    private static void Visible(HavenElement element, bool visible) =>
        Set(element, HavenProperties.Visibility, visible ? HavenVisibility.Visible : HavenVisibility.Collapsed);

    private static void Enabled(HavenElement element, bool enabled) => Set(element, HavenProperties.Enabled, enabled);
    private static void Set<T>(HavenElement element, HavenProperty<T> property, T value) => element.SetValue(property, value);

    private static int IndexOf<T>(IReadOnlyList<T> items, T? selected, Func<T, T, bool> equals)
    {
        if (selected is null) return -1;
        for (var index = 0; index < items.Count; index++)
            if (equals(items[index], selected)) return index;
        return -1;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.CallEnded -= OnCallEnded;
        CallButton.Invoked -= OnCallInvoked;
        MuteButton.Invoked -= OnMuteInvoked;
        PauseButton.Invoked -= OnPauseInvoked;
        InterruptButton.Invoked -= OnInterruptInvoked;
        PushToTalkButton.Invoked -= OnPushToTalkInvoked;
        ShareAction.Invoked -= OnShareInvoked;
        SendButton.Invoked -= OnSendInvoked;
        VoiceMode.SelectionChanged -= OnVoiceModeChanged;
        VoiceProfile.SelectionChanged -= OnVoiceProfileChanged;
        VoiceChoice.SelectionChanged -= OnVoiceChanged;
        Microphone.SelectionChanged -= OnMicrophoneChanged;
        Reasoning.ValueChanged -= OnReasoningChanged;
        TranscriptInput.Invalidated -= OnTranscriptInputInvalidated;
    }
}
