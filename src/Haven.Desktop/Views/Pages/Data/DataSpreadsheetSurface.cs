using System.Text;
using Haven.Core;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Data;

/// <summary>Spreadsheet-specific retained surface. It virtualizes only spreadsheet cells and relies on Haven's shared input contracts.</summary>
internal sealed partial class DataSpreadsheetSurface : HavenElement, IHavenDrawCommandSource, IHavenPointerInputTarget, IHavenScrollInputTarget, IHavenKeyboardInputTarget, IHavenTextInputTarget, IHavenClipboardInputTarget
{
    public const int MaximumRows = 1_048_576;
    public const int MaximumColumns = 16_384;
    private const double HeaderWidth = 54;
    private const double HeaderHeight = 30;
    private const double BaseColumnWidth = 132;
    private const double BaseRowHeight = 32;
    private const double MinimumZoom = .6;
    private const double MaximumZoom = 2;
    private DataSheet? _sheet;
    private int _activeRow; private int _activeColumn; private int _anchorRow; private int _anchorColumn; private int _extentRow; private int _extentColumn;
    private bool _dragSelecting; private bool _editing; private string _editBuffer = string.Empty;

    public DataSpreadsheetSurface()
    {
        Name = "Data.Cell.A1.Spreadsheet"; Accessibility.Role = HavenAccessibleRole.Group; Accessibility.AccessibleName = "Spreadsheet grid"; Accessibility.Description = "Scrollable spreadsheet. Use arrows to move, Shift to extend a selection, type to edit, and copy or paste tab-separated cells."; Accessibility.Focusable = true;
        SetValue(HavenProperties.Background, "SurfaceRaised"); SetValue(HavenProperties.Clip, true); SetValue(HavenProperties.MinHeight, HavenLength.Px(520)); SetValue(HavenProperties.Width, HavenLength.Percent(100)); SetValue(HavenProperties.Height, HavenLength.Percent(100));
    }

