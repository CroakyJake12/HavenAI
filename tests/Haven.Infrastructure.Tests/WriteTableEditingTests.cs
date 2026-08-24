using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

public sealed class WriteTableEditingTests
{
    [Fact]
    public void Insert_table_uses_requested_dimensions()
    {
        var document = NotesDocument.Create();
        var editor = new WriteDocumentEditor(document);

        var table = editor.InsertTable(4, 6);

        Assert.Equal(4, table.Table!.Rows.Count);
        Assert.All(table.Table.Rows, row => Assert.Equal(6, row.Cells.Count));
        Assert.Equal(table.Table.Rows[0].Cells[0].Id, editor.SelectedTableCellId);
    }

    [Fact]
    public void Row_and_column_edits_follow_the_active_cell()
    {
        var document = NotesDocument.Create();
        var editor = new WriteDocumentEditor(document);
        var table = editor.InsertTable(3, 3);
        var active = table.Table!.Rows[1].Cells[1];
        editor.SelectTableCell(table.Id, active.Id);

        editor.AddTableRow();
        Assert.Equal(4, table.Table.Rows.Count);
        Assert.Equal(editor.SelectedTableCellId, table.Table.Rows[2].Cells[0].Id);

        editor.SelectTableCell(table.Id, table.Table.Rows[1].Cells[1].Id);
        editor.AddTableColumn();
        Assert.All(table.Table.Rows, row => Assert.Equal(4, row.Cells.Count));
        Assert.Equal(table.Table.Rows[0].Cells[2].Id, editor.SelectedTableCellId);

        editor.SelectTableCell(table.Id, table.Table.Rows[1].Cells[1].Id);
        editor.RemoveTableRow();
        Assert.Equal(3, table.Table.Rows.Count);

        editor.SelectTableCell(table.Id, table.Table.Rows[0].Cells[1].Id);
        editor.RemoveTableColumn();
        Assert.All(table.Table.Rows, row => Assert.Equal(3, row.Cells.Count));
    }

    [Fact]
    public void Merge_and_split_preserve_persisted_column_spans_and_history()
    {
        var document = NotesDocument.Create();
        var editor = new WriteDocumentEditor(document);
        var table = editor.InsertTable(2, 3);
        var row = table.Table!.Rows[0];
        row.Cells[0].Text = "Alpha";
        row.Cells[1].Text = "Beta";
        editor.SelectTableCell(table.Id, row.Cells[0].Id);

        Assert.True(editor.MergeTableCellRight());
        Assert.Equal(2, row.Cells.Count);
        Assert.Equal(2, row.Cells[0].ColumnSpan);
        Assert.Equal("Alpha Beta", row.Cells[0].Text);

        Assert.True(editor.SplitTableCell());
        Assert.Equal(3, row.Cells.Count);
        Assert.Equal(1, row.Cells[0].ColumnSpan);

        Assert.True(editor.Undo());
        var restoredTable = editor.SelectedBlock!.Table!;
        Assert.Equal(2, restoredTable.Rows[0].Cells.Count);
        Assert.Equal(2, restoredTable.Rows[0].Cells[0].ColumnSpan);

        Assert.True(editor.Redo());
        restoredTable = editor.SelectedBlock!.Table!;
        Assert.Equal(3, restoredTable.Rows[0].Cells.Count);
        Assert.Equal(1, restoredTable.Rows[0].Cells[0].ColumnSpan);
    }
}
