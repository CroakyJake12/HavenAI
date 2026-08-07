using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Views.Pages.Tasks;

/// <summary>
/// Repository-backed Haven Tasks dashboard and reusable-task editor. The
/// existing workspace-state rows are treated as compatibility storage only;
/// the user-facing surface and vocabulary are Tasks throughout.
/// </summary>
public sealed partial class TasksPage : UserControl
{
    private static IBrush CardBrush => PaletteBrush("HavenPanelBrush", "#FFFEF8");
    private static IBrush FieldBrush => PaletteBrush("HavenPanel2Brush", "#FAFAF7");
    private static IBrush BorderStroke => PaletteBrush("HavenLineBrush", "#E9E9E2");
    private static IBrush AccentBrush => PaletteBrush("HavenAccentBrush", "#5AAE2B");
    private static IBrush AccentInkBrush => PaletteBrush("HavenAccentInkBrush", "#FFFFFF");
    private static IBrush AccentSoftBrush => PaletteBrush("HavenAccentSoftBrush", "#E5F7D4");
    private static IBrush AccentPaleBrush => PaletteBrush("HavenAccentSoftBrush", "#E5F7D4");
    private static IBrush TextBrush => PaletteBrush("HavenTextBrush", "#111111");
    private static IBrush MutedBrush => PaletteBrush("HavenMutedBrush", "#5E5E5E");

