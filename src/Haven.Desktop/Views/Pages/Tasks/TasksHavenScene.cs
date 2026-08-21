using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Tasks;

internal enum TasksHavenSection { Running, History, Reusable }

internal sealed record TasksHavenReusableItem(Guid Id, string Name, string Description, string Instruction, DateTimeOffset UpdatedAt);
internal sealed record TasksHavenScheduledItem(Guid Id, string Name, string Instruction, string Detail);
internal sealed record TasksHavenHistoryItem(Guid Id, string Title, DateTimeOffset UpdatedAt);

internal sealed class TasksHavenTaskEventArgs(Guid taskId) : EventArgs
{
    public Guid TaskId { get; } = taskId;
}

internal sealed class TasksHavenInstructionEventArgs(string instruction) : EventArgs
{
    public string Instruction { get; } = instruction;
}

internal sealed class TasksHavenDraftEventArgs(Guid? taskId, string name, string goal, string rules, string instruction) : EventArgs
{
    public Guid? TaskId { get; } = taskId;
    public string Name { get; } = name;
    public string Goal { get; } = goal;
    public string Rules { get; } = rules;
    public string Instruction { get; } = instruction;
}

/// <summary>Pure Haven.UI presentation contract for the reusable Tasks app.</summary>
internal sealed partial class TasksHavenScene : IDisposable
{
    private readonly List<TasksHavenReusableItem> _reusable = [];
    private readonly List<TasksHavenScheduledItem> _scheduled = [];
    private readonly List<TasksHavenHistoryItem> _history = [];
    private string _query = string.Empty;

