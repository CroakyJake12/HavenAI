using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;

namespace Haven.Desktop.Views.Pages.Macros;

public sealed partial class MacrosPage : UserControl
{
    private readonly HavenEventBus _bus;
    private readonly IWorkspaceStateRepository _workspaceState;
    private readonly Guid? _containerId;
    private readonly Func<string, Task> _invoke;
    private MacroDefinition? _editing;
    private string _scriptDraft = string.Empty;

    public MacrosPage(
        HavenEventBus bus,
        IWorkspaceStateRepository workspaceState,
        Guid? containerId,
        Func<string, Task> invoke)
    {
        _bus = bus;
        _workspaceState = workspaceState;
        _containerId = containerId;
        _invoke = invoke;

        InitializeComponent();
        WireEvents();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) => _ = RefreshAsync();

    private void WireEvents()
    {
        RunTabButton.Click += (_, _) => ShowDashboard();
        EditTabButton.Click += (_, _) => ShowEditor(_editing);
        CreateReusableButton.Click += (_, _) => ShowEditor(null);
        RequestButton.Click += async (_, _) => await StartRequestAsync();
        TypeBox.SelectionChanged += (_, _) => UpdateTriggerVisibility();
        SaveButton.Click += async (_, _) => await SaveAsync();
        DiscardButton.Click += (_, _) => ShowDashboard();
        TestButton.Click += async (_, _) => await TestAsync();
        ViewScriptButton.Click += (_, _) => OpenScriptEditor();
        CancelScriptButton.Click += (_, _) => ScriptOverlay.IsVisible = false;
        SaveScriptButton.Click += (_, _) =>
        {
            _scriptDraft = ScriptBox.Text ?? string.Empty;
            ScriptOverlay.IsVisible = false;
            StatusText.Text = "Action Script updated. Save changes to persist it.";
        };
        DocumentationButton.Click += (_, _) =>
        {
            AssistantText.Text =
                "HAS uses structured natural-language sections such as <Name>, <Goal>, <Trigger>, <Rules>, and <Task>. " +
                "Name and Task are required. Automatic tasks include one or more triggers. Test Task runs the same script in SIMULATE mode.";
            ScriptOverlay.IsVisible = false;
        };
        AssistantSendButton.Click += (_, _) =>
        {
            var request = AssistantBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(request)) return;
            AssistantText.Text =
                "I captured that request for this draft. Review the Haven Action Script, then save the task.";
            RulesBox.Text = string.IsNullOrWhiteSpace(RulesBox.Text)
                ? request
                : $"{RulesBox.Text}{Environment.NewLine}{request}";
            AssistantBox.Text = string.Empty;
            _scriptDraft = BuildScript();
        };
    }

    private async Task RefreshAsync()
    {
        ManualItemsPanel.Children.Clear();
        AutomaticItemsPanel.Children.Clear();
        StatusText.Text = "Loading Haven Tasks…";

        try
        {
            var items = await _workspaceState.GetMacrosAsync(_containerId, CancellationToken.None);
            foreach (var item in items.Where(item => item.IsEnabled))
            {
                var automatic = IsAutomatic(item.Instruction);
                var button = new Button
                {
                    Content = item.Name,
                    Margin = new Avalonia.Thickness(0, 0, 10, 10),
                    MinWidth = 150,
                    HorizontalContentAlignment = HorizontalAlignment.Center
                };

                button.Click += async (_, _) =>
                {
                    _editing = item;
                    if (EditTabButton.IsFocused)
                    {
                        ShowEditor(item);
                        return;
                    }

                    StatusText.Text = $"Running {item.Name}…";
                    await _invoke(item.Instruction);
                    StatusText.Text = $"Started {item.Name}.";
                };

                button.ContextFlyout = BuildItemMenu(item);
                (automatic ? AutomaticItemsPanel : ManualItemsPanel).Children.Add(button);
            }

            EmptyText.IsVisible =
                ManualItemsPanel.Children.Count == 0 &&
                AutomaticItemsPanel.Children.Count == 0;
            StatusText.Text = $"{items.Count} reusable task{(items.Count == 1 ? string.Empty : "s")} available.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not load Haven Tasks: {ex.Message}";
        }
    }

    private FlyoutBase BuildItemMenu(MacroDefinition item)
    {
        var flyout = new MenuFlyout();
        var edit = new MenuItem { Header = "Edit task" };
        edit.Click += (_, _) => ShowEditor(item);
        var test = new MenuItem { Header = "Test safely" };
        test.Click += async (_, _) =>
        {
            _editing = item;
            LoadEditor(item);
            await TestAsync();
        };
        var delete = new MenuItem { Header = "Delete" };
        delete.Click += async (_, _) =>
        {
            await _workspaceState.DeleteMacroAsync(item.Id, CancellationToken.None);
            await RefreshAsync();
        };
        flyout.Items.Add(edit);
        flyout.Items.Add(test);
        flyout.Items.Add(delete);
        return flyout;
    }

    private void ShowDashboard()
    {
        EditorPanel.IsVisible = false;
        DashboardPanel.IsVisible = true;
        ScriptOverlay.IsVisible = false;
        _ = RefreshAsync();
    }

    private void ShowEditor(MacroDefinition? item)
    {
        DashboardPanel.IsVisible = false;
        EditorPanel.IsVisible = true;
        ScriptOverlay.IsVisible = false;
        _editing = item;
        LoadEditor(item);
    }

    private void LoadEditor(MacroDefinition? item)
    {
        EditorTitle.Text = item is null ? "Create Task" : "Edit Task";
        NameBox.Text = item?.Name ?? string.Empty;
        GoalBox.Text = ExtractSection(item?.Instruction, "Goal") ?? item?.Description ?? string.Empty;
        RulesBox.Text = ExtractSection(item?.Instruction, "Rules") ?? string.Empty;
        TriggerBox.Text = ExtractSection(item?.Instruction, "Trigger") ?? string.Empty;
        TypeBox.SelectedIndex = item is not null && IsAutomatic(item.Instruction) ? 1 : 0;
        _scriptDraft = item?.Instruction ?? string.Empty;
        UpdateTriggerVisibility();
        AssistantText.Text =
            "Ask me anything about this task! I can configure the task, test it, and explain parts of the task.";
    }

    private void UpdateTriggerVisibility()
        => TriggerPanel.IsVisible = TypeBox.SelectedIndex == 1;

    private async Task SaveAsync()
    {
        var name = NameBox.Text?.Trim() ?? string.Empty;
        var goal = GoalBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusText.Text = "Task Name is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_scriptDraft) ||
            !string.Equals(ExtractSection(_scriptDraft, "Name"), name, StringComparison.Ordinal))
        {
            _scriptDraft = BuildScript();
        }

        if (string.IsNullOrWhiteSpace(ExtractSection(_scriptDraft, "Task")))
        {
            StatusText.Text = "The Haven Action Script must contain a <Task> section.";
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var definition = new MacroDefinition(
            _editing?.Id ?? Guid.NewGuid(),
            name,
            goal,
            _scriptDraft,
            _containerId,
            true,
            _editing?.CreatedAt ?? now,
            now);

        await _workspaceState.UpsertMacroAsync(definition, CancellationToken.None);
        _editing = definition;
        StatusText.Text = $"Saved {name}.";
        ShowDashboard();
    }

    private async Task TestAsync()
    {
        var script = string.IsNullOrWhiteSpace(_scriptDraft) ? BuildScript() : _scriptDraft;
        if (string.IsNullOrWhiteSpace(ExtractSection(script, "Task")))
        {
            StatusText.Text = "Add task instructions before testing.";
            return;
        }

        var simulation =
            "MODE: SIMULATE\n" +
            "Do not mutate files, repositories, accounts, services, messages, settings, or external state. " +
            "Demonstrate the planned steps and validation only.\n\n" +
            script;

        StatusText.Text = "Testing in safe simulation mode…";
        await _invoke(simulation);
        StatusText.Text = "Simulation started. No real target should be changed.";
    }

    private void OpenScriptEditor()
    {
        _scriptDraft = string.IsNullOrWhiteSpace(_scriptDraft) ? BuildScript() : _scriptDraft;
        ScriptBox.Text = _scriptDraft;
        ScriptOverlay.IsVisible = true;
    }

    private async Task StartRequestAsync()
    {
        StatusText.Text = "Opening a one-time Haven Request…";
        await _invoke(
            "Start a new Haven Request. Treat it as a persistent, one-time task conversation. " +
            "Ask what outcome the user wants, perform the work through Haven Tasks, preserve the history, " +
            "and accept follow-up instructions.");
    }

    private string BuildScript()
    {
        var name = NameBox.Text?.Trim() ?? "Untitled Task";
        var goal = GoalBox.Text?.Trim() ?? string.Empty;
        var rules = RulesBox.Text?.Trim() ?? string.Empty;
        var trigger = TriggerBox.Text?.Trim() ?? string.Empty;
        var task = string.IsNullOrWhiteSpace(goal)
            ? "Ask the user for the task input, then follow the rules and produce the requested outcome."
            : $"Ask for any required input, then achieve this goal: {goal}";

        var sections = new List<string>
        {
            "HAVEN PROMPT 1.0",
            $"<Name>{Environment.NewLine}{name}{Environment.NewLine}</Name>"
        };

        if (!string.IsNullOrWhiteSpace(goal))
            sections.Add($"<Goal>{Environment.NewLine}{goal}{Environment.NewLine}</Goal>");
        if (TypeBox.SelectedIndex == 1 && !string.IsNullOrWhiteSpace(trigger))
            sections.Add($"<Trigger>{Environment.NewLine}{trigger}{Environment.NewLine}</Trigger>");
        if (!string.IsNullOrWhiteSpace(rules))
            sections.Add($"<Rules>{Environment.NewLine}{rules}{Environment.NewLine}</Rules>");

        sections.Add($"<Task>{Environment.NewLine}{task}{Environment.NewLine}</Task>");
        sections.Add(
            "<Failure>\nStop safely, explain what blocked the task, and preserve all existing user data.\n</Failure>");
        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private static bool IsAutomatic(string script)
        => !string.IsNullOrWhiteSpace(ExtractSection(script, "Trigger"));

    private static string? ExtractSection(string? script, string name)
    {
        if (string.IsNullOrWhiteSpace(script)) return null;
        var open = $"<{name}>";
        var close = $"</{name}>";
        var start = script.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += open.Length;
        var end = script.IndexOf(close, start, StringComparison.OrdinalIgnoreCase);
        return end < 0 ? null : script[start..end].Trim();
    }
}