    public event Action<int, int>? SelectionChanged; public event Action<int, int, string>? CellCommitted; public event Action? ViewportChanged; public event Action? UndoRequested; public event Action? RedoRequested;
    public double OffsetX { get; private set; } public double OffsetY { get; private set; } public double Zoom { get; private set; } = 1; public int ActiveRow => _activeRow; public int ActiveColumn => _activeColumn; public int RealizedCellCount { get; private set; }
    public double RowHeight => BaseRowHeight * Zoom; public double ColumnWidth => BaseColumnWidth * Zoom;
    public int FirstVisibleRow => FirstScrollableRow(); public int FirstVisibleColumn => FirstScrollableColumn();
    public DataSpreadsheetRange Selection => DataSpreadsheetRange.Normalize(_anchorRow, _anchorColumn, _extentRow, _extentColumn);
    public string ActiveAddress => ColumnName(_activeColumn) + (_activeRow + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string ViewportSummary => $"{ColumnName(FirstVisibleColumn)}{FirstVisibleRow + 1} · {Math.Round(Zoom * 100):0}% · {RealizedCellCount} visible cells{(FrozenRows > 0 || FrozenColumns > 0 ? $" · frozen {FrozenRows}r/{FrozenColumns}c" : string.Empty)}";

    public void SetSheet(DataSheet sheet, int selectedRow, int selectedColumn)
    {
        ArgumentNullException.ThrowIfNull(sheet); var changedSheet = !ReferenceEquals(_sheet, sheet); _sheet = sheet; LoadTableFromSheet(sheet); _activeRow = Math.Clamp(selectedRow, 0, MaximumRows - 1); _activeColumn = Math.Clamp(selectedColumn, 0, MaximumColumns - 1); _anchorRow = _extentRow = _activeRow; _anchorColumn = _extentColumn = _activeColumn; _editing = false; _editBuffer = string.Empty; if (changedSheet) { OffsetX = 0; OffsetY = 0; } if (IsRowFiltered(_activeRow)) _activeRow = _anchorRow = _extentRow = Math.Clamp(NextVisibleRow(_activeRow), 0, MaximumRows - 1); EnsureActiveVisible(); UpdateSemanticState(); Invalidate();
    }

    public void SelectCell(int row, int column, bool extend = false, bool raiseChanged = true)
    {
        row = Math.Clamp(row, 0, MaximumRows - 1); column = Math.Clamp(column, 0, MaximumColumns - 1); if (!extend) { _anchorRow = row; _anchorColumn = column; } _activeRow = _extentRow = row; _activeColumn = _extentColumn = column; _editing = false; _editBuffer = string.Empty; EnsureActiveVisible(); UpdateSemanticState(); Invalidate(); if (raiseChanged) SelectionChanged?.Invoke(row, column);
    }

    public void SelectRange(int startRow, int startColumn, int endRow, int endColumn, bool raiseChanged = false)
    {
        _anchorRow = Math.Clamp(startRow, 0, MaximumRows - 1); _anchorColumn = Math.Clamp(startColumn, 0, MaximumColumns - 1); _activeRow = _extentRow = Math.Clamp(endRow, 0, MaximumRows - 1); _activeColumn = _extentColumn = Math.Clamp(endColumn, 0, MaximumColumns - 1); EnsureActiveVisible(); UpdateSemanticState(); Invalidate(); if (raiseChanged) SelectionChanged?.Invoke(_activeRow, _activeColumn);
    }

    public void ScrollByPixels(double deltaX, double deltaY) { OffsetX = Math.Clamp(OffsetX + deltaX, 0, MaxOffsetX()); OffsetY = Math.Clamp(OffsetY + deltaY, 0, MaxOffsetY()); ViewportChanged?.Invoke(); Invalidate(); }
    public void ScrollToCell(int row, int column) { _activeRow = _extentRow = Math.Clamp(row, 0, MaximumRows - 1); _activeColumn = _extentColumn = Math.Clamp(column, 0, MaximumColumns - 1); _anchorRow = _activeRow; _anchorColumn = _activeColumn; EnsureActiveVisible(); UpdateSemanticState(); Invalidate(); }
    public void SetZoom(double value) { var next = Math.Clamp(value, MinimumZoom, MaximumZoom); if (Math.Abs(next - Zoom) < .001) return; Zoom = next; ClampOffsets(); EnsureActiveVisible(); ViewportChanged?.Invoke(); Invalidate(); }

    public bool PointerPressed(HavenPointerInput input)
    {
        if (_sheet is null || input.Button != HavenPointerButton.Primary) return false;
        var local = input.LocalPosition;
        if (local.X < 0 || local.Y < 0 || local.X > Bounds.Width || local.Y > Bounds.Height) return false;
        _editing = false; _editBuffer = string.Empty; _dragSelecting = false;
        if (TryBeginResize(local)) return true;
        if (local.X < HeaderWidth && local.Y < HeaderHeight)
        {
            _anchorRow = 0; _anchorColumn = 0; _extentRow = MaximumRows - 1; _extentColumn = MaximumColumns - 1; _activeRow = FirstScrollableRow(); _activeColumn = FirstScrollableColumn();
            UpdateSemanticState(); SelectionChanged?.Invoke(_activeRow, _activeColumn); Invalidate(); return true;
        }
        if (local.Y < HeaderHeight && local.X >= HeaderWidth && TryColumnAtScreenX(local.X, out var column))
        {
            _anchorRow = 0; _extentRow = MaximumRows - 1; _anchorColumn = _extentColumn = _activeColumn = column; _activeRow = FirstScrollableRow();
            UpdateSemanticState(); SelectionChanged?.Invoke(_activeRow, _activeColumn); Invalidate(); return true;
        }
        if (local.X < HeaderWidth && local.Y >= HeaderHeight && TryRowAtScreenY(local.Y, out var row))
        {
            _anchorColumn = 0; _extentColumn = MaximumColumns - 1; _anchorRow = _extentRow = _activeRow = row; _activeColumn = FirstScrollableColumn();
            UpdateSemanticState(); SelectionChanged?.Invoke(_activeRow, _activeColumn); Invalidate(); return true;
        }
        if (!TryCellAt(local, out var cellRow, out var cellColumn)) return false;
        var extend = input.Modifiers.HasFlag(HavenKeyModifiers.Shift); if (!extend) { _anchorRow = cellRow; _anchorColumn = cellColumn; } _activeRow = _extentRow = cellRow; _activeColumn = _extentColumn = cellColumn; _dragSelecting = true; UpdateSemanticState(); SelectionChanged?.Invoke(cellRow, cellColumn); Invalidate(); return true;
    }
    public bool PointerMoved(HavenPointerInput input) { if (ContinueResize(input.LocalPosition)) return true; if (!_dragSelecting || !TryCellAt(input.LocalPosition, out var row, out var column)) return false; _activeRow = _extentRow = row; _activeColumn = _extentColumn = column; UpdateSemanticState(); Invalidate(); return true; }
    public bool PointerReleased(HavenPointerInput input) { if (FinishResize()) return true; if (!_dragSelecting) return false; _dragSelecting = false; SelectionChanged?.Invoke(_activeRow, _activeColumn); return true; }
    public bool PointerWheel(HavenPoint localPosition, double deltaX, double deltaY) { if (Math.Abs(deltaX) < .001 && Math.Abs(deltaY) < .001) return false; ScrollByPixels(-deltaX * 42, -deltaY * 42); return true; }

    public bool KeyDown(HavenKeyInput input)
    {
        if (_sheet is null) return false;
        if (input.PrimaryModifier && input.Key == HavenKey.Z) { UndoRequested?.Invoke(); return true; }
        if (input.PrimaryModifier && input.Key == HavenKey.Y) { RedoRequested?.Invoke(); return true; }
        if (_editing)
        {
            if (input.Key == HavenKey.Enter) { CommitEditing(); MoveActive(input.Shift ? -1 : 1, 0, false); return true; } if (input.Key == HavenKey.Escape) { _editing = false; _editBuffer = string.Empty; Invalidate(); return true; } if (input.Key == HavenKey.Backspace) { if (_editBuffer.Length > 0) _editBuffer = _editBuffer[..^1]; Invalidate(); return true; }
        }
        if (input.PrimaryModifier && input.Key == HavenKey.A)
        {
            var lastRow = _sheet.Cells.Count == 0 ? _activeRow : Math.Clamp(_sheet.Cells.Max(cell => cell.Row), 0, MaximumRows - 1);
            var lastColumn = _sheet.Cells.Count == 0 ? _activeColumn : Math.Clamp(_sheet.Cells.Max(cell => cell.Column), 0, MaximumColumns - 1);
            SelectRange(0, 0, lastRow, lastColumn); return true;
        }
        if (input.PrimaryModifier && input.Key == HavenKey.Home) { SelectCell(0, 0); return true; }
        if (input.PrimaryModifier && input.Key == HavenKey.End)
        {
            var lastRow = _sheet.Cells.Count == 0 ? _activeRow : Math.Clamp(_sheet.Cells.Max(cell => cell.Row), 0, MaximumRows - 1);
            var lastColumn = _sheet.Cells.Count == 0 ? _activeColumn : Math.Clamp(_sheet.Cells.Max(cell => cell.Column), 0, MaximumColumns - 1);
            SelectCell(lastRow, lastColumn); return true;
        }
        if (input.Key is HavenKey.Delete or HavenKey.Backspace) { ClearSelection(); return true; } if (input.Key == HavenKey.Enter) return MoveActive(input.Shift ? -1 : 1, 0, false); if (input.Key == HavenKey.Tab) return MoveActive(0, input.Shift ? -1 : 1, false); if (input.Key == HavenKey.Home) { SelectCell(_activeRow, 0, input.Shift); return true; }
        if (input.Key == HavenKey.End) { var last = _sheet.Cells.Count == 0 ? _activeColumn : Math.Clamp(_sheet.Cells.Max(cell => cell.Column), 0, MaximumColumns - 1); SelectCell(_activeRow, last, input.Shift); return true; }
        return input.Key switch { HavenKey.Left => MoveActive(0, -1, input.Shift), HavenKey.Right => MoveActive(0, 1, input.Shift), HavenKey.Up => MoveActive(-1, 0, input.Shift), HavenKey.Down => MoveActive(1, 0, input.Shift), _ => false };
    }

    public bool TextInput(string? text) { if (_sheet is null || CellCommitted is null || string.IsNullOrEmpty(text)) return false; if (!_editing) { _editing = true; _editBuffer = string.Empty; } _editBuffer += text; Invalidate(); return true; }
    public string? Copy()
    {
        if (_sheet is null) return null; var range = Selection; if ((long)range.RowCount * range.ColumnCount > 100_000) return null; var builder = new StringBuilder();
        for (var row = range.StartRow; row <= range.EndRow; row++) { if (row > range.StartRow) builder.AppendLine(); for (var column = range.StartColumn; column <= range.EndColumn; column++) { if (column > range.StartColumn) builder.Append('	'); var cell = _sheet.GetCell(row, column); var text = !string.IsNullOrWhiteSpace(cell?.Formula) ? cell!.Formula : cell?.Value ?? string.Empty; builder.Append(text.Replace("\t", " ", StringComparison.Ordinal).Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal)); } } return builder.ToString();
    }
    public string? Cut() { var copied = Copy(); if (copied is not null) ClearSelection(); return copied; }
    public bool Paste(string? text)
    {
        if (_sheet is null || string.IsNullOrEmpty(text)) return false; var rows = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'); var pastedRows = 0; var pastedColumns = 0;
        for (var r = 0; r < rows.Length && _activeRow + r < MaximumRows; r++) { var values = rows[r].Split('\t'); pastedRows = r + 1; pastedColumns = Math.Max(pastedColumns, values.Length); for (var c = 0; c < values.Length && _activeColumn + c < MaximumColumns; c++) CellCommitted?.Invoke(_activeRow + r, _activeColumn + c, values[c]); }
        if (pastedRows == 0) return false; SelectRange(_activeRow, _activeColumn, _activeRow + pastedRows - 1, Math.Min(MaximumColumns - 1, _activeColumn + pastedColumns - 1)); return true;
    }

