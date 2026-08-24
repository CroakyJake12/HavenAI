using Haven.Application.Automations;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using GraphNodeEditor = Haven.UI.Components.NodeEditor;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Automations;

internal sealed partial class AutomationsHavenScene
{
    private readonly GraphNodeEditor _graphEditor = new() { Name = "Automations.Graph" };
    private readonly Container _inspectorFields = new() { Name = "Automations.Inspector.Fields", Layout = HavenLayout.Vertical };
    private readonly Container _diagnostics = new() { Name = "Automations.Diagnostics", Layout = HavenLayout.Vertical };
    private readonly Container _testTrace = new() { Name = "Automations.TestTrace", Layout = HavenLayout.Vertical };
    private readonly Container _nodePickerItems = new() { Name = "Automations.NodePicker.Items", Layout = HavenLayout.Vertical };
    private readonly Container _deviceParameterFields = new() { Name = "Automations.Device.Parameters", Layout = HavenLayout.Vertical };
    private readonly Dictionary<string, Input> _deviceParameterInputs = new(StringComparer.OrdinalIgnoreCase);
    private Input _nameInput = null!;
    private Input _goalInput = null!;
    private Input _rulesInput = null!;
    private Input _aiInput = null!;
    private Input _nodeSearchInput = null!;
    private Select _deviceTargetSelect = null!;
    private Select _deviceActionSelect = null!;
    private HavenText _graphSummary = null!;
    private HavenText _deviceStatus = null!;
    private HavenText _editorTitle = null!;
    private ReusableTaskDefinition? _editingWorkflow;
    private AutomationGraphDefinition _editingGraph = AutomationGraphDefinition.Empty;
    private DeviceCapabilitySnapshot? _deviceSnapshot;
    private HavenPoint? _pendingNodeInsertWorld;
    private Guid? _inspectedNodeId;
    private bool _suppressGraphChanges;
    private bool _graphLoadFailed;

    public event EventHandler? BackRequested;
    public event EventHandler? SaveRequested;
    public event EventHandler? TestGraphRequested;
    public event Action<string>? AiEditRequested;

    public GraphNodeEditor GraphEditor => _graphEditor;
    public ReusableTaskDefinition? EditingWorkflow => _editingWorkflow;
    public string WorkflowName => _nameInput.Text.Trim();
    public string WorkflowGoal => _goalInput.Text.Trim();
    public string WorkflowRules => _rulesInput.Text.Trim();

    private Container BuildEditor()
    {
        _graphEditor.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _graphEditor.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        _graphEditor.SetValue(HavenProperties.MinHeight, HavenLength.Px(520));
        _graphEditor.DocumentChanged += OnGraphDocumentChanged;
        _graphEditor.SelectionChanged += OnGraphSelectionChanged;
        _graphEditor.EmptySpaceContextRequested += point => OpenNodePicker(point);

        _inspectorFields.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        _diagnostics.SetValue(HavenProperties.Gap, HavenLength.Px(5));
        _testTrace.SetValue(HavenProperties.Gap, HavenLength.Px(7));
        _deviceParameterFields.SetValue(HavenProperties.Gap, HavenLength.Px(6));

        var layer = new Container { Name = "Automations.Editor", Layout = HavenLayout.Grid, Columns = "1fr", Rows = "Auto 1fr" };
        layer.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        layer.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        layer.SetValue(HavenProperties.Padding, HavenThickness.Parse("16px 18px 18px 18px"));
        layer.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        layer.SetValue(HavenProperties.Background, "Surface");

        var header = new Container { Name = "Automations.Editor.Header", Layout = HavenLayout.Grid, Columns = "1fr Auto Auto Auto", Rows = "Auto" };
        header.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        header.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        _editorTitle = Heading("Automations.Editor.Title", "Create workflow", TextLevel.H2);
        header.Add(_editorTitle);
        var test = ActionButton("Automations.Editor.Test", "Test graph", ButtonVariant.Secondary, (_, _) => TestGraphRequested?.Invoke(this, EventArgs.Empty));
        test.SetValue(HavenProperties.Column, 1);
        header.Add(test);
        var back = ActionButton("Automations.Editor.Back", "Back", ButtonVariant.Ghost, (_, _) => BackRequested?.Invoke(this, EventArgs.Empty));
        back.SetValue(HavenProperties.Column, 2);
        header.Add(back);
        var save = ActionButton("Automations.Editor.Save", "Save", ButtonVariant.Primary, (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty));
        save.SetValue(HavenProperties.Column, 3);
        header.Add(save);
        layer.Add(header);

        var workspace = new Container { Name = "Automations.Editor.Workspace", Layout = HavenLayout.Grid, Columns = "1fr 340px", Rows = "1fr" };
        workspace.SetValue(HavenProperties.Row, 1);
        workspace.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        workspace.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        workspace.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        workspace.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);

