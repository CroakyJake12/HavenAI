using Haven.Core;
using Haven.Desktop.Views.Pages.Data;
using Haven.UI;

namespace Haven.Desktop.Tests;

public sealed class DataSpreadsheetSurfaceTests
{
    [Fact]
    public void Million_row_sheet_realizes_only_viewport_cells_and_scrolls_to_deep_rows()
    {
        var sheet = DataSheet.Create(0, "Large"); sheet.SetCell(0, 0, "Top"); sheet.SetCell(9_999, 5, "Deep"); var surface = Layout(sheet, 920, 620); var renderer = new HavenSceneRenderer(); renderer.Render(surface); Assert.InRange(surface.RealizedCellCount, 1, 400); Assert.Equal(0, surface.FirstVisibleRow); surface.ScrollToCell(9_999, 5); renderer.Render(surface); Assert.True(surface.FirstVisibleRow > 9_900); Assert.Equal(9_999, surface.ActiveRow); Assert.Equal(5, surface.ActiveColumn); Assert.InRange(surface.RealizedCellCount, 1, 400);
    }

    [Fact]
    public void Selection_keyboard_text_and_formula_commit_use_spreadsheet_semantics()
    {
        var surface = Layout(DataSheet.Create(0)); var commits = new List<(int Row, int Column, string Text)>(); surface.CellCommitted += (row, column, text) => commits.Add((row, column, text)); surface.SelectCell(4, 3, raiseChanged: false); Assert.True(surface.KeyDown(new HavenKeyInput(HavenKey.Right, HavenKeyModifiers.Shift))); Assert.Equal(2, surface.Selection.ColumnCount); Assert.True(surface.TextInput("=A1*2")); Assert.True(surface.KeyDown(new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None))); var commit = Assert.Single(commits); Assert.Equal(4, commit.Row); Assert.Equal(4, commit.Column); Assert.Equal("=A1*2", commit.Text); Assert.Equal(5, surface.ActiveRow);
    }

    [Fact]
    public void Copy_and_paste_use_tab_separated_rectangular_ranges()
    {
        var sheet = DataSheet.Create(0); sheet.SetCell(0, 0, "one"); sheet.SetCell(0, 1, "two"); sheet.SetCell(1, 0, "three"); sheet.SetCell(1, 1, "four"); var surface = Layout(sheet); surface.SelectRange(0, 0, 1, 1); Assert.Equal("one\ttwo" + Environment.NewLine + "three\tfour", surface.Copy()); var commits = new List<(int Row, int Column, string Text)>(); surface.CellCommitted += (row, column, text) => commits.Add((row, column, text)); surface.SelectCell(10, 4, raiseChanged: false); Assert.True(surface.Paste("a\tb\nc\td")); Assert.Equal(4, commits.Count); Assert.Contains(commits, edit => edit == (10, 4, "a")); Assert.Contains(commits, edit => edit == (11, 5, "d")); Assert.Equal(2, surface.Selection.RowCount); Assert.Equal(2, surface.Selection.ColumnCount);
    }

    [Fact]
    public void Continuous_scroll_and_zoom_are_clamped_without_paging_state()
    {
        var surface = Layout(DataSheet.Create(0)); surface.ScrollByPixels(320.5, 777.25); Assert.Equal(320.5, surface.OffsetX, 3); Assert.Equal(777.25, surface.OffsetY, 3); surface.SetZoom(99); Assert.Equal(2, surface.Zoom); surface.SetZoom(.01); Assert.Equal(.6, surface.Zoom); surface.ScrollByPixels(-100_000, -100_000); Assert.Equal(0, surface.OffsetX); Assert.Equal(0, surface.OffsetY);
    }

    [Fact]
    public void Spreadsheet_headers_select_whole_columns_rows_and_sheet_without_jumping_the_viewport()
    {
        var surface = Layout(DataSheet.Create(0));
        Assert.True(surface.PointerPressed(new HavenPointerInput(new HavenPoint(328, 12), new HavenPoint(328, 12), HavenPointerKind.Mouse)));
        Assert.Equal(2, surface.Selection.StartColumn); Assert.Equal(2, surface.Selection.EndColumn); Assert.Equal(0, surface.Selection.StartRow); Assert.Equal(DataSpreadsheetSurface.MaximumRows - 1, surface.Selection.EndRow); Assert.Equal(0, surface.FirstVisibleRow);
        Assert.True(surface.PointerPressed(new HavenPointerInput(new HavenPoint(12, 168), new HavenPoint(12, 168), HavenPointerKind.Mouse)));
        Assert.Equal(4, surface.Selection.StartRow); Assert.Equal(4, surface.Selection.EndRow); Assert.Equal(0, surface.Selection.StartColumn); Assert.Equal(DataSpreadsheetSurface.MaximumColumns - 1, surface.Selection.EndColumn);
        Assert.True(surface.PointerPressed(new HavenPointerInput(new HavenPoint(12, 12), new HavenPoint(12, 12), HavenPointerKind.Mouse)));
        Assert.Equal(DataSpreadsheetSurface.MaximumRows, surface.Selection.RowCount); Assert.Equal(DataSpreadsheetSurface.MaximumColumns, surface.Selection.ColumnCount);
    }

    [Fact]
    public void Spreadsheet_primary_shortcuts_and_enter_direction_follow_spreadsheet_navigation()
    {
        var sheet = DataSheet.Create(0); sheet.SetCell(12, 7, "last"); var surface = Layout(sheet); surface.SelectCell(5, 3, raiseChanged: false);
        Assert.True(surface.KeyDown(new HavenKeyInput(HavenKey.A, HavenKeyModifiers.Control))); Assert.Equal(0, surface.Selection.StartRow); Assert.Equal(0, surface.Selection.StartColumn); Assert.Equal(12, surface.Selection.EndRow); Assert.Equal(7, surface.Selection.EndColumn);
        Assert.True(surface.KeyDown(new HavenKeyInput(HavenKey.Home, HavenKeyModifiers.Control))); Assert.Equal(0, surface.ActiveRow); Assert.Equal(0, surface.ActiveColumn);
        Assert.True(surface.KeyDown(new HavenKeyInput(HavenKey.End, HavenKeyModifiers.Control))); Assert.Equal(12, surface.ActiveRow); Assert.Equal(7, surface.ActiveColumn);
        Assert.True(surface.KeyDown(new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.Shift))); Assert.Equal(11, surface.ActiveRow); Assert.Equal(7, surface.ActiveColumn);
        Assert.True(surface.KeyDown(new HavenKeyInput(HavenKey.Tab, HavenKeyModifiers.Shift))); Assert.Equal(11, surface.ActiveRow); Assert.Equal(6, surface.ActiveColumn);
    }

    [Fact]
    public void Surface_without_a_commit_target_rejects_direct_typing_and_remains_copyable()
    {
        var sheet = DataSheet.Create(0); sheet.SetCell(0, 0, "locked"); var surface = Layout(sheet); surface.SelectCell(0, 0, raiseChanged: false); Assert.Equal("locked", surface.Copy()); Assert.False(surface.TextInput("changed")); Assert.Equal("locked", sheet.GetCell(0, 0)?.Value);
    }

    [Fact]
    public void Frozen_panes_and_sparse_sizes_stay_interactive_after_scrolling_and_header_drag_resize()
    {
        var surface = Layout(DataSheet.Create(0)); var changes = new List<DataSpreadsheetLayoutState>(); surface.LayoutChanged += changes.Add;
        surface.SetFrozenPanes(2, 1); surface.SetColumnWidth(0, 200); surface.SetRowHeight(0, 50); surface.ScrollByPixels(600, 900); new HavenSceneRenderer().Render(surface);
        Assert.Equal(2, surface.FrozenRows); Assert.Equal(1, surface.FrozenColumns); Assert.Equal(200, surface.ColumnWidthAt(0), 3); Assert.Equal(50, surface.RowHeightAt(0), 3); Assert.True(surface.OffsetX > 0); Assert.True(surface.OffsetY > 0); Assert.InRange(surface.RealizedCellCount, 1, 600);
        Assert.True(surface.PointerPressed(new HavenPointerInput(new HavenPoint(70, 45), new HavenPoint(70, 45), HavenPointerKind.Mouse))); Assert.Equal(0, surface.ActiveRow); Assert.Equal(0, surface.ActiveColumn);
        var boundary = 54 + surface.ColumnWidthAt(0); Assert.True(surface.PointerPressed(new HavenPointerInput(new HavenPoint(boundary - 1, 12), new HavenPoint(boundary - 1, 12), HavenPointerKind.Mouse))); Assert.True(surface.PointerMoved(new HavenPointerInput(new HavenPoint(boundary + 39, 12), new HavenPoint(boundary + 39, 12), HavenPointerKind.Mouse))); Assert.True(surface.PointerReleased(new HavenPointerInput(new HavenPoint(boundary + 39, 12), new HavenPoint(boundary + 39, 12), HavenPointerKind.Mouse))); Assert.True(surface.ColumnWidthAt(0) > 230); Assert.NotEmpty(changes);
    }

    [Fact]
    public void Canonical_table_filters_hide_rows_without_mutating_underlying_cells()
    {
        var sheet = DataSheet.Create(0, "Filtered"); sheet.SetCell(0, 0, "Name"); sheet.SetCell(0, 1, "Score"); sheet.SetCell(1, 0, "Ada"); sheet.SetCell(1, 1, "8"); sheet.SetCell(2, 0, "Grace"); sheet.SetCell(2, 1, "10"); sheet.SetCell(3, 0, "Linus"); sheet.SetCell(3, 1, "7");
        var before = sheet.Cells.Select(cell => (cell.Row, cell.Column, cell.Value)).ToArray(); var surface = Layout(sheet);
        surface.ApplyTableDefinition(new DataTableDefinition { SheetId = sheet.Id, Name = "Scores", Range = new DataCellRange { StartRow = 0, StartColumn = 0, EndRow = 3, EndColumn = 1 }, HasHeaders = true, Filters = [new DataTableFilter { Column = 0, Operator = DataFilterOperator.Contains, Value = "a" }, new DataTableFilter { Column = 1, Operator = DataFilterOperator.GreaterThanOrEqual, Value = "9" }] });
        Assert.Equal(2, surface.FilteredOutRowCount); Assert.Equal(0, surface.RowHeightAt(1)); Assert.True(surface.RowHeightAt(2) > 0); Assert.Equal(0, surface.RowHeightAt(3)); Assert.Equal(before, sheet.Cells.Select(cell => (cell.Row, cell.Column, cell.Value)).ToArray());
    }

    [Fact]
    public void Live_chart_re_reads_source_range_when_spreadsheet_cells_change()
    {
        var sheet = DataSheet.Create(0, "Chart data"); sheet.SetCell(0, 0, "Month"); sheet.SetCell(0, 1, "Sales"); sheet.SetCell(1, 0, "Jan"); sheet.SetCell(1, 1, "10"); sheet.SetCell(2, 0, "Feb"); sheet.SetCell(2, 1, "20");
        var chart = new DataChartDefinition { SheetId = sheet.Id, SourceRange = new DataCellRange { StartRow = 0, StartColumn = 0, EndRow = 2, EndColumn = 1 }, FirstRowIsHeaders = true, CategoryColumn = 0, SeriesColumns = [1] }; var surface = new DataChartSurface(sheet, chart);
        Assert.Equal(new[] { 10d, 20d }, Assert.Single(surface.SnapshotSeriesValues())); sheet.SetCell(2, 1, "42", kind: DataCellKind.Number); surface.Update(sheet, chart); Assert.Equal(new[] { 10d, 42d }, Assert.Single(surface.SnapshotSeriesValues()));
    }

    private static DataSpreadsheetSurface Layout(DataSheet sheet, double width = 960, double height = 640)
    {
        var surface = new DataSpreadsheetSurface(); surface.SetValue(HavenProperties.Width, HavenLength.Px(width)); surface.SetValue(HavenProperties.Height, HavenLength.Px(height)); surface.SetSheet(sheet, 0, 0); new HavenLayoutEngine().Layout(surface, new HavenSize(width, height), HavenPlatform.Windows, new FixedMeasure()); return surface;
    }
    private sealed class FixedMeasure : IHavenMeasureContext { public HavenSize MeasureLeaf(HavenElement element, HavenSize available) => available; }
}
