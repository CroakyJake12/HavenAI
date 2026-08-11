using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.HavenUI.Components;
using Haven.Desktop.HavenUI.Floating;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views.Shell.Overlays;

public sealed partial class GlobalCallWidget : UserControl, IDisposable
{
    private readonly InChatCallWidgetViewModel _viewModel;
    private readonly DispatcherTimer _durationTimer;
    private readonly List<string> _selectedContext = [];

    private HavenFloatingSurface? _surface;
    private TextBlock? _statusText;
    private TextBlock? _durationText;
    private TextBlock? _summaryText;
    private TextBlock? _muteLabel;
    private PathIcon? _muteIcon;
    private HavenHeaderPillButton? _callActionButton;
    private HavenSelect? _voiceModeSelect;
    private TextBlock? _reactionText;
    private TextBlock? _transcriptBody;
    private StackPanel? _transcriptMessages;
    private ScrollViewer? _transcriptScroller;
    private TextBox? _transcriptInput;
    private Button? _transcriptSendButton;
    private HavenToolbar? _toolbar;
    private HavenPanel? _detailPanel;
    private DateTimeOffset? _startedAt;
    private string? _openPanel;
    private Point? _dragStart;

    public GlobalCallWidget()
        : this(null)
    {
    }

    public GlobalCallWidget(InChatCallWidgetViewModel? viewModel)
    {
        _viewModel = viewModel ?? throw new InvalidOperationException(
            "GlobalCallWidget must be created with the application call view-model.");

        InitializeComponent();
        BuildSurface();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.CallEnded += OnCallEnded;

        _durationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _durationTimer.Tick += (_, _) => UpdateDuration();
        _durationTimer.Start();

        Refresh();
    }

