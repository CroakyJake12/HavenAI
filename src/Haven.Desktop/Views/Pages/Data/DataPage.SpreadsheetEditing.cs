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
        _spreadsheetUndo.Push(CaptureSheetSnapshot(Workbook.Id, CurrentSheet));
        while (_spreadsheetUndo.Count > SpreadsheetHistoryLimit) TrimOldest(_spreadsheetUndo);
        _spreadsheetRedo.Clear(); UpdateSpreadsheetHistoryButtons();
    }

    private void UndoSpreadsheetEdit()
    {
        if (Workbook is null || _spreadsheetUndo.Count == 0) return;
        var snapshot = _spreadsheetUndo.Pop(); if (snapshot.WorkbookId != Workbook.Id) { _spreadsheetUndo.Clear(); UpdateSpreadsheetHistoryButtons(); return; }
        var sheet = Workbook.Sheets.FirstOrDefault(item => item.Id == snapshot.SheetId); if (sheet is null) return;
        _spreadsheetRedo.Push(CaptureSheetSnapshot(Workbook.Id, sheet)); RestoreSheetSnapshot(sheet, snapshot); _sheetIndex = Workbook.Sheets.IndexOf(sheet); FinishSpreadsheetRestore("Undid spreadsheet change.");
    }

    private void RedoSpreadsheetEdit()
    {
        if (Workbook is null || _spreadsheetRedo.Count == 0) return;
        var snapshot = _spreadsheetRedo.Pop(); if (snapshot.WorkbookId != Workbook.Id) { _spreadsheetRedo.Clear(); UpdateSpreadsheetHistoryButtons(); return; }
        var sheet = Workbook.Sheets.FirstOrDefault(item => item.Id == snapshot.SheetId); if (sheet is null) return;
        _spreadsheetUndo.Push(CaptureSheetSnapshot(Workbook.Id, sheet)); RestoreSheetSnapshot(sheet, snapshot); _sheetIndex = Workbook.Sheets.IndexOf(sheet); FinishSpreadsheetRestore("Redid spreadsheet change.");
    }

    private void FinishSpreadsheetRestore(string status)
    {
        _restoringSpreadsheetEdit = true;
        try { RecalculateWorkbook(); _lastQueryResult = null; MarkDirty(); RenderCurrent(); _route.SetStatus(status); }
        finally { _restoringSpreadsheetEdit = false; UpdateSpreadsheetHistoryButtons(); }
    }

    private void CreateSpreadsheetTable()
    {
        var sheet = CurrentSheet; var surface = SpreadsheetSurface(); if (sheet is null || surface is null) return;
        var range = EffectiveSpreadsheetRange(sheet, surface); if (!ValidateCommandRange(range, "create a table")) return;
        CaptureSpreadsheetUndo(); var table = new DataSpreadsheetTableState(DataSpreadsheetTableState.CurrentVersion, range.StartRow, range.StartColumn, range.EndRow, range.EndColumn, range.RowCount > 1, null, string.Empty).Normalize();
        DataSpreadsheetTableMetadata.Write(sheet.Metadata, table); MarkDirty(); RenderCurrent(); _route.SetStatus($"Table created · {Address(range.StartRow, range.StartColumn)}:{Address(range.EndRow, range.EndColumn)}.");
    }

    private void RemoveSpreadsheetTable()
    {
        var sheet = CurrentSheet; if (sheet is null || DataSpreadsheetTableMetadata.Read(sheet.Metadata) is null) return; CaptureSpreadsheetUndo(); DataSpreadsheetTableMetadata.Write(sheet.Metadata, null); MarkDirty(); RenderCurrent(); _route.SetStatus("Removed the table view. Cell data is unchanged.");
    }

    private void ApplySpreadsheetFilter()
    {
        var sheet = CurrentSheet; var surface = SpreadsheetSurface(); if (sheet is null || surface is null) return;
        var table = DataSpreadsheetTableMetadata.Read(sheet.Metadata); if (table is null) { CreateSpreadsheetTable(); table = DataSpreadsheetTableMetadata.Read(sheet.Metadata); if (table is null) return; }
        var column = table.Contains(surface.ActiveRow, surface.ActiveColumn) ? surface.ActiveColumn : table.StartColumn; var text = (_spreadsheetFilterInput?.Text ?? string.Empty).Trim();
        CaptureSpreadsheetUndo(); DataSpreadsheetTableMetadata.Write(sheet.Metadata, table with { FilterColumn = string.IsNullOrEmpty(text) ? null : column, FilterText = text }); MarkDirty(); RenderCurrent();
        _route.SetStatus(string.IsNullOrEmpty(text) ? "Table filter cleared." : $"Filtered table by {ColumnNameForTools(column)} containing ‘{text}’.");
    }

    private void ClearSpreadsheetFilter()
    {
        var sheet = CurrentSheet; if (sheet is null) return; var table = DataSpreadsheetTableMetadata.Read(sheet.Metadata); if (table is null || (table.FilterColumn is null && string.IsNullOrWhiteSpace(table.FilterText))) return;
        CaptureSpreadsheetUndo(); DataSpreadsheetTableMetadata.Write(sheet.Metadata, table with { FilterColumn = null, FilterText = string.Empty }); MarkDirty(); RenderCurrent(); _route.SetStatus("Table filter cleared.");
    }

    private void SortSpreadsheet(bool ascending)
    {
        var sheet = CurrentSheet; var surface = SpreadsheetSurface(); if (sheet is null || surface is null) return; var range = EffectiveSpreadsheetRange(sheet, surface); if (!ValidateCommandRange(range, "sort this range")) return;
        var table = DataSpreadsheetTableMetadata.Read(sheet.Metadata); var tableRange = table is not null && table.Contains(surface.ActiveRow, surface.ActiveColumn) && table.StartRow == range.StartRow && table.EndRow == range.EndRow && table.StartColumn == range.StartColumn && table.EndColumn == range.EndColumn;
        var firstDataRow = tableRange && table!.HasHeaders ? range.StartRow + 1 : range.StartRow; if (firstDataRow >= range.EndRow) { _route.SetStatus("The selected range needs at least two data rows to sort."); return; }
        var sortColumn = Math.Clamp(surface.ActiveColumn, range.StartColumn, range.EndColumn);
        if (sheet.Cells.Any(cell => cell.Row >= firstDataRow && cell.Row <= range.EndRow && cell.Column >= range.StartColumn && cell.Column <= range.EndColumn && !string.IsNullOrWhiteSpace(cell.Formula))) { _route.SetStatus("Sort was not applied because this range contains formulas. Sort value-only rows to avoid changing formula references."); return; }
        CaptureSpreadsheetUndo();
        var sourceRows = Enumerable.Range(firstDataRow, range.EndRow - firstDataRow + 1).Select(row => new SpreadsheetSortRow(row, sheet.GetCell(row, sortColumn))).ToList(); sourceRows.Sort((left, right) => CompareSortRows(left, right, ascending));
        var outside = sheet.Cells.Where(cell => cell.Row < firstDataRow || cell.Row > range.EndRow || cell.Column < range.StartColumn || cell.Column > range.EndColumn).Select(CloneCell).ToList();
        for (var targetOffset = 0; targetOffset < sourceRows.Count; targetOffset++)
        {
            var sourceRow = sourceRows[targetOffset].Row; var targetRow = firstDataRow + targetOffset;
            foreach (var cell in sheet.Cells.Where(cell => cell.Row == sourceRow && cell.Column >= range.StartColumn && cell.Column <= range.EndColumn)) { var clone = CloneCell(cell); clone.Row = targetRow; outside.Add(clone); }
        }
        sheet.Cells = outside; sheet.Normalize(sheet.Order); RecalculateWorkbook(); _lastQueryResult = null; MarkDirty(); RenderCurrent(); _route.SetStatus($"Sorted {Address(firstDataRow, range.StartColumn)}:{Address(range.EndRow, range.EndColumn)} by {ColumnNameForTools(sortColumn)} {(ascending ? "ascending" : "descending")}.");
    }

    private DataSpreadsheetRange EffectiveSpreadsheetRange(DataSheet sheet, DataSpreadsheetSurface surface)
    {
        var table = DataSpreadsheetTableMetadata.Read(sheet.Metadata); if (table is not null && table.Contains(surface.ActiveRow, surface.ActiveColumn)) return new DataSpreadsheetRange(table.StartRow, table.StartColumn, table.EndRow, table.EndColumn);
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
        try { var table = CurrentSheet is null ? null : DataSpreadsheetTableMetadata.Read(CurrentSheet.Metadata); if (_spreadsheetFilterInput is not null) _spreadsheetFilterInput.Text = table?.FilterText ?? string.Empty; UpdateSpreadsheetHistoryButtons(); }
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

    private static DataSheetEditSnapshot CaptureSheetSnapshot(Guid workbookId, DataSheet sheet) => new(workbookId, sheet.Id, sheet.Cells.Select(CloneCell).ToList(), new Dictionary<string, string>(sheet.Metadata, StringComparer.Ordinal));
    private static void RestoreSheetSnapshot(DataSheet sheet, DataSheetEditSnapshot snapshot) { sheet.Cells = snapshot.Cells.Select(CloneCell).ToList(); sheet.Metadata = new Dictionary<string, string>(snapshot.Metadata, StringComparer.Ordinal); sheet.Normalize(sheet.Order); }
    private static DataCell CloneCell(DataCell cell) => new() { Row = cell.Row, Column = cell.Column, Kind = cell.Kind, Value = cell.Value, Formula = cell.Formula, Metadata = new Dictionary<string, string>(cell.Metadata, StringComparer.Ordinal) };
    private static void TrimOldest(Stack<DataSheetEditSnapshot> stack) { var keep = stack.Reverse().Skip(1).ToArray(); stack.Clear(); foreach (var item in keep) stack.Push(item); }
    private static string Address(int row, int column) => ColumnNameForTools(column) + (row + 1).ToString(CultureInfo.InvariantCulture);
    private static string ColumnNameForTools(int column) { var value = column + 1; var result = string.Empty; while (value > 0) { value--; result = (char)('A' + value % 26) + result; value /= 26; } return result; }

    private sealed record DataSheetEditSnapshot(Guid WorkbookId, Guid SheetId, List<DataCell> Cells, Dictionary<string, string> Metadata);
    private sealed record SpreadsheetSortRow(int Row, DataCell? Cell);
}
