using Haven.Core;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Data;

internal sealed partial class DataSpreadsheetSurface
{
    private const double ResizeHitTolerance = 6;
    private DataSpreadsheetLayoutState _layoutState = DataSpreadsheetLayoutState.Empty;
    private SpreadsheetResizeAxis _resizeAxis;
    private int _resizeIndex = -1;
    private double _resizeStartPosition;
    private double _resizeStartSize;

    public event Action<DataSpreadsheetLayoutState>? LayoutChanged;

    public int FrozenRows => _layoutState.FrozenRows;
    public int FrozenColumns => _layoutState.FrozenColumns;
    public DataSpreadsheetLayoutState LayoutState => CloneLayoutState(_layoutState);

    public double RowHeightAt(int row)
    {
        row = Math.Clamp(row, 0, MaximumRows - 1);
        if (IsRowFiltered(row)) return 0;
        var size = _layoutState.RowHeights.TryGetValue(row, out var custom) ? custom : BaseRowHeight;
        return size * Zoom;
    }

    public double ColumnWidthAt(int column)
    {
        column = Math.Clamp(column, 0, MaximumColumns - 1);
        var size = _layoutState.ColumnWidths.TryGetValue(column, out var custom) ? custom : BaseColumnWidth;
        return size * Zoom;
    }

    public void ApplyLayoutState(DataSpreadsheetLayoutState state, bool raiseChanged = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        _layoutState = CloneLayoutState(state.Normalize());
        RebuildFilteredRows();
        ClampOffsets();
        EnsureActiveVisible();
        Invalidate();
        if (raiseChanged) PublishLayoutChanged();
    }

    public void SetFrozenPanes(int rows, int columns)
    {
        var next = _layoutState with
        {
            FrozenRows = Math.Clamp(rows, 0, DataSpreadsheetLayoutState.MaximumFrozenRows),
            FrozenColumns = Math.Clamp(columns, 0, DataSpreadsheetLayoutState.MaximumFrozenColumns)
        };
        if (next.FrozenRows == _layoutState.FrozenRows && next.FrozenColumns == _layoutState.FrozenColumns) return;
        _layoutState = CloneLayoutState(next);
        OffsetX = OffsetY = 0;
        EnsureActiveVisible();
        PublishLayoutChanged();
    }

    public void SetColumnWidth(int column, double width)
    {
        column = Math.Clamp(column, 0, MaximumColumns - 1);
        width = Math.Clamp(width, DataSpreadsheetLayoutState.MinimumColumnWidth, DataSpreadsheetLayoutState.MaximumColumnWidth);
        var widths = new Dictionary<int, double>(_layoutState.ColumnWidths);
        if (Math.Abs(width - BaseColumnWidth) < .5) widths.Remove(column); else widths[column] = width;
        _layoutState = _layoutState with { ColumnWidths = widths };
        ClampOffsets();
        EnsureActiveVisible();
        PublishLayoutChanged();
    }

    public void SetRowHeight(int row, double height)
    {
        row = Math.Clamp(row, 0, MaximumRows - 1);
        height = Math.Clamp(height, DataSpreadsheetLayoutState.MinimumRowHeight, DataSpreadsheetLayoutState.MaximumRowHeight);
        var heights = new Dictionary<int, double>(_layoutState.RowHeights);
        if (Math.Abs(height - BaseRowHeight) < .5) heights.Remove(row); else heights[row] = height;
        _layoutState = _layoutState with { RowHeights = heights };
        ClampOffsets();
        EnsureActiveVisible();
        PublishLayoutChanged();
    }

    private void LoadLayoutFromSheet(DataSheet? sheet)
    {
        _layoutState = sheet is null
            ? DataSpreadsheetLayoutState.Empty
            : CloneLayoutState(DataSpreadsheetLayoutMetadata.Read(sheet.Metadata));
        _resizeAxis = SpreadsheetResizeAxis.None;
        _resizeIndex = -1;
    }

    private void PublishLayoutChanged()
    {
        ClampOffsets();
        ViewportChanged?.Invoke();
        LayoutChanged?.Invoke(CloneLayoutState(_layoutState));
        Invalidate();
    }

    private double ColumnLogicalOffset(int exclusiveColumn)
    {
        exclusiveColumn = Math.Clamp(exclusiveColumn, 0, MaximumColumns);
        var value = exclusiveColumn * BaseColumnWidth;
        foreach (var (index, width) in _layoutState.ColumnWidths)
            if (index < exclusiveColumn) value += width - BaseColumnWidth;
        return value * Zoom;
    }

    private double RowLogicalOffset(int exclusiveRow)
    {
        exclusiveRow = Math.Clamp(exclusiveRow, 0, MaximumRows);
        var value = exclusiveRow * BaseRowHeight;
        foreach (var (index, height) in _layoutState.RowHeights)
            if (index < exclusiveRow) value += height - BaseRowHeight;
        value -= HiddenRawHeightBefore(exclusiveRow);
        return value * Zoom;
    }

