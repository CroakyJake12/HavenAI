using Haven.Core;

namespace Haven.Application;

public sealed class DataSheetDrawingEditor
{
    public DataSheetDrawingEditor(DataSheet sheet) => Sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));

    public DataSheet Sheet { get; }
    public Guid? SelectedDrawingId { get; private set; }
    public DataDrawingObject? SelectedDrawing => SelectedDrawingId is { } id ? Sheet.Drawings.FirstOrDefault(value => value.Id == id) : null;

    public DataDrawingObject AddCustomShape(DocumentVectorShape shape, Guid? gallerySourceId = null)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var inserted = DocumentVectorShapes.CloneForInsertion(shape, gallerySourceId);
        var index = Sheet.Drawings.Count;
        var drawing = new DataDrawingObject
        {
            Kind = DataDrawingKind.CustomShape,
            Name = inserted.Name,
            X = 36 + (index % 6) * 24,
            Y = 36 + (index % 6) * 24,
            Width = 240,
            Height = 160,
            ZIndex = Sheet.Drawings.Count == 0 ? 0 : Sheet.Drawings.Max(value => value.ZIndex) + 1,
            VectorShape = inserted
        };
        drawing.Normalize();
        Sheet.Drawings.Add(drawing);
        SelectedDrawingId = drawing.Id;
        return drawing;
    }

    public bool Select(Guid drawingId)
    {
        if (!Sheet.Drawings.Any(value => value.Id == drawingId)) return false;
        SelectedDrawingId = drawingId;
        return true;
    }

    public bool SelectAt(int index)
    {
        if (index < 0 || index >= Sheet.Drawings.Count) return false;
        SelectedDrawingId = Sheet.Drawings[index].Id;
        return true;
    }

    public bool RemoveSelected()
    {
        var selected = SelectedDrawing;
        if (selected is null || selected.Locked) return false;
        var index = Sheet.Drawings.IndexOf(selected);
        Sheet.Drawings.RemoveAt(index);
        SelectedDrawingId = Sheet.Drawings.Count == 0 ? null : Sheet.Drawings[Math.Clamp(index, 0, Sheet.Drawings.Count - 1)].Id;
        return true;
    }

    public bool MoveSelected(double x, double y)
    {
        var selected = SelectedDrawing;
        if (selected is null || selected.Locked || !double.IsFinite(x) || !double.IsFinite(y)) return false;
        if (Math.Abs(selected.X - x) < .001 && Math.Abs(selected.Y - y) < .001) return false;
        selected.X = x; selected.Y = y; return true;
    }

    public bool ResizeSelected(double width, double height)
    {
        var selected = SelectedDrawing;
        if (selected is null || selected.Locked || !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return false;
        selected.Width = Math.Clamp(width, 1, 100000); selected.Height = Math.Clamp(height, 1, 100000); return true;
    }

    public bool RotateSelected(double degrees)
    {
        var selected = SelectedDrawing;
        if (selected is null || selected.Locked || !double.IsFinite(degrees)) return false;
        selected.Rotation = ((degrees % 360) + 360) % 360; return true;
    }

    public bool UpdateSelectedCustomShape(Action<DocumentVectorShapeEditor> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var selected = SelectedDrawing;
        if (selected is null || selected.Locked || selected.VectorShape is null) return false;
        var vectorEditor = new DocumentVectorShapeEditor(DocumentVectorShapes.Clone(selected.VectorShape));
        edit(vectorEditor);
        selected.VectorShape = DocumentVectorShapes.Clone(vectorEditor.Shape);
        selected.Name = selected.VectorShape.Name;
        return true;
    }
}
