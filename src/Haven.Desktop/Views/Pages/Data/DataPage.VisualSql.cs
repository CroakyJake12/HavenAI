using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Haven.Application;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Data;

public sealed partial class DataPage
{
    private DataVisualQueryGraphController? _visualQueryGraph;
    internal DataVisualQueryGraphController VisualQueryGraph => _visualQueryGraph ??= new(_route, () => CurrentQuery, () => _lastQueryResult, MarkDirty);
}

internal sealed class DataVisualQueryGraphController
{
    private readonly DataHavenScene _route;
    private readonly Func<DataQuery?> _query;
    private readonly Func<DataQueryResult?> _result;
    private readonly Action _markDirty;
    private readonly NodeEditor _editor = new() { Name = "Data.Query.Visual.Graph" };
    private readonly Container _inspector = new() { Name = "Data.Query.Visual.Inspector", Layout = HavenLayout.Vertical };
    private readonly HavenText _selection = new() { Name = "Data.Query.Visual.Selection", Level = TextLevel.Caption };
    private readonly DataQueryResultNameScope _resultHost = new() { Name = "Data.Query.ResultGrid", Layout = HavenLayout.Grid, Columns = "1fr", Rows = "Auto" };
    private readonly Dictionary<string, Container> _fields = new(StringComparer.Ordinal);
    private bool _suppressGraph;

    public DataVisualQueryGraphController(DataHavenScene route, Func<DataQuery?> query, Func<DataQueryResult?> result, Action markDirty)
    {
        _route = route; _query = query; _result = result; _markDirty = markDirty;
        ConfigureBuilder(); ConfigureResultGrid();
        _editor.DocumentChanged += OnDocumentChanged;
        _editor.SelectionChanged += _ => { if (!_suppressGraph) UpdateInspector(); };
        foreach (var input in VisualInputs()) input.Invalidated += (_, _) => { RefreshGraph(); RefreshResult(); };
        _route.QueryTabs.Invalidated += (_, _) => { RefreshGraph(); RefreshResult(); };
        _route.ResultsText.Invalidated += (_, _) => RefreshResult();
        RefreshGraph(); RefreshResult();
    }

    internal NodeEditor Editor => _editor;

    private void ConfigureBuilder()
    {
        var visual = _route.Root.DescendantsAndSelf().OfType<Container>().Single(element => element.Name == "Data.Query.VisualBuilder");
        var original = visual.Children.ToArray(); foreach (var child in original) visual.Remove(child); if (original.FirstOrDefault() is { } intro) visual.Add(intro);
        _editor.Accessibility.AccessibleName = "Visual SQL pipeline"; _editor.Accessibility.Description = "Select a SQL stage to edit it below. Drag stages to arrange the pipeline; pan and zoom use the shared Haven node editor.";
        _editor.SetValue(HavenProperties.Width, HavenLength.Percent(100)); _editor.SetValue(HavenProperties.Height, HavenLength.Px(360)); _editor.SetValue(HavenProperties.MinHeight, HavenLength.Px(320));
        var toolbar = new Container { Name = "Data.Query.Visual.Toolbar", Layout = HavenLayout.Horizontal }; toolbar.SetValue(HavenProperties.Gap, HavenLength.Px(8)); toolbar.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        var hint = new HavenText("Drag stages to arrange · select a stage to edit") { Level = TextLevel.Caption }; hint.SetValue(HavenProperties.Foreground, "TextSecondary"); toolbar.Add(hint);
        var undo = GraphButton("Data.Query.Visual.Undo", "Undo layout"); undo.Invoked += (_, _) => _editor.Undo(); var redo = GraphButton("Data.Query.Visual.Redo", "Redo layout"); redo.Invoked += (_, _) => _editor.Redo();
        var resetView = GraphButton("Data.Query.Visual.ResetView", "Reset view"); resetView.Invoked += (_, _) => _editor.ResetViewport(); var resetLayout = GraphButton("Data.Query.Visual.ResetLayout", "Reset layout"); resetLayout.Invoked += (_, _) => { if (_query() is not { } current) return; DataVisualQueryGraphAdapter.ClearLayout(current); _markDirty(); RefreshGraph(); };
        toolbar.Add(undo); toolbar.Add(redo); toolbar.Add(resetView); toolbar.Add(resetLayout); visual.Add(toolbar); visual.Add(_editor); _selection.SetValue(HavenProperties.Foreground, "TextSecondary"); visual.Add(_selection); _inspector.SetValue(HavenProperties.Gap, HavenLength.Px(6)); visual.Add(_inspector);
        AddField("source", "Source sheet", _route.VisualSourceInput); AddField("select", "Columns", _route.VisualColumnsInput); AddField("filter", "Filter rows", _route.VisualFilterInput); AddField("group", "Group rows", _route.VisualGroupInput); AddField("sort", "Sort rows", _route.VisualOrderInput); AddField("limit", "Row limit", _route.VisualLimitInput);
        if (_route.BuildSqlButton.Parent is not null) _route.BuildSqlButton.Parent.Remove(_route.BuildSqlButton); visual.Add(_route.BuildSqlButton);
    }

