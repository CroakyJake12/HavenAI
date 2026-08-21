using Haven.Application;
using Haven.Core;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Write;

internal sealed partial class WriteDocumentSurface
{
    private enum WriteObjectDragMode { None, Move, Resize }

    private Guid? _activeTableCellId;
    private int _tableCellCaret;
    private WriteObjectDragMode _objectDragMode;
    private HavenPoint _objectDragStart;
    private HavenPoint _objectDragCurrent;
    private double _startMediaWidth;
    private double _startMediaHeight;
    private DocumentVectorTransform? _startVectorTransform;

    public Guid? ActiveTableCellId => _activeTableCellId;

    internal int LaidOutPageCount
    {
        get { BuildLayout(); return _pages.Count; }
    }

    internal bool TryGetBlockBounds(Guid blockId, out HavenRect bounds)
    {
        BuildLayout();
        var layout = _layouts.FirstOrDefault(value => value.Block.Id == blockId);
        if (layout is null) { bounds = default; return false; }
        bounds = DisplayRectLocal(layout);
        return true;
    }

    internal bool TryGetTableCellBounds(Guid cellId, out HavenRect bounds)
    {
        BuildLayout();
        foreach (var layout in _layouts.Where(value => value.Block.Kind == NotesBlockKind.Table))
        {
            var hit = BuildTableCellLayouts(layout).FirstOrDefault(value => value.Cell.Id == cellId);
            if (hit is not null) { bounds = hit.Rect; return true; }
        }
        bounds = default;
        return false;
    }

    private bool TryPointerPressSpecial(HavenPointerInput input)
    {
        if (_editor is null) return false;
        if (HitTableCell(input.LocalPosition) is { } tableHit)
        {
            _editor.SelectBlock(tableHit.Block.Id);
            _activeTableCellId = tableHit.Cell.Id;
            _tableCellCaret = CaretForCellPoint(tableHit, input.LocalPosition);
            _pointerSelecting = false;
            _objectDragMode = WriteObjectDragMode.None;
            Accessibility.Description = $"Editing table cell {tableHit.Row + 1}, {tableHit.Column + 1}. Tab moves between cells.";
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
            return true;
        }

        var objectLayout = _layouts.LastOrDefault(layout =>
            layout.Block.Kind is NotesBlockKind.Image or NotesBlockKind.Audio or NotesBlockKind.Video or NotesBlockKind.Shape &&
            DisplayRectLocal(layout).Contains(input.LocalPosition));
        if (objectLayout is null) return false;

        _editor.SelectBlock(objectLayout.Block.Id);
        _activeTableCellId = null;
        _pointerSelecting = false;
        _objectDragStart = input.LocalPosition;
        _objectDragCurrent = input.LocalPosition;
        _objectDragMode = ResizeHandle(DisplayRectLocal(objectLayout)).Contains(input.LocalPosition) ? WriteObjectDragMode.Resize : WriteObjectDragMode.Move;
        if (objectLayout.Block.Media is { } media)
        {
            _startMediaWidth = media.Width;
            _startMediaHeight = media.Height;
        }
        _startVectorTransform = objectLayout.Block.VectorShape is { } shape ? CloneTransform(shape.Transform) : null;
        Accessibility.Description = _objectDragMode == WriteObjectDragMode.Resize
            ? "Resizing selected document object. Hold Shift while dragging to rotate."
            : "Moving selected document object.";
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
        return true;
    }

    private bool TryPointerMoveSpecial(HavenPointerInput input)
    {
        if (_objectDragMode == WriteObjectDragMode.None || _editor?.SelectedBlock is null) return false;
        _objectDragCurrent = input.LocalPosition;
        Invalidate();
        return true;
    }

