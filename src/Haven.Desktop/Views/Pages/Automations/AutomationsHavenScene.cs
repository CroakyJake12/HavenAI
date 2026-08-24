using Haven.Application.Automations;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Automations;

internal sealed record AutomationsWorkflowCard(
    Guid Id, string Name, string Description, bool IsEnabled, bool HasSchedule, string ScheduleDetail);

internal sealed record AutomationsScheduledCard(
    Guid Id, string Name, string Detail, bool IsEnabled);

internal sealed record AutomationsRunCard(
    Guid AutomationId, string Name, string Status, string Detail, bool IsActive);

/// <summary>All visible Automations UI. Avalonia only hosts this scene through one HavenSceneControl.</summary>
internal sealed partial class AutomationsHavenScene : IDisposable
{
    private enum DashboardSection { Running, History, Workflows }

    private readonly List<AutomationsWorkflowCard> _workflows = [];
    private readonly List<AutomationsScheduledCard> _scheduled = [];
    private readonly List<AutomationsRunCard> _runs = [];
    private readonly List<AutomationGraphHistoryEntry> _graphHistory = [];
    private DashboardSection _section = DashboardSection.Running;
    private string _query = string.Empty;
    private bool _disposed;

    public AutomationsHavenScene()
    {
        Root = new Page { Name = "Automations.Root", Layout = HavenLayout.Overlay };
        Root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Background, "Surface");
        Root.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);

        DashboardLayer = BuildDashboard();
        Root.Add(DashboardLayer);
        EditorLayer = BuildEditor();
        Root.Add(EditorLayer);
        NodePickerLayer = BuildNodePicker();
        Root.Add(NodePickerLayer);

        SetVisible(EditorLayer, false);
        SetVisible(NodePickerLayer, false);
        RefreshDashboard();
    }

    public Page Root { get; }
    public Container DashboardLayer { get; }
    public Container EditorLayer { get; private set; } = null!;
    public Container NodePickerLayer { get; private set; } = null!;
    public Input SearchInput { get; private set; } = null!;
    public HavenText StatusText { get; private set; } = null!;
    public Container DashboardContent { get; private set; } = null!;
    public HavenButton RunningTab { get; private set; } = null!;
    public HavenButton HistoryTab { get; private set; } = null!;
    public HavenButton WorkflowsTab { get; private set; } = null!;

    public event EventHandler? RefreshRequested;
    public event EventHandler? OpenTasksRequested;
    public event EventHandler? NewWorkflowRequested;
    public event Action<Guid>? RunWorkflowRequested;
    public event Action<Guid>? EditWorkflowRequested;
    public event Action<Guid>? TestWorkflowRequested;
    public event Action<Guid>? DeleteWorkflowRequested;
    public event Action<Guid, bool>? SetWorkflowEnabledRequested;
    public event Action<Guid>? OpenScheduledRequested;

    public void SetDashboardData(
        IEnumerable<AutomationsWorkflowCard> workflows,
        IEnumerable<AutomationsScheduledCard> scheduled,
        IEnumerable<AutomationsRunCard> runs,
        IEnumerable<AutomationGraphHistoryEntry> graphHistory)
    {
        _workflows.Clear();
        _workflows.AddRange(workflows ?? []);
        _scheduled.Clear();
        _scheduled.AddRange(scheduled ?? []);
        _runs.Clear();
        _runs.AddRange(runs ?? []);
        _graphHistory.Clear();
        _graphHistory.AddRange(graphHistory ?? []);
        RefreshDashboard();
    }

    public void SetStatus(string text, bool isError = false)
    {
        StatusText.Content = text ?? string.Empty;
        StatusText.SetValue(HavenProperties.Foreground, isError ? "Danger" : "TextSecondary");
    }

    public void ShowDashboard()
    {
        CloseNodePicker();
        SetVisible(EditorLayer, false);
        SetVisible(DashboardLayer, true);
        RefreshDashboard();
    }

    private Container BuildDashboard()
    {
        var layer = new Container { Name = "Automations.Dashboard", Layout = HavenLayout.Vertical };
        layer.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        layer.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        layer.SetValue(HavenProperties.Padding, HavenThickness.Parse("24px 28px 36px 28px"));
        layer.SetValue(HavenProperties.Gap, HavenLength.Px(16));
        layer.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);

        var header = new Container { Name = "Automations.Header", Layout = HavenLayout.Grid, Columns = "1fr Auto Auto Auto", Rows = "Auto" };
        header.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        header.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        var copy = new Container { Layout = HavenLayout.Vertical };
        copy.SetValue(HavenProperties.Gap, HavenLength.Px(3));
        copy.Add(Heading("Automations.Title", "Automations", TextLevel.H1));
        copy.Add(Muted("Automations.Subtitle", "Build, schedule, test, and inspect reusable workflows as typed node graphs."));
        header.Add(copy);

        var refresh = ActionButton("Automations.Refresh", "Refresh", ButtonVariant.Ghost, (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));
        refresh.SetValue(HavenProperties.Column, 1);
        header.Add(refresh);
        var oneTime = ActionButton("Automations.OpenTasks", "One-time task", ButtonVariant.Secondary, (_, _) => OpenTasksRequested?.Invoke(this, EventArgs.Empty));
        oneTime.SetValue(HavenProperties.Column, 2);
        header.Add(oneTime);
        var create = ActionButton("Automations.New", "+ New workflow", ButtonVariant.Primary, (_, _) => NewWorkflowRequested?.Invoke(this, EventArgs.Empty));
        create.SetValue(HavenProperties.Column, 3);
        header.Add(create);
        layer.Add(header);

        var tabs = new Container { Name = "Automations.Tabs", Layout = HavenLayout.Horizontal };
        tabs.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        tabs.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        RunningTab = ActionButton("Automations.Tab.Running", "Running", ButtonVariant.Primary, (_, _) => SelectSection(DashboardSection.Running));
        HistoryTab = ActionButton("Automations.Tab.History", "History", ButtonVariant.Tertiary, (_, _) => SelectSection(DashboardSection.History));
        WorkflowsTab = ActionButton("Automations.Tab.Workflows", "Reusable workflows", ButtonVariant.Tertiary, (_, _) => SelectSection(DashboardSection.Workflows));
        tabs.Add(RunningTab); tabs.Add(HistoryTab); tabs.Add(WorkflowsTab);
        layer.Add(tabs);

        SearchInput = new Input { Name = "Automations.Search", Placeholder = "Search workflows and runs" };
        SearchInput.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SearchInput.SetValue(HavenProperties.MaxWidth, HavenLength.Px(900));
        SearchInput.Invalidated += OnSearchInvalidated;
        layer.Add(SearchInput);

        StatusText = Muted("Automations.Status", string.Empty);
        StatusText.SetValue(HavenProperties.MinHeight, HavenLength.Px(20));
        layer.Add(StatusText);

        DashboardContent = new Container { Name = "Automations.Dashboard.Content", Layout = HavenLayout.Vertical };
        DashboardContent.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        DashboardContent.SetValue(HavenProperties.Gap, HavenLength.Px(14));
        layer.Add(DashboardContent);
        return layer;
    }

    private void SelectSection(DashboardSection section)
    {
        _section = section;
        RefreshDashboard();
    }

    private void RefreshDashboard()
    {
        if (DashboardContent is null) return;
        RunningTab.Variant = _section == DashboardSection.Running ? ButtonVariant.Primary : ButtonVariant.Tertiary;
        HistoryTab.Variant = _section == DashboardSection.History ? ButtonVariant.Primary : ButtonVariant.Tertiary;
        WorkflowsTab.Variant = _section == DashboardSection.Workflows ? ButtonVariant.Primary : ButtonVariant.Tertiary;
        Clear(DashboardContent);

        switch (_section)
        {
            case DashboardSection.Running:
                RenderRunning();
                break;
            case DashboardSection.History:
                RenderHistory();
                break;
            default:
                RenderWorkflows();
                break;
        }
    }

    private void RenderRunning()
    {
        DashboardContent.Add(Heading("Automations.Running.Title", "Running now", TextLevel.H2));
        var active = _runs.Where(item => item.IsActive && Matches(item.Name, item.Detail, item.Status)).ToArray();
        if (active.Length == 0)
        {
            DashboardContent.Add(EmptyCard("Automations.Running.Empty", "Nothing is running", "Pending and active scheduled graph runs appear here."));
            return;
        }
        foreach (var run in active) DashboardContent.Add(BuildRunCard(run));
    }

    private void RenderHistory()
    {
        DashboardContent.Add(Heading("Automations.History.Title", "Automation history", TextLevel.H2));
        var persisted = _runs.Where(item => !item.IsActive && Matches(item.Name, item.Detail, item.Status)).ToArray();
        foreach (var run in persisted) DashboardContent.Add(BuildRunCard(run));
        var graph = _graphHistory.Where(entry => Matches(entry.WorkflowName, entry.FailureMessage ?? string.Empty, entry.Mode.ToString())).ToArray();
        foreach (var entry in graph) DashboardContent.Add(BuildGraphHistoryCard(entry));
        if (persisted.Length == 0 && graph.Length == 0)
            DashboardContent.Add(EmptyCard("Automations.History.Empty", "No run history yet", "Test or run a graph and its real result will appear here."));
    }

    private void RenderWorkflows()
    {
        DashboardContent.Add(Heading("Automations.Workflows.Title", "Reusable workflows", TextLevel.H2));
        var manual = _workflows.Where(item => !item.HasSchedule && Matches(item.Name, item.Description, item.ScheduleDetail)).OrderBy(item => item.Name).ToArray();
        var automatic = _workflows.Where(item => item.HasSchedule && Matches(item.Name, item.Description, item.ScheduleDetail)).OrderBy(item => item.Name).ToArray();
        var scheduledOnly = _scheduled.Where(item => !_workflows.Any(workflow => workflow.Id == item.Id) && Matches(item.Name, item.Detail)).ToArray();

        RenderWorkflowGroup("Manual workflows", manual);
        RenderWorkflowGroup("Scheduled automations", automatic);
        foreach (var item in scheduledOnly) DashboardContent.Add(BuildScheduledCard(item));

        if (manual.Length == 0 && automatic.Length == 0 && scheduledOnly.Length == 0)
            DashboardContent.Add(EmptyCard("Automations.Workflows.Empty", "No workflows found", "Create a workflow, add nodes, connect them, then test and save it."));
    }

    private void RenderWorkflowGroup(string title, IReadOnlyList<AutomationsWorkflowCard> items)
    {
        if (items.Count == 0) return;
        DashboardContent.Add(Heading(null, title, TextLevel.H3));
        foreach (var item in items) DashboardContent.Add(BuildWorkflowCard(item));
    }

    private Container BuildWorkflowCard(AutomationsWorkflowCard item)
    {
        var card = Card($"Automations.Workflow.{item.Id:N}");
        var row = new Container { Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "Auto" };
        row.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        row.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        var copy = new Container { Layout = HavenLayout.Vertical };
        copy.SetValue(HavenProperties.Gap, HavenLength.Px(3));
        copy.Add(Heading(null, item.Name, TextLevel.H3));
        copy.Add(Muted(null, string.IsNullOrWhiteSpace(item.Description) ? "No description" : item.Description));
        copy.Add(Muted(null, item.IsEnabled ? "Enabled" : "Paused"));
        if (item.HasSchedule) copy.Add(Muted(null, item.ScheduleDetail));
        row.Add(copy);
        var actions = new Container { Layout = HavenLayout.Horizontal };
        actions.SetValue(HavenProperties.Column, 1);
        actions.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        var run = ActionButton($"Automations.Workflow.{item.Id:N}.Run", "Run", ButtonVariant.Primary, (_, _) => RunWorkflowRequested?.Invoke(item.Id));
        run.SetState(HavenElementState.Disabled, !item.IsEnabled);
        actions.Add(run);
        actions.Add(ActionButton($"Automations.Workflow.{item.Id:N}.Test", "Test", ButtonVariant.Secondary, (_, _) => TestWorkflowRequested?.Invoke(item.Id)));
        actions.Add(ActionButton($"Automations.Workflow.{item.Id:N}.Edit", "Edit", ButtonVariant.Tertiary, (_, _) => EditWorkflowRequested?.Invoke(item.Id)));
        actions.Add(ActionButton($"Automations.Workflow.{item.Id:N}.Enabled", item.IsEnabled ? "Pause" : "Resume", ButtonVariant.Tertiary, (_, _) => SetWorkflowEnabledRequested?.Invoke(item.Id, !item.IsEnabled)));
        actions.Add(ActionButton($"Automations.Workflow.{item.Id:N}.Delete", "Delete", ButtonVariant.Danger, (_, _) => DeleteWorkflowRequested?.Invoke(item.Id)));
        row.Add(actions);
        card.Add(row);
        return card;
    }

    private Container BuildScheduledCard(AutomationsScheduledCard item)
    {
        var card = Card($"Automations.Scheduled.{item.Id:N}");
        card.Add(Heading(null, item.Name, TextLevel.H3));
        card.Add(Muted(null, item.Detail));
        card.Add(ActionButton(null, "Open scheduled graph", ButtonVariant.Secondary, (_, _) => OpenScheduledRequested?.Invoke(item.Id)));
        return card;
    }

    private static Container BuildRunCard(AutomationsRunCard run)
    {
        var card = Card($"Automations.Run.{run.AutomationId:N}.{run.Status}");
        card.Add(Heading(null, $"{run.Name} · {run.Status}", TextLevel.H3));
        card.Add(Muted(null, run.Detail));
        return card;
    }

    private static Container BuildGraphHistoryCard(AutomationGraphHistoryEntry entry)
    {
        var card = Card($"Automations.GraphHistory.{entry.Id:N}");
        var outcome = entry.Succeeded ? "Succeeded" : "Failed";
        card.Add(Heading(null, entry.WorkflowName, TextLevel.H3));
        card.Add(Muted(null, $"{entry.Mode.ToString().ToUpperInvariant()} · {outcome} · {entry.CompletedAt.LocalDateTime:g}"));
        card.Add(Muted(null, entry.Succeeded
            ? $"{entry.Trace.Count} node{(entry.Trace.Count == 1 ? string.Empty : "s")} traced"
            : entry.FailureMessage ?? entry.ValidationIssues.FirstOrDefault()?.Message ?? "Graph run failed."));
        return card;
    }

    private static Container EmptyCard(string name, string title, string detail)
    {
        var card = Card(name);
        card.Add(Heading(null, title, TextLevel.H3));
        card.Add(Muted(null, detail));
        return card;
    }

    private bool Matches(params string[] values)
    {
        if (string.IsNullOrWhiteSpace(_query)) return true;
        return values.Any(value => (value ?? string.Empty).Contains(_query, StringComparison.OrdinalIgnoreCase));
    }

    private void OnSearchInvalidated(object? sender, EventArgs e)
    {
        var value = SearchInput.Text.Trim();
        if (string.Equals(value, _query, StringComparison.Ordinal)) return;
        _query = value;
        RefreshDashboard();
    }

    private static Container Card(string? name)
    {
        var card = new Container { Name = name, Layout = HavenLayout.Vertical };
        card.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        card.SetValue(HavenProperties.Background, "SurfaceRaised");
        card.SetValue(HavenProperties.BorderColor, "Border");
        card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        card.SetValue(HavenProperties.Shadow, "Card");
        card.SetValue(HavenProperties.Padding, HavenThickness.Parse("14px 16px"));
        card.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        return card;
    }

    private static HavenButton ActionButton(string? name, string content, ButtonVariant variant, EventHandler handler)
    {
        var button = new HavenButton { Name = name, Content = content, Variant = variant };
        button.Invoked += handler;
        return button;
    }

    private static HavenText Heading(string? name, string content, TextLevel level) => new(content) { Name = name, Level = level };

    private static HavenText Muted(string? name, string content)
    {
        var text = new HavenText(content) { Name = name, Level = TextLevel.Paragraph };
        text.SetValue(HavenProperties.Foreground, "TextSecondary");
        text.SetValue(HavenProperties.FontSize, 12d);
        return text;
    }

    private static void Clear(Container container)
    {
        foreach (var child in container.Children.ToArray()) container.Remove(child);
    }

    private static void SetVisible(HavenElement element, bool visible) =>
        element.SetValue(HavenProperties.Visibility, visible ? HavenVisibility.Visible : HavenVisibility.Collapsed);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SearchInput.Invalidated -= OnSearchInvalidated;
        DisposeEditor();
    }
}
