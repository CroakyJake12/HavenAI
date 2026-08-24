using System.Globalization;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Views.Pages.Data;

public sealed partial class DataPage
{
    private const int SpreadsheetHistoryLimit = 50;
    private readonly Stack<DataSheetEditSnapshot> _spreadsheetUndo = new();
    private readonly Stack<DataSheetEditSnapshot> _spreadsheetRedo = new();
    private Input? _spreadsheetFilterInput;
    private HavenButton? _spreadsheetUndoButton;
    private HavenButton? _spreadsheetRedoButton;
    private bool _restoringSpreadsheetEdit;
    private bool _syncingSpreadsheetTools;

    internal void InitializeSpreadsheetEditingTools()
    {
        var toolbar = _route.Root.DescendantsAndSelf().OfType<Container>().FirstOrDefault(element => element.Name == "Data.Grid.Toolbar");
        if (toolbar is null || toolbar.Children.Any(child => child.Name == "Data.Grid.Undo")) return;
        _spreadsheetUndoButton = ToolButton("Data.Grid.Undo", "Undo", UndoSpreadsheetEdit);
        _spreadsheetRedoButton = ToolButton("Data.Grid.Redo", "Redo", RedoSpreadsheetEdit);
        toolbar.Add(_spreadsheetUndoButton); toolbar.Add(_spreadsheetRedoButton);
        toolbar.Add(ToolButton("Data.Grid.Sort.Ascending", "Sort A→Z", () => SortSpreadsheet(true)));
        toolbar.Add(ToolButton("Data.Grid.Sort.Descending", "Sort Z→A", () => SortSpreadsheet(false)));
        toolbar.Add(ToolButton("Data.Grid.Table.Create", "Create table", CreateSpreadsheetTable));
        toolbar.Add(ToolButton("Data.Grid.Table.Clear", "Remove table", RemoveSpreadsheetTable));
        _spreadsheetFilterInput = new Input { Name = "Data.Grid.Filter.Value", Placeholder = "Filter value" };
        _spreadsheetFilterInput.Accessibility.AccessibleName = "Table filter text for the active column"; _spreadsheetFilterInput.SetValue(HavenProperties.Width, HavenLength.Px(150)); _spreadsheetFilterInput.SetValue(HavenProperties.MinWidth, HavenLength.Px(120));
        toolbar.Add(_spreadsheetFilterInput); toolbar.Add(ToolButton("Data.Grid.Filter.Apply", "Apply filter", ApplySpreadsheetFilter)); toolbar.Add(ToolButton("Data.Grid.Filter.Clear", "Clear filter", ClearSpreadsheetFilter));
        SyncSpreadsheetEditingUi();
    }

    private HavenButton ToolButton(string name, string label, Action action)
    {
        var button = new HavenButton { Name = name, Content = label, Variant = ButtonVariant.Tertiary }; button.Accessibility.AccessibleName = label; button.SetValue(HavenProperties.MinHeight, HavenLength.Px(38)); button.Invoked += (_, _) => action(); return button;
    }

    private DataSpreadsheetSurface? SpreadsheetSurface() => _route.GridHost.Children.OfType<DataSpreadsheetSurface>().FirstOrDefault();

    private void CaptureSpreadsheetUndo()
    {
        if (_restoringSpreadsheetEdit || Workbook is null || CurrentSheet is null) return;
        _spreadsheetUndo.Push(CaptureSheetSnapshot(Workbook, CurrentSheet));
        while (_spreadsheetUndo.Count > SpreadsheetHistoryLimit) TrimOldest(_spreadsheetUndo);
        _spreadsheetRedo.Clear(); UpdateSpreadsheetHistoryButtons();
    }