        var canvas = Card("Automations.Editor.Canvas");
        canvas.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        canvas.SetValue(HavenProperties.Padding, HavenThickness.Parse("10px"));
        canvas.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);
        var toolbar = new Container { Name = "Automations.Editor.Toolbar", Layout = HavenLayout.Wrap };
        toolbar.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        var add = ActionButton("Automations.Editor.AddNode", "+ Add node", ButtonVariant.Primary, (_, _) => OpenNodePicker(null));
        var undo = ActionButton("Automations.Editor.Undo", "Undo", ButtonVariant.Tertiary, (_, _) => { _graphEditor.Undo(); RefreshDiagnostics(); });
        var redo = ActionButton("Automations.Editor.Redo", "Redo", ButtonVariant.Tertiary, (_, _) => { _graphEditor.Redo(); RefreshDiagnostics(); });
        var duplicate = ActionButton("Automations.Editor.Duplicate", "Duplicate", ButtonVariant.Tertiary, (_, _) => { _graphEditor.DuplicateSelection(); RefreshDiagnostics(); });
        var delete = ActionButton("Automations.Editor.Delete", "Delete", ButtonVariant.Tertiary, (_, _) => { _graphEditor.DeleteSelection(); RefreshDiagnostics(); });
        var fit = ActionButton("Automations.Editor.Fit", "Fit", ButtonVariant.Tertiary, (_, _) => { _graphEditor.FitToDocument(); RefreshDiagnostics(); });
        toolbar.Add(add); toolbar.Add(undo); toolbar.Add(redo); toolbar.Add(duplicate); toolbar.Add(delete); toolbar.Add(fit);
        _graphSummary = Muted("Automations.Editor.GraphSummary", "0 nodes · 0 connections");
        canvas.Add(toolbar);
        canvas.Add(_graphSummary);
        canvas.Add(_graphEditor);
        workspace.Add(canvas);

        var inspector = Card("Automations.Editor.Inspector");
        inspector.SetValue(HavenProperties.Column, 1);
        inspector.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        inspector.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        inspector.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        inspector.Add(Heading(null, "Workflow", TextLevel.H3));
        _nameInput = NewInput("Automations.Editor.Name", "Workflow name", false);
        _goalInput = NewInput("Automations.Editor.Goal", "Desired outcome", true);
        _rulesInput = NewInput("Automations.Editor.Rules", "Rules that must be followed", true);
        inspector.Add(Field("Name", _nameInput));
        inspector.Add(Field("Goal", _goalInput));
        inspector.Add(Field("Rules", _rulesInput));
        inspector.Add(Heading(null, "Edit graph with Haven", TextLevel.H3));
        _aiInput = NewInput("Automations.Editor.AiInstruction", "Describe a typed graph edit", true);
        inspector.Add(_aiInput);
        inspector.Add(ActionButton("Automations.Editor.AiApply", "Edit with Haven", ButtonVariant.Secondary, (_, _) =>
        {
            var instruction = _aiInput.Text.Trim();
            if (instruction.Length == 0) SetStatus("Describe the graph change you want Haven to make.", true);
            else AiEditRequested?.Invoke(instruction);
        }));
        inspector.Add(Heading(null, "Selected node", TextLevel.H3));
        inspector.Add(_inspectorFields);
        inspector.Add(Heading(null, "Validation", TextLevel.H3));
        inspector.Add(_diagnostics);
        inspector.Add(Heading(null, "Test trace", TextLevel.H3));
        inspector.Add(_testTrace);
        workspace.Add(inspector);
        layer.Add(workspace);
        return layer;
    }

    private Container BuildNodePicker()
    {
        var overlay = new Container { Name = "Automations.NodePicker", Layout = HavenLayout.Overlay };
        overlay.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        overlay.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        overlay.SetValue(HavenProperties.ZIndex, 100);
        overlay.SetValue(HavenProperties.Background, "Overlay");

        var card = Card("Automations.NodePicker.Card");
        card.SetValue(HavenProperties.Width, HavenLength.Px(420));
        card.SetValue(HavenProperties.MaxHeight, HavenLength.Px(560));
        card.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        card.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        card.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        var header = new Container { Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "Auto" };
        header.Add(Heading(null, "Add node", TextLevel.H2));
        var close = ActionButton("Automations.NodePicker.Close", "Close", ButtonVariant.Ghost, (_, _) => CloseNodePicker());
        close.SetValue(HavenProperties.Column, 1);
        header.Add(close);
        card.Add(header);
        _nodeSearchInput = NewInput("Automations.NodePicker.Search", "Search nodes", false);
        _nodeSearchInput.Invalidated += OnNodeSearchInvalidated;
        card.Add(_nodeSearchInput);
        _nodePickerItems.SetValue(HavenProperties.Gap, HavenLength.Px(7));
        card.Add(_nodePickerItems);
        overlay.Add(card);
        RebuildNodePicker();
        return overlay;
    }

    public void ShowEditor(ReusableTaskDefinition? workflow)
    {
        _editingWorkflow = workflow;
        _editorTitle.Content = workflow is null ? "Create workflow" : "Edit workflow";
        _nameInput.Text = workflow?.Name ?? string.Empty;
        _goalInput.Text = workflow?.Description ?? string.Empty;
        _rulesInput.Text = ExtractSection(workflow?.Instruction ?? string.Empty, "Rules:", "Completion:") ?? string.Empty;
        _aiInput.Text = string.Empty;
        _inspectedNodeId = null;
        _graphLoadFailed = false;
        _editingGraph = AutomationGraphDefinition.Empty;
        if (!string.IsNullOrWhiteSpace(workflow?.GraphJson))
        {
            if (AutomationGraphCodec.TryDeserialize(workflow.GraphJson, out var graph)) _editingGraph = graph;
            else _graphLoadFailed = true;
        }

        _suppressGraphChanges = true;
        try
        {
            _graphEditor.Document = AutomationGraphEditorAdapter.ToEditor(_editingGraph);
            _graphEditor.ClearSelection();
            _graphEditor.ResetViewport();
        }
        finally { _suppressGraphChanges = false; }
        Clear(_testTrace);
        _testTrace.Add(Muted(null, _graphLoadFailed
            ? "Stored graph data could not be read. Add a node to replace it or go back without saving."
            : "Run Test graph to see validation and per-node execution trace."));
        SetVisible(DashboardLayer, false);
        SetVisible(EditorLayer, true);
        CloseNodePicker();
        RefreshInspector();
        RefreshDiagnostics();
    }

    public void ApplyAiGraph(AutomationGraphDefinition graph, string status)
    {
        ArgumentNullException.ThrowIfNull(graph);
        _suppressGraphChanges = true;
        try
        {
            _editingGraph = graph;
            _graphEditor.Document = AutomationGraphEditorAdapter.ToEditor(graph);
            _graphEditor.ClearSelection();
            _graphLoadFailed = false;
        }
        finally { _suppressGraphChanges = false; }
        _aiInput.Text = string.Empty;
        _graphEditor.FitToDocument();
        RefreshInspector();
        RefreshDiagnostics();
        SetStatus(status);
    }

    public void SetDeviceCapability(DeviceCapabilitySnapshot? snapshot)
    {
        _deviceSnapshot = snapshot;
        if (_inspectedNodeId is not null) RefreshInspector();
        RefreshDiagnostics();
    }

    public bool TryGetGraph(out AutomationGraphDefinition graph, out string? error)
    {
        graph = _editingGraph;
        error = null;
        if (_graphLoadFailed)
        {
            error = "Stored graph data is unreadable. Add a node to replace it or go back without saving.";
            return false;
        }
        var structural = _graphEditor.ValidateDocument();
        if (structural.Count > 0)
        {
            error = structural[0].Message;
            return false;
        }
        graph = AutomationGraphEditorAdapter.FromEditor(_graphEditor.Document, _editingGraph);
        var configuration = AutomationGraphEditorAdapter.ValidateConfiguration(graph);
        if (configuration.Count > 0)
        {
            error = configuration[0].Message;
            return false;
        }
        if (!AutomationGraphScheduleBinder.TryBind(graph, DateTimeOffset.UtcNow, out _, out var scheduleError))
        {
            error = scheduleError ?? "The graph schedule is invalid.";
            return false;
        }
        if (!ValidateDeviceAvailability(graph, out error)) return false;
        _editingGraph = graph;
        return true;
    }

    public string BuildInstructions()
    {
        var name = string.IsNullOrWhiteSpace(WorkflowName) ? "Untitled workflow" : WorkflowName;
        return $"Task: {name}\n\nGoal:\n{WorkflowGoal}\n\nRules:\n{WorkflowRules}\n\nCompletion:\nVerify the requested outcome and report confirmed evidence. Stop safely and explain any blocker.";
    }

    public void SetGraphTestResult(AutomationGraphRunResult result)
    {
        Clear(_testTrace);
        if (result.ValidationIssues.Count > 0)
        {
            foreach (var issue in result.ValidationIssues) _testTrace.Add(TraceCard("Validation", issue.Message));
        }
        else
        {
            foreach (var trace in result.Trace)
            {
                var title = _graphEditor.Document.Nodes.FirstOrDefault(node => node.Id == trace.NodeId)?.Title ?? trace.Category;
                var detail = trace.Message;
                if (trace.Inputs is { Count: > 0 }) detail += " · inputs: " + string.Join(", ", trace.Inputs.Select(pair => $"{NodeLabel(pair.Key)}={pair.Value ?? "null"}"));
                if (!string.IsNullOrWhiteSpace(trace.Output)) detail += $" · output: {trace.Output}";
                if (!string.IsNullOrWhiteSpace(trace.Branch)) detail += $" · branch: {trace.Branch}";
                _testTrace.Add(TraceCard($"{trace.Status}: {title}", detail));
            }
        }
        var failed = result.Trace.FirstOrDefault(trace => trace.Status == AutomationGraphTraceStatus.Failed);
        if (failed is not null) _graphEditor.SelectNode(failed.NodeId);
    }

    private void OnGraphDocumentChanged(NodeEditorDocument document)
    {
        if (_suppressGraphChanges) return;
        _graphLoadFailed = false;
        _editingGraph = AutomationGraphEditorAdapter.FromEditor(document, _editingGraph);
        RefreshDiagnostics();
    }

    private void OnGraphSelectionChanged(IReadOnlyCollection<Guid> selection)
    {
        if (_suppressGraphChanges) return;
        _editingGraph = AutomationGraphEditorAdapter.FromEditor(_graphEditor.Document, _editingGraph);
        RefreshInspector();
    }

    private void OpenNodePicker(HavenPoint? worldPoint)
    {
        _pendingNodeInsertWorld = worldPoint;
        _nodeSearchInput.Text = string.Empty;
        RebuildNodePicker();
        SetVisible(NodePickerLayer, true);
    }

    private void CloseNodePicker()
    {
        _pendingNodeInsertWorld = null;
        if (NodePickerLayer is not null) SetVisible(NodePickerLayer, false);
    }

    private void OnNodeSearchInvalidated(object? sender, EventArgs e) => RebuildNodePicker();

    private void RebuildNodePicker()
    {
        if (_nodeSearchInput is null) return;
        Clear(_nodePickerItems);
        var matches = _graphEditor.SearchTemplates(AutomationGraphEditorAdapter.Templates, _nodeSearchInput.Text);
        foreach (var group in matches.GroupBy(template => template.Category).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var category = Muted(null, group.Key.ToUpperInvariant());
            category.SetValue(HavenProperties.FontWeight, 800);
            _nodePickerItems.Add(category);
            foreach (var template in group.OrderBy(template => template.Title, StringComparer.OrdinalIgnoreCase))
            {
                var button = new HavenButton { Content = template.Title, Variant = ButtonVariant.Tertiary };
                button.Accessibility.AccessibleName = $"Add {template.Title} node";
                button.Accessibility.Description = template.Subtitle;
                button.SetValue(HavenProperties.Width, HavenLength.Percent(100));
                button.Invoked += (_, _) =>
                {
                    var point = _pendingNodeInsertWorld ?? _graphEditor.ViewportCenterWorld;
                    _graphEditor.AddNode(template, point.X - 110, point.Y - 59);
                    CloseNodePicker();
                    RefreshDiagnostics();
                };
                _nodePickerItems.Add(button);
            }
        }
        if (matches.Count == 0) _nodePickerItems.Add(Muted(null, "No nodes match this search."));
    }

    private void RefreshInspector()
    {
        Clear(_inspectorFields);
        if (_graphEditor.SelectedNodeIds.Count == 0)
        {
            _inspectedNodeId = null;
            _inspectorFields.Add(Muted(null, "Select a node on the canvas to configure it."));
            return;
        }
        var nodeId = _graphEditor.SelectedNodeIds.First();
        var node = _graphEditor.Document.Nodes.FirstOrDefault(value => value.Id == nodeId);
        if (node is null) return;
        _inspectedNodeId = nodeId;
        var category = Muted(null, node.Category.ToUpperInvariant());
        category.SetValue(HavenProperties.FontWeight, 800);
        _inspectorFields.Add(category);
        AddNodeTextField(node, "Title", node.Title, (value, current) => current with { Title = value });
        AddNodeTextField(node, "Subtitle", node.Subtitle, (value, current) => current with { Subtitle = value });

        if (node.Category.Equals("Condition", StringComparison.OrdinalIgnoreCase) || node.Category.Equals("Branch", StringComparison.OrdinalIgnoreCase))
            AddNodeParameterField(node, "expression", "Expression", "true or false");
        else if (node.Category.Equals("Schedule", StringComparison.OrdinalIgnoreCase))
        {
            var recurrence = node.Title.Contains("Recurrence", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(AutomationGraphEditorAdapter.ReadParameter(node, "recurrence"));
            AddNodeParameterField(node, recurrence ? "recurrence" : "schedule", recurrence ? "Recurrence" : "Run at", recurrence ? "daily 08:30" : "2026-08-22T09:00:00+01:00");
            _inspectorFields.Add(Muted(null, recurrence ? "Examples: hourly · every 2 hours · daily 08:30 · weekly Monday 08:30." : "Use a future date/time including its UTC offset."));
        }
        else if (node.Category.Equals("ConditionWatch", StringComparison.OrdinalIgnoreCase) || node.Category.Equals("Condition Watch", StringComparison.OrdinalIgnoreCase))
        {
            AddNodeParameterField(node, "watch", "Watch condition", "Describe what should become true");
            AddNodeParameterField(node, "intervalMinutes", "Check every (minutes)", "60");
            _inspectorFields.Add(Muted(null, "Condition watches run no more often than hourly."));
        }
        else if (node.Category.Equals(BuiltInAutomationNodeCategory.App, StringComparison.OrdinalIgnoreCase))
            AddNodeParameterField(node, "name", "Application", "Calculator");
        else if (node.Category.Equals(BuiltInAutomationNodeCategory.File, StringComparison.OrdinalIgnoreCase))
        {
            var operation = AutomationGraphEditorAdapter.ReadParameter(node, "operation") ?? "read";
            AddNodeParameterField(node, "workspaceRoot", "Workspace root", "C:\\path\\to\\workspace");
            AddNodeParameterField(node, operation.Equals("search", StringComparison.OrdinalIgnoreCase) ? "pattern" : "path", operation.Equals("search", StringComparison.OrdinalIgnoreCase) ? "Filename pattern" : "Relative file path", operation.Equals("search", StringComparison.OrdinalIgnoreCase) ? "*.md" : "notes/today.md");
        }
        else if (node.Category.Equals(BuiltInAutomationNodeCategory.Action, StringComparison.OrdinalIgnoreCase))
        {
            var action = AutomationGraphEditorAdapter.ReadParameter(node, "action") ?? "emit";
            AddNodeParameterField(node, action.Equals("delay", StringComparison.OrdinalIgnoreCase) ? "milliseconds" : "value", action.Equals("delay", StringComparison.OrdinalIgnoreCase) ? "Delay (milliseconds)" : "Value", action.Equals("delay", StringComparison.OrdinalIgnoreCase) ? "1000" : "ready");
        }
        else if (node.Category.Equals(DeviceAutomationNodeCategory.Key, StringComparison.OrdinalIgnoreCase))
            BuildDeviceInspector(node);

        var actions = new Container { Layout = HavenLayout.Horizontal };
        actions.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        actions.Add(ActionButton(null, "Duplicate node", ButtonVariant.Tertiary, (_, _) => _graphEditor.DuplicateSelection()));
        actions.Add(ActionButton(null, "Delete node", ButtonVariant.Ghost, (_, _) => _graphEditor.DeleteSelection()));
        _inspectorFields.Add(actions);
    }

    private void AddNodeTextField(NodeEditorNode node, string label, string value, Func<string, NodeEditorNode, NodeEditorNode> update)
    {
        var input = NewInput(null, label, false);
        input.Text = value;
        var previous = value;
        input.Invalidated += (_, _) =>
        {
            if (_suppressGraphChanges || input.Text == previous) return;
            previous = input.Text;
            _graphEditor.UpdateNode(node.Id, current => update(input.Text, current));
        };
        _inspectorFields.Add(Field(label, input));
    }

    private void AddNodeParameterField(NodeEditorNode node, string key, string label, string placeholder)
    {
        var input = NewInput(null, placeholder, false);
        input.Text = AutomationGraphEditorAdapter.ReadParameter(node, key) ?? string.Empty;
        var previous = input.Text;
        input.Invalidated += (_, _) =>
        {
            if (_suppressGraphChanges || input.Text == previous) return;
            previous = input.Text;
            _graphEditor.UpdateNode(node.Id, current => current with { Metadata = AutomationGraphEditorAdapter.WithParameter(current.Metadata, key, input.Text) });
        };
        _inspectorFields.Add(Field(label, input));
    }

    private void BuildDeviceInspector(NodeEditorNode node)
    {
        _deviceTargetSelect = new Select { Name = "Automations.Device.Target" };
        _deviceTargetSelect.Accessibility.AccessibleName = "Target device";
        _deviceActionSelect = new Select { Name = "Automations.Device.Action" };
        _deviceActionSelect.Accessibility.AccessibleName = "Device action";
        _deviceStatus = Muted("Automations.Device.Status", string.Empty);
        Clear(_deviceParameterFields);
        _deviceParameterInputs.Clear();

        var savedTarget = AutomationGraphEditorAdapter.ReadDeviceTarget(node);
        var savedAction = AutomationGraphEditorAdapter.ReadDeviceAction(node);
        if (_deviceSnapshot is null)
        {
            _deviceTargetSelect.Items = savedTarget is null ? [] : [savedTarget.DisplayName + " (unavailable)"];
            _deviceTargetSelect.SelectedIndex = savedTarget is null ? -1 : 0;
            _deviceActionSelect.Items = string.IsNullOrWhiteSpace(savedAction) ? [] : [savedAction + " (unavailable)"];
            _deviceActionSelect.SelectedIndex = string.IsNullOrWhiteSpace(savedAction) ? -1 : 0;
            _deviceStatus.Content = savedTarget is null
                ? "No real device-action target is available in this host."
                : $"Saved target {savedTarget.DisplayName} is not currently available. Haven will not substitute another device.";
        }
        else
        {
            var targetMatches = savedTarget is null || (string.Equals(savedTarget.Id, _deviceSnapshot.Target.Id, StringComparison.OrdinalIgnoreCase) && (string.IsNullOrWhiteSpace(savedTarget.ProviderId) || string.Equals(savedTarget.ProviderId, _deviceSnapshot.Target.ProviderId, StringComparison.OrdinalIgnoreCase)));
            _deviceTargetSelect.Items = targetMatches ? [_deviceSnapshot.Target.DisplayName] : [savedTarget!.DisplayName + " (unavailable)"];
            _deviceTargetSelect.SelectedIndex = 0;
            var actions = _deviceSnapshot.Actions.ToArray();
            _deviceActionSelect.Items = actions.Select(action => $"{action.Name} — {action.Availability}").ToArray();
            var selectedActionIndex = Array.FindIndex(actions, action => string.Equals(action.Key, savedAction, StringComparison.OrdinalIgnoreCase));
            _deviceActionSelect.SelectedIndex = selectedActionIndex >= 0 ? selectedActionIndex : actions.Length > 0 && savedTarget is null ? 0 : -1;
            _deviceStatus.Content = targetMatches
                ? (_deviceSnapshot.IsReachable ? $"Capabilities from {_deviceSnapshot.Target.DisplayName}." : $"{_deviceSnapshot.Target.DisplayName} is currently unavailable.")
                : $"Saved target {savedTarget!.DisplayName} is not currently available. Haven will not substitute another device.";

            _deviceActionSelect.SelectionChanged += (_, _) => ApplyDeviceSelection(node.Id);
            if (savedTarget is null) StoreDeviceTarget(node.Id, _deviceSnapshot.Target);
            ConfigureDeviceParameters(node.Id);
        }

        _inspectorFields.Add(Field("Target device", _deviceTargetSelect));
        _inspectorFields.Add(Field("Device action", _deviceActionSelect));
        _inspectorFields.Add(_deviceStatus);
        _inspectorFields.Add(_deviceParameterFields);
    }

    private void ApplyDeviceSelection(Guid nodeId)
    {
        if (_deviceSnapshot is null || _deviceActionSelect.SelectedIndex < 0 || _deviceActionSelect.SelectedIndex >= _deviceSnapshot.Actions.Count) return;
        var action = _deviceSnapshot.Actions[_deviceActionSelect.SelectedIndex];
        _graphEditor.UpdateNode(nodeId, node => node with
        {
            Title = action.Name,
            Subtitle = _deviceSnapshot.Target.DisplayName,
            Metadata = AutomationGraphEditorAdapter.WithDeviceAction(AutomationGraphEditorAdapter.WithDeviceTarget(node.Metadata, _deviceSnapshot.Target), action.Key)
        });
        ConfigureDeviceParameters(nodeId);
        RefreshDiagnostics();
    }

    private void ConfigureDeviceParameters(Guid nodeId)
    {
        Clear(_deviceParameterFields);
        _deviceParameterInputs.Clear();
        if (_deviceSnapshot is null || _deviceActionSelect.SelectedIndex < 0 || _deviceActionSelect.SelectedIndex >= _deviceSnapshot.Actions.Count) return;
        var action = _deviceSnapshot.Actions[_deviceActionSelect.SelectedIndex];
        _deviceStatus.Content = action.Availability switch
        {
            DeviceActionAvailability.Supported => $"Supported on {_deviceSnapshot.Target.DisplayName}.",
            DeviceActionAvailability.PermissionRequired => $"Available on {_deviceSnapshot.Target.DisplayName} with permission. Haven will request permission before execution.",
            DeviceActionAvailability.AvailableThroughPlugin => $"Available through {action.ProviderId}.",
            DeviceActionAvailability.Unsupported => $"Unsupported on {_deviceSnapshot.Target.DisplayName}. Haven will not save this action as runnable.",
            _ => "Availability could not be resolved."
        };
        var current = _graphEditor.Document.Nodes.FirstOrDefault(node => node.Id == nodeId);
        foreach (var parameter in action.RequiredParameters)
        {
            var input = NewInput(null, parameter, false);
            input.Text = current is null ? string.Empty : AutomationGraphEditorAdapter.ReadParameter(current, parameter) ?? string.Empty;
            var previous = input.Text;
            input.Invalidated += (_, _) =>
            {
                if (_suppressGraphChanges || input.Text == previous) return;
                previous = input.Text;
                _graphEditor.UpdateNode(nodeId, node => node with { Metadata = AutomationGraphEditorAdapter.WithParameter(node.Metadata, parameter, input.Text) });
            };
            _deviceParameterInputs[parameter] = input;
            _deviceParameterFields.Add(Field(parameter, input));
        }
        if (action.RequiredParameters.Count == 0) _deviceParameterFields.Add(Muted(null, "No parameters required."));
    }

    private void StoreDeviceTarget(Guid nodeId, DeviceTargetDescriptor target)
    {
        _graphEditor.UpdateNode(nodeId, node => node with
        {
            Subtitle = target.DisplayName,
            Metadata = AutomationGraphEditorAdapter.WithDeviceTarget(node.Metadata, target)
        });
    }

    private bool ValidateDeviceAvailability(AutomationGraphDefinition graph, out string? error)
    {
        error = null;
        foreach (var node in graph.Nodes.Where(node => node.Category.Equals(DeviceAutomationNodeCategory.Key, StringComparison.OrdinalIgnoreCase)))
        {
            if (node.DeviceTarget is null || string.IsNullOrWhiteSpace(node.ActionKey)) continue;
            if (_deviceSnapshot is null || !string.Equals(node.DeviceTarget.Id, _deviceSnapshot.Target.Id, StringComparison.OrdinalIgnoreCase))
            {
                error = $"Saved target {node.DeviceTarget.DisplayName} is not currently available. Haven will not substitute another device.";
                return false;
            }
            var action = _deviceSnapshot.Actions.FirstOrDefault(value => string.Equals(value.Key, node.ActionKey, StringComparison.OrdinalIgnoreCase));
            if (action is null || action.Availability is DeviceActionAvailability.Unsupported or DeviceActionAvailability.Unknown)
            {
                error = action is null ? $"{node.ActionKey} is not exposed by the current device provider." : $"{action.Name} is {action.Availability.ToString().ToLowerInvariant()} on {_deviceSnapshot.Target.DisplayName}.";
                return false;
            }
            foreach (var required in action.RequiredParameters)
            {
                if (!node.Parameters.TryGetValue(required, out var value) || string.IsNullOrWhiteSpace(value))
                {
                    error = $"Enter a value for {required}.";
                    return false;
                }
            }
        }
        return true;
    }

    private void RefreshDiagnostics()
    {
        _editingGraph = AutomationGraphEditorAdapter.FromEditor(_graphEditor.Document, _editingGraph);
        var list = _graphEditor.ValidateDocument().Concat(AutomationGraphEditorAdapter.ValidateConfiguration(_editingGraph)).ToList();
        if (!AutomationGraphScheduleBinder.TryBind(_editingGraph, DateTimeOffset.UtcNow, out _, out var scheduleError) && !string.IsNullOrWhiteSpace(scheduleError))
            list.Add(new NodeEditorDiagnostic("schedule.invalid", scheduleError));
        if (!ValidateDeviceAvailabilityForDiagnostics(_editingGraph, out var deviceError) && !string.IsNullOrWhiteSpace(deviceError))
            list.Add(new NodeEditorDiagnostic("device.availability", deviceError));
        Clear(_diagnostics);
        if (_graphLoadFailed) _diagnostics.Add(Muted(null, "Stored graph data is unreadable. Add a node to replace it or go back without saving."));
        foreach (var diagnostic in list.Take(8)) _diagnostics.Add(Muted(null, "• " + diagnostic.Message));
        if (!_graphLoadFailed && list.Count == 0) _diagnostics.Add(Muted(null, "Graph structure, scheduling, configuration, and current device capabilities are valid."));
        if (list.Count > 8) _diagnostics.Add(Muted(null, $"+{list.Count - 8} more validation issues"));
        _graphEditor.Diagnostics = list;
        _graphSummary.Content = $"{_graphEditor.Document.Nodes.Count} nodes · {_graphEditor.Document.Edges.Count} connections · {Math.Round(_graphEditor.Zoom * 100)}%";
    }

    private bool ValidateDeviceAvailabilityForDiagnostics(AutomationGraphDefinition graph, out string? error)
    {
        var deviceNodes = graph.Nodes.Where(node => node.Category.Equals(DeviceAutomationNodeCategory.Key, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (deviceNodes.Length == 0) { error = null; return true; }
        return ValidateDeviceAvailability(graph, out error);
    }

    private string NodeLabel(Guid id) => _graphEditor.Document.Nodes.FirstOrDefault(node => node.Id == id)?.Title ?? id.ToString("N")[..8];

    private static Container TraceCard(string title, string detail)
    {
        var card = Card(null);
        card.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 10px"));
        card.SetValue(HavenProperties.Shadow, "None");
        card.Add(Heading(null, title, TextLevel.H4));
        card.Add(Muted(null, detail));
        return card;
    }

    private static Container Field(string label, HavenElement control)
    {
        var field = new Container { Layout = HavenLayout.Vertical };
        field.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        var caption = Muted(null, label);
        caption.SetValue(HavenProperties.FontWeight, 750);
        field.Add(caption);
        field.Add(control);
        return field;
    }

    private static Input NewInput(string? name, string placeholder, bool multiline)
    {
        var input = new Input { Name = name, Placeholder = placeholder, Multiline = multiline };
        input.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        if (multiline) input.SetValue(HavenProperties.MinHeight, HavenLength.Px(86));
        return input;
    }

    private static string? ExtractSection(string text, string startLabel, string endLabel)
    {
        var start = text.IndexOf(startLabel, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += startLabel.Length;
        var end = text.IndexOf(endLabel, start, StringComparison.OrdinalIgnoreCase);
        return (end < 0 ? text[start..] : text[start..end]).Trim();
    }

    private void DisposeEditor()
    {
        _graphEditor.DocumentChanged -= OnGraphDocumentChanged;
        _graphEditor.SelectionChanged -= OnGraphSelectionChanged;
        if (_nodeSearchInput is not null) _nodeSearchInput.Invalidated -= OnNodeSearchInvalidated;
    }
}
