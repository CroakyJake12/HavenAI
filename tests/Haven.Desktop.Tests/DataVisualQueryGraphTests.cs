using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Data;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class DataVisualQueryGraphTests
{
    [Fact]
    public void Visual_query_graph_has_stable_pipeline_ids_and_persists_stage_layout()
    {
        var query = DataQuery.Create("People query"); query.Visual.Source = "People"; query.Visual.Columns = "A, B"; query.Visual.Filter = "B > 10"; query.Visual.OrderBy = "B DESC"; query.Visual.Limit = 50; var first = DataVisualQueryGraphAdapter.ToEditor(query); Assert.Equal(7, first.Nodes.Count); Assert.Equal(6, first.Edges.Count);
        var filter = first.Nodes.Single(node => DataVisualQueryGraphAdapter.Stage(node) == "filter"); var moved = first with { Nodes = first.Nodes.Select(node => node.Id == filter.Id ? node with { X = node.X + 77, Y = node.Y + 31 } : node).ToArray() }; DataVisualQueryGraphAdapter.PersistLayout(query, moved); var second = DataVisualQueryGraphAdapter.ToEditor(query); var restored = second.Nodes.Single(node => node.Id == filter.Id);
        Assert.Equal(filter.Id, restored.Id); Assert.Equal(filter.X + 77, restored.X); Assert.Equal(filter.Y + 31, restored.Y); Assert.True(DataVisualQueryGraphAdapter.IsCanonicalStructure(query, second)); var editor = new NodeEditor { Document = second }; Assert.Empty(editor.ValidateDocument());
    }

    [AvaloniaFact]
    public void Data_scene_uses_shared_node_editor_and_retained_spreadsheet_for_visual_sql_results()
    {
        var workbook = DataWorkbook.Create("Visual SQL"); workbook.Sheets[0].Name = "People"; var query = workbook.Queries[0]; query.Visual.Source = "People"; query.Visual.Columns = "Name, Score"; query.Visual.Filter = "Score > 10"; var result = new DataQueryResult(["Name", "Score"], [["Ada", "42"]], false, "test");
        using var scene = new DataHavenScene(); var dirty = false; var controller = new DataVisualQueryGraphController(scene, () => query, () => result, () => dirty = true); scene.SetWorkbook(workbook, 0, 1, 0, 0, 0, 0, 0, 0, result); var editor = controller.Editor; Assert.Equal(7, editor.Document.Nodes.Count); Assert.Contains(editor.Document.Nodes, node => node.Title == "Filter" && node.Subtitle == "Score > 10");
        var resultHost = scene.Root.DescendantsAndSelf().OfType<Container>().Single(element => element.Name == "Data.Query.ResultGrid"); var grid = Assert.Single(resultHost.Children.OfType<DataSpreadsheetSurface>()); grid.SelectCell(0, 0, raiseChanged: false); Assert.Equal("Name", grid.Copy()); Assert.False(grid.TextInput("mutate"));
        var filter = editor.Document.Nodes.Single(node => DataVisualQueryGraphAdapter.Stage(node) == "filter"); editor.SelectNode(filter.Id); editor.MoveSelectionBy(40, 20); Assert.True(dirty); Assert.True(query.Metadata.ContainsKey("visualGraph.layout.filter.x")); scene.Root.ValidateUniqueNames();
    }
}