    private bool TryPointerReleaseSpecial(HavenPointerInput input)
    {
        if (_objectDragMode == WriteObjectDragMode.None || _editor?.SelectedBlock is not { } block) return false;
        _objectDragCurrent = input.LocalPosition;
        var dx = (_objectDragCurrent.X - _objectDragStart.X) / Math.Max(.001, _zoom);
        var dy = (_objectDragCurrent.Y - _objectDragStart.Y) / Math.Max(.001, _zoom);
        var changed = false;

        if (block.Media is not null)
        {
            if (_objectDragMode == WriteObjectDragMode.Resize)
                changed = input.Modifiers.HasFlag(HavenKeyModifiers.Shift)
                    ? _editor.RotateSelectedMedia(dx * .35)
                    : _editor.ResizeSelectedMedia(_startMediaWidth + dx, _startMediaHeight + dy);
            else if (Math.Abs(dy) >= 24)
                changed = _editor.MoveSelected(dy < 0 ? -1 : 1);
        }
        else if (block.VectorShape is not null && _startVectorTransform is { } start)
        {
            var next = CloneTransform(start);
            if (_objectDragMode == WriteObjectDragMode.Resize)
            {
                if (input.Modifiers.HasFlag(HavenKeyModifiers.Shift)) next.RotationDegrees += dx * .35;
                else
                {
                    next.ScaleX = ClampScale(start.ScaleX * (1 + dx / 220));
                    next.ScaleY = ClampScale(start.ScaleY * (1 + dy / 160));
                }
            }
            else
            {
                next.TranslateX = start.TranslateX + dx;
                next.TranslateY = start.TranslateY + dy;
            }
            changed = _editor.UpdateSelectedCustomShape(shapeEditor => shapeEditor.SetTransform(next));
        }

        _objectDragMode = WriteObjectDragMode.None;
        _startVectorTransform = null;
        Accessibility.Description = "Document editor. Select text, table cells, images or shapes to edit them directly.";
        if (changed) InvalidateDocument(); else Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool TryTableTextInput(string? text)
    {
        if (_editor is null || ActiveTableCell() is not { } cell || string.IsNullOrEmpty(text)) return false;
        var caret = Math.Clamp(_tableCellCaret, 0, cell.Text.Length);
        var next = cell.Text.Insert(caret, text);
        _editor.UpdateTableCell(cell.Id, next);
        _tableCellCaret = caret + text.Length;
        InvalidateDocument();
        return true;
    }

    private bool TryTableKeyDown(HavenKey key, HavenInputModifiers modifiers)
    {
        if (_editor is null || ActiveTableCell() is not { } cell || modifiers.Control) return false;
        switch (key)
        {
            case HavenKey.Left: _tableCellCaret = Math.Max(0, _tableCellCaret - 1); Invalidate(); return true;
            case HavenKey.Right: _tableCellCaret = Math.Min(cell.Text.Length, _tableCellCaret + 1); Invalidate(); return true;
            case HavenKey.Home: _tableCellCaret = 0; Invalidate(); return true;
            case HavenKey.End: _tableCellCaret = cell.Text.Length; Invalidate(); return true;
            case HavenKey.Backspace:
                if (_tableCellCaret <= 0) return true;
                _editor.UpdateTableCell(cell.Id, cell.Text.Remove(_tableCellCaret - 1, 1));
                _tableCellCaret--; InvalidateDocument(); return true;
            case HavenKey.Delete:
                if (_tableCellCaret >= cell.Text.Length) return true;
                _editor.UpdateTableCell(cell.Id, cell.Text.Remove(_tableCellCaret, 1));
                InvalidateDocument(); return true;
            case HavenKey.Enter: return TryTableTextInput("\n");
            case HavenKey.Tab: return MoveTableCell(modifiers.Shift ? -1 : 1);
            case HavenKey.Up: return MoveTableCellVertical(-1);
            case HavenKey.Down: return MoveTableCellVertical(1);
            default: return false;
        }
    }

    private bool MoveTableCell(int delta)
    {
        if (_editor?.SelectedBlock?.Table is not { } table || _activeTableCellId is null) return false;
        var cells = table.Rows.SelectMany(row => row.Cells).ToArray();
        var index = Array.FindIndex(cells, cell => cell.Id == _activeTableCellId);
        if (index < 0 || cells.Length == 0) return false;
        var next = Math.Clamp(index + delta, 0, cells.Length - 1);
        _activeTableCellId = cells[next].Id;
        _tableCellCaret = Math.Min(_tableCellCaret, cells[next].Text.Length);
        SelectionChanged?.Invoke(this, EventArgs.Empty); Invalidate();
        return true;
    }

    private bool MoveTableCellVertical(int deltaRow)
    {
        if (_editor?.SelectedBlock?.Table is not { } table || _activeTableCellId is null) return false;
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var cellIndex = row.Cells.FindIndex(cell => cell.Id == _activeTableCellId);
            if (cellIndex < 0) continue;
            var targetRow = Math.Clamp(rowIndex + deltaRow, 0, table.Rows.Count - 1);
            var target = table.Rows[targetRow].Cells[Math.Min(cellIndex, table.Rows[targetRow].Cells.Count - 1)];
            _activeTableCellId = target.Id;
            _tableCellCaret = Math.Min(_tableCellCaret, target.Text.Length);
            SelectionChanged?.Invoke(this, EventArgs.Empty); Invalidate();
            return true;
        }
        return false;
    }

    private NotesTableCell? ActiveTableCell()
    {
        if (_activeTableCellId is not { } id || _editor?.SelectedBlock?.Table is not { } table) return null;
        return table.Rows.SelectMany(row => row.Cells).FirstOrDefault(cell => cell.Id == id);
    }

    private TableCellLayout? HitTableCell(HavenPoint point)
    {
        foreach (var layout in _layouts.Where(value => value.Block.Kind == NotesBlockKind.Table))
        {
            var hit = BuildTableCellLayouts(layout).LastOrDefault(value => value.Rect.Contains(point));
            if (hit is not null) return hit;
        }
        return null;
    }

