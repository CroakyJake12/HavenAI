using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using Haven.UI.Components;
using GraphNodeEditor = Haven.UI.Components.NodeEditor;

namespace Haven.Desktop.Views.Pages.Automations;

public sealed partial class AutomationsPage
{
    private readonly GraphNodeEditor _graphEditor = new();
    private readonly HavenSearchInput _nodeSearch = new() { PlaceholderText = "Search nodes" };
    private readonly StackPanel _nodeLibraryItems = new() { Spacing = 8 };
    private readonly StackPanel _nodeInspectorFields = new() { Spacing = 10 };
    private readonly StackPanel _graphDiagnostics = new() { Spacing = 6 };
    private readonly StackPanel _graphTestTrace = new() { Spacing = 8 };
    private readonly TextBlock _graphSummary = Muted("0 nodes · 0 connections");
    private HavenSceneControl? _graphHost;
    private Guid? _inspectedNodeId;
    private bool _graphUiConfigured;
    private bool _suppressGraphUi;
    private bool _graphLoadFailed;

    private void BuildEditor()
    {
        var discard = SoftButton("Discard Changes"); discard.Click += (_, _) => ShowDashboard();
        var save = AccentButton("Save Changes"); save.Click += async (_, _) => await SaveGraphAsync();
        var test = AccentButton("Test Graph"); test.Click += async (_, _) => await TestGraphAsync();
        var viewInstructions = SoftButton("Instructions"); viewInstructions.Click += (_, _) => ShowInstructions();
        ConfigureDeviceEditor(); ConfigureGraphEditor();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"), ColumnSpacing = 10, Children = { _editorTitle, Column(test, 1), Column(viewInstructions, 2), Column(discard, 3), Column(save, 4) } };
        _editorScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _editor.Margin = new Thickness(20, 20, 20, 18); _editor.ColumnDefinitions = new ColumnDefinitions("*"); _editor.RowDefinitions = new RowDefinitions("Auto,*"); _editor.RowSpacing = 12; _editor.Children.Add(header); _editor.Children.Add(Row(BuildGraphWorkspace(), 1));
    }

