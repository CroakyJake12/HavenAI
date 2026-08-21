using Haven.Core;

namespace Haven.Desktop.Views.Pages.Data;

internal sealed partial class DataSpreadsheetSurface
{
    private DataSpreadsheetTableState? _tableState;
    private readonly HashSet<int> _filteredRows = [];
    private int[] _filteredRowsSorted = [];
    private double[] _filteredHeightPrefix = [];

    public DataSpreadsheetTableState? TableState => _tableState;
    public int FilteredOutRowCount => _filteredRows.Count;

    public void ApplyTableState(DataSpreadsheetTableState? state)
    {
        _tableState = state?.Normalize();
        RebuildFilteredRows();
        if (IsRowFiltered(_activeRow))
        {
            var replacement = NextVisibleRow(_activeRow);
            if (replacement >= MaximumRows && _tableState is not null) replacement = Math.Max(0, _tableState.StartRow);
            _activeRow = _anchorRow = _extentRow = Math.Clamp(replacement, 0, MaximumRows - 1);
        }
        ClampOffsets(); EnsureActiveVisible(); UpdateSemanticState(); ViewportChanged?.Invoke(); Invalidate();
    }

    private void LoadTableFromSheet(DataSheet? sheet) => ApplyTableState(sheet is null ? null : DataSpreadsheetTableMetadata.Read(sheet.Metadata));

    private bool IsRowFiltered(int row) => _filteredRows.Contains(row);

    private int NextVisibleRow(int row)
    {
        row = Math.Clamp(row, 0, MaximumRows - 1);
        while (row < MaximumRows && IsRowFiltered(row)) row++;
        return row;
    }

    private double HiddenRawHeightBefore(int exclusiveRow)
    {
        if (_filteredRowsSorted.Length == 0 || exclusiveRow <= _filteredRowsSorted[0]) return 0;
        var index = Array.BinarySearch(_filteredRowsSorted, exclusiveRow);
        var count = index >= 0 ? index : ~index;
        return count <= 0 ? 0 : _filteredHeightPrefix[count - 1];
    }

    private void RebuildFilteredRows()
    {
        _filteredRows.Clear();
        var table = _tableState;
        if (_sheet is not null && table?.FilterColumn is int column && !string.IsNullOrWhiteSpace(table.FilterText))
        {
            for (var row = table.DataStartRow; row <= table.EndRow; row++)
            {
                var value = _sheet.GetCell(row, column)?.Value ?? string.Empty;
                if (!value.Contains(table.FilterText, StringComparison.OrdinalIgnoreCase)) _filteredRows.Add(row);
            }
        }
        _filteredRowsSorted = _filteredRows.Order().ToArray();
        _filteredHeightPrefix = new double[_filteredRowsSorted.Length];
        var total = 0d;
        for (var index = 0; index < _filteredRowsSorted.Length; index++)
        {
            var row = _filteredRowsSorted[index];
            total += _layoutState.RowHeights.TryGetValue(row, out var custom) ? custom : BaseRowHeight;
            _filteredHeightPrefix[index] = total;
        }
    }
}
