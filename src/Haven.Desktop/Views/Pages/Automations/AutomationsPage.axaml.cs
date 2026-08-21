using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.HavenUI.Components;

namespace Haven.Desktop.Views.Pages.Automations;

/// <summary>
/// Repository-backed Haven Automations dashboard and reusable-workflow editor. The
/// existing workspace-state rows are treated as compatibility storage only;
/// the user-facing surface and vocabulary are Automations throughout.
/// </summary>
public sealed partial class AutomationsPage : UserControl
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
    private readonly StackPanel _manualItems = new() { Spacing = 8 };
    private readonly StackPanel _automaticItems = new() { Spacing = 8 };
    private readonly StackPanel _runningItems = new() { Spacing = 8 };
    private readonly StackPanel _historyItems = new() { Spacing = 8 };
    private readonly StackPanel _runningView = new() { Spacing = 14 };
    private readonly StackPanel _historyView = new() { Spacing = 14, IsVisible = false };
    private readonly StackPanel _reusableView = new() { Spacing = 20, IsVisible = false };
    private readonly HavenSearchInput _taskSearch = new() { PlaceholderText = "Search automations" };
    private readonly TextBlock _emptyText = Muted("No reusable workflows yet. Create one to give Haven a repeatable outcome and rules.");
    private readonly TextBlock _status = Muted(string.Empty);
    private readonly Grid _dashboard = new();
    private readonly Grid _editor = new();
    private readonly ScrollViewer _editorScroll = new()
    {
        IsVisible = false,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };
    private readonly TextBlock _editorTitle = Heading("Create Workflow", 42);
    private readonly HavenTextInput _name = Field("Workflow Name");
    private readonly HavenTextInput _goal = Field("Your Desired Outcome");
    private readonly HavenTextInput _rules = Field("Define rules that must be followed.", multiline: true);
    private readonly HavenComboBox _workflowType = new() { ItemsSource = new[] { "Instruction", DeviceAutomationNodeCategory.Key }, SelectedItem = "Instruction", MinWidth = 180 };
    private readonly HavenComboBox _deviceTarget = new() { MinWidth = 260 };
    private readonly HavenComboBox _deviceAction = new() { MinWidth = 260 };
    private readonly StackPanel _deviceParameters = new() { Spacing = 8 };
    private readonly StackPanel _deviceEditor = new() { Spacing = 8, IsVisible = false };
    private readonly TextBlock _deviceAvailability = Muted("Choose a target and action.");
    private readonly Dictionary<string, HavenTextInput> _deviceParameterInputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly DeviceActionRouter? _deviceActions;
    private DeviceCapabilitySnapshot? _deviceSnapshot;
    private AutomationGraphDefinition _editingGraph = AutomationGraphDefinition.Empty;
    private DeviceAutomationNodeDefinition? _editingDeviceNode;
    private readonly TextBlock _assistantMessage = new()
    {
        Text = "Ask me anything about this workflow. I can configure it, test it, and explain how it will run.",
        TextWrapping = TextWrapping.Wrap,
        FontSize = 15,
        FontWeight = FontWeight.SemiBold
    };
    private readonly HavenTextInput _assistant = Field("Ask Haven Anything");
    private readonly HavenPopupCard _instructionsOverlay = new() { IsVisible = false };
    private readonly HavenMultilineInput _instructions = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = "Cascadia Mono, Consolas",
        MinHeight = 420,
        Padding = new Thickness(18),
        CornerRadius = new CornerRadius(16)
    };
    private ReusableTaskDefinition? _editing;
    private bool _editMode;
    private bool _busy;

    public AutomationsPage(
        IWorkspaceStateRepository tasks,
        IAutomationRepository automations,
        Guid? containerId,
        Func<Task> startOneTimeTask,
        Func<string, Task> runTask,
        IVersionedSettingsStore? versionedSettings = null)
    {
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _automations = automations ?? throw new ArgumentNullException(nameof(automations));
        _containerId = containerId;
        _startOneTimeTask = startOneTimeTask ?? throw new ArgumentNullException(nameof(startOneTimeTask));
        _runTask = runTask ?? throw new ArgumentNullException(nameof(runTask));
        _graphHistorySettings = versionedSettings;
        _deviceActions = Haven.Desktop.App.Services?.GetService(typeof(DeviceActionRouter)) as DeviceActionRouter;
        var deviceExecutor = Haven.Desktop.App.Services?.GetService(typeof(DeviceAutomationNodeExecutor)) as DeviceAutomationNodeExecutor;
        var builtInExecutor = Haven.Desktop.App.Services?.GetService(typeof(BuiltInAutomationActionNodeExecutor)) as BuiltInAutomationActionNodeExecutor;
        _deviceWorkflowRunner = deviceExecutor is null && builtInExecutor is null
            ? null
            : new ReusableDeviceWorkflowRunner(deviceExecutor, builtInExecutor);

        InitializeComponent();
        var host = this.FindControl<Grid>("CodeBehindHost")
            ?? throw new InvalidOperationException("Automations host was not initialized.");
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
        var runningTab = TabButton("Running", selected: true);
        var historyTab = TabButton("Automation History", selected: false);
        var reusableTab = TabButton("Reusable Workflows", selected: false);
        var tabs = new[] { runningTab, historyTab, reusableTab };

        void SelectSection(HavenTabButton selected, StackPanel view, bool editMode, string status)
        {
            foreach (var tab in tabs) ApplyTabState(tab, ReferenceEquals(tab, selected));
            _runningView.IsVisible = ReferenceEquals(view, _runningView);
            _historyView.IsVisible = ReferenceEquals(view, _historyView);
            _reusableView.IsVisible = ReferenceEquals(view, _reusableView);
            _editMode = editMode;
            _status.Text = status;
            ApplyTaskSearch();
        }

        runningTab.Click += (_, _) => SelectSection(
            runningTab, _runningView, false,
            "Live one-time and reusable task runs appear here while they are active.");
        historyTab.Click += (_, _) => SelectSection(
            historyTab, _historyView, false,
            "Open a previous Tasks conversation to continue or review it.");
        reusableTab.Click += (_, _) => SelectSection(
            reusableTab, _reusableView, true,
            "Select a reusable task to edit it; use its Run button to execute it.");

        var oneTime = AccentButton("Open Tasks Space");
        oneTime.MinWidth = 138;
        oneTime.Click += async (_, _) => await StartOneTimeTaskAsync();

        var create = AccentButton("+ New Workflow");
        create.MinWidth = 176;
        create.Click += (_, _) => ShowEditor(null);

        var tabsHost = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 24,
                Children = { runningTab, historyTab, reusableTab }
            }
        };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Children = { oneTime, create }
        };
        var navigation = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 14,
            Children =
            {
                tabsHost,
                Column(actions, 1)
            }
        };

        _taskSearch.MinHeight = 52;
        _taskSearch.CornerRadius = new CornerRadius(26);
        _taskSearch.Padding = new Thickness(46, 10, 18, 10);
        _taskSearch.TextChanged += (_, _) => ApplyTaskSearch();
        var searchHost = new Grid
        {
            Children =
            {
                _taskSearch,
                new HavenIcon
                {
                    IconKey = "search",
                    Width = 19,
                    Height = 19,
                    Margin = new Thickness(17, 0, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false
                }
            }
        };

        _runningView.Children.Add(_runningItems);
        _historyView.Children.Add(_historyItems);
        _reusableView.Children.Add(TaskSection("Manual Workflows", _manualItems));
        _reusableView.Children.Add(TaskSection("Scheduled Automations", _automaticItems));
        _reusableView.Children.Add(_emptyText);

        var title = new TextBlock
        {
            Text = "Automations",
            FontSize = 40,
            FontWeight = FontWeight.ExtraBold,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var content = new StackPanel
        {
            Spacing = 22,
            Margin = new Thickness(42, 18, 42, 42),
            Children =
            {
                title,
                navigation,
                searchHost,
                _runningView,
                _historyView,
                _reusableView,
                _status
            }
        };

        void ApplyResponsiveLayout(double width)
        {
            var compact = width < 720;
            content.Margin = compact
                ? new Thickness(18, 14, 18, 30)
                : new Thickness(42, 18, 42, 42);
            content.Spacing = compact ? 18 : 22;
            title.FontSize = compact ? 36 : 40;

            navigation.ColumnDefinitions = compact
                ? new ColumnDefinitions("*")
                : new ColumnDefinitions("*,Auto");
            navigation.RowDefinitions = compact
                ? new RowDefinitions("Auto,Auto")
                : new RowDefinitions("Auto");
            navigation.RowSpacing = compact ? 14 : 0;
            navigation.ColumnSpacing = compact ? 0 : 14;
            Grid.SetColumn(actions, compact ? 0 : 1);
            Grid.SetRow(actions, compact ? 1 : 0);
            actions.HorizontalAlignment = compact
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Right;
            tabsHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        SizeChanged += (_, args) => ApplyResponsiveLayout(args.NewSize.Width);
        ApplyResponsiveLayout(Bounds.Width);

        _dashboard.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content
        });
    }

    private void BuildLegacyEditor()
    {
        var discard = SoftButton("Discard Changes");
        discard.Click += (_, _) => ShowDashboard();
        var save = AccentButton("Save Changes");
        save.Click += async (_, _) => await SaveAsync();

        var test = AccentButton("Test Workflow");
        test.HorizontalAlignment = HorizontalAlignment.Stretch;
        test.Click += async (_, _) => await TestAsync();
        var viewInstructions = SoftButton("View Workflow Instructions");
        viewInstructions.HorizontalAlignment = HorizontalAlignment.Stretch;
        viewInstructions.Click += (_, _) => ShowInstructions();
        ConfigureDeviceEditor();

        var form = new HavenCard
        {
            Width = 390,
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(18),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
                RowSpacing = 10,
                Children =
                {
                    Label("Name"),
                    Row(_name, 1),
                    Row(Label("Goal"), 2),
                    Row(_goal, 3),
                    Row(Label("Rules"), 4),
                    Row(_rules, 5),
                    Row(Label("Type"), 6),
                    Row(_workflowType, 7),
                    Row(_deviceEditor, 8),
                    Row(test, 9),
                    Row(viewInstructions, 10)
                }
            }
        };

        var assistantBubble = new HavenCard
        {
            MaxWidth = 830,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(18),
            Child = _assistantMessage
        };
        var assistantSend = AccentIconButton("arrow-up", "Ask Haven about this task");
        assistantSend.Click += async (_, _) => await AskAssistantAsync();
        _assistant.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                e.Handled = true;
                await AskAssistantAsync();
            }
        };

        var composer = new HavenComposerShell
        {
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 10,
                Children =
                {
                    RoundButton("plus", "Task context is included"),
                    Column(_assistant, 1),
                    Column(assistantSend, 2)
                }
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
            _status.Text = "Workflow instructions applied to this draft.";
        };

        _instructionsOverlay.Margin = new Thickness(48);
        _instructionsOverlay.CornerRadius = new CornerRadius(30);
        _instructionsOverlay.Padding = new Thickness(24);
        _instructionsOverlay.Child = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 14,
            Children =
            {
                Heading("Workflow Instructions", 36),
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
        _runningItems.Children.Clear();
        _historyItems.Children.Clear();
        _status.Text = "Loading Automations…";
        try
        {
            var reusableTask = _tasks.GetReusableTasksAsync(_containerId, CancellationToken.None);
            var scheduledTask = _automations.GetAllAsync(CancellationToken.None);
            await Task.WhenAll(reusableTask, scheduledTask);

            foreach (var item in reusableTask.Result.Where(item => item.IsEnabled))
                _manualItems.Children.Add(TaskChip(item.Name, async () =>
                {
                    if (_editMode) ShowEditor(item);
                    else await RunReusableAsync(item);
                }, item));

            foreach (var item in scheduledTask.Result
                         .Where(item => item.IsEnabled && item.ContainerId == _containerId)
                         .OrderBy(item => item.NextRunAt))
            {
                var detail = item.NextRunAt is null
                    ? "Waiting for trigger"
                    : "Next " + item.NextRunAt.Value.LocalDateTime.ToString("g");
                _automaticItems.Children.Add(TaskChip(item.Name, () => OpenScheduledAutomationAsync(item, reusableTask.Result), detail));
            }

            var runs = new List<(AutomationDefinition Definition, AutomationRun Run)>();
            foreach (var definition in scheduledTask.Result.Where(item => item.IsEnabled && item.ContainerId == _containerId))
            {
                foreach (var run in await _automations.GetRunsAsync(definition.Id, 50, CancellationToken.None))
                    runs.Add((definition, run));
            }

            foreach (var entry in runs.Where(entry => entry.Run.Status is AutomationRunStatus.Pending or AutomationRunStatus.Running).OrderByDescending(entry => entry.Run.StartedAt ?? entry.Run.ScheduledFor))
                _runningItems.Children.Add(AutomationRunRow(entry.Definition, entry.Run));

            if (_runningItems.Children.Count == 0)
                _runningItems.Children.Add(Muted("No automation is currently pending or running."));

            await RefreshGraphHistoryRowsAsync();
            foreach (var entry in runs.Where(entry => entry.Run.Status is not (AutomationRunStatus.Pending or AutomationRunStatus.Running)).OrderByDescending(entry => entry.Run.CompletedAt ?? entry.Run.StartedAt ?? entry.Run.ScheduledFor))
                _historyItems.Children.Add(AutomationRunRow(entry.Definition, entry.Run));

            if (_historyItems.Children.Count == 0)
                _historyItems.Children.Add(Muted("No automation runs have completed yet."));

            _emptyText.IsVisible = _manualItems.Children.Count == 0 && _automaticItems.Children.Count == 0;
            _status.Text = $"{_manualItems.Children.Count} reusable and {_automaticItems.Children.Count} automatic task{(_manualItems.Children.Count + _automaticItems.Children.Count == 1 ? string.Empty : "s")} available.";
            ApplyTaskSearch();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _status.Text = "Automations could not be loaded: " + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private Control TaskChip(string name, Func<Task> action, object context)
    {
        var detail = context switch
        {
            ReusableTaskDefinition macro when !string.IsNullOrWhiteSpace(macro.Description) => macro.Description,
            string text => text,
            _ => "Ready to run"
        };
        var actionLabel = _editMode && context is ReusableTaskDefinition ? "Edit" : "Run";
        var button = new HavenNavigationButton
        {
            Tag = name,
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 14,
                Children =
                {
                    new HavenPill
                    {
                        Width = 42,
                        Height = 42,
                        CornerRadius = new CornerRadius(21),
                        Background = AccentBrush,
                        Child = new HavenIcon
                        {
                            IconKey = "tasks",
                            Width = 19,
                            Height = 19,
                            Foreground = AccentInkBrush,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    },
                    Column(new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Spacing = 2,
                        Children =
                        {
                            new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeight.ExtraBold },
                            new TextBlock { Text = detail, FontSize = 11, Foreground = MutedBrush, TextTrimming = TextTrimming.CharacterEllipsis }
                        }
                    }, 1),
                    Column(new HavenPill
                    {
                        MinWidth = 76,
                        Padding = new Thickness(16, 8),
                        CornerRadius = new CornerRadius(18),
                        Background = AccentBrush,
                        Child = new TextBlock
                        {
                            Text = actionLabel,
                            FontSize = 12,
                            FontWeight = FontWeight.ExtraBold,
                            Foreground = AccentInkBrush,
                            HorizontalAlignment = HorizontalAlignment.Center
                        }
                    }, 2)
                }
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinHeight = 64,
            Padding = new Thickness(12, 9),
            CornerRadius = new CornerRadius(18),
        };
        button.Click += async (_, _) => await action();
        if (context is ReusableTaskDefinition item)
        {
            button.ContextMenu = new HavenContextMenu
            {
                ItemsSource = new object[]
                {
                    MenuItem("Edit task", () => ShowEditor(item)),
                    MenuItem("Test safely", () => _ = TestDefinitionAsync(item)),
                    MenuItem("Delete task", () => _ = DeleteAsync(item))
                }
            };
        }
        else if (context is string tooltipDetail)
        {
            ToolTip.SetTip(button, tooltipDetail);
        }
        AutomationProperties.SetName(button, $"{(_editMode ? "Edit" : "Run")} task {name}");
        return button;
    }

    private Control AutomationRunRow(AutomationDefinition definition, AutomationRun run)
    {
        var timestamp = run.CompletedAt ?? run.StartedAt ?? run.ScheduledFor;
        var status = run.Status == AutomationRunStatus.SkippedDuplicate ? "Skipped duplicate" : run.Status.ToString();
        var detail = run.Status switch
        {
            AutomationRunStatus.Pending => "Scheduled " + run.ScheduledFor.LocalDateTime.ToString("g"),
            AutomationRunStatus.Running => "Started " + (run.StartedAt ?? run.ScheduledFor).LocalDateTime.ToString("g"),
            AutomationRunStatus.Succeeded when !string.IsNullOrWhiteSpace(run.Result) => run.Result!,
            AutomationRunStatus.Failed when !string.IsNullOrWhiteSpace(run.Error) => run.Error!,
            _ => status + " " + timestamp.LocalDateTime.ToString("g")
        };
        var card = new HavenCard
        {
            Tag = definition.Name,
            Padding = new Thickness(14, 10),
            CornerRadius = new CornerRadius(18),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 14,
                Children =
                {
                    new StackPanel { Spacing = 2, Children = { new TextBlock { Text = definition.Name, FontWeight = FontWeight.ExtraBold, FontSize = 14 }, new TextBlock { Text = detail, Foreground = MutedBrush, FontSize = 11, TextWrapping = TextWrapping.Wrap } } },
                    Column(new HavenPill { Padding = new Thickness(12, 6), CornerRadius = new CornerRadius(16), Background = AccentBrush, Child = new TextBlock { Text = status, FontSize = 11, FontWeight = FontWeight.ExtraBold, Foreground = AccentInkBrush } }, 1)
                }
            }
        };
        AutomationProperties.SetName(card, $"{definition.Name} automation run {status}");
        return card;
    }

    private void ApplyTaskSearch()
    {
        var query = _taskSearch.Text?.Trim() ?? string.Empty;
        foreach (var panel in new[] { _manualItems, _automaticItems, _historyItems })
        {
            foreach (var child in panel.Children)
            {
                if (child.Tag is not string label) continue;
                child.IsVisible = query.Length == 0 || label.Contains(query, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private void ShowEditor(ReusableTaskDefinition? item)
    {
        _editing = item;
        _editorTitle.Text = item is null ? "Create Workflow" : "Edit Workflow";
        _name.Text = item?.Name ?? string.Empty;
        _goal.Text = item?.Description ?? string.Empty;
        _rules.Text = ExtractRules(item?.Instruction);
        HydrateDeviceEditor(item);
        _assistant.Text = string.Empty;
        _assistantMessage.Text = "Ask me anything about this workflow. I can configure it, test it, and explain how it will run.";
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
            _status.Text = "Workflow Name is required.";
            return;
        }

        if (!TryBuildGraphJson(out var graphJson, out var graphError))
        {
            _status.Text = graphError ?? "DEVICE configuration is incomplete.";
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var item = new ReusableTaskDefinition(
            _editing?.Id ?? Guid.NewGuid(),
            name,
            _goal.Text?.Trim() ?? string.Empty,
            BuildInstructions(),
            _containerId,
            true,
            _editing?.CreatedAt ?? now,
            now,
            graphJson);
        await _tasks.UpsertReusableTaskAsync(item, CancellationToken.None);
        _status.Text = $"Saved {name}.";
        ShowDashboard();
    }

    private async Task TestAsync()
    {
        if (!TryBuildGraphJson(out var graphJson, out var graphError))
        {
            _status.Text = graphError ?? "DEVICE configuration is incomplete.";
            return;
        }

        if (string.Equals(_workflowType.SelectedItem as string, DeviceAutomationNodeCategory.Key, StringComparison.OrdinalIgnoreCase))
        {
            _status.Text = "DEVICE node validated against current capabilities. Test Workflow does not execute physical-device actions.";
            return;
        }

        await TestDefinitionAsync(new ReusableTaskDefinition(
            _editing?.Id ?? Guid.Empty,
            _name.Text?.Trim() ?? "Workflow draft",
            _goal.Text?.Trim() ?? string.Empty,
            BuildInstructions(),
            _containerId,
            true,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            graphJson));
    }

    private async Task TestDefinitionAsync(ReusableTaskDefinition item)
    {
        // Safe tests stay inside Automations: hydrate the persisted graph and use the
        // graph runner's Test mode so no Tasks conversation or external side effect is created.
        ShowEditor(item);
        await TestGraphAsync();
    }

    private async Task AskAssistantAsync()
    {
        var request = _assistant.Text?.Trim();
        if (string.IsNullOrWhiteSpace(request)) return;
        var draft = BuildInstructions();
        _assistant.Text = string.Empty;
        await InvokeAsync(
            "Help me configure this reusable Haven workflow. Review the current task draft below, answer my request, and propose exact improved Name, Goal, and Rules. Do not run the task yet.\n\n" +
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

    private async Task DeleteAsync(ReusableTaskDefinition item)
    {
        await DeleteLinkedScheduledGraphAsync(item.Id);
        await _tasks.DeleteReusableTaskAsync(item.Id, CancellationToken.None);
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

    private static HavenMenuItem MenuItem(string label, Action action)
    {
        var item = new HavenMenuItem { Header = label };
        item.Click += (_, _) => action();
        return item;
    }

    private static HavenTabButton TabButton(string label, bool selected)
    {
        var button = new HavenTabButton
        {
            Content = label,
            IsSelected = selected,
            FontSize = 16,
            FontWeight = FontWeight.Bold
        };
        return button;
    }

    private static void ApplyTabState(HavenTabButton button, bool selected)
    {
        button.IsSelected = selected;
    }

    private static HavenPrimaryButton AccentButton(string label) => new()
    {
        Content = label,
        FontWeight = FontWeight.Bold,
        FontSize = 15
    };

    private static HavenTertiaryButton SoftButton(string label) => new()
    {
        Content = label,
        FontWeight = FontWeight.Bold,
        FontSize = 15
    };

    private static HavenIconButton AccentIconButton(string icon, string name)
    {
        var button = new HavenIconButton
        {
            Classes = { "accent" },
            Width = 62,
            Height = 62,
            Content = new HavenIcon { IconKey = icon, Width = 27, Height = 27, Foreground = AccentInkBrush }
        };
        AutomationProperties.SetName(button, name);
        return button;
    }

    private static HavenIconButton RoundButton(string icon, string name)
    {
        var button = new HavenIconButton
        {
            Width = 62,
            Height = 62,
            CornerRadius = new CornerRadius(31),
            Content = new HavenIcon { IconKey = icon, Width = 24, Height = 24 }
        };
        AutomationProperties.SetName(button, name);
        return button;
    }

    private static HavenTextInput Field(string placeholder, bool multiline = false)
    {
        HavenTextInput field = multiline ? new HavenMultilineInput() : new HavenTextInput();
        field.PlaceholderText = placeholder;
        field.MinHeight = multiline ? 124 : 48;
        field.FontSize = 14;
        field.VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center;
        return field;
    }

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
