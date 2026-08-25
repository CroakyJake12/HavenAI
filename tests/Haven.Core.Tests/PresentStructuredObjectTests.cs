using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class PresentStructuredObjectTests
{
    [Fact]
    public void Table_has_exact_dimensions_and_structural_edits_are_undoable()
    {
        var document = PresentDocument.Create("Structured deck");
        var editor = new PresentEditor(document);
        var slideId = document.Slides[0].Id;
        var tableElement = editor.AddTable(slideId, 3, 4);
        var table = tableElement.ReadTable();
        Assert.Equal(PresentElementKind.Table, tableElement.Kind);
        Assert.Equal(3, table.Rows);
        Assert.Equal(4, table.Columns);
        Assert.Equal(12, table.Cells.Count);

        Assert.True(editor.SetTableCellText(slideId, tableElement.Id, 1, 2, "Result"));
        Assert.Equal("Result", tableElement.ReadTable().GetCell(1, 2).Text);
        Assert.True(editor.InsertTableRow(slideId, tableElement.Id, 1));
        Assert.Equal(4, tableElement.ReadTable().Rows);
        Assert.True(editor.InsertTableColumn(slideId, tableElement.Id, 2));
        Assert.Equal(5, tableElement.ReadTable().Columns);
        Assert.Equal(20, tableElement.ReadTable().Cells.Count);

        Assert.True(editor.Undo());
        var afterUndo = editor.Document.Slides[0].Elements.Single(element => element.Id == tableElement.Id);
        Assert.Equal(4, afterUndo.ReadTable().Columns);
        Assert.Equal(16, afterUndo.ReadTable().Cells.Count);
    }

    [Fact]
    public void Chart_preserves_type_data_and_style_through_editor_history()
    {
        var document = PresentDocument.Create("Chart deck");
        var editor = new PresentEditor(document);
        var slideId = document.Slides[0].Id;
        var chartElement = editor.AddChart(slideId, PresentChartType.Line);

        Assert.True(editor.SetChartData(slideId, chartElement.Id, ["Maths", "Law", "CS"],
        [
            new PresentChartSeries { Name = "Score", Values = [71, 68, 83] }
        ]));
        Assert.True(editor.SetChartStyle(slideId, chartElement.Id, "Minimal", showLegend: false));
        var chart = chartElement.ReadChart();
        Assert.Equal(PresentChartType.Line, chart.Type);
        Assert.Equal(["Maths", "Law", "CS"], chart.Categories);
        Assert.Equal([71d, 68d, 83d], chart.Series[0].Values);
        Assert.Equal("Minimal", chart.Style);
        Assert.False(chart.ShowLegend);

        Assert.True(editor.SetChartType(slideId, chartElement.Id, PresentChartType.Bar));
        Assert.Equal(PresentChartType.Bar, chartElement.ReadChart().Type);
        Assert.True(editor.Undo());
        var restored = editor.Document.Slides[0].Elements.Single(element => element.Id == chartElement.Id);
        Assert.Equal(PresentChartType.Line, restored.ReadChart().Type);
        Assert.Equal("Minimal", restored.ReadChart().Style);
        Assert.Equal(PresentDocument.CurrentSchemaVersion, editor.Document.SchemaVersion);
    }
}