    private void ConfigureGraphEditor()
    {
        if (_graphUiConfigured) return; _graphUiConfigured = true;
        _graphEditor.SetValue(HavenProperties.Width, HavenLength.Percent(100)); _graphEditor.SetValue(HavenProperties.Height, HavenLength.Percent(100)); _graphEditor.SetValue(HavenProperties.MinHeight, HavenLength.Px(560));
        _graphHost = new HavenSceneControl { Root = _graphEditor, MinHeight = 560, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        _nodeSearch.MinHeight = 44; _nodeSearch.TextChanged += (_, _) => RebuildNodeLibrary();
        _graphEditor.DocumentChanged += document => { if (_suppressGraphUi) return; _graphLoadFailed = false; _editingGraph = AutomationGraphEditorAdapter.FromEditor(document, _editingGraph); RefreshGraphDiagnostics(); };
        _graphEditor.SelectionChanged += _ => { if (_suppressGraphUi) return; _editingGraph = AutomationGraphEditorAdapter.FromEditor(_graphEditor.Document, _editingGraph); RefreshNodeInspector(); };
        _deviceTarget.SelectionChanged += (_, _) => { if (!_suppressGraphUi && _deviceTarget.SelectedItem is DeviceTargetChoice choice) StoreDeviceTarget(choice.Target); };
        _deviceAction.SelectionChanged += (_, _) => { if (_suppressGraphUi || _deviceAction.SelectedItem is not DeviceActionChoice choice) return; StoreDeviceAction(choice.Action); AttachDeviceParameterBindings(); };
        _editorScroll.PropertyChanged += (_, e) => { if (e.Property.Name == "IsVisible" && _editorScroll.IsVisible) HydrateGraphEditor(_editing); };
        RebuildNodeLibrary();
    }

    private Control BuildGraphWorkspace()
    {
        if (_graphHost is null) throw new InvalidOperationException("The Automation graph host has not been configured.");
        var palette = new HavenCard { Padding = new Thickness(14), CornerRadius = new CornerRadius(18), Child = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 10, Children = { Heading("Nodes", 18), Row(_nodeSearch, 1), Row(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _nodeLibraryItems }, 2) } } };
        var undo = SoftButton("Undo"); var redo = SoftButton("Redo"); var duplicate = SoftButton("Duplicate"); var delete = SoftButton("Delete"); var reset = SoftButton("Reset view");
        undo.Click += (_, _) => { _graphEditor.Undo(); RefreshGraphDiagnostics(); }; redo.Click += (_, _) => { _graphEditor.Redo(); RefreshGraphDiagnostics(); }; duplicate.Click += (_, _) => { _graphEditor.DuplicateSelection(); RefreshGraphDiagnostics(); }; delete.Click += (_, _) => { _graphEditor.DeleteSelection(); RefreshGraphDiagnostics(); }; reset.Click += (_, _) => { _graphEditor.ResetViewport(); RefreshGraphDiagnostics(); };
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { _graphSummary, Column(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { undo, redo, duplicate, delete, reset } }, 1) } };
        var canvas = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 8, Children = { toolbar, Row(new Border { BorderBrush = BorderStroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Background = CardBrush, ClipToBounds = true, Child = _graphHost }, 1) } };
        var inspectorContent = new StackPanel { Spacing = 10, Children = { Heading("Workflow", 18), Label("Name"), _name, Label("Goal"), _goal, Label("Rules"), _rules, Heading("Selected node", 18), _nodeInspectorFields, Heading("Validation", 18), _graphDiagnostics, Heading("Test trace", 18), _graphTestTrace } };
        var inspector = new HavenCard { Padding = new Thickness(14), CornerRadius = new CornerRadius(18), Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = inspectorContent } };
        return new Grid { ColumnDefinitions = new ColumnDefinitions("230,*,340"), ColumnSpacing = 12, MinHeight = 620, Children = { palette, Column(canvas, 1), Column(inspector, 2) } };
    }

    private void RebuildNodeLibrary()
    {
        _nodeLibraryItems.Children.Clear();
        foreach (var template in _graphEditor.SearchTemplates(AutomationGraphEditorAdapter.Templates, _nodeSearch.Text))
        {
            var button = new HavenNavigationButton { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 8), CornerRadius = new CornerRadius(14), Content = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = template.Title, FontWeight = Avalonia.Media.FontWeight.ExtraBold, FontSize = 13 }, new TextBlock { Text = template.Category, Foreground = MutedBrush, FontSize = 10 }, new TextBlock { Text = template.Subtitle, Foreground = MutedBrush, FontSize = 10, TextWrapping = Avalonia.Media.TextWrapping.Wrap } } } };
            AutomationProperties.SetName(button, $"Add {template.Title} node");
            button.Click += (_, _) => { var index = _graphEditor.Document.Nodes.Count; _graphEditor.AddNode(template, 80 + (index % 4) * 260, 80 + (index / 4) * 160); RefreshGraphDiagnostics(); };
            _nodeLibraryItems.Children.Add(button);
        }
    }

    private void HydrateGraphEditor(ReusableTaskDefinition? item)
    {
        _suppressGraphUi = true;
        try
        {
            _graphLoadFailed = !AutomationGraphCodec.TryDeserialize(item?.GraphJson, out var graph); _editingGraph = _graphLoadFailed ? AutomationGraphDefinition.Empty : graph;
            _graphEditor.Document = AutomationGraphEditorAdapter.ToEditor(_editingGraph); _graphEditor.ClearSelection(); _graphEditor.ResetViewport(); _inspectedNodeId = null; _editingDeviceNode = null;
            _graphTestTrace.Children.Clear(); _graphTestTrace.Children.Add(Muted(_graphLoadFailed ? "Stored graph data could not be read. Add a node to replace it or discard the edit." : "Run Test Graph to see validation and per-node execution trace."));
        }
        finally { _suppressGraphUi = false; }
        RefreshNodeInspector(); RefreshGraphDiagnostics();
    }

    private void RefreshNodeInspector()
    {
        _suppressGraphUi = true;
        try
        {
            _nodeInspectorFields.Children.Clear();
            if (_graphEditor.SelectedNodeIds.Count == 0) { _inspectedNodeId = null; _editingDeviceNode = null; _workflowType.SelectedItem = "Instruction"; _deviceEditor.IsVisible = false; _nodeInspectorFields.Children.Add(Muted("Select a node on the canvas to configure it.")); return; }
            var nodeId = _graphEditor.SelectedNodeIds.First(); var node = _graphEditor.Document.Nodes.FirstOrDefault(value => value.Id == nodeId); if (node is null) return; _inspectedNodeId = nodeId;
            _nodeInspectorFields.Children.Add(new TextBlock { Text = node.Category, FontSize = 11, FontWeight = Avalonia.Media.FontWeight.ExtraBold, Foreground = MutedBrush });
            AddNodeTextField(node, "Title", node.Title, (value, current) => current with { Title = value }); AddNodeTextField(node, "Subtitle", node.Subtitle, (value, current) => current with { Subtitle = value });
            if (node.Category.Equals("Condition", StringComparison.OrdinalIgnoreCase) || node.Category.Equals("Branch", StringComparison.OrdinalIgnoreCase)) AddNodeParameterField(node, "expression", "Expression", "true or false");
            else if (node.Category.Equals("Schedule", StringComparison.OrdinalIgnoreCase))
            {
                var recurrence = node.Title.Contains("Recurrence", StringComparison.OrdinalIgnoreCase);
                AddNodeParameterField(node, recurrence ? "recurrence" : "schedule", recurrence ? "Recurrence" : "Run at", recurrence ? "daily 08:30" : "2026-08-21T09:00:00+01:00");
                _nodeInspectorFields.Children.Add(Muted(recurrence
                    ? "Examples: hourly · every 2 hours · daily 08:30 · weekly Monday 08:30."
                    : "Use a future date/time with an offset, for example 2026-08-21T09:00:00+01:00."));
            }
            else if (node.Category.Equals("ConditionWatch", StringComparison.OrdinalIgnoreCase) || node.Category.Equals("Condition Watch", StringComparison.OrdinalIgnoreCase))
            {
                AddNodeParameterField(node, "watch", "Watch condition", "Describe what should become true");
                AddNodeParameterField(node, "intervalMinutes", "Check every (minutes)", "60");
                _nodeInspectorFields.Children.Add(Muted("Condition checks run no more often than hourly. Use 60–10080 minutes."));
            }
            else if (node.Category.Equals(BuiltInAutomationNodeCategory.App, StringComparison.OrdinalIgnoreCase))
            {
                AddNodeParameterField(node, "name", "Application", "Calculator");
                _nodeInspectorFields.Children.Add(Muted("App nodes launch through Haven's capability router. Test Graph never opens the application."));
            }
            else if (node.Category.Equals(BuiltInAutomationNodeCategory.File, StringComparison.OrdinalIgnoreCase))
            {
                var operation = AutomationGraphEditorAdapter.ReadParameter(node, "operation") ?? "read";
                AddNodeParameterField(node, "workspaceRoot", "Workspace root", "C:\\path\\to\\workspace");
                if (operation.Equals("search", StringComparison.OrdinalIgnoreCase))
                    AddNodeParameterField(node, "pattern", "Filename pattern", "*.md");
                else
                    AddNodeParameterField(node, "path", "Relative file path", "notes/today.md");
                _nodeInspectorFields.Children.Add(Muted("File nodes are workspace-scoped. This graph surface intentionally exposes read/search only; mutation stays on Haven's explicit permissioned file-action path."));
            }
            else if (node.Category.Equals(BuiltInAutomationNodeCategory.Action, StringComparison.OrdinalIgnoreCase))
            {
                var action = AutomationGraphEditorAdapter.ReadParameter(node, "action") ?? "emit";
                if (action.Equals("delay", StringComparison.OrdinalIgnoreCase))
                    AddNodeParameterField(node, "milliseconds", "Delay (milliseconds)", "1000");
                else
                    AddNodeParameterField(node, "value", "Value", "ready");
            }
            if (node.Category.Equals(DeviceAutomationNodeCategory.Key, StringComparison.OrdinalIgnoreCase)) { HydrateDeviceInspector(node); _nodeInspectorFields.Children.Add(_deviceEditor); } else { _workflowType.SelectedItem = "Instruction"; _deviceEditor.IsVisible = false; _editingDeviceNode = null; }
            var duplicate = SoftButton("Duplicate node"); duplicate.Click += (_, _) => _graphEditor.DuplicateSelection(); var delete = SoftButton("Delete node"); delete.Click += (_, _) => _graphEditor.DeleteSelection(); _nodeInspectorFields.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { duplicate, delete } });
        }
        finally { _suppressGraphUi = false; }
    }

    private void AddNodeTextField(NodeEditorNode node, string label, string value, Func<string, NodeEditorNode, NodeEditorNode> update) { _nodeInspectorFields.Children.Add(Label(label)); var input = Field(label); input.Text = value; input.TextChanged += (_, _) => { if (!_suppressGraphUi) _graphEditor.UpdateNode(node.Id, current => update(input.Text ?? string.Empty, current)); }; _nodeInspectorFields.Children.Add(input); }
    private void AddNodeParameterField(NodeEditorNode node, string key, string label, string placeholder) { _nodeInspectorFields.Children.Add(Label(label)); var input = Field(placeholder); input.Text = AutomationGraphEditorAdapter.ReadParameter(node, key) ?? string.Empty; input.TextChanged += (_, _) => { if (!_suppressGraphUi) _graphEditor.UpdateNode(node.Id, current => current with { Metadata = AutomationGraphEditorAdapter.WithParameter(current.Metadata, key, input.Text) }); }; _nodeInspectorFields.Children.Add(input); }

    private void HydrateDeviceInspector(NodeEditorNode node)
    {
        _editingGraph = AutomationGraphEditorAdapter.FromEditor(_graphEditor.Document, _editingGraph); var automationNode = _editingGraph.Nodes.FirstOrDefault(value => value.Id == node.Id); _editingDeviceNode = automationNode?.ToDevice();
        _workflowType.SelectedItem = DeviceAutomationNodeCategory.Key; _deviceEditor.IsVisible = true; _deviceSnapshot = null; _deviceAction.ItemsSource = Array.Empty<DeviceActionChoice>(); _deviceAction.SelectedItem = null; _deviceTarget.SelectedItem = null; _deviceParameters.Children.Clear(); _deviceParameterInputs.Clear();
        var target = automationNode?.DeviceTarget ?? AutomationGraphEditorAdapter.ReadDeviceTarget(node); if (target is null) { _deviceAvailability.Text = "Choose a target and action for this DEVICE node."; return; }
        var choices = (_deviceTarget.ItemsSource as IEnumerable<DeviceTargetChoice>)?.ToArray() ?? []; var targetChoice = choices.FirstOrDefault(choice => string.Equals(choice.Target.Id, target.Id, StringComparison.OrdinalIgnoreCase) && (string.IsNullOrWhiteSpace(target.ProviderId) || string.Equals(choice.Target.ProviderId, target.ProviderId, StringComparison.OrdinalIgnoreCase)));
        if (targetChoice is null) { _deviceAvailability.Text = $"Saved target {target.DisplayName} is not currently available. Haven will not substitute another device."; return; } _deviceTarget.SelectedItem = targetChoice;
    }

    private void StoreDeviceTarget(DeviceTargetDescriptor target) { if (_inspectedNodeId is not { } id) return; _graphEditor.UpdateNode(id, node => node.Category.Equals(DeviceAutomationNodeCategory.Key, StringComparison.OrdinalIgnoreCase) ? node with { Subtitle = target.DisplayName, Metadata = AutomationGraphEditorAdapter.WithDeviceTarget(node.Metadata, target) } : node); }
    private void StoreDeviceAction(DeviceActionDescriptor action) { if (_inspectedNodeId is not { } id) return; _graphEditor.UpdateNode(id, node => node.Category.Equals(DeviceAutomationNodeCategory.Key, StringComparison.OrdinalIgnoreCase) ? node with { Title = action.Name, Metadata = AutomationGraphEditorAdapter.WithDeviceAction(node.Metadata, action.Key) } : node); }
    private void AttachDeviceParameterBindings() { foreach (var pair in _deviceParameterInputs.ToArray()) { var key = pair.Key; var input = pair.Value; input.TextChanged += (_, _) => { if (!_suppressGraphUi && _inspectedNodeId is { } id) _graphEditor.UpdateNode(id, node => node with { Metadata = AutomationGraphEditorAdapter.WithParameter(node.Metadata, key, input.Text) }); }; } }

    private void RefreshGraphDiagnostics()
    {
        _editingGraph = AutomationGraphEditorAdapter.FromEditor(_graphEditor.Document, _editingGraph);
        _graphDiagnostics.Children.Clear();
        var diagnostics = _graphEditor.ValidateDocument().Concat(AutomationGraphEditorAdapter.ValidateConfiguration(_editingGraph)).ToList();
        if (!AutomationGraphScheduleBinder.TryBind(_editingGraph, DateTimeOffset.UtcNow, out _, out var scheduleError) && !string.IsNullOrWhiteSpace(scheduleError))
            diagnostics.Add(new NodeEditorDiagnostic("schedule.invalid", scheduleError));
        if (_graphLoadFailed) _graphDiagnostics.Children.Add(Muted("Stored graph data is unreadable. Add a node to replace it or discard this edit."));
        foreach (var diagnostic in diagnostics.Take(8)) _graphDiagnostics.Children.Add(Muted("• " + diagnostic.Message));
        if (!_graphLoadFailed && diagnostics.Count == 0) _graphDiagnostics.Children.Add(Muted("Graph structure, scheduling, and configured DEVICE nodes are valid."));
        if (diagnostics.Count > 8) _graphDiagnostics.Children.Add(Muted($"+{diagnostics.Count - 8} more validation issues"));
        _graphSummary.Text = $"{_graphEditor.Document.Nodes.Count} nodes · {_graphEditor.Document.Edges.Count} connections · {Math.Round(_graphEditor.Zoom * 100)}%";
    }

    private bool TryBuildGraph(out string? graphJson, out string? error)
    {
        graphJson = null; error = null; if (_graphLoadFailed) { error = "Stored graph data is unreadable. Add a node to replace it or discard this edit before saving."; return false; }
        var structural = _graphEditor.ValidateDocument(); if (structural.Count > 0) { error = structural[0].Message; return false; }
        _editingGraph = AutomationGraphEditorAdapter.FromEditor(_graphEditor.Document, _editingGraph); var configuration = AutomationGraphEditorAdapter.ValidateConfiguration(_editingGraph); if (configuration.Count > 0) { error = configuration[0].Message; return false; }
        graphJson = _editingGraph.Nodes.Count == 0 && _editingGraph.Edges.Count == 0 ? null : AutomationGraphCodec.Serialize(_editingGraph); return true;
    }

    private async Task SaveGraphAsync()
    {
        var name = _name.Text?.Trim() ?? string.Empty; if (name.Length == 0) { _status.Text = "Workflow Name is required."; return; }
        if (!TryBuildGraph(out var graphJson, out var error)) { _status.Text = error ?? "The graph is not ready to save."; return; }
        AutomationGraphScheduleBinding? scheduleBinding = null;
        if (!string.IsNullOrWhiteSpace(graphJson) && !AutomationGraphScheduleBinder.TryBind(_editingGraph, DateTimeOffset.UtcNow, out scheduleBinding, out var scheduleError))
        {
            _status.Text = scheduleError ?? "The graph schedule is not ready to save.";
            return;
        }
        var now = DateTimeOffset.UtcNow; var item = new ReusableTaskDefinition(_editing?.Id ?? Guid.NewGuid(), name, _goal.Text?.Trim() ?? string.Empty, BuildInstructions(), _containerId, true, _editing?.CreatedAt ?? now, now, graphJson);
        try
        {
            await _tasks.UpsertReusableTaskAsync(item, CancellationToken.None);
            var scheduleDescription = await SyncScheduledGraphAsync(item, graphJson, scheduleBinding);
            _status.Text = scheduleDescription is null ? $"Saved {name}." : $"Saved {name} · {scheduleDescription}.";
            ShowDashboard();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _status.Text = "The workflow could not be fully saved: " + ex.Message;
        }
    }

    private async Task TestGraphAsync()
    {
        if (!TryBuildGraph(out var graphJson, out var error)) { _status.Text = error ?? "The graph is not ready to test."; return; }
        if (string.IsNullOrWhiteSpace(graphJson)) { _graphTestTrace.Children.Clear(); _graphTestTrace.Children.Add(GraphTraceRow("No graph", "Add at least one node before testing this workflow.")); _status.Text = "Add at least one node before testing this workflow."; return; }
        if (!AutomationGraphCodec.TryDeserialize(graphJson, out var graph)) { _status.Text = "The workflow graph could not be read."; return; }
        _status.Text = "Running non-destructive graph test…";
        var result = await AutomationGraphTestRunner.RunAsync(graph, CancellationToken.None);
        var historyWorkflow = new ReusableTaskDefinition(
            _editing?.Id ?? Guid.Empty,
            string.IsNullOrWhiteSpace(_name.Text) ? "Unsaved workflow" : _name.Text.Trim(),
            _goal.Text?.Trim() ?? string.Empty,
            BuildInstructions(),
            _containerId,
            true,
            _editing?.CreatedAt ?? result.StartedAt,
            result.CompletedAt,
            graphJson);
        await RecordGraphHistoryAsync(historyWorkflow, graphJson, result);
        ShowGraphTestResult(result);
        _status.Text = result.Succeeded ? $"Test passed: {result.Trace.Count} node{(result.Trace.Count == 1 ? string.Empty : "s")} traced without external side effects." : result.FailureMessage ?? result.ValidationIssues.FirstOrDefault()?.Message ?? "Graph test failed.";
    }

    private void ShowGraphTestResult(AutomationGraphRunResult result)
    {
        _graphTestTrace.Children.Clear(); if (result.ValidationIssues.Count > 0) { foreach (var issue in result.ValidationIssues) _graphTestTrace.Children.Add(GraphTraceRow("Validation", issue.Message)); return; }
        foreach (var trace in result.Trace) { var title = _graphEditor.Document.Nodes.FirstOrDefault(node => node.Id == trace.NodeId)?.Title ?? trace.Category; var detail = trace.Message; var inputs = FormatGraphTraceInputs(trace.Inputs); if (!string.IsNullOrWhiteSpace(inputs)) detail += $" · inputs: {inputs}"; if (!string.IsNullOrWhiteSpace(trace.Output)) detail += $" · output: {trace.Output}"; if (!string.IsNullOrWhiteSpace(trace.Branch)) detail += $" · branch: {trace.Branch}"; _graphTestTrace.Children.Add(GraphTraceRow($"{trace.Status}: {title}", detail)); }
        var failed = result.Trace.FirstOrDefault(trace => trace.Status == AutomationGraphTraceStatus.Failed); if (failed is not null) _graphEditor.SelectNode(failed.NodeId);
    }

    private string FormatGraphTraceInputs(Dictionary<Guid, string?>? inputs)
    {
        if (inputs is not { Count: > 0 }) return string.Empty;
        return string.Join(", ", inputs.Select(pair =>
        {
            var source = _graphEditor.Document.Nodes.FirstOrDefault(node => node.Id == pair.Key);
            var label = source?.Title ?? pair.Key.ToString("N")[..8];
            return $"{label}={pair.Value ?? "null"}";
        }));
    }

    private static Control GraphTraceRow(string title, string detail) => new HavenCard { Padding = new Thickness(10, 8), CornerRadius = new CornerRadius(12), Child = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = title, FontSize = 12, FontWeight = Avalonia.Media.FontWeight.ExtraBold }, new TextBlock { Text = detail, FontSize = 10, Foreground = MutedBrush, TextWrapping = Avalonia.Media.TextWrapping.Wrap } } } };
}