    private readonly IWorkspaceStateRepository _tasks;
    private readonly IAutomationRepository _automations;
    private readonly Guid? _containerId;
    private readonly Func<Task> _startOneTimeTask;
    private readonly Func<string, Task> _runTask;
    private readonly WrapPanel _manualItems = new() { Orientation = Orientation.Horizontal };
    private readonly WrapPanel _automaticItems = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _emptyText = Muted("No reusable tasks yet. Create one to give Haven a repeatable outcome and rules.");
    private readonly TextBlock _status = Muted(string.Empty);
    private readonly Grid _dashboard = new();
    private readonly Grid _editor = new();
    private readonly ScrollViewer _editorScroll = new()
    {
        IsVisible = false,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };
    private readonly TextBlock _editorTitle = Heading("Create Task", 42);
    private readonly TextBox _name = Field("Task Name");
    private readonly TextBox _goal = Field("Your Desired Outcome");
    private readonly TextBox _rules = Field("Define rules that must be followed.", multiline: true);
    private readonly TextBlock _assistantMessage = new()
    {
        Text = "Ask me anything about this task. I can configure it, test it, and explain how it will run.",
        TextWrapping = TextWrapping.Wrap,
        FontSize = 15,
        FontWeight = FontWeight.SemiBold
    };
    private readonly TextBox _assistant = Field("Ask Haven Anything");
    private readonly Border _instructionsOverlay = new() { IsVisible = false };
    private readonly TextBox _instructions = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = "Cascadia Mono, Consolas",
        MinHeight = 420,
        Padding = new Thickness(18),
        Background = FieldBrush,
        BorderBrush = BorderStroke,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(16)
    };
    private MacroDefinition? _editing;
    private bool _editMode;
    private bool _busy;

    public TasksPage(
        IWorkspaceStateRepository tasks,
        IAutomationRepository automations,
        Guid? containerId,
        Func<Task> startOneTimeTask,
        Func<string, Task> runTask)
    {
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _automations = automations ?? throw new ArgumentNullException(nameof(automations));
        _containerId = containerId;
        _startOneTimeTask = startOneTimeTask ?? throw new ArgumentNullException(nameof(startOneTimeTask));
        _runTask = runTask ?? throw new ArgumentNullException(nameof(runTask));

        InitializeComponent();
        var host = this.FindControl<Grid>("CodeBehindHost")
            ?? throw new InvalidOperationException("Tasks host was not initialized.");
        host.Children.Add(BuildLayout());
    }

    private Control BuildLayout()
    {
        // The window owns the surface-aware dual-tone tide. A page-local
        // colour would cover it and break continuity across the shell.
        var root = new Grid { Background = Brushes.Transparent };
        BuildDashboard();
        BuildEditor();
        BuildInstructionsOverlay();
        root.Children.Add(_dashboard);
        _editorScroll.Content = _editor;
        root.Children.Add(_editorScroll);
        root.Children.Add(_instructionsOverlay);
        return root;
    }

    private void BuildDashboard()
    {
        var runTab = TabButton("Run", selected: true);
        var editTab = TabButton("Edit", selected: false);
        runTab.Click += (_, _) =>
        {
            _editMode = false;
            ApplyTabState(runTab, true);
            ApplyTabState(editTab, false);
            _status.Text = "Choose a task to run it in a persistent Tasks conversation.";
        };
        editTab.Click += (_, _) =>
        {
            _editMode = true;
            ApplyTabState(runTab, false);
            ApplyTabState(editTab, true);
            _status.Text = "Choose a reusable task to edit it.";
        };

        var oneTime = SoftButton("Run a One-Time Task");
        oneTime.MinWidth = 250;
        oneTime.Click += async (_, _) => await StartOneTimeTaskAsync();

        var create = AccentButton("+ Create a Re-Usable Task");
        create.MinWidth = 280;
        create.Click += (_, _) => ShowEditor(null);

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 10,
            Children =
            {
                new StackPanel
                {
                    Spacing = 18,
                    Children =
                    {
                        Heading("Haven Tasks", 44),
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { runTab, editTab } }
                    }
                },
                Column(oneTime, 1),
                Column(create, 2)
            }
        };

        var content = new StackPanel
        {
            Spacing = 28,
            Margin = new Thickness(34, 28, 34, 42),
            Children =
            {
                header,
                TaskSection("Manual Tasks", _manualItems),
                TaskSection("Automatic Tasks", _automaticItems),
                _emptyText,
                _status
            }
        };

        _dashboard.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content
        });
    }

    private void BuildEditor()
    {
        var discard = SoftButton("Discard Changes");
        discard.Click += (_, _) => ShowDashboard();
        var save = AccentButton("Save Changes");
        save.Click += async (_, _) => await SaveAsync();

        var test = AccentButton("Test Task");
        test.HorizontalAlignment = HorizontalAlignment.Stretch;
        test.Click += async (_, _) => await TestAsync();
        var viewInstructions = SoftButton("View Task Instructions");
        viewInstructions.HorizontalAlignment = HorizontalAlignment.Stretch;
        viewInstructions.Click += (_, _) => ShowInstructions();

        var form = new Border
        {
            Width = 390,
            Background = CardBrush,
            BorderBrush = BorderStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(18),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,*,Auto,Auto"),
                RowSpacing = 10,
                Children =
                {
                    Label("Name"),
                    Row(_name, 1),
                    Row(Label("Goal"), 2),
                    Row(_goal, 3),
                    Row(Label("Rules"), 4),
                    Row(_rules, 5),
                    Row(new TextBlock { Text = "Type: Manual", FontSize = 15, FontWeight = FontWeight.Bold, Margin = new Thickness(2, 10, 0, 0) }, 6),
                    Row(test, 7),
                    Row(viewInstructions, 8)
                }
            }
        };

        var assistantBubble = new Border
        {
            MaxWidth = 830,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = CardBrush,
            BorderBrush = BorderStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(18),
            Child = _assistantMessage
        };
        var assistantSend = AccentIconButton("send", "Ask Haven about this task");
        assistantSend.Click += async (_, _) => await AskAssistantAsync();
        _assistant.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                e.Handled = true;
                await AskAssistantAsync();
            }
        };

        var composer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 10,
            Children =
            {
                RoundButton("plus", "Task context is included"),
                Column(_assistant, 1),
                Column(assistantSend, 2)
            }
        };

        var main = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 16,
            Margin = new Thickness(24, 0, 0, 0),
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                    ColumnSpacing = 10,
                    Children = { _editorTitle, Column(discard, 1), Column(save, 2) }
                },
                Row(new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Spacing = 8,
                    Children = { Label("Haven"), assistantBubble }
                }, 1),
                Row(composer, 2)
            }
        };

        _editor.Margin = new Thickness(24, 26, 24, 24);
        _editor.ColumnDefinitions = new ColumnDefinitions("390,*");
        _editor.Children.Add(form);
        Grid.SetColumn(main, 1);
        _editor.Children.Add(main);
    }

    private void BuildInstructionsOverlay()
    {
        var cancel = SoftButton("Cancel");
        cancel.Click += (_, _) => _instructionsOverlay.IsVisible = false;
        var apply = AccentButton("Apply");
        apply.Click += (_, _) =>
        {
            ParseInstructions(_instructions.Text ?? string.Empty);
            _instructionsOverlay.IsVisible = false;
            _status.Text = "Task instructions applied to this draft.";
        };

        _instructionsOverlay.Margin = new Thickness(48);
        _instructionsOverlay.Background = CardBrush;
        _instructionsOverlay.BorderBrush = BorderStroke;
        _instructionsOverlay.BorderThickness = new Thickness(1);
        _instructionsOverlay.CornerRadius = new CornerRadius(30);
        _instructionsOverlay.Padding = new Thickness(24);
        _instructionsOverlay.Child = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 14,
            Children =
            {
                Heading("Task Instructions", 36),
                Row(_instructions, 1),
                Row(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { cancel, apply }
                }, 2)
            }
        };
    }

    private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        _manualItems.Children.Clear();
        _automaticItems.Children.Clear();
        _status.Text = "Loading Haven Tasks…";
        try
        {
            var reusableTask = _tasks.GetMacrosAsync(_containerId, CancellationToken.None);
            var scheduledTask = _automations.GetAllAsync(CancellationToken.None);
            await Task.WhenAll(reusableTask, scheduledTask);

            foreach (var item in reusableTask.Result.Where(item => item.IsEnabled))
                _manualItems.Children.Add(TaskChip(item.Name, async () =>
                {
                    if (_editMode) ShowEditor(item);
                    else await InvokeAsync(item.Instruction);
                }, item));

            foreach (var item in scheduledTask.Result
                         .Where(item => item.IsEnabled && item.ContainerId == _containerId)
                         .OrderBy(item => item.NextRunAt))
            {
                var detail = item.NextRunAt is null
                    ? "Waiting for trigger"
                    : "Next " + item.NextRunAt.Value.LocalDateTime.ToString("g");
                _automaticItems.Children.Add(TaskChip(item.Name, () => InvokeAsync(item.Instruction), detail));
            }

            _emptyText.IsVisible = _manualItems.Children.Count == 0 && _automaticItems.Children.Count == 0;
            _status.Text = $"{_manualItems.Children.Count} reusable and {_automaticItems.Children.Count} automatic task{(_manualItems.Children.Count + _automaticItems.Children.Count == 1 ? string.Empty : "s")} available.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _status.Text = "Haven Tasks could not be loaded: " + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private Control TaskChip(string name, Func<Task> action, object context)
    {
        var button = new Button
        {
            Content = name,
            Margin = new Thickness(0, 0, 12, 10),
            MinWidth = 160,
            MinHeight = 54,
            Padding = new Thickness(20, 10),
            CornerRadius = new CornerRadius(24),
            Background = AccentPaleBrush,
            BorderThickness = new Thickness(0),
            FontSize = 15,
            FontStyle = FontStyle.Italic,
            FontWeight = FontWeight.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        button.Click += async (_, _) => await action();
        if (context is MacroDefinition item)
        {
            button.ContextMenu = new ContextMenu
            {
                ItemsSource = new object[]
                {
                    MenuItem("Edit task", () => ShowEditor(item)),
                    MenuItem("Test safely", () => _ = TestDefinitionAsync(item)),
                    MenuItem("Delete task", () => _ = DeleteAsync(item))
                }
            };
        }
        else if (context is string detail)
        {
            ToolTip.SetTip(button, detail);
        }
        AutomationProperties.SetName(button, $"{(_editMode ? "Edit" : "Run")} task {name}");
        return button;
    }

    private void ShowEditor(MacroDefinition? item)
    {
        _editing = item;
        _editorTitle.Text = item is null ? "Create Task" : "Edit Task";
        _name.Text = item?.Name ?? string.Empty;
        _goal.Text = item?.Description ?? string.Empty;
        _rules.Text = ExtractRules(item?.Instruction);
        _assistant.Text = string.Empty;
        _assistantMessage.Text = "Ask me anything about this task. I can configure it, test it, and explain how it will run.";
        _dashboard.IsVisible = false;
        _editorScroll.IsVisible = true;
        _instructionsOverlay.IsVisible = false;
    }

    private void ShowDashboard()
    {
        _editing = null;
        _editorScroll.IsVisible = false;
        _instructionsOverlay.IsVisible = false;
        _dashboard.IsVisible = true;
        _ = RefreshAsync();
    }

    private async Task SaveAsync()
    {
        var name = _name.Text?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            _status.Text = "Task Name is required.";
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var item = new MacroDefinition(
            _editing?.Id ?? Guid.NewGuid(),
            name,
            _goal.Text?.Trim() ?? string.Empty,
            BuildInstructions(),
            _containerId,
            true,
            _editing?.CreatedAt ?? now,
            now);
        await _tasks.UpsertMacroAsync(item, CancellationToken.None);
        _status.Text = $"Saved {name}.";
        ShowDashboard();
    }

    private async Task TestAsync() => await TestDefinitionAsync(new MacroDefinition(
        _editing?.Id ?? Guid.Empty,
        _name.Text?.Trim() ?? "Task draft",
        _goal.Text?.Trim() ?? string.Empty,
        BuildInstructions(),
        _containerId,
        true,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow));

    private async Task TestDefinitionAsync(MacroDefinition item) => await InvokeAsync(
        "TEST MODE — do not mutate files, applications, accounts, services, messages, settings, or external state. " +
        "Walk through the task plan, identify missing inputs and risks, and explain exactly how success would be verified.\n\n" +
        item.Instruction);

    private async Task AskAssistantAsync()
    {
        var request = _assistant.Text?.Trim();
        if (string.IsNullOrWhiteSpace(request)) return;
        var draft = BuildInstructions();
        _assistant.Text = string.Empty;
        await InvokeAsync(
            "Help me configure this reusable Haven Task. Review the current task draft below, answer my request, and propose exact improved Name, Goal, and Rules. Do not run the task yet.\n\n" +
            draft + "\n\nRequest:\n" + request);
    }

    private async Task InvokeAsync(string instruction)
    {
        _status.Text = "Opening this task in its persistent Tasks conversation…";
        try
        {
            await _runTask(instruction);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _status.Text = "The task could not be opened: " + ex.Message;
        }
    }

    private async Task StartOneTimeTaskAsync()
    {
        _status.Text = "Opening a new one-time task…";
        try
        {
            await _startOneTimeTask();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _status.Text = "The one-time task could not be opened: " + ex.Message;
        }
    }

    private async Task DeleteAsync(MacroDefinition item)
    {
        await _tasks.DeleteMacroAsync(item.Id, CancellationToken.None);
        await RefreshAsync();
        _status.Text = $"Deleted {item.Name}.";
    }

    private void ShowInstructions()
    {
        _instructions.Text = BuildInstructions();
        _instructionsOverlay.IsVisible = true;
    }

    private string BuildInstructions()
    {
        var name = _name.Text?.Trim() ?? "Untitled Task";
        var goal = _goal.Text?.Trim() ?? string.Empty;
        var rules = _rules.Text?.Trim() ?? string.Empty;
        return $"Task: {name}\n\nGoal:\n{goal}\n\nRules:\n{rules}\n\nCompletion:\nVerify the requested outcome and report confirmed evidence. Stop safely and explain any blocker.";
    }

    private void ParseInstructions(string text)
    {
        _name.Text = ExtractSection(text, "Task:", "Goal:") ?? _name.Text;
        _goal.Text = ExtractSection(text, "Goal:", "Rules:") ?? _goal.Text;
        _rules.Text = ExtractSection(text, "Rules:", "Completion:") ?? _rules.Text;
    }

    private static string ExtractRules(string? instruction) =>
        ExtractSection(instruction ?? string.Empty, "Rules:", "Completion:") ?? string.Empty;

    private static string? ExtractSection(string text, string startLabel, string endLabel)
    {
        var start = text.IndexOf(startLabel, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += startLabel.Length;
        var end = text.IndexOf(endLabel, start, StringComparison.OrdinalIgnoreCase);
        return (end < 0 ? text[start..] : text[start..end]).Trim();
    }

    private static Control TaskSection(string title, Control content) => new StackPanel
    {
        Spacing = 10,
        Children = { Heading(title, 17), content }
    };

    private static MenuItem MenuItem(string label, Action action)
    {
        var item = new MenuItem { Header = label };
        item.Click += (_, _) => action();
        return item;
    }

    private static Button TabButton(string label, bool selected)
    {
        var button = new Button
        {
            Content = label,
            Background = Brushes.Transparent,
            // Keep the underline in layout for every state. Only its colour
            // changes, so selecting a tab never shifts either label.
            BorderThickness = new Thickness(0, 0, 0, 3),
            BorderBrush = selected ? AccentBrush : Brushes.Transparent,
            Foreground = selected ? AccentBrush : TextBrush,
            Padding = new Thickness(4, 4, 4, 7),
            CornerRadius = new CornerRadius(0),
            FontSize = 16,
            FontWeight = FontWeight.Bold
        };
        return button;
    }

    private static void ApplyTabState(Button button, bool selected)
    {
        button.BorderThickness = new Thickness(0, 0, 0, 3);
        button.BorderBrush = selected ? AccentBrush : Brushes.Transparent;
        button.Foreground = selected ? AccentBrush : TextBrush;
    }

    private static Button AccentButton(string label) => new()
    {
        Content = label,
        MinHeight = 52,
        Padding = new Thickness(24, 12),
        CornerRadius = new CornerRadius(26),
        Background = AccentBrush,
        Foreground = AccentInkBrush,
        BorderThickness = new Thickness(0),
        FontWeight = FontWeight.Bold,
        FontSize = 15
    };

    private static Button SoftButton(string label) => new()
    {
        Content = label,
        MinHeight = 52,
        Padding = new Thickness(24, 12),
        CornerRadius = new CornerRadius(26),
        Background = AccentSoftBrush,
        Foreground = TextBrush,
        BorderThickness = new Thickness(0),
        FontWeight = FontWeight.Bold,
        FontSize = 15
    };

    private static Button AccentIconButton(string icon, string name)
    {
        var button = new Button
        {
            Width = 62,
            Height = 62,
            CornerRadius = new CornerRadius(22),
            Background = AccentBrush,
            BorderThickness = new Thickness(0),
            Content = new HavenIcon { IconKey = icon, Width = 27, Height = 27, Foreground = AccentInkBrush }
        };
        AutomationProperties.SetName(button, name);
        return button;
    }

    private static Button RoundButton(string icon, string name)
    {
        var button = new Button
        {
            Width = 62,
            Height = 62,
            CornerRadius = new CornerRadius(31),
            Background = CardBrush,
            BorderBrush = BorderStroke,
            BorderThickness = new Thickness(1),
            Content = new HavenIcon { IconKey = icon, Width = 24, Height = 24 }
        };
        AutomationProperties.SetName(button, name);
        return button;
    }

    private static TextBox Field(string placeholder, bool multiline = false) => new()
    {
        PlaceholderText = placeholder,
        AcceptsReturn = multiline,
        TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        MinHeight = multiline ? 124 : 48,
        Padding = new Thickness(14),
        CornerRadius = new CornerRadius(16),
        Background = FieldBrush,
        BorderBrush = BorderStroke,
        BorderThickness = new Thickness(1),
        FontSize = 14,
        VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center
    };

    private static TextBlock Heading(string text, double size) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = FontWeight.Bold,
        TextWrapping = TextWrapping.Wrap
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 15,
        FontWeight = FontWeight.Bold,
        Margin = new Thickness(2, 2, 0, 0)
    };

    private static TextBlock Muted(string text) => new()
    {
        Text = text,
        Foreground = MutedBrush,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap
    };

    private static T Column<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static T Row<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    private static IBrush Brush(string value) => new SolidColorBrush(Color.Parse(value));

    private static IBrush PaletteBrush(string key, string fallback) =>
        Avalonia.Application.Current?.Resources[key] as IBrush ?? Brush(fallback);
}
