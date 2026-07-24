using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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
    private TextBlock? _transcriptText;
    private TextBlock? _summaryText;
    private TextBlock? _muteLabel;
    private ProgressBar? _audioLevel;
    private StackPanel? _inactivePanel;
    private StackPanel? _activeToolbar;
    private Border? _detailPanel;
    private DateTimeOffset? _startedAt;
    private string? _openPanel;

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
        _statusText = Text("Ready", 13, FontWeight.SemiBold);
        _durationText = Text("00:00", 12, FontWeight.Medium);
        _transcriptText = Text(string.Empty, 13, FontWeight.Normal);
        _transcriptText.TextWrapping = TextWrapping.Wrap;
        _summaryText = Text(string.Empty, 11, FontWeight.Normal);
        _summaryText.Opacity = 0.68;
        _muteLabel = Text("Mute", 12, FontWeight.SemiBold);
        _audioLevel = new ProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Height = 5,
            MinWidth = 130,
            VerticalAlignment = VerticalAlignment.Center
        };

        var titleStack = new StackPanel { Spacing = 1 };
        titleStack.Children.Add(Text("Voice session", 16, FontWeight.Bold));
        titleStack.Children.Add(_statusText);

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 10
        };
        header.Children.Add(titleStack);
        Grid.SetColumn(_durationText, 1);
        header.Children.Add(_durationText);

        var closeButton = IconButton("×", "Close voice session");
        closeButton.Click += (_, _) => _viewModel.Close();
        Grid.SetColumn(closeButton, 2);
        header.Children.Add(closeButton);

        _inactivePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        var startButton = ActionButton("Start", true);
        startButton.Click += (_, _) => Execute(_viewModel.StartCallCommand);
        _inactivePanel.Children.Add(startButton);

        _activeToolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var addButton = IconButton("+", "Add context");
        addButton.Click += (_, _) => ShowAddMenu(addButton);

        var endButton = ActionButton("End", false);
        endButton.Background = new SolidColorBrush(Color.Parse("#CF3035"));
        endButton.Foreground = Brushes.White;
        endButton.Click += (_, _) => Execute(_viewModel.EndCallCommand);

        var muteButton = IconButton("🎙", "Mute or unmute");
        muteButton.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { new TextBlock { Text = "🎙", FontSize = 15 }, _muteLabel }
        };
        muteButton.Click += (_, _) => Execute(_viewModel.ToggleMuteCommand);

        var transcriptButton = IconButton("Transcript", "Show transcript");
        transcriptButton.Click += (_, _) => TogglePanel("transcript");

        var participantsButton = IconButton("People", "Show participants");
        participantsButton.Click += (_, _) => TogglePanel("participants");

        var shareButton = IconButton("Share", "Share and camera options");
        shareButton.Click += (_, _) => TogglePanel("share");

        var settingsButton = IconButton("Settings", "Voice session settings");
        settingsButton.Click += (_, _) => TogglePanel("settings");

        _activeToolbar.Children.Add(addButton);
        _activeToolbar.Children.Add(endButton);
        _activeToolbar.Children.Add(muteButton);
        _activeToolbar.Children.Add(transcriptButton);
        _activeToolbar.Children.Add(participantsButton);
        _activeToolbar.Children.Add(shareButton);
        _activeToolbar.Children.Add(settingsButton);

        var levelRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 10
        };
        levelRow.Children.Add(Text("Input", 10, FontWeight.Medium));
        Grid.SetColumn(_audioLevel, 1);
        levelRow.Children.Add(_audioLevel);

        _detailPanel = new Border
        {
            IsVisible = false,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(12),
            Background = Brush("HavenPanel2Brush"),
            BorderBrush = Brush("HavenLineBrush"),
            BorderThickness = new Thickness(1)
        };

        var root = new StackPanel { Spacing = 12 };
        root.Children.Add(header);
        root.Children.Add(levelRow);
        root.Children.Add(_detailPanel);
        root.Children.Add(_summaryText);
        root.Children.Add(_inactivePanel);
        root.Children.Add(_activeToolbar);

        _surface = new Border
        {
            Width = 560,
            MaxWidth = 620,
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(22),
            Background = Brush("HavenPanelBrush") ?? Brushes.White,
            BorderBrush = Brush("HavenLineBrush") ?? new SolidColorBrush(Color.Parse("#22000000")),
            BorderThickness = new Thickness(1),
            Child = root,
            IsVisible = false
        };

        CodeBehindHost.Children.Clear();
        CodeBehindHost.Children.Add(_surface);
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
                var remove = IconButton("×", $"Remove {item}");
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

        SetDetailContent(stack);
    }

    private void TogglePanel(string panel)
    {
        if (_openPanel == panel)
        {
            _openPanel = null;
            if (_detailPanel is not null)
            {
                _detailPanel.IsVisible = false;
                _detailPanel.Child = null;
            }

            return;
        }

        _openPanel = panel;
        switch (panel)
        {
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

        var scroll = new ScrollViewer
        {
            MaxHeight = 230,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _transcriptText
        };
        stack.Children.Add(scroll);
        SetDetailContent(stack);
    }

    private void ShowParticipantsPanel()
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(Text("Participants", 13, FontWeight.SemiBold));
        stack.Children.Add(ParticipantRow("You", _viewModel.IsMuted ? "Muted" : "Microphone on"));
        stack.Children.Add(ParticipantRow("Haven", _viewModel.IsActive ? "Connected locally" : "Waiting"));
        SetDetailContent(stack);
    }

    private void ShowSharePanel()
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(Text("Share", 13, FontWeight.SemiBold));
        stack.Children.Add(Muted(
            "The current local call backend does not expose screen or camera capture. " +
            "These controls remain disabled rather than pretending to share content."));

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new Button { Content = "Share screen", IsEnabled = false });
        row.Children.Add(new Button { Content = "Camera", IsEnabled = false });
        stack.Children.Add(row);
        SetDetailContent(stack);
    }

    private void ShowSettingsPanel()
    {
        var stack = new StackPanel { Spacing = 9 };
        stack.Children.Add(Text("Voice session settings", 13, FontWeight.SemiBold));

        stack.Children.Add(Muted("Microphone"));
        var microphone = new ComboBox
        {
            ItemsSource = new[] { "System default" },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        stack.Children.Add(microphone);

        stack.Children.Add(Muted("Voice"));
        var voice = new ComboBox
        {
            ItemsSource = new[] { "Haven Neural", "System voice" },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        stack.Children.Add(voice);

        stack.Children.Add(Muted("Reasoning"));
        var reasoning = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var percent in new[] { 25, 50, 75, 100 })
        {
            var button = new Button { Content = $"{percent}%" };
            button.Click += (_, _) => ShowUnavailable(
                $"Reasoning set to {percent}% for the next Chat request. Call reasoning remains backend-managed.");
            reasoning.Children.Add(button);
        }

        stack.Children.Add(reasoning);
        SetDetailContent(stack);
    }

    private void ShowUnavailable(string message)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(Text("Voice session", 13, FontWeight.SemiBold));
        stack.Children.Add(Muted(message));
        SetDetailContent(stack);
    }

    private void SetDetailContent(Control content)
    {
        if (_detailPanel is null)
        {
            return;
        }

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
        Dispatcher.UIThread.Post(Refresh);
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
            _transcriptText is null ||
            _summaryText is null ||
            _muteLabel is null ||
            _audioLevel is null ||
            _inactivePanel is null ||
            _activeToolbar is null)
        {
            return;
        }

        _surface.IsVisible = _viewModel.IsVisible;
        _statusText.Text = _viewModel.Status;
        _transcriptText.Text = string.IsNullOrWhiteSpace(_viewModel.Transcript)
            ? "Transcript will appear here while the session is active."
            : _viewModel.Transcript;
        _summaryText.Text = _viewModel.CallSummary ?? string.Empty;
        _summaryText.IsVisible = !string.IsNullOrWhiteSpace(_summaryText.Text);
        _muteLabel.Text = _viewModel.IsMuted ? "Unmute" : "Mute";
        _audioLevel.Value = Math.Clamp(_viewModel.AudioLevel, 0, 1);

        _inactivePanel.IsVisible = !_viewModel.IsActive;
        _activeToolbar.IsVisible = _viewModel.IsActive;

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
            ShowTranscriptPanel();
        }
        else if (_openPanel == "participants")
        {
            ShowParticipantsPanel();
        }

        UpdateDuration();
    }

    private void UpdateDuration()
    {
        if (_durationText is null)
        {
            return;
        }

        var elapsed = _startedAt is null
            ? TimeSpan.Zero
            : DateTimeOffset.Now - _startedAt.Value;

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

    private static Button IconButton(string text, string toolTip)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 34,
            MinWidth = 34,
            Padding = new Thickness(10, 6),
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
            MinHeight = 38,
            Padding = new Thickness(18, 8),
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