    private int ColumnAtLogicalOffset(double offset)
    {
        offset = Math.Max(0, offset);
        var low = 0; var high = MaximumColumns - 1;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (ColumnLogicalOffset(middle + 1) <= offset) low = middle + 1; else high = middle;
        }
        return Math.Clamp(low, 0, MaximumColumns - 1);
    }

    private int RowAtLogicalOffset(double offset)
    {
        offset = Math.Max(0, offset);
        var low = 0; var high = MaximumRows - 1;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (RowLogicalOffset(middle + 1) <= offset) low = middle + 1; else high = middle;
        }
        return Math.Clamp(low, 0, MaximumRows - 1);
    }

    private double FrozenWidth => ColumnLogicalOffset(FrozenColumns);
    private double FrozenHeight => RowLogicalOffset(FrozenRows);

    private double ColumnScreenX(int column)
    {
        var logical = ColumnLogicalOffset(column);
        if (column < FrozenColumns) return HeaderWidth + logical;
        return HeaderWidth + FrozenWidth + (logical - ColumnLogicalOffset(FrozenColumns)) - OffsetX;
    }

    private double RowScreenY(int row)
    {
        var logical = RowLogicalOffset(row);
        if (row < FrozenRows) return HeaderHeight + logical;
        return HeaderHeight + FrozenHeight + (logical - RowLogicalOffset(FrozenRows)) - OffsetY;
    }

    private bool TryColumnAtScreenX(double localX, out int column)
    {
        column = 0;
        if (localX < HeaderWidth || localX > Bounds.Width) return false;
        var bodyX = localX - HeaderWidth;
        double logical;
        if (bodyX < FrozenWidth) logical = bodyX;
        else logical = ColumnLogicalOffset(FrozenColumns) + OffsetX + bodyX - FrozenWidth;
        column = ColumnAtLogicalOffset(logical);
        return true;
    }

    private bool TryRowAtScreenY(double localY, out int row)
    {
        row = 0;
        if (localY < HeaderHeight || localY > Bounds.Height) return false;
        var bodyY = localY - HeaderHeight;
        double logical;
        if (bodyY < FrozenHeight) logical = bodyY;
        else logical = RowLogicalOffset(FrozenRows) + OffsetY + bodyY - FrozenHeight;
        row = NextVisibleRow(RowAtLogicalOffset(logical));
        if (row >= MaximumRows) return false;
        return true;
    }

    private bool TryBeginResize(HavenPoint local)
    {
        if (local.Y < HeaderHeight && TryColumnAtScreenX(local.X, out var column))
        {
            var boundary = ColumnScreenX(column) + ColumnWidthAt(column);
            if (Math.Abs(local.X - boundary) <= ResizeHitTolerance)
            {
                _resizeAxis = SpreadsheetResizeAxis.Column; _resizeIndex = column; _resizeStartPosition = local.X; _resizeStartSize = ColumnWidthAt(column) / Zoom;
                return true;
            }
        }
        if (local.X < HeaderWidth && TryRowAtScreenY(local.Y, out var row))
        {
            var boundary = RowScreenY(row) + RowHeightAt(row);
            if (Math.Abs(local.Y - boundary) <= ResizeHitTolerance)
            {
                _resizeAxis = SpreadsheetResizeAxis.Row; _resizeIndex = row; _resizeStartPosition = local.Y; _resizeStartSize = RowHeightAt(row) / Zoom;
                return true;
            }
        }
        return false;
    }

    private bool ContinueResize(HavenPoint local)
    {
        if (_resizeAxis == SpreadsheetResizeAxis.None || _resizeIndex < 0) return false;
        if (_resizeAxis == SpreadsheetResizeAxis.Column)
        {
            var next = _resizeStartSize + (local.X - _resizeStartPosition) / Zoom;
            var widths = new Dictionary<int, double>(_layoutState.ColumnWidths)
            {
                [_resizeIndex] = Math.Clamp(next, DataSpreadsheetLayoutState.MinimumColumnWidth, DataSpreadsheetLayoutState.MaximumColumnWidth)
            };
            _layoutState = _layoutState with { ColumnWidths = widths };
        }
        else
        {
            var next = _resizeStartSize + (local.Y - _resizeStartPosition) / Zoom;
            var heights = new Dictionary<int, double>(_layoutState.RowHeights)
            {
                [_resizeIndex] = Math.Clamp(next, DataSpreadsheetLayoutState.MinimumRowHeight, DataSpreadsheetLayoutState.MaximumRowHeight)
            };
            _layoutState = _layoutState with { RowHeights = heights };
        }
        ClampOffsets();
        Invalidate();
        return true;
    }

    private bool FinishResize()
    {
        if (_resizeAxis == SpreadsheetResizeAxis.None) return false;
        _resizeAxis = SpreadsheetResizeAxis.None; _resizeIndex = -1;
        PublishLayoutChanged();
        return true;
    }

    private int FirstScrollableColumn() => Math.Max(FrozenColumns, ColumnAtLogicalOffset(ColumnLogicalOffset(FrozenColumns) + OffsetX));
    private int FirstScrollableRow() => Math.Min(MaximumRows - 1, NextVisibleRow(Math.Max(FrozenRows, RowAtLogicalOffset(RowLogicalOffset(FrozenRows) + OffsetY))));

    private double AdvancedMaxOffsetX()
    {
        var total = ColumnLogicalOffset(MaximumColumns) - ColumnLogicalOffset(FrozenColumns);
        var viewport = Math.Max(1, Bounds.Width - HeaderWidth - FrozenWidth);
        return Math.Max(0, total - viewport);
    }

    private double AdvancedMaxOffsetY()
    {
        var total = RowLogicalOffset(MaximumRows) - RowLogicalOffset(FrozenRows);
        var viewport = Math.Max(1, Bounds.Height - HeaderHeight - FrozenHeight);
        return Math.Max(0, total - viewport);
    }

    private void EnsureActiveVisibleAdvanced()
    {
        if (_activeColumn >= FrozenColumns)
        {
            var frozenLogical = ColumnLogicalOffset(FrozenColumns);
            var left = ColumnLogicalOffset(_activeColumn) - frozenLogical;
            var right = left + ColumnWidthAt(_activeColumn);
            var viewport = Math.Max(ColumnWidthAt(_activeColumn), Bounds.Width - HeaderWidth - FrozenWidth);
            if (left < OffsetX) OffsetX = left;
            else if (right > OffsetX + viewport) OffsetX = right - viewport;
        }
        if (_activeRow >= FrozenRows)
        {
            var frozenLogical = RowLogicalOffset(FrozenRows);
            var top = RowLogicalOffset(_activeRow) - frozenLogical;
            var bottom = top + RowHeightAt(_activeRow);
            var viewport = Math.Max(RowHeightAt(_activeRow), Bounds.Height - HeaderHeight - FrozenHeight);
            if (top < OffsetY) OffsetY = top;
            else if (bottom > OffsetY + viewport) OffsetY = bottom - viewport;
        }
        ClampOffsets();
        ViewportChanged?.Invoke();
    }

    private IReadOnlyList<int> VisibleColumnsAdvanced()
    {
        var columns = new List<int>();
        for (var column = 0; column < FrozenColumns; column++)
        {
            if (ColumnScreenX(column) >= Bounds.Width) break;
            columns.Add(column);
        }
        for (var column = FirstScrollableColumn(); column < MaximumColumns; column++)
        {
            var x = ColumnScreenX(column);
            if (x >= Bounds.Width) break;
            if (x + ColumnWidthAt(column) > HeaderWidth + FrozenWidth) columns.Add(column);
        }
        return columns;
    }

    private IReadOnlyList<int> VisibleRowsAdvanced()
    {
        var rows = new List<int>();
        for (var row = 0; row < FrozenRows; row++)
        {
            if (IsRowFiltered(row)) continue;
            if (RowScreenY(row) >= Bounds.Height) break;
            rows.Add(row);
        }
        for (var row = FirstScrollableRow(); row < MaximumRows; row++)
        {
            if (IsRowFiltered(row)) continue;
            var y = RowScreenY(row);
            if (y >= Bounds.Height) break;
            if (y + RowHeightAt(row) > HeaderHeight + FrozenHeight) rows.Add(row);
        }
        return rows;
    }

    private void DrawAdvanced(HavenDrawingContext context, double opacity)
    {
        if (Bounds.Width <= HeaderWidth || Bounds.Height <= HeaderHeight) return;
        context.Add(new HavenFillRoundedRectCommand(Bounds, new HavenTokenBrush("SurfaceRaised"), 8, opacity));
        context.Add(new HavenFillRoundedRectCommand(new HavenRect(Bounds.X, Bounds.Y, Bounds.Width, HeaderHeight), new HavenTokenBrush("Surface"), 0, opacity));
        context.Add(new HavenFillRoundedRectCommand(new HavenRect(Bounds.X, Bounds.Y, HeaderWidth, Bounds.Height), new HavenTokenBrush("Surface"), 0, opacity));

        var columns = VisibleColumnsAdvanced();
        var rows = VisibleRowsAdvanced();
        RealizedCellCount = rows.Count * columns.Count;
        var gridPen = new HavenPen(new HavenTokenBrush("Border"), 1);
        var selected = Selection;

        foreach (var column in columns)
        {
            var x = Bounds.X + ColumnScreenX(column);
            var width = ColumnWidthAt(column);
            var rect = new HavenRect(x, Bounds.Y, width, HeaderHeight);
            context.Add(new HavenStrokeRoundedRectCommand(rect, gridPen, 0, opacity));
            DrawText(context, ColumnName(column), new HavenRect(x + 6, Bounds.Y + 7, Math.Max(8, width - 12), HeaderHeight - 8), 11, 650, "TextSecondary", opacity);
        }

        foreach (var row in rows)
        {
            var y = Bounds.Y + RowScreenY(row);
            var height = RowHeightAt(row);
            var rowHeader = new HavenRect(Bounds.X, y, HeaderWidth, height);
            context.Add(new HavenStrokeRoundedRectCommand(rowHeader, gridPen, 0, opacity));
            DrawText(context, (row + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), new HavenRect(Bounds.X + 6, y + 8, HeaderWidth - 12, Math.Max(8, height - 8)), 10, 600, "TextSecondary", opacity);
            foreach (var column in columns)
            {
                var x = Bounds.X + ColumnScreenX(column);
                var width = ColumnWidthAt(column);
                var rect = new HavenRect(x, y, width, height);
                var isSelected = row >= selected.StartRow && row <= selected.EndRow && column >= selected.StartColumn && column <= selected.EndColumn;
                if (isSelected) context.Add(new HavenFillRoundedRectCommand(rect, new HavenSolidBrush(34, 86, 153, 255), 0, opacity));
                context.Add(new HavenStrokeRoundedRectCommand(rect, new HavenPen(new HavenTokenBrush(row == _activeRow && column == _activeColumn ? "Accent" : "Border"), row == _activeRow && column == _activeColumn ? 2 : 1), 0, opacity));
                var cell = _sheet?.GetCell(row, column);
                var value = row == _activeRow && column == _activeColumn && _editing ? _editBuffer : cell?.Value ?? string.Empty;
                if (!string.IsNullOrEmpty(value)) DrawText(context, value, new HavenRect(x + 7, y + 8, Math.Max(8, width - 14), Math.Max(8, height - 8)), 11, 450, "TextPrimary", opacity);
            }
        }

        if (FrozenColumns > 0)
        {
            var x = Bounds.X + HeaderWidth + FrozenWidth;
            context.Add(new HavenLineCommand(new HavenPoint(x, Bounds.Y), new HavenPoint(x, Bounds.Bottom), new HavenPen(new HavenTokenBrush("Accent"), 2), opacity * .75));
        }
        if (FrozenRows > 0)
        {
            var y = Bounds.Y + HeaderHeight + FrozenHeight;
            context.Add(new HavenLineCommand(new HavenPoint(Bounds.X, y), new HavenPoint(Bounds.Right, y), new HavenPen(new HavenTokenBrush("Accent"), 2), opacity * .75));
        }
        DrawAdvancedScrollIndicators(context, opacity);
    }

    private void DrawAdvancedScrollIndicators(HavenDrawingContext context, double opacity)
    {
        var maxX = AdvancedMaxOffsetX(); var maxY = AdvancedMaxOffsetY();
        if (maxY > 0)
        {
            var viewport = Math.Max(1, Bounds.Height - HeaderHeight - FrozenHeight); var total = viewport + maxY; var track = Math.Max(20, viewport - 8);
            var thumb = Math.Max(18, track * Math.Min(1, viewport / total)); var y = Bounds.Y + HeaderHeight + FrozenHeight + 4 + (track - thumb) * (OffsetY / maxY);
            context.Add(new HavenFillRoundedRectCommand(new HavenRect(Bounds.Right - 5, y, 3, thumb), new HavenTokenBrush("TextSecondary"), 2, opacity * .5));
        }
        if (maxX > 0)
        {
            var viewport = Math.Max(1, Bounds.Width - HeaderWidth - FrozenWidth); var total = viewport + maxX; var track = Math.Max(20, viewport - 8);
            var thumb = Math.Max(18, track * Math.Min(1, viewport / total)); var x = Bounds.X + HeaderWidth + FrozenWidth + 4 + (track - thumb) * (OffsetX / maxX);
            context.Add(new HavenFillRoundedRectCommand(new HavenRect(x, Bounds.Bottom - 5, thumb, 3), new HavenTokenBrush("TextSecondary"), 2, opacity * .5));
        }
    }

    private static DataSpreadsheetLayoutState CloneLayoutState(DataSpreadsheetLayoutState state) => new(
        DataSpreadsheetLayoutState.CurrentVersion,
        state.FrozenRows,
        state.FrozenColumns,
        new Dictionary<int, double>(state.RowHeights),
        new Dictionary<int, double>(state.ColumnWidths));

    private enum SpreadsheetResizeAxis
    {
        None,
        Row,
        Column
    }
}