    private void UndoSpreadsheetEdit()
    {
        if (Workbook is null || _spreadsheetUndo.Count == 0) return;
        var snapshot = _spreadsheetUndo.Pop(); if (snapshot.WorkbookId != Workbook.Id) { _spreadsheetUndo.Clear(); UpdateSpreadsheetHistoryButtons(); return; }
        var sheet = Workbook.Sheets.FirstOrDefault(item => item.Id == snapshot.SheetId); if (sheet is null) return;
        _spreadsheetRedo.Push(CaptureSheetSnapshot(Workbook, sheet)); RestoreSheetSnapshot(Workbook, sheet, snapshot); _sheetIndex = Workbook.Sheets.IndexOf(sheet); FinishSpreadsheetRestore("Undid spreadsheet change.");
    }

    private void RedoSpreadsheetEdit()
    {
        if (Workbook is null || _spreadsheetRedo.Count == 0) return;
        var snapshot = _spreadsheetRedo.Pop(); if (snapshot.WorkbookId != Workbook.Id) { _spreadsheetRedo.Clear(); UpdateSpreadsheetHistoryButtons(); return; }
        var sheet = Workbook.Sheets.FirstOrDefault(item => item.Id == snapshot.SheetId); if (sheet is null) return;
        _spreadsheetUndo.Push(CaptureSheetSnapshot(Workbook, sheet)); RestoreSheetSnapshot(Workbook, sheet, snapshot); _sheetIndex = Workbook.Sheets.IndexOf(sheet); FinishSpreadsheetRestore("Redid spreadsheet change.");
    }

    private void FinishSpreadsheetRestore(string status)
    {
        _restoringSpreadsheetEdit = true;
        try { RecalculateWorkbook(); _lastQueryResult = null; MarkDirty(); RenderCurrent(); _route.SetStatus(status); }
        finally { _restoringSpreadsheetEdit = false; UpdateSpreadsheetHistoryButtons(); }
    }