    private IReadOnlyList<TableCellLayout> BuildTableCellLayouts(BlockLayout layout)
    {
        if (layout.Block.Table is not { Rows.Count: > 0 } table) return Array.Empty<TableCellLayout>();
        var columns = Math.Max(1, table.Rows.Max(row => row.Cells.Sum(cell => Math.Max(1, cell.ColumnSpan))));
        var unit = layout.ContentRect.Width / columns;
        var rowHeight = 42 * _zoom;
        var result = new List<TableCellLayout>();
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var column = 0;
            foreach (var cell in table.Rows[rowIndex].Cells)
            {
                var span = Math.Clamp(cell.ColumnSpan, 1, columns - Math.Min(column, columns - 1));
                var rect = new HavenRect(layout.ContentRect.X + column * unit, layout.ContentRect.Y + rowIndex * rowHeight, unit * span, rowHeight * Math.Max(1, cell.RowSpan));
                result.Add(new TableCellLayout(layout.Block, cell, rowIndex, column, rect));
                column += span;
            }
        }
        return result;
    }

    private int CaretForCellPoint(TableCellLayout hit, HavenPoint point)
    {
        var relative = Math.Max(0, point.X - hit.Rect.X - 6);
        var charWidth = Math.Max(5, 6.2 * _zoom);
        return Math.Clamp((int)Math.Round(relative / charWidth), 0, hit.Cell.Text.Length);
    }

    private HavenRect DisplayRectLocal(BlockLayout layout)
    {
        var rect = layout.ContentRect;
        if (layout.Block.Media is { } media)
        {
            rect = new HavenRect(rect.X, rect.Y, Math.Clamp(media.Width * _zoom, 80 * _zoom, rect.Width), Math.Clamp(media.Height * _zoom, 80 * _zoom, Math.Max(80 * _zoom, layout.ContentRect.Height)));
        }
        else if (layout.Block.VectorShape is { } shape)
        {
            var transform = shape.Transform;
            rect = new HavenRect(rect.X + transform.TranslateX * _zoom, rect.Y + transform.TranslateY * _zoom, Math.Max(60 * _zoom, rect.Width * Math.Abs(transform.ScaleX)), Math.Max(50 * _zoom, rect.Height * Math.Abs(transform.ScaleY)));
        }

        if (_objectDragMode != WriteObjectDragMode.None && _editor?.SelectedBlockId == layout.Block.Id)
        {
            var dx = _objectDragCurrent.X - _objectDragStart.X;
            var dy = _objectDragCurrent.Y - _objectDragStart.Y;
            if (_objectDragMode == WriteObjectDragMode.Move) rect = new HavenRect(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);
            else if (_objectDragMode == WriteObjectDragMode.Resize) rect = new HavenRect(rect.X, rect.Y, Math.Max(40, rect.Width + dx), Math.Max(40, rect.Height + dy));
        }
        return rect;
    }

    private void DrawObjectHandles(HavenDrawingContext context, BlockLayout layout, double opacity)
    {
        if (layout.Block.Kind is not (NotesBlockKind.Image or NotesBlockKind.Audio or NotesBlockKind.Video or NotesBlockKind.Shape)) return;
        var local = DisplayRectLocal(layout);
        var handle = Absolute(ResizeHandle(local));
        context.Add(new HavenFillRoundedRectCommand(handle, new HavenSolidBrush(255, 57, 110, 220), 2, opacity));
        context.Add(new HavenStrokeRoundedRectCommand(handle, new HavenPen(new HavenSolidBrush(255, 255, 255, 255), 1), 2, opacity));
        if (layout.Block.Media is { } media)
        {
            var info = $"{media.Width:0}×{media.Height:0} · {media.Rotation:0.#}° · crop {media.CropLeft:P0}/{media.CropTop:P0}/{media.CropRight:P0}/{media.CropBottom:P0}";
            var rect = Absolute(local);
            context.Add(new HavenTextCommand(new HavenRect(rect.X, Math.Max(Bounds.Y, rect.Y - 21), rect.Width, 18), new HavenTextLayout(info, "Montserrat", 9 * _zoom, 500, rect.Width), new HavenSolidBrush(255, 57, 73, 96), opacity));
        }
    }

    private HavenRect ResizeHandle(HavenRect rect) => new(rect.Right - 7, rect.Bottom - 7, 14, 14);

    private static DocumentVectorTransform CloneTransform(DocumentVectorTransform source) => new()
    {
        TranslateX = source.TranslateX, TranslateY = source.TranslateY, ScaleX = source.ScaleX, ScaleY = source.ScaleY,
        RotationDegrees = source.RotationDegrees, OriginX = source.OriginX, OriginY = source.OriginY
    };

    private static double ClampScale(double value)
    {
        if (!double.IsFinite(value)) return 1;
        var sign = value < 0 ? -1 : 1;
        return sign * Math.Clamp(Math.Abs(value), .1, 10);
    }

    private sealed record TableCellLayout(NotesBlock Block, NotesTableCell Cell, int Row, int Column, HavenRect Rect);
}