    public TasksHavenScene()
    {
        Root = new Page { Name = "Tasks.Root", Layout = HavenLayout.Vertical };
        Root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("24px 28px 40px 28px"));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(16));
        Root.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);

        WideHeader = Header(compact: false);
        WideHeader.Conditions.Add(new HavenScreenRangeCondition(HavenScreenAxis.Width, HavenLength.Px(760)));
        Root.Add(WideHeader);
        CompactHeader = Header(compact: true);
        CompactHeader.Conditions.Add(new HavenScreenRangeCondition(HavenScreenAxis.Width, maximum: HavenLength.Px(759.999)));
        Root.Add(CompactHeader);

        var tabs = new Container { Name = "Tasks.Tabs", Layout = HavenLayout.Wrap };
        tabs.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        tabs.Add(Action("Tasks.Tab.Running", "Running", ButtonVariant.Secondary, (_, _) => Select(TasksHavenSection.Running)));
        tabs.Add(Action("Tasks.Tab.History", "Task history", ButtonVariant.Secondary, (_, _) => Select(TasksHavenSection.History)));
        tabs.Add(Action("Tasks.Tab.Reusable", "Reusable tasks", ButtonVariant.Secondary, (_, _) => Select(TasksHavenSection.Reusable)));
        Root.Add(tabs);

        SearchInput = new Input { Name = "Tasks.Search", Placeholder = "Search tasks" };
        SearchInput.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SearchInput.SetValue(HavenProperties.MaxWidth, HavenLength.Px(900));
        SearchInput.Invalidated += OnSearchInvalidated;
        Root.Add(SearchInput);

        StatusText = Muted("Tasks.Status", string.Empty);
        Root.Add(StatusText);
        Content = new Container { Name = "Tasks.Content", Layout = HavenLayout.Vertical };
        Content.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Content.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        Root.Add(Content);
        Render();
        InitializeEditor();
    }

    public Page Root { get; }
    public Container WideHeader { get; }
    public Container CompactHeader { get; }
    public Container Content { get; }
    public Input SearchInput { get; }
    public HavenText StatusText { get; }
    public TasksHavenSection SelectedSection { get; private set; } = TasksHavenSection.Running;

    public event EventHandler? RefreshRequested;
    public event EventHandler? StartOneTimeRequested;
    public event EventHandler? CreateReusableRequested;
    public event EventHandler<TasksHavenInstructionEventArgs>? RunRequested;
    public event EventHandler<TasksHavenTaskEventArgs>? EditRequested;
    public event EventHandler<TasksHavenTaskEventArgs>? OpenHistoryRequested;

    public void SetData(IEnumerable<TasksHavenReusableItem> reusable, IEnumerable<TasksHavenScheduledItem> scheduled, IEnumerable<TasksHavenHistoryItem> history)
    {
        _reusable.Clear();
        _reusable.AddRange(reusable);
        _scheduled.Clear();
        _scheduled.AddRange(scheduled);
        _history.Clear();
        _history.AddRange(history);
        Render();
    }

    public void SetStatus(string text, bool isError = false)
    {
        StatusText.Content = text ?? string.Empty;
        StatusText.SetValue(HavenProperties.Foreground, isError ? "Danger" : "TextSecondary");
    }

    private Container Header(bool compact)
    {
        var header = new Container { Name = compact ? "Tasks.Header.Compact" : "Tasks.Header.Wide", Layout = HavenLayout.Vertical };
        header.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        header.Add(new HavenText("Tasks") { Level = TextLevel.H1 });
        header.Add(Muted(null, compact ? "Run, reuse and review Haven Tasks." : "Run something once, reuse a proven workflow, or review what Haven has already done."));
        var actions = new Container { Layout = HavenLayout.Wrap };
        actions.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        actions.Add(Action(header.Name + ".Refresh", "Refresh", ButtonVariant.Ghost, (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty)));
        actions.Add(Action(header.Name + ".New", "+ New task", ButtonVariant.Primary, (_, _) => StartOneTimeRequested?.Invoke(this, EventArgs.Empty)));
        actions.Add(Action(header.Name + ".Reusable", "+ Create reusable", ButtonVariant.Secondary, (_, _) => BeginCreateReusable()));
        header.Add(actions);
        return header;
    }

    private void Select(TasksHavenSection section)
    {
        SelectedSection = section;
        Render();
    }

    private void Render()
    {
        foreach (var child in Content.Children.ToArray()) Content.Remove(child);
        switch (SelectedSection)
        {
            case TasksHavenSection.Running:
                Content.Add(Heading("Running now"));
                Content.Add(Muted("Tasks.Running.Empty", "No task is currently reporting an active run."));
                break;
            case TasksHavenSection.History:
                Content.Add(Heading("Task history"));
                foreach (var item in _history.Where(item => Match(item.Title)).OrderByDescending(item => item.UpdatedAt))
                    Content.Add(ItemCard("Tasks.History." + item.Id.ToString("N"), item.Title, item.UpdatedAt.LocalDateTime.ToString("g"), "Open", (_, _) => OpenHistoryRequested?.Invoke(this, new TasksHavenTaskEventArgs(item.Id))));
                break;
            case TasksHavenSection.Reusable:
                Content.Add(Heading("Reusable tasks"));
                foreach (var item in _reusable.Where(item => Match(item.Name) || Match(item.Description)).OrderByDescending(item => item.UpdatedAt))
                    Content.Add(ItemCard("Tasks.Reusable." + item.Id.ToString("N"), item.Name, item.Description, "Edit", (_, _) => OpenReusableEditor(item)));
                foreach (var item in _scheduled.Where(item => Match(item.Name) || Match(item.Detail)))
                    Content.Add(ItemCard("Tasks.Automatic." + item.Id.ToString("N"), item.Name, item.Detail, "Run now", (_, _) => RunRequested?.Invoke(this, new TasksHavenInstructionEventArgs(item.Instruction))));
                break;
        }
    }

    private bool Match(string value) => string.IsNullOrWhiteSpace(_query) || value.Contains(_query.Trim(), StringComparison.OrdinalIgnoreCase);

    private void OnSearchInvalidated(object? sender, EventArgs e)
    {
        if (string.Equals(_query, SearchInput.Text, StringComparison.Ordinal)) return;
        _query = SearchInput.Text;
        Render();
    }

    private static Container ItemCard(string name, string title, string detail, string action, EventHandler handler)
    {
        var card = new Container { Name = name, Layout = HavenLayout.Vertical };
        card.SetValue(HavenProperties.Background, "AccentMuted");
        card.SetValue(HavenProperties.BorderColor, "AccentSecondary");
        card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        card.SetValue(HavenProperties.Padding, HavenThickness.Parse("16px"));
        card.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        card.Add(new HavenText(title) { Level = TextLevel.H3 });
        card.Add(Muted(null, detail));
        card.Add(Action(name + ".Action", action, ButtonVariant.Secondary, handler));
        return card;
    }

    private static HavenText Heading(string content) => new(content) { Level = TextLevel.H2 };

    private static HavenText Muted(string? name, string content)
    {
        var text = new HavenText(content) { Name = name, Level = TextLevel.Caption };
        text.SetValue(HavenProperties.Foreground, "TextSecondary");
        return text;
    }

    private static HavenButton Action(string name, string content, ButtonVariant variant, EventHandler handler)
    {
        var button = new HavenButton { Name = name, Content = content, Variant = variant };
        button.Invoked += handler;
        return button;
    }

    public void Dispose() => SearchInput.Invalidated -= OnSearchInvalidated;
}
