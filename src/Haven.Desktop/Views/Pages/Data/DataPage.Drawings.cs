using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Data;

public sealed partial class DataPage
{
    private DataSheetDrawingEditor? DrawingEditorForCurrent()
    {
        var sheet = CurrentSheet;
        if (sheet is null) return null;
        var editor = new DataSheetDrawingEditor(sheet);
        if (sheet.Drawings.Count > 0) editor.SelectAt(Math.Clamp(_drawingIndex, 0, sheet.Drawings.Count - 1));
        return editor;
    }

    private void OnAddShapeRequested(object? sender, EventArgs e)
    {
        var editor = DrawingEditorForCurrent();
        if (editor is null) return;
        editor.AddCustomShape(DocumentVectorShapes.CreateEditableStarter());
        _drawingIndex = editor.Sheet.Drawings.Count - 1;
        MarkDirty();
        RenderCurrent();
        _bus.Fire("Data.Drawing.Added");
    }

    private void OnPreviousDrawingRequested(object? sender, EventArgs e) => MoveDrawingSelection(-1);
    private void OnNextDrawingRequested(object? sender, EventArgs e) => MoveDrawingSelection(1);

    private void MoveDrawingSelection(int offset)
    {
        var sheet = CurrentSheet;
        if (sheet is null || sheet.Drawings.Count == 0) return;
        _drawingIndex = (_drawingIndex + offset + sheet.Drawings.Count) % sheet.Drawings.Count;
        RenderCurrent();
    }

    private void OnRotateDrawingRequested(object? sender, EventArgs e)
    {
        var editor = DrawingEditorForCurrent();
        var selected = editor?.SelectedDrawing;
        if (editor is null || selected is null) return;
        if (!editor.RotateSelected(selected.Rotation + 15)) return;
        MarkDirty();
        RenderCurrent();
        _bus.Fire("Data.Drawing.Changed");
    }

    private void OnDeleteDrawingRequested(object? sender, EventArgs e)
    {
        var editor = DrawingEditorForCurrent();
        if (editor is null || !editor.RemoveSelected()) return;
        _drawingIndex = Math.Clamp(_drawingIndex, 0, Math.Max(0, editor.Sheet.Drawings.Count - 1));
        MarkDirty();
        RenderCurrent();
        _bus.Fire("Data.Drawing.Deleted");
    }
}