    private void BuildSurface()
    {
        _statusText = Text("Ready", 11, FontWeight.Medium);
        _statusText.Opacity = 0.68;
        _statusText.IsVisible = false;
        _durationText = Text("Ready", 12, FontWeight.Bold);
        _summaryText = Text(string.Empty, 11, FontWeight.Normal);
        _summaryText.Opacity = 0.68;
        _muteLabel = Text("ON", 12, FontWeight.Bold);
        _muteIcon = new PathIcon
        {
            Data = HavenIcon.GeometryFor("mic"),
            Width = 20,
            Height = 20,
            Foreground = Brush("HavenTextPrimaryBrush")
        };

        var titleArea = new StackPanel
        {
            Spacing = 0,
            Children =
            {
                Text("Voice", 16, FontWeight.Bold)
            }
        };
        titleArea.Tapped += (_, _) => TogglePanel("model");
        ToolTip.SetTip(titleArea, "Drag to move · Click for voice model and reasoning");

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        header.Children.Add(titleArea);
        Grid.SetColumn(_durationText, 1);
        header.Children.Add(_durationText);
        header.Cursor = new Cursor(StandardCursorType.SizeAll);
        header.PointerPressed += OnDragPointerPressed;
        header.PointerMoved += OnDragPointerMoved;
        header.PointerReleased += OnDragPointerReleased;

        _voiceModeSelect = new HavenSelect
        {
            ItemsSource = _viewModel.VoiceProfiles.Select(profile => profile.Name).ToArray(),
            SelectedIndex = Math.Max(0, _viewModel.VoiceProfiles
                .Select((profile, index) => (profile, index))
                .FirstOrDefault(pair => pair.profile.Id == _viewModel.SelectedVoiceProfile?.Id).index),
            MinWidth = 190
        };

        AutomationProperties.SetAutomationId(_voiceModeSelect, "VoiceModeSelect");
        AutomationProperties.SetName(_voiceModeSelect, "Voice mode");
        ToolTip.SetTip(_voiceModeSelect, "Choose how Voice reacts during this session");
        _voiceModeSelect.SelectionChanged += (_, _) =>
        {
            var index = _voiceModeSelect.SelectedIndex;
            if (index >= 0 && index < _viewModel.VoiceProfiles.Count)
                _viewModel.SelectedVoiceProfile = _viewModel.VoiceProfiles[index];
        };

        _reactionText = Muted(_viewModel.LiveReaction);
        AutomationProperties.SetAutomationId(_reactionText, "VoiceReactionStatus");
        AutomationProperties.SetName(_reactionText, "Live Voice reaction");
        _reactionText.VerticalAlignment = VerticalAlignment.Center;
        _reactionText.MaxWidth = 290;

        var liveVoiceRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        liveVoiceRow.Children.Add(_voiceModeSelect);
        Grid.SetColumn(_reactionText, 1);
        liveVoiceRow.Children.Add(_reactionText);

        var toolbarItems = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 7
        };
        _toolbar = new HavenToolbar
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = toolbarItems
        };
        _callActionButton = ActionButton("Start", true);
        AutomationProperties.SetAutomationId(_callActionButton, "VoiceCallActionButton");
        AutomationProperties.SetName(_callActionButton, "Start Voice call");
        _callActionButton.Width = 124;
        _callActionButton.Click += (_, _) => Execute(
            _viewModel.IsActive ? _viewModel.EndCallCommand : _viewModel.StartCallCommand);

        var muteButton = new HavenSecondaryButton
        {
            MinWidth = 102,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { _muteIcon, _muteLabel }
            }
        };
        ToolTip.SetTip(muteButton, "Mute or unmute");
        muteButton.Click += (_, _) => Execute(_viewModel.ToggleMuteCommand);

        var transcriptButton = IconButton("chat", "Show transcript");
        transcriptButton.Click += (_, _) => TogglePanel("transcript");

        var participantsButton = IconButton("agents", "Model, voice and reasoning");
        participantsButton.Click += (_, _) => TogglePanel("model");

        var shareButton = IconButton("screen-share", "Share screen or app");
        shareButton.Click += (_, _) => TogglePanel("share");

        var settingsButton = IconButton("settings", "Voice session settings");
        settingsButton.Click += (_, _) => TogglePanel("settings");

        toolbarItems.Children.Add(_callActionButton);
        toolbarItems.Children.Add(muteButton);
        toolbarItems.Children.Add(transcriptButton);
        toolbarItems.Children.Add(participantsButton);
        toolbarItems.Children.Add(shareButton);
        toolbarItems.Children.Add(settingsButton);

        _detailPanel = new HavenPanel
        {
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var compactContent = new StackPanel { Spacing = 10 };
        compactContent.Children.Add(header);
        compactContent.Children.Add(liveVoiceRow);
        compactContent.Children.Add(_summaryText);
        compactContent.Children.Add(_toolbar);

        var root = new StackPanel { Spacing = 10 };
        root.Children.Add(_detailPanel);
        root.Children.Add(compactContent);

        _surface = new HavenFloatingSurface
        {
            Width = 620,
            Child = root,
            IsVisible = false
        };
        _surface.Classes.Add("voice");
        AutomationProperties.SetAutomationId(_surface, "VoiceFloatingSurface");
        AutomationProperties.SetName(_surface, "Voice floating surface");

        CodeBehindHost.Children.Clear();
        CodeBehindHost.Children.Add(_surface);
    }

    public event EventHandler<Vector>? DragDelta;

    private void OnDragPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            e.Source is Control source &&
            (source is Button || source.FindAncestorOfType<Button>() is not null))
        {
            return;
        }

        _dragStart = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is not Point start || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var current = e.GetPosition(this);
        var delta = current - start;
        if (Math.Abs(delta.X) < 0.5 && Math.Abs(delta.Y) < 0.5)
        {
            return;
        }

        DragDelta?.Invoke(this, delta);
        _dragStart = current;
        e.Handled = true;
    }

    private void OnDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragStart = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void ShowAddMenu(Control placementTarget)
    {
        var panel = new StackPanel { Width = 230, Spacing = 4, Margin = new Thickness(8) };
        panel.Children.Add(Text("Add to voice session", 12, FontWeight.SemiBold));

        AddMenuButton(panel, "File", async () =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider is null)
            {
                return;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Add a file to this voice session",
                AllowMultiple = true
            });

            foreach (var file in files)
            {
                var name = file.Name;
                if (!_selectedContext.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    _selectedContext.Add(name);
                }
            }

            ShowContextPanel();
        });

        AddMenuButton(panel, "Agent", () => ShowUnavailable(
            "Agents can be selected from Chat before opening the voice session."));
        AddMenuButton(panel, "Plugin", () => ShowUnavailable(
            "Plugins can be selected from Chat before opening the voice session."));
        AddMenuButton(panel, "Instruction", () => ShowUnavailable(
            "Instructions can be selected from Chat before opening the voice session."));
        AddMenuButton(panel, "App", () => ShowUnavailable(
            "Apps can be opened from the app launcher while the session remains active."));

        new HavenDropdown
        {
            Placement = PlacementMode.TopEdgeAlignedRight,
            Content = new HavenDropdownCard
            {
                MinWidth = 250,
                Padding = new Thickness(8),
                Child = panel
            }
        }.ShowAt(placementTarget);
    }

    private static void AddMenuButton(StackPanel panel, string label, Action action)
    {
        var button = new HavenDropdownItemButton
        {
            Content = label,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        button.Click += (_, _) => action();
        panel.Children.Add(button);
    }

    private static void AddMenuButton(StackPanel panel, string label, Func<System.Threading.Tasks.Task> action)
    {
        var button = new HavenDropdownItemButton
        {
            Content = label,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        button.Click += async (_, _) => await action();
        panel.Children.Add(button);
    }

    private void ShowContextPanel()
    {
        _openPanel = "context";
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(Text("Session context", 13, FontWeight.SemiBold));

        if (_selectedContext.Count == 0)
        {
            stack.Children.Add(Muted("No files selected."));
        }
        else
        {
            foreach (var item in _selectedContext)
            {
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = 8
                };
                row.Children.Add(Text(item, 12, FontWeight.Normal));
                var remove = IconButton("close", $"Remove {item}");
                remove.Click += (_, _) =>
                {
                    _selectedContext.Remove(item);
                    ShowContextPanel();
                };
                Grid.SetColumn(remove, 1);
                row.Children.Add(remove);
                stack.Children.Add(row);
            }
        }

        SetDetailContent(stack, 440);
    }

    private void TogglePanel(string panel)
    {
        if (_openPanel == panel)
        {
            _openPanel = null;
            ClearTranscriptControls();
            if (_detailPanel is not null)
            {
                _detailPanel.IsVisible = false;
                _detailPanel.Child = null;
            }

            return;
        }

        _openPanel = panel;
        if (panel != "transcript")
        {
            ClearTranscriptControls();
        }

        switch (panel)
        {
            case "model":
                ShowModelPanel();
                break;
            case "transcript":
                ShowTranscriptPanel();
                break;
            case "participants":
                ShowParticipantsPanel();
                break;
            case "share":
                ShowSharePanel();
                break;
            case "settings":
                ShowSettingsPanel();
                break;
        }
    }

    private void ShowTranscriptPanel()
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(Text("Live transcript", 13, FontWeight.SemiBold));

        _transcriptMessages = new StackPanel { Spacing = 10 };
        _transcriptScroller = new ScrollViewer
        {
            MinHeight = 160,
            MaxHeight = 300,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _transcriptMessages
        };
        stack.Children.Add(_transcriptScroller);
        RenderTranscriptTurns();

        var composer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 7
        };
        var addButton = IconButton("plus", "Add context");
        addButton.Click += (_, _) => ShowAddMenu(addButton);
        composer.Children.Add(addButton);

        _transcriptInput = new HavenTextInput
        {
            Text = _viewModel.TypedTranscript,
            PlaceholderText = _viewModel.IsActive
                ? "Talk to Haven Voice"
                : "Choose Start before sending a message",
            MinHeight = 42,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsEnabled = _viewModel.IsActive
        };
        _transcriptInput.TextChanged += (_, _) =>
            _viewModel.TypedTranscript = _transcriptInput.Text ?? string.Empty;
        _transcriptInput.KeyDown += OnTranscriptInputKeyDown;
        Grid.SetColumn(_transcriptInput, 1);
        composer.Children.Add(_transcriptInput);

        _transcriptSendButton = IconButton("send", "Send typed message (Ctrl+Enter)");
        _transcriptSendButton.IsEnabled = _viewModel.SubmitTextCommand.CanExecute(null);
        _transcriptSendButton.Click += (_, _) => Execute(_viewModel.SubmitTextCommand);
        Grid.SetColumn(_transcriptSendButton, 2);
        composer.Children.Add(_transcriptSendButton);
        stack.Children.Add(composer);
        SetDetailContent(stack, 820);
        Dispatcher.UIThread.Post(() => _transcriptInput?.Focus());
    }

    private void OnTranscriptInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        Execute(_viewModel.SubmitTextCommand);
        e.Handled = true;
    }

    private void ClearTranscriptControls()
    {
        if (_transcriptInput is not null)
        {
            _transcriptInput.KeyDown -= OnTranscriptInputKeyDown;
        }

        _transcriptBody = null;
        _transcriptMessages = null;
        _transcriptScroller = null;
        _transcriptInput = null;
        _transcriptSendButton = null;
    }

    private void RefreshTranscriptControls()
    {
        RenderTranscriptTurns();

        if (_transcriptInput is not null &&
            !string.Equals(_transcriptInput.Text, _viewModel.TypedTranscript, StringComparison.Ordinal))
        {
            _transcriptInput.Text = _viewModel.TypedTranscript;
        }

        if (_transcriptInput is not null)
        {
            _transcriptInput.IsEnabled = _viewModel.IsActive;
            _transcriptInput.PlaceholderText = _viewModel.IsActive
                ? "Talk to Haven Voice"
                : "Choose Start before sending a message";
        }

        if (_transcriptSendButton is not null)
        {
            _transcriptSendButton.IsEnabled = _viewModel.SubmitTextCommand.CanExecute(null);
        }
    }

    private void RenderTranscriptTurns()
    {
        if (_transcriptMessages is null)
        {
            return;
        }

        _transcriptMessages.Children.Clear();
        if (_viewModel.TranscriptTurns.Count == 0)
        {
            _transcriptBody = Muted("Transcript will appear here while the session is active.");
            _transcriptMessages.Children.Add(_transcriptBody);
            return;
        }

        _transcriptBody = null;
        foreach (var turn in _viewModel.TranscriptTurns)
        {
            var isUser = turn.Role == MessageRole.User;
            var content = new StackPanel { Spacing = 5 };
            content.Children.Add(Text(isUser ? "You" : "Haven", 11, FontWeight.Bold));
            content.Children.Add(new TextBlock
            {
                Text = turn.Content,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            });

            if (turn.WasInterrupted)
            {
                content.Children.Add(Muted("Interrupted"));
            }

            _transcriptMessages.Children.Add(new HavenAdaptiveSurface
            {
                Child = content,
                MaxWidth = isUser ? 560 : 680,
                HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Background = isUser
                    ? Brush("HavenAccentTertiaryBrush")
                    : Brush("HavenCardSurfaceBrush"),
                BorderBrush = Brush("HavenBorderSubtleBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(16, 12)
            });
        }

        Dispatcher.UIThread.Post(
            () => _transcriptScroller?.ScrollToEnd(),
            DispatcherPriority.Background);
    }

    private void ShowParticipantsPanel()
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(Text("Participants", 13, FontWeight.SemiBold));
        stack.Children.Add(ParticipantRow("You", _viewModel.IsMuted ? "Muted" : "Microphone on"));
        stack.Children.Add(ParticipantRow("Haven", _viewModel.IsActive ? "Connected locally" : "Waiting"));
        SetDetailContent(stack, 410);
    }

    private void ShowSharePanel()
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(Text("Share", 13, FontWeight.SemiBold));
        stack.Children.Add(Muted(_viewModel.ScreenShareStatus));

        HavenButtonBase share = _viewModel.IsScreenSharing
            ? new HavenNegativeButton()
            : new HavenPrimaryButton();
        share.Content = _viewModel.IsScreenSharing ? "Stop sharing" : "Share Screen or App";
        share.IsEnabled = _viewModel.IsActive && _viewModel.CanShareScreen;
        share.HorizontalContentAlignment = HorizontalAlignment.Left;
        share.Click += (_, _) => Execute(_viewModel.ToggleScreenShareCommand);
        stack.Children.Add(share);

        var camera = new HavenSecondaryButton
        {
            Content = "Connect Camera",
            IsEnabled = false,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        ToolTip.SetTip(camera, "Camera capture is not exposed by the current call backend.");
        stack.Children.Add(camera);
        SetDetailContent(stack, 440);
    }

    private void ShowSettingsPanel()
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(Text("Settings", 16, FontWeight.Bold));

        var microphone = new HavenSelect
        {
            ItemsSource = _viewModel.InputDevices.Count == 0
                ? new[] { "System default" }
                : _viewModel.InputDevices.Select(item => item.Name).ToArray(),
            SelectedIndex = Math.Max(0, _viewModel.SelectedInputDevice is null
                ? 0
                : _viewModel.InputDevices.ToList().IndexOf(_viewModel.SelectedInputDevice)),
            MinWidth = 180,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        microphone.SelectionChanged += (_, _) =>
        {
            if (microphone.SelectedIndex >= 0 && microphone.SelectedIndex < _viewModel.InputDevices.Count)
            {
                _viewModel.SelectedInputDevice = _viewModel.InputDevices[microphone.SelectedIndex];
            }
        };
        stack.Children.Add(SettingRow("Microphone", microphone));

        var speedValue = Text($"{_viewModel.SpeechSpeedPercent}%", 12, FontWeight.Bold);
        stack.Children.Add(SettingRow("Speech Speed", speedValue));
        var speed = SessionSlider(50, 200, _viewModel.SpeechSpeedPercent);
        speed.PropertyChanged += (_, args) =>
        {
            if (args.Property == RangeBase.ValueProperty)
            {
                _viewModel.SpeechSpeedPercent = (int)Math.Round(speed.Value);
                speedValue.Text = $"{_viewModel.SpeechSpeedPercent}%";
            }
        };
        stack.Children.Add(speed);
        stack.Children.Add(Muted(_viewModel.SpeechSpeedPercent >= 150
            ? "Speak faster."
            : _viewModel.SpeechSpeedPercent <= 75 ? "Speak more slowly." : "Natural speaking speed."));
        SetDetailContent(stack, 410);
    }

    private void ShowModelPanel()
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(SettingRow("Model", Text(_viewModel.SelectedModelName, 14, FontWeight.Bold)));

        var voice = new HavenSelect
        {
            ItemsSource = _viewModel.Voices.Count == 0
                ? new[] { "System voice" }
                : _viewModel.Voices.Select(item => item.Name).ToArray(),
            SelectedIndex = Math.Max(0, _viewModel.SelectedVoice is null
                ? 0
                : _viewModel.Voices.ToList().IndexOf(_viewModel.SelectedVoice)),
            MinWidth = 220,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        voice.SelectionChanged += (_, _) =>
        {
            if (voice.SelectedIndex >= 0 && voice.SelectedIndex < _viewModel.Voices.Count)
            {
                _viewModel.SelectedVoice = _viewModel.Voices[voice.SelectedIndex];
            }
        };
        stack.Children.Add(SettingRow("Voice", voice));

        var reasoningValue = Text($"{_viewModel.ReasoningPercent}%", 14, FontWeight.Bold);
        stack.Children.Add(SettingRow("Reasoning", reasoningValue));
        var reasoning = SessionSlider(25, 100, _viewModel.ReasoningPercent);
        reasoning.PropertyChanged += (_, args) =>
        {
            if (args.Property == RangeBase.ValueProperty)
            {
                _viewModel.Effort = EffortFromPercent((int)Math.Round(reasoning.Value / 25) * 25);
                reasoningValue.Text = $"{_viewModel.ReasoningPercent}%";
            }
        };
        stack.Children.Add(reasoning);
        stack.Children.Add(Muted(_viewModel.ReasoningPercent >= 75
            ? "Slower, more accurate responses."
            : "Faster responses."));
        SetDetailContent(stack, 410);
    }

    private static Grid SettingRow(string label, Control value)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12
        };
        row.Children.Add(Text(label, 15, FontWeight.Bold));
        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        return row;
    }

    private static HavenSlider SessionSlider(double minimum, double maximum, double value) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Value = value,
        TickFrequency = maximum == 100 ? 25 : 10
    };

    private static EffortLevel EffortFromPercent(int percent) => percent switch
    {
        <= 25 => EffortLevel.Low,
        <= 50 => EffortLevel.Medium,
        <= 75 => EffortLevel.High,
        _ => EffortLevel.Max
    };

    private void ShowUnavailable(string message)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(Text("Voice session", 13, FontWeight.SemiBold));
        stack.Children.Add(Muted(message));
        SetDetailContent(stack, 440);
    }

    private void SetDetailContent(Control content, double width)
    {
        if (_detailPanel is null)
        {
            return;
        }

        _detailPanel.Width = width;
        _detailPanel.Child = content;
        _detailPanel.IsVisible = true;
    }

    private static Control ParticipantRow(string name, string state)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 9
        };
        row.Children.Add(new HavenAdaptiveSurface
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = Brush("HavenBlueSoftBrush"),
            Child = new TextBlock
            {
                Text = name[..1],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeight.Bold
            }
        });
        var label = Text(name, 12, FontWeight.SemiBold);
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        var stateText = Muted(state);
        Grid.SetColumn(stateText, 2);
        row.Children.Add(stateText);
        return row;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (e.PropertyName is nameof(InChatCallWidgetViewModel.TypedTranscript)
                or nameof(InChatCallWidgetViewModel.Transcript)
                or nameof(InChatCallWidgetViewModel.TranscriptTurns))
            {
                RefreshTranscriptControls();
                return;
            }

            Refresh();
        });
    }

    private void OnCallEnded(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _startedAt = null;
            Refresh();
        });
    }

    private void Refresh()
    {
        if (_surface is null ||
            _statusText is null ||
            _durationText is null ||
            _summaryText is null ||
            _muteLabel is null ||
            _muteIcon is null ||
            _callActionButton is null ||
            _voiceModeSelect is null ||
            _reactionText is null ||
            _toolbar is null)
        {
            return;
        }

        _surface.IsVisible = _viewModel.IsVisible;
        _statusText.Text = _viewModel.Status;
        var selectedVoiceModeIndex = _viewModel.VoiceProfiles
            .Select((profile, index) => (profile, index))
            .FirstOrDefault(pair => pair.profile.Id == _viewModel.SelectedVoiceProfile?.Id).index;
        if (selectedVoiceModeIndex >= 0 && _voiceModeSelect.SelectedIndex != selectedVoiceModeIndex)
            _voiceModeSelect.SelectedIndex = selectedVoiceModeIndex;
        _voiceModeSelect.IsEnabled = !_viewModel.IsActive;
        _reactionText.Text = _viewModel.IsActive ? _viewModel.LiveReaction : "Choose a live Voice mode";
        _summaryText.Text = _viewModel.CallSummary ?? string.Empty;
        _summaryText.IsVisible = !string.IsNullOrWhiteSpace(_summaryText.Text);
        _muteLabel.Text = _viewModel.IsMuted ? "OFF" : "ON";
        _muteIcon.Data = HavenIcon.GeometryFor(_viewModel.IsMuted ? "mute" : "mic");
        _callActionButton.Content = _viewModel.IsActive ? "End" : "Start";
        _callActionButton.IsEnabled = _viewModel.IsActive || _viewModel.StartCallCommand.CanExecute(null);
        _callActionButton.Classes.Set("negative", _viewModel.IsActive);
        AutomationProperties.SetName(_callActionButton, _viewModel.IsActive ? "End Voice call" : "Start Voice call");

        if (_viewModel.IsActive && _startedAt is null)
        {
            _startedAt = DateTimeOffset.Now;
        }
        else if (!_viewModel.IsActive)
        {
            _startedAt = null;
        }

        if (_openPanel == "transcript")
        {
            RefreshTranscriptControls();
        }
        else if (_openPanel == "participants")
        {
            ShowParticipantsPanel();
        }
        else if (_openPanel == "share")
        {
            ShowSharePanel();
        }
        else if (_openPanel == "model")
        {
            ShowModelPanel();
        }

        UpdateDuration();
    }

    private void UpdateDuration()
    {
        if (_durationText is null)
        {
            return;
        }

        if (_startedAt is null)
        {
            _durationText.Text = "Ready";
            return;
        }

        var elapsed = DateTimeOffset.Now - _startedAt.Value;

        _durationText.Text = elapsed.TotalHours >= 1
            ? elapsed.ToString(@"hh\:mm\:ss")
            : elapsed.ToString(@"mm\:ss");
    }

    private static void Execute(ICommand command)
    {
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private static HavenHeaderIconButton IconButton(string iconKey, string toolTip)
    {
        var button = new HavenHeaderIconButton
        {
            Content = new PathIcon
            {
                Data = HavenIcon.GeometryFor(iconKey),
                Width = 20,
                Height = 20,
                Foreground = Brush("HavenTextPrimaryBrush")
            },
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        ToolTip.SetTip(button, toolTip);
        return button;
    }

    private static HavenHeaderPillButton ActionButton(string text, bool positive)
    {
        var button = new HavenHeaderPillButton
        {
            Content = text,
            MinWidth = 112,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        if (!positive) button.Classes.Add("negative");
        return button;
    }

    private static TextBlock Text(string value, double size, FontWeight weight) =>
        new()
        {
            Text = value,
            FontSize = size,
            FontWeight = weight,
            VerticalAlignment = VerticalAlignment.Center
        };

    private static TextBlock Muted(string value) =>
        new()
        {
            Text = value,
            FontSize = 11,
            Opacity = 0.66,
            TextWrapping = TextWrapping.Wrap
        };

    private static IBrush? Brush(string key) =>
        Avalonia.Application.Current?.TryFindResource(key, out var value) == true
            ? value as IBrush
            : null;

    public void Dispose()
    {
        _durationTimer.Stop();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.CallEnded -= OnCallEnded;
    }
}
