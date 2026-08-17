using Haven.UI;
using Haven.UI.Components;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Tasks;

internal sealed partial class TasksHavenScene
{
    private Container _editorView = null!;
    private Container _instructionsView = null!;
    private Input _nameInput = null!;
    private Input _goalInput = null!;
    private Input _rulesInput = null!;
    private Input _assistantInput = null!;
    private Input _instructionsInput = null!;
    private HavenText _editorStatus = null!;

    public Container EditorView => _editorView;
    public Container InstructionsView => _instructionsView;
    public Input NameInput => _nameInput;
    public Input GoalInput => _goalInput;
    public Input RulesInput => _rulesInput;
    public Input AssistantInput => _assistantInput;
    public Input InstructionsInput => _instructionsInput;
    public HavenText EditorStatusText => _editorStatus;
    public Guid? EditingTaskId { get; private set; }

    public event EventHandler<TasksHavenTaskEventArgs>? DeleteRequested;
    public event EventHandler<TasksHavenInstructionEventArgs>? TestRequested;
    public event EventHandler<TasksHavenInstructionEventArgs>? AssistantRequested;
    public event EventHandler<TasksHavenDraftEventArgs>? SaveRequested;

    private void InitializeEditor()
    {
        _nameInput = EditorInput("Tasks.Editor.Name", "Task name");
        _goalInput = EditorInput("Tasks.Editor.Goal", "What outcome should Haven achieve?");
        _rulesInput = EditorInput("Tasks.Editor.Rules", "Rules Haven must follow", multiline: true, minimumHeight: 150);
        _assistantInput = EditorInput("Tasks.Editor.Assistant", "Ask Haven about this task");
        _instructionsInput = EditorInput("Tasks.Instructions.Input", "Task instructions", multiline: true, minimumHeight: 320);
        _editorStatus = Muted("Tasks.Editor.Status", string.Empty);

        _editorView = new Container { Name = "Tasks.Editor", Layout = HavenLayout.Vertical };
        _editorView.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _editorView.SetValue(HavenProperties.MaxWidth, HavenLength.Px(920));
        _editorView.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        _editorView.SetValue(HavenProperties.Gap, HavenLength.Px(18));
        _editorView.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);

        var header = new Container { Layout = HavenLayout.Vertical };
        header.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        header.Add(new HavenText("Reusable task") { Name = "Tasks.Editor.Title", Level = TextLevel.H1 });
        header.Add(Muted(null, "Shape a repeatable outcome, test it safely, and keep the instructions readable."));
        var headerActions = new Container { Layout = HavenLayout.Wrap };
        headerActions.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        headerActions.Add(Action("Tasks.Editor.Discard", "Discard", ButtonVariant.Ghost, (_, _) => ShowDashboard()));
        headerActions.Add(Action("Tasks.Editor.Save", "Save task", ButtonVariant.Primary, (_, _) => RequestSave()));
        header.Add(headerActions);
        _editorView.Add(header);