    private void CreateSpreadsheetTable()
    {
        var sheet = CurrentSheet; var surface = SpreadsheetSurface(); if (Workbook is null || sheet is null || surface is null) return;
        var range = EffectiveSpreadsheetRange(sheet, surface); if (!ValidateCommandRange(range, "create a table")) return;
        CaptureSpreadsheetUndo();
        Workbook.Tables.RemoveAll(table => table.SheetId == sheet.Id && RangesOverlap(table.Range, range));
        var suffix = Workbook.Tables.Count + 1; var name = $"Table{suffix}"; while (Workbook.Tables.Any(table => table.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name = $"Table{++suffix}";
        Workbook.Tables.Add(new DataTableDefinition { SheetId = sheet.Id, Name = name, Range = ToCoreRange(range), HasHeaders = range.RowCount > 1 });
        DataSpreadsheetTableMetadata.Write(sheet.Metadata, null);
        MarkDirty(); RenderCurrent(); _route.SetStatus($"Table {name} created · {Address(range.StartRow, range.StartColumn)}:{Address(range.EndRow, range.EndColumn)}.");
    }

    private void RemoveSpreadsheetTable()
    {
        var sheet = CurrentSheet; if (Workbook is null || sheet is null) return;
        var table = CurrentTableDefinition(sheet); var legacy = DataSpreadsheetTableMetadata.Read(sheet.Metadata); if (table is null && legacy is null) return;
        CaptureSpreadsheetUndo(); if (table is not null) Workbook.Tables.Remove(table); DataSpreadsheetTableMetadata.Write(sheet.Metadata, null); MarkDirty(); RenderCurrent(); _route.SetStatus("Removed the table definition. Cell data is unchanged.");
    }

    private void ApplySpreadsheetFilter()
    {
        var sheet = CurrentSheet; var surface = SpreadsheetSurface(); if (Workbook is null || sheet is null || surface is null) return;
        var table = CurrentTableDefinition(sheet, surface); if (table is null) { CreateSpreadsheetTable(); table = CurrentTableDefinition(sheet, surface); if (table is null) return; }
        var column = table.Range.Contains(surface.ActiveRow, surface.ActiveColumn) ? surface.ActiveColumn : table.Range.StartColumn; var text = (_spreadsheetFilterInput?.Text ?? string.Empty).Trim();
        CaptureSpreadsheetUndo();
        table.Filters.RemoveAll(filter => filter.Column == column && filter.Operator == DataFilterOperator.Contains);
        if (!string.IsNullOrEmpty(text)) table.Filters.Add(new DataTableFilter { Column = column, Operator = DataFilterOperator.Contains, Value = text });
        table.Filters = table.Filters.OrderBy(filter => filter.Column).ThenBy(filter => filter.Operator).ThenBy(filter => filter.Value, StringComparer.OrdinalIgnoreCase).ToList();
        DataSpreadsheetTableMetadata.Write(sheet.Metadata, null); MarkDirty(); RenderCurrent();
        _route.SetStatus(string.IsNullOrEmpty(text) ? $"Contains filter cleared for {ColumnNameForTools(column)}." : $"Filtered table by {ColumnNameForTools(column)} containing ‘{text}’. Existing predicates still compose with it.");
    }

    private void ClearSpreadsheetFilter()
    {
        var sheet = CurrentSheet; if (Workbook is null || sheet is null) return; var table = CurrentTableDefinition(sheet); var legacy = DataSpreadsheetTableMetadata.Read(sheet.Metadata);
        if ((table is null || table.Filters.Count == 0) && (legacy is null || (legacy.FilterColumn is null && string.IsNullOrWhiteSpace(legacy.FilterText)))) return;
        CaptureSpreadsheetUndo(); if (table is not null) table.Filters.Clear(); DataSpreadsheetTableMetadata.Write(sheet.Metadata, null); MarkDirty(); RenderCurrent(); _route.SetStatus("Table filters cleared. Underlying cell data is unchanged.");
    }

    private void SortSpreadsheet(bool ascending)
    {
        var sheet = CurrentSheet; var surface = SpreadsheetSurface(); if (Workbook is null || sheet is null || surface is null) return; var range = EffectiveSpreadsheetRange(sheet, surface); if (!ValidateCommandRange(range, "sort this range")) return;
        var table = CurrentTableDefinition(sheet, surface); var tableRange = table is not null && table.Range.StartRow == range.StartRow && table.Range.EndRow == range.EndRow && table.Range.StartColumn == range.StartColumn && table.Range.EndColumn == range.EndColumn;
        var hasHeaders = tableRange && table!.HasHeaders; var firstDataRow = range.StartRow + (hasHeaders ? 1 : 0); if (firstDataRow >= range.EndRow) { _route.SetStatus("The selected range needs at least two data rows to sort."); return; }
        var sortColumn = Math.Clamp(surface.ActiveColumn, range.StartColumn, range.EndColumn);
        var activity = BeginDataActivity("Sort spreadsheet range", $"Sorting {Address(range.StartRow, range.StartColumn)}:{Address(range.EndRow, range.EndColumn)} by {ColumnNameForTools(sortColumn)}.");
        CaptureSpreadsheetUndo(); DataSpreadsheetOperations.SortRange(sheet, ToCoreRange(range), sortColumn, descending: !ascending, hasHeader: hasHeaders);
        if (tableRange) { table!.SortColumn = sortColumn; table.SortDescending = !ascending; }
        DataSpreadsheetTableMetadata.Write(sheet.Metadata, null); RecalculateWorkbook(); _lastQueryResult = null; MarkDirty(); RenderCurrent(); var status = $"Sorted {Address(firstDataRow, range.StartColumn)}:{Address(range.EndRow, range.EndColumn)} by {ColumnNameForTools(sortColumn)} {(ascending ? "ascending" : "descending")}."; _route.SetStatus(status); CompleteDataActivity(activity, status);
    }

    private DataSpreadsheetRange EffectiveSpreadsheetRange(DataSheet sheet, DataSpreadsheetSurface surface)
    {
        var table = CurrentTableDefinition(sheet, surface); if (table is not null && table.Range.Contains(surface.ActiveRow, surface.ActiveColumn)) return new DataSpreadsheetRange(table.Range.StartRow, table.Range.StartColumn, table.Range.EndRow, table.Range.EndColumn);
        var legacy = DataSpreadsheetTableMetadata.Read(sheet.Metadata); if (legacy is not null && legacy.Contains(surface.ActiveRow, surface.ActiveColumn)) return new DataSpreadsheetRange(legacy.StartRow, legacy.StartColumn, legacy.EndRow, legacy.EndColumn);
        var selection = surface.Selection; if (selection.RowCount > 1 || selection.ColumnCount > 1) return selection; if (sheet.Cells.Count == 0) return selection;
        return new DataSpreadsheetRange(sheet.Cells.Min(cell => cell.Row), sheet.Cells.Min(cell => cell.Column), sheet.Cells.Max(cell => cell.Row), sheet.Cells.Max(cell => cell.Column));
    }

    private bool ValidateCommandRange(DataSpreadsheetRange range, string action)
    {
        var count = (long)range.RowCount * range.ColumnCount; if (count is > 0 and <= 100_000) return true; _route.SetStatus($"Couldn’t {action}: select a range of 100,000 cells or fewer."); return false;
    }

    private void SyncSpreadsheetEditingUi()
    {
        if (_syncingSpreadsheetTools) return; _syncingSpreadsheetTools = true;
        try
        {
            var table = CurrentSheet is null ? null : CurrentTableDefinition(CurrentSheet); var activeColumn = SpreadsheetSurface()?.ActiveColumn;
            var filter = table?.Filters.FirstOrDefault(value => value.Operator == DataFilterOperator.Contains && (!activeColumn.HasValue || value.Column == activeColumn.Value)) ?? table?.Filters.FirstOrDefault(value => value.Operator == DataFilterOperator.Contains);
            var legacy = CurrentSheet is null ? null : DataSpreadsheetTableMetadata.Read(CurrentSheet.Metadata); if (_spreadsheetFilterInput is not null) _spreadsheetFilterInput.Text = filter?.Value ?? legacy?.FilterText ?? string.Empty; UpdateSpreadsheetHistoryButtons();
        }
        finally { _syncingSpreadsheetTools = false; }
    }

    private void UpdateSpreadsheetHistoryButtons()
    {
        _spreadsheetUndoButton?.SetValue(HavenProperties.Enabled, _spreadsheetUndo.Count > 0); _spreadsheetRedoButton?.SetValue(HavenProperties.Enabled, _spreadsheetRedo.Count > 0);
    }

    private void ResetSpreadsheetHistory() { _spreadsheetUndo.Clear(); _spreadsheetRedo.Clear(); UpdateSpreadsheetHistoryButtons(); }

    private static int CompareSortRows(SpreadsheetSortRow left, SpreadsheetSortRow right, bool ascending)
    {
        var comparison = CompareSortCells(left.Cell, right.Cell); if (comparison == 0) comparison = left.Row.CompareTo(right.Row); return ascending ? comparison : -comparison;
    }

    private static int CompareSortCells(DataCell? left, DataCell? right)
    {
        var leftText = left?.Value ?? string.Empty; var rightText = right?.Value ?? string.Empty; if (leftText.Length == 0 || rightText.Length == 0) return leftText.Length == rightText.Length ? 0 : leftText.Length == 0 ? 1 : -1;
        if (double.TryParse(leftText, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftNumber) && double.TryParse(rightText, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightNumber)) return leftNumber.CompareTo(rightNumber);
        if (DateTimeOffset.TryParse(leftText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var leftDate) && DateTimeOffset.TryParse(rightText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var rightDate)) return leftDate.CompareTo(rightDate);
        if (bool.TryParse(leftText, out var leftBool) && bool.TryParse(rightText, out var rightBool)) return leftBool.CompareTo(rightBool); return StringComparer.CurrentCultureIgnoreCase.Compare(leftText, rightText);
    }

    private static DataSheetEditSnapshot CaptureSheetSnapshot(DataWorkbook workbook, DataSheet sheet) => new(
        workbook.Id, sheet.Id, sheet.Cells.Select(CloneCell).ToList(), new Dictionary<string, string>(sheet.Metadata, StringComparer.Ordinal),
        workbook.Tables.Where(value => value.SheetId == sheet.Id).Select(CloneTableDefinition).ToList(),
        workbook.Validations.Where(value => value.SheetId == sheet.Id).Select(CloneValidationRule).ToList(),
        workbook.Charts.Where(value => value.SheetId == sheet.Id).Select(CloneChartDefinition).ToList());

    private static void RestoreSheetSnapshot(DataWorkbook workbook, DataSheet sheet, DataSheetEditSnapshot snapshot)
    {
        sheet.Cells = snapshot.Cells.Select(CloneCell).ToList(); sheet.Metadata = new Dictionary<string, string>(snapshot.Metadata, StringComparer.Ordinal); sheet.Normalize(sheet.Order);
        workbook.Tables.RemoveAll(value => value.SheetId == sheet.Id); workbook.Tables.AddRange(snapshot.Tables.Select(CloneTableDefinition));
        workbook.Validations.RemoveAll(value => value.SheetId == sheet.Id); workbook.Validations.AddRange(snapshot.Validations.Select(CloneValidationRule));
        workbook.Charts.RemoveAll(value => value.SheetId == sheet.Id); workbook.Charts.AddRange(snapshot.Charts.Select(CloneChartDefinition));
    }

    private static DataCell CloneCell(DataCell cell) => new() { Row = cell.Row, Column = cell.Column, Kind = cell.Kind, Value = cell.Value, Formula = cell.Formula, Metadata = new Dictionary<string, string>(cell.Metadata, StringComparer.Ordinal) };
    private static DataTableDefinition CloneTableDefinition(DataTableDefinition value) => new() { Id = value.Id, SheetId = value.SheetId, Name = value.Name, Range = value.Range.Clone(), HasHeaders = value.HasHeaders, SortColumn = value.SortColumn, SortDescending = value.SortDescending, Filters = value.Filters.Select(filter => new DataTableFilter { Column = filter.Column, Operator = filter.Operator, Value = filter.Value }).ToList(), Metadata = new Dictionary<string, string>(value.Metadata, StringComparer.Ordinal) };
    private static DataValidationRule CloneValidationRule(DataValidationRule value) => new() { Id = value.Id, SheetId = value.SheetId, Range = value.Range.Clone(), Kind = value.Kind, AllowBlank = value.AllowBlank, AllowedValues = value.AllowedValues.ToList(), Minimum = value.Minimum, Maximum = value.Maximum, InputMessage = value.InputMessage, ErrorMessage = value.ErrorMessage };
    private static DataChartDefinition CloneChartDefinition(DataChartDefinition value) => new() { Id = value.Id, SheetId = value.SheetId, Type = value.Type, SourceRange = value.SourceRange.Clone(), Title = value.Title, XAxisTitle = value.XAxisTitle, YAxisTitle = value.YAxisTitle, ShowLegend = value.ShowLegend, FirstRowIsHeaders = value.FirstRowIsHeaders, CategoryColumn = value.CategoryColumn, SeriesColumns = value.SeriesColumns.ToList(), Metadata = new Dictionary<string, string>(value.Metadata, StringComparer.Ordinal) };
    private static void TrimOldest(Stack<DataSheetEditSnapshot> stack) { var keep = stack.Reverse().Skip(1).ToArray(); stack.Clear(); foreach (var item in keep) stack.Push(item); }
    private static string Address(int row, int column) => ColumnNameForTools(column) + (row + 1).ToString(CultureInfo.InvariantCulture);
    private static string ColumnNameForTools(int column) { var value = column + 1; var result = string.Empty; while (value > 0) { value--; result = (char)('A' + value % 26) + result; value /= 26; } return result; }

    private sealed record DataSheetEditSnapshot(Guid WorkbookId, Guid SheetId, List<DataCell> Cells, Dictionary<string, string> Metadata, List<DataTableDefinition> Tables, List<DataValidationRule> Validations, List<DataChartDefinition> Charts);
    private sealed record SpreadsheetSortRow(int Row, DataCell? Cell);
}
