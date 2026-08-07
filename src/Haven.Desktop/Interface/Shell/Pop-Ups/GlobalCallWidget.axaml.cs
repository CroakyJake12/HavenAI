using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using Avalonia;
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
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views.Shell.Overlays;

public sealed partial class GlobalCallWidget : UserControl, IDisposable
{
    private readonly InChatCallWidgetViewModel _viewModel;
    private readonly DispatcherTimer _durationTimer;
    private readonly List<string> _selectedContext = [];

    private Border? _surface;
    private TextBlock? _statusText;
    private TextBlock? _durationText;
    private TextBlock? _summaryText;
    private TextBlock? _muteLabel;
    private PathIcon? _muteIcon;
    private Button? _callActionButton;
    private TextBlock? _transcriptBody;
    private StackPanel? _transcriptMessages;
    private ScrollViewer? _transcriptScroller;
    private TextBox? _transcriptInput;
    private Button? _transcriptSendButton;
    private StackPanel? _toolbar;
    private Border? _detailPanel;
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
            Foreground = Brushes.Black
        };

        var titleArea = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            Child = new StackPanel
            {
                Spacing = 0,
                Children =
                {
                    Text("Voice", 16, FontWeight.Bold)
                }
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

        _toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 7
        };
        _callActionButton = ActionButton("Start", true);
        _callActionButton.Width = 124;
        _callActionButton.Click += (_, _) => Execute(
            _viewModel.IsActive ? _viewModel.EndCallCommand : _viewModel.StartCallCommand);

        var muteButton = IconButton("mic", "Mute or unmute");
        muteButton.Width = 102;
        muteButton.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { _muteIcon, _muteLabel }
        };
        muteButton.Click += (_, _) => Execute(_viewModel.ToggleMuteCommand);

        var transcriptButton = IconButton("chat", "Show transcript");
        transcriptButton.Click += (_, _) => TogglePanel("transcript");

        var participantsButton = IconButton("agents", "Model, voice and reasoning");
        participantsButton.Click += (_, _) => TogglePanel("model");

        var shareButton = IconButton("screen-share", "Share screen or app");
        shareButton.Click += (_, _) => TogglePanel("share");

        var settingsButton = IconButton("settings", "Voice session settings");
        settingsButton.Click += (_, _) => TogglePanel("settings");

        _toolbar.Children.Add(_callActionButton);
        _toolbar.Children.Add(muteButton);
        _toolbar.Children.Add(transcriptButton);
        _toolbar.Children.Add(participantsButton);
        _toolbar.Children.Add(shareButton);
        _toolbar.Children.Add(settingsButton);

        _detailPanel = new Border
        {
            IsVisible = false,
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(18),
            Background = Brush("HavenPanel2Brush"),
            BorderBrush = Brush("HavenLineBrush"),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var compactContent = new StackPanel { Spacing = 10 };
        compactContent.Children.Add(header);
        compactContent.Children.Add(_summaryText);
        compactContent.Children.Add(_toolbar);

        var compactBar = new Border
        {
            Width = 570,
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(34),
            Background = new SolidColorBrush(Color.Parse("#F1F1EF")),
            Child = compactContent
        };

        var root = new StackPanel { Spacing = 8 };
        root.Children.Add(_detailPanel);
        root.Children.Add(compactBar);

        _surface = new Border
        {
            Width = 820,
            MaxWidth = 840,
            Background = Brushes.Transparent,
            Child = root,
            IsVisible = false
        };

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

        new Flyout
        {
            Placement = PlacementMode.TopEdgeAlignedRight,
            Content = panel
        }.ShowAt(placementTarget);
    }

    private static void AddMenuButton(StackPanel panel, string label, Action action)
    {
        var button = new Button
        {
            Content = label,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        button.Classes.Add("sidebar");
        button.Click += (_, _) => action();
        panel.Children.Add(button);
    }

    private static void AddMenuButton(StackPanel panel, string label, Func<System.Threading.Tasks.Task> action)
    {
        var button = new Button
        {
            Content = label,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        button.Classes.Add("sidebar");
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

        _transcriptInput = new TextBox
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

            _transcriptMessages.Children.Add(new Border
            {
                Child = content,
                MaxWidth = isUser ? 560 : 680,
                HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Background = isUser
                    ? Brush("HavenAccentSoftBrush") ?? new SolidColorBrush(Color.Parse("#E0F7FA"))
                    : new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(35, 0, 0, 0)),
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

        var share = new Button
        {
            Content = _viewModel.IsScreenSharing ? "Stop sharing" : "Share Screen or App",
            IsEnabled = _viewModel.IsActive && _viewModel.CanShareScreen,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        share.Click += (_, _) => Execute(_viewModel.ToggleScreenShareCommand);
        stack.Children.Add(share);

        var camera = new Button
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

        var microphone = new ComboBox
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
        var speed = SessionSlider(50, 200, _viewModel.SpeechSpeedPercent, "#56E0EE", "#FFE864");
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

    private void ShowLegacySettingsPanel()
    {
        var stack = new StackPanel { Spacing = 9 };
        stack.Children.Add(Text("Voice session settings", 13, FontWeight.SemiBold));

        stack.Children.Add(Muted("Microphone"));
        var microphone = new ComboBox
        {
            ItemsSource = _viewModel.InputDevices.Count == 0
                ? new[] { "System default" }
                : _viewModel.InputDevices.Select(item => item.Name).ToArray(),
            SelectedIndex = Math.Max(0, _viewModel.SelectedInputDevice is null
                ? 0
                : _viewModel.InputDevices.ToList().IndexOf(_viewModel.SelectedInputDevice)),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        microphone.SelectionChanged += (_, _) =>
        {
            if (microphone.SelectedIndex >= 0 && microphone.SelectedIndex < _viewModel.InputDevices.Count)
            {
                _viewModel.SelectedInputDevice = _viewModel.InputDevices[microphone.SelectedIndex];
            }
        };
        stack.Children.Add(microphone);

        stack.Children.Add(Muted("Voice"));
        var voice = new ComboBox
        {
            ItemsSource = _viewModel.Voices.Count == 0
                ? new[] { "System voice" }
                : _viewModel.Voices.Select(item => item.Name).ToArray(),
            SelectedIndex = Math.Max(0, _viewModel.SelectedVoice is null
                ? 0
                : _viewModel.Voices.ToList().IndexOf(_viewModel.SelectedVoice)),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        voice.SelectionChanged += (_, _) =>
        {
            if (voice.SelectedIndex >= 0 && voice.SelectedIndex < _viewModel.Voices.Count)
            {
                _viewModel.SelectedVoice = _viewModel.Voices[voice.SelectedIndex];
            }
        };
        stack.Children.Add(voice);

        stack.Children.Add(Muted("Reasoning"));
        var reasoning = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var percent in new[] { 25, 50, 75, 100 })
        {
            var button = new Button { Content = $"{percent}%" };
            button.Click += (_, _) =>
            {
                _viewModel.Effort = EffortFromPercent(percent);
                ShowSettingsPanel();
            };
            reasoning.Children.Add(button);
        }

        stack.Children.Add(reasoning);
        stack.Children.Add(Muted($"Speech speed: System default · Reasoning: {_viewModel.ReasoningPercent}%"));
        SetDetailContent(stack, 440);
    }

    private void ShowModelPanel()
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(SettingRow("Model", Text(_viewModel.SelectedModelName, 14, FontWeight.Bold)));

        var voice = new ComboBox
        {
            ItemsSource = _viewModel.Voices.Count == 0
                ? new[] { "System voice" }
                : _viewModel.Voices.Select(item => item.Name).ToArray(),
            SelectedIndex = Math.Max(0, _viewModel.SelectedVoice is null
                ? 0
                : _viewModel.Voices.ToList().IndexOf(_viewModel.SelectedVoice)),
            MinWidth = 180,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeight.Bold
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
        var reasoning = SessionSlider(25, 100, _viewModel.ReasoningPercent, "#FFF86A", "#FFB928");
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

    private void ShowLegacyModelPanel()
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(Text("Voice model", 13, FontWeight.SemiBold));
        stack.Children.Add(Text(_viewModel.SelectedModelName, 14, FontWeight.Bold));
        stack.Children.Add(Muted(
            $"Voice: {_viewModel.SelectedVoice?.Name ?? "System voice"} · " +
            $"Reasoning: {_viewModel.ReasoningPercent}%"));

        var reasoning = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var percent in new[] { 25, 50, 75, 100 })
        {
            var button = new Button
            {
                Content = $"{percent}%",
                IsEnabled = _viewModel.ReasoningPercent != percent
            };
            button.Click += (_, _) =>
            {
                _viewModel.Effort = EffortFromPercent(percent);
                ShowModelPanel();
            };
            reasoning.Children.Add(button);
        }

        stack.Children.Add(reasoning);
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

    private static Slider SessionSlider(double minimum, double maximum, double value, string start, string end) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Value = value,
        Height = 34,
        TickFrequency = maximum == 100 ? 25 : 10,
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse(start), 0),
                new GradientStop(Color.Parse(end), 1)
            }
        }
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
        row.Children.Add(new Border
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
            _toolbar is null)
        {
            return;
        }

        _surface.IsVisible = _viewModel.IsVisible;
        _statusText.Text = _viewModel.Status;
        _summaryText.Text = _viewModel.CallSummary ?? string.Empty;
        _summaryText.IsVisible = !string.IsNullOrWhiteSpace(_summaryText.Text);
        _muteLabel.Text = _viewModel.IsMuted ? "OFF" : "ON";
        _muteIcon.Data = HavenIcon.GeometryFor(_viewModel.IsMuted ? "mute" : "mic");
        _callActionButton.Content = _viewModel.IsActive ? "End" : "Start";
        _callActionButton.IsEnabled = _viewModel.IsActive || _viewModel.StartCallCommand.CanExecute(null);
        _callActionButton.Background = _viewModel.IsActive
            ? new SolidColorBrush(Color.Parse("#FF6B61"))
            : Brush("HavenCyanBrush") ?? new SolidColorBrush(Color.Parse("#55D9E8"));
        _callActionButton.Foreground = _viewModel.IsActive ? Brushes.White : Brushes.Black;

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

    private static Button IconButton(string iconKey, string toolTip)
    {
        var button = new Button
        {
            Content = new PathIcon
            {
                Data = HavenIcon.GeometryFor(iconKey),
                Width = 20,
                Height = 20,
                Foreground = Brushes.Black
            },
            Width = 54,
            Height = 54,
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(27),
            Background = new SolidColorBrush(Color.Parse("#F7EFF8")),
            Foreground = Brushes.Black,
            BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        button.Classes.Add("chrome");
        ToolTip.SetTip(button, toolTip);
        return button;
    }

    private static Button ActionButton(string text, bool positive)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 92,
            MinHeight = 44,
            Padding = new Thickness(18, 8),
            CornerRadius = new CornerRadius(22),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };

        button.Classes.Add(positive ? "accent" : "primary");
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