    private void ConfigureResultGrid() { _resultHost.SetValue(HavenProperties.Width, HavenLength.Percent(100)); _resultHost.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed); _route.Editor.Add(_resultHost); }
    private void AddField(string stage, string label, Input input) { if (input.Parent is not null) input.Parent.Remove(input); var field = new Container { Name = $"Data.Query.Visual.Field.{stage}", Layout = HavenLayout.Vertical }; field.SetValue(HavenProperties.Gap, HavenLength.Px(4)); var caption = new HavenText(label) { Level = TextLevel.Caption }; caption.SetValue(HavenProperties.Foreground, "TextSecondary"); field.Add(caption); field.Add(input); _inspector.Add(field); _fields[stage] = field; }
    private IEnumerable<Input> VisualInputs() { yield return _route.VisualSourceInput; yield return _route.VisualColumnsInput; yield return _route.VisualFilterInput; yield return _route.VisualGroupInput; yield return _route.VisualOrderInput; yield return _route.VisualLimitInput; }

    private void RefreshGraph()
    {
        var query = _query(); _suppressGraph = true;
        try
        {
            if (query is null) { _editor.Document = NodeEditorDocument.Empty; _editor.ClearSelection(); }
            else { var selectedStage = _editor.SelectedNodeIds.Select(id => _editor.Document.Nodes.FirstOrDefault(node => node.Id == id)).Where(node => node is not null).Select(DataVisualQueryGraphAdapter.Stage).FirstOrDefault(stage => stage is not null); _editor.Document = DataVisualQueryGraphAdapter.ToEditor(query); var stage = selectedStage ?? "source"; var node = _editor.Document.Nodes.FirstOrDefault(value => DataVisualQueryGraphAdapter.Stage(value) == stage) ?? _editor.Document.Nodes.First(); _editor.SelectNode(node.Id); }
        }
        finally { _suppressGraph = false; } UpdateInspector();
    }

    private void OnDocumentChanged(NodeEditorDocument document)
    {
        if (_suppressGraph || _query() is not { } query) return; DataVisualQueryGraphAdapter.PersistLayout(query, document);
        if (!DataVisualQueryGraphAdapter.IsCanonicalStructure(query, document)) { _suppressGraph = true; try { _editor.Document = DataVisualQueryGraphAdapter.ToEditor(query); _editor.SelectNode(DataVisualQueryGraphAdapter.NodeId(query, "source")); } finally { _suppressGraph = false; } }
        _markDirty(); UpdateInspector();
    }

    private void UpdateInspector()
    {
        var selected = _editor.SelectedNodeIds.FirstOrDefault(); var node = selected == Guid.Empty ? null : _editor.Document.Nodes.FirstOrDefault(value => value.Id == selected); var stage = node is null ? null : DataVisualQueryGraphAdapter.Stage(node); foreach (var pair in _fields) pair.Value.SetValue(HavenProperties.Visibility, pair.Key == stage ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        _selection.Content = node is null ? "Select a query stage on the graph." : stage == "result" ? "Preview result · Run the read-only preview below to inspect rows." : $"Selected stage · {node.Title}";
    }

    private void RefreshResult()
    {
        _resultHost.Children.ToList().ForEach(child => _resultHost.Remove(child)); var result = _result(); if (result is null) { _resultHost.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed); return; }
        var sheet = DataSheet.Create(0, "Query result"); for (var column = 0; column < result.Columns.Count; column++) sheet.SetCell(0, column, result.Columns[column]); for (var row = 0; row < result.Rows.Count; row++) for (var column = 0; column < result.Rows[row].Count; column++) sheet.SetCell(row + 1, column, result.Rows[row][column]);
        var surface = new DataSpreadsheetSurface(); surface.Accessibility.AccessibleName = "Read-only SQL result grid"; surface.SetValue(HavenProperties.Width, HavenLength.Percent(100)); surface.SetValue(HavenProperties.Height, HavenLength.Px(360)); surface.SetValue(HavenProperties.MinHeight, HavenLength.Px(320)); surface.SetSheet(sheet, 0, 0); _resultHost.Add(surface); _resultHost.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
    }

    private static HavenButton GraphButton(string name, string text) { var button = new HavenButton { Name = name, Content = text, Variant = ButtonVariant.Tertiary }; button.Accessibility.AccessibleName = text; button.SetValue(HavenProperties.MinHeight, HavenLength.Px(36)); return button; }
}