        var form = new Container { Name = "Tasks.Editor.Form", Layout = HavenLayout.Vertical };
        form.SetValue(HavenProperties.Background, "SurfaceRaised");
        form.SetValue(HavenProperties.BorderColor, "AccentSecondary");
        form.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        form.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(22)));
        form.SetValue(HavenProperties.Shadow, "Card");
        form.SetValue(HavenProperties.Padding, HavenThickness.Parse("18px"));
        form.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        form.Add(Field("Name", _nameInput));
        form.Add(Field("Goal", _goalInput));
        form.Add(Field("Rules", _rulesInput));

        var taskActions = new Container { Layout = HavenLayout.Wrap };
        taskActions.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        taskActions.Add(Action("Tasks.Editor.Run", "Run task", ButtonVariant.Primary, (_, _) =>
            RunRequested?.Invoke(this, new TasksHavenInstructionEventArgs(BuildInstructions()))));
        taskActions.Add(Action("Tasks.Editor.Test", "Test safely", ButtonVariant.Secondary, (_, _) =>
            TestRequested?.Invoke(this, new TasksHavenInstructionEventArgs(BuildSafeTestInstruction()))));
        taskActions.Add(Action("Tasks.Editor.Instructions", "View instructions", ButtonVariant.Ghost, (_, _) => ShowInstructions()));
        taskActions.Add(Action("Tasks.Editor.Delete", "Delete task", ButtonVariant.Danger, (_, _) => RequestDelete()));
        form.Add(taskActions);
        _editorView.Add(form);

        var assistant = new Container { Name = "Tasks.Editor.AssistantPanel", Layout = HavenLayout.Vertical };
        assistant.SetValue(HavenProperties.Background, "AccentMuted");
        assistant.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(20)));
        assistant.SetValue(HavenProperties.Padding, HavenThickness.Parse("16px"));
        assistant.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        assistant.Add(new HavenText("Ask Haven") { Level = TextLevel.H3 });
        assistant.Add(Muted(null, "Ask for help improving the task. Haven opens the request in a Tasks conversation; it does not silently change this draft."));
        assistant.Add(_assistantInput);
        assistant.Add(Action("Tasks.Editor.Assistant.Send", "Ask Haven", ButtonVariant.Secondary, (_, _) => RequestAssistant()));
        _editorView.Add(assistant);
        _editorView.Add(_editorStatus);
        Root.Add(_editorView);

        _instructionsView = new Container { Name = "Tasks.Instructions", Layout = HavenLayout.Vertical };
        _instructionsView.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _instructionsView.SetValue(HavenProperties.MaxWidth, HavenLength.Px(920));
        _instructionsView.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        _instructionsView.SetValue(HavenProperties.Background, "SurfaceRaised");
        _instructionsView.SetValue(HavenProperties.BorderColor, "AccentSecondary");
        _instructionsView.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        _instructionsView.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(22)));
        _instructionsView.SetValue(HavenProperties.Shadow, "Card");
        _instructionsView.SetValue(HavenProperties.Padding, HavenThickness.Parse("18px"));
        _instructionsView.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        _instructionsView.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        _instructionsView.Add(new HavenText("Task instructions") { Level = TextLevel.H1 });
        _instructionsView.Add(Muted(null, "Edit the portable task definition directly. Apply updates the Name, Goal, and Rules fields in this draft."));
        _instructionsView.Add(_instructionsInput);
        var instructionActions = new Container { Layout = HavenLayout.Wrap };
        instructionActions.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        instructionActions.Add(Action("Tasks.Instructions.Cancel", "Cancel", ButtonVariant.Ghost, (_, _) => ReturnToEditor()));
        instructionActions.Add(Action("Tasks.Instructions.Apply", "Apply", ButtonVariant.Primary, (_, _) =>
        {
            ParseInstructions(_instructionsInput.Text);
            SetEditorStatus("Task instructions applied to this draft.");
            ReturnToEditor();
        }));
        _instructionsView.Add(instructionActions);
        Root.Add(_instructionsView);
    }

    private static Input EditorInput(string name, string placeholder, bool multiline = false, double minimumHeight = 48)
    {
        var input = new Input { Name = name, Placeholder = placeholder, Multiline = multiline };
        input.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        input.SetValue(HavenProperties.MinHeight, HavenLength.Px(minimumHeight));
        return input;
    }

    private static Container Field(string label, Input input)
    {
        var field = new Container { Layout = HavenLayout.Vertical };
        field.SetValue(HavenProperties.Gap, HavenLength.Px(5));
        var caption = new HavenText(label) { Level = TextLevel.Caption };
        caption.SetValue(HavenProperties.Foreground, "TextSecondary");
        field.Add(caption);
        field.Add(input);
        return field;
    }

    private void BeginCreateReusable()
    {
        CreateReusableRequested?.Invoke(this, EventArgs.Empty);
        ShowEditor(null);
    }

    private void OpenReusableEditor(TasksHavenReusableItem item)
    {
        EditRequested?.Invoke(this, new TasksHavenTaskEventArgs(item.Id));
        ShowEditor(item);
    }

    internal void ShowEditor(TasksHavenReusableItem? item)
    {
        EditingTaskId = item?.Id;
        _nameInput.Text = item?.Name ?? string.Empty;
        _goalInput.Text = item?.Description ?? string.Empty;
        _rulesInput.Text = ExtractRules(item?.Instruction);
        _assistantInput.Text = string.Empty;
        SetEditorStatus(string.Empty);
        SetDashboardVisible(false);
        _instructionsView.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        _editorView.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
    }

    internal void ShowDashboard()
    {
        EditingTaskId = null;
        _editorView.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        _instructionsView.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        SetDashboardVisible(true);
    }

    private void SetDashboardVisible(bool visible)
    {
        var visibility = visible ? HavenVisibility.Visible : HavenVisibility.Collapsed;
        foreach (var child in Root.Children)
        {
            if (ReferenceEquals(child, _editorView) || ReferenceEquals(child, _instructionsView)) continue;
            child.SetValue(HavenProperties.Visibility, visibility);
        }
    }

    private void ShowInstructions()
    {
        _instructionsInput.Text = BuildInstructions();
        _editorView.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        _instructionsView.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
    }

    private void ReturnToEditor()
    {
        _instructionsView.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        _editorView.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
    }

    private void RequestSave()
    {
        var name = _nameInput.Text.Trim();
        if (name.Length == 0)
        {
            SetEditorStatus("Task name is required.", isError: true);
            return;
        }
        SaveRequested?.Invoke(this, new TasksHavenDraftEventArgs(
            EditingTaskId,
            name,
            _goalInput.Text.Trim(),
            _rulesInput.Text.Trim(),
            BuildInstructions()));
    }

    private void RequestDelete()
    {
        if (EditingTaskId is not Guid taskId)
        {
            SetEditorStatus("Save this task before deleting it.", isError: true);
            return;
        }
        DeleteRequested?.Invoke(this, new TasksHavenTaskEventArgs(taskId));
    }

    private void RequestAssistant()
    {
        var request = _assistantInput.Text.Trim();
        if (request.Length == 0) return;
        _assistantInput.Text = string.Empty;
        AssistantRequested?.Invoke(this, new TasksHavenInstructionEventArgs(
            "Help me configure this reusable Haven Task. Review the current task draft below, answer my request, and propose exact improved Name, Goal, and Rules. Do not run the task yet.\n\n" +
            BuildInstructions() + "\n\nRequest:\n" + request));
    }

    private string BuildSafeTestInstruction() =>
        "TEST MODE — do not mutate files, applications, accounts, services, messages, settings, or external state. " +
        "Walk through the task plan, identify missing inputs and risks, and explain exactly how success would be verified.\n\n" +
        BuildInstructions();

    internal string BuildInstructions()
    {
        var name = _nameInput.Text.Trim();
        if (name.Length == 0) name = "Untitled Task";
        return $"Task: {name}\n\nGoal:\n{_goalInput.Text.Trim()}\n\nRules:\n{_rulesInput.Text.Trim()}\n\nCompletion:\nVerify the requested outcome and report confirmed evidence. Stop safely and explain any blocker.";
    }

    internal void ApplyInstructions(string text)
    {
        _instructionsInput.Text = text ?? string.Empty;
        ParseInstructions(_instructionsInput.Text);
    }

    private void ParseInstructions(string text)
    {
        _nameInput.Text = ExtractSection(text, "Task:", "Goal:") ?? _nameInput.Text;
        _goalInput.Text = ExtractSection(text, "Goal:", "Rules:") ?? _goalInput.Text;
        _rulesInput.Text = ExtractSection(text, "Rules:", "Completion:") ?? _rulesInput.Text;
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

    internal void SetEditorStatus(string text, bool isError = false)
    {
        _editorStatus.Content = text ?? string.Empty;
        _editorStatus.SetValue(HavenProperties.Foreground, isError ? "Danger" : "TextSecondary");
    }
}