    public void Draw(HavenDrawingContext context, double opacity) => DrawAdvanced(context, opacity);

    private bool MoveActive(int rowDelta, int columnDelta, bool extend) { SelectCell(Math.Clamp(_activeRow + rowDelta, 0, MaximumRows - 1), Math.Clamp(_activeColumn + columnDelta, 0, MaximumColumns - 1), extend); return true; }
    private void CommitEditing() { if (!_editing) return; var value = _editBuffer; _editing = false; _editBuffer = string.Empty; CellCommitted?.Invoke(_activeRow, _activeColumn, value); Invalidate(); }
    private void ClearSelection() { var range = Selection; if ((long)range.RowCount * range.ColumnCount > 100_000) return; for (var row = range.StartRow; row <= range.EndRow; row++) for (var column = range.StartColumn; column <= range.EndColumn; column++) CellCommitted?.Invoke(row, column, string.Empty); }
    private bool TryCellAt(HavenPoint local, out int row, out int column) { row = column = 0; if (local.X < HeaderWidth || local.Y < HeaderHeight || local.X > Bounds.Width || local.Y > Bounds.Height) return false; return TryColumnAtScreenX(local.X, out column) && TryRowAtScreenY(local.Y, out row); }
    private void EnsureActiveVisible() => EnsureActiveVisibleAdvanced();
    private void ClampOffsets() { OffsetX = Math.Clamp(OffsetX, 0, MaxOffsetX()); OffsetY = Math.Clamp(OffsetY, 0, MaxOffsetY()); } private double MaxOffsetX() => AdvancedMaxOffsetX(); private double MaxOffsetY() => AdvancedMaxOffsetY();
    private void UpdateSemanticState() { Name = $"Data.Cell.{ActiveAddress}.Spreadsheet"; var range = Selection; Accessibility.Selected = true; Accessibility.Description = range.RowCount == 1 && range.ColumnCount == 1 ? $"Selected cell {ActiveAddress}. Type to edit or paste tab-separated data." : $"Selected range {ColumnName(range.StartColumn)}{range.StartRow + 1}:{ColumnName(range.EndColumn)}{range.EndRow + 1}."; }
    private static void DrawText(HavenDrawingContext context, string text, HavenRect rect, double size, int weight, string token, double opacity) { context.Add(new HavenTextCommand(rect, new HavenTextLayout(text, "Montserrat", size, weight, Math.Max(8, rect.Width)), new HavenTokenBrush(token), opacity)); }
    private static string ColumnName(int column) { var value = column + 1; var result = string.Empty; while (value > 0) { value--; result = (char)('A' + value % 26) + result; value /= 26; } return result; }
}

internal readonly record struct DataSpreadsheetRange(int StartRow, int StartColumn, int EndRow, int EndColumn)
{
    public int RowCount => EndRow - StartRow + 1; public int ColumnCount => EndColumn - StartColumn + 1; public static DataSpreadsheetRange Normalize(int row1, int column1, int row2, int column2) => new(Math.Min(row1, row2), Math.Min(column1, column2), Math.Max(row1, row2), Math.Max(column1, column2));
}