internal static class DataVisualQueryGraphAdapter
{
    internal const string StageMetadataKey = "data.sql.stage"; private static readonly (string Key, string Title)[] Stages = [("source", "Source"), ("select", "Select columns"), ("filter", "Filter"), ("group", "Group"), ("sort", "Sort"), ("limit", "Limit"), ("result", "Preview")];
    internal static NodeEditorDocument ToEditor(DataQuery query) { ArgumentNullException.ThrowIfNull(query); query.Metadata ??= new(StringComparer.Ordinal); var nodes = Stages.Select((stage, index) => CreateNode(query, stage.Key, stage.Title, index)).ToArray(); var edges = Enumerable.Range(0, nodes.Length - 1).Select(index => new NodeEditorEdge(StableId(query.Id, $"edge:{Stages[index].Key}:{Stages[index + 1].Key}"), nodes[index].Id, "out", nodes[index + 1].Id, "in")).ToArray(); return new NodeEditorDocument(nodes, edges); }
    internal static Guid NodeId(DataQuery query, string stage) => StableId(query.Id, "node:" + stage); internal static string? Stage(NodeEditorNode? node) => node is not null && node.Metadata.TryGetValue(StageMetadataKey, out var stage) ? stage : null;
    internal static void PersistLayout(DataQuery query, NodeEditorDocument document) { query.Metadata ??= new(StringComparer.Ordinal); foreach (var node in document.Nodes) if (Stage(node) is { } stage && Stages.Any(value => value.Key == stage)) { query.Metadata[LayoutKey(stage, "x")] = node.X.ToString("R", CultureInfo.InvariantCulture); query.Metadata[LayoutKey(stage, "y")] = node.Y.ToString("R", CultureInfo.InvariantCulture); } }
    internal static void ClearLayout(DataQuery query) { query.Metadata ??= new(StringComparer.Ordinal); foreach (var key in query.Metadata.Keys.Where(key => key.StartsWith("visualGraph.layout.", StringComparison.Ordinal)).ToArray()) query.Metadata.Remove(key); }
    internal static bool IsCanonicalStructure(DataQuery query, NodeEditorDocument document) { var canonical = ToEditor(query); if (canonical.Nodes.Count != document.Nodes.Count || canonical.Edges.Count != document.Edges.Count) return false; if (!canonical.Nodes.Select(node => node.Id).ToHashSet().SetEquals(document.Nodes.Select(node => node.Id))) return false; var expected = canonical.Edges.ToDictionary(edge => edge.Id); return document.Edges.All(edge => expected.TryGetValue(edge.Id, out var match) && match.FromNodeId == edge.FromNodeId && match.ToNodeId == edge.ToNodeId && match.FromPortId == edge.FromPortId && match.ToPortId == edge.ToPortId); }
    private static NodeEditorNode CreateNode(DataQuery query, string stage, string title, int index) { IReadOnlyList<NodeEditorPort> ports = stage == "source" ? [new NodeEditorPort("out", "Rows", NodeEditorPortDirection.Output, "rows", false)] : stage == "result" ? [new NodeEditorPort("in", "Rows", NodeEditorPortDirection.Input, "rows", false)] : [new NodeEditorPort("in", "Rows", NodeEditorPortDirection.Input, "rows", false), new NodeEditorPort("out", "Rows", NodeEditorPortDirection.Output, "rows", false)]; var x = ReadLayout(query, stage, "x") ?? 50 + index * 250; var y = ReadLayout(query, stage, "y") ?? 90; return new NodeEditorNode(NodeId(query, stage), "SQL", title) { Subtitle = Subtitle(query.Visual, stage), X = x, Y = y, Width = 210, Height = 108, Ports = ports, Metadata = new Dictionary<string, string>(StringComparer.Ordinal) { [StageMetadataKey] = stage } }; }
    private static string Subtitle(DataVisualQuery visual, string stage) => stage switch { "source" => string.IsNullOrWhiteSpace(visual.Source) ? "Choose a sheet" : visual.Source, "select" => string.IsNullOrWhiteSpace(visual.Columns) ? "*" : visual.Columns, "filter" => string.IsNullOrWhiteSpace(visual.Filter) ? "No filter" : visual.Filter, "group" => string.IsNullOrWhiteSpace(visual.GroupBy) ? "No grouping" : visual.GroupBy, "sort" => string.IsNullOrWhiteSpace(visual.OrderBy) ? "No ordering" : visual.OrderBy, "limit" => visual.Limit is > 0 ? $"{visual.Limit.Value} rows" : "No limit", "result" => "Read-only result grid", _ => string.Empty };
    private static double? ReadLayout(DataQuery query, string stage, string axis) => query.Metadata.TryGetValue(LayoutKey(stage, axis), out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null; private static string LayoutKey(string stage, string axis) => $"visualGraph.layout.{stage}.{axis}"; private static Guid StableId(Guid queryId, string purpose) { var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{queryId:N}:{purpose}")); return new Guid(hash.AsSpan(0, 16)); }
}

internal sealed class DataQueryResultNameScope : Container { public override bool CreatesNameScope => true; }
