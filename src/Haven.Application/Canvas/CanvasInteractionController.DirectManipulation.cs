using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public enum CanvasAlignment { Left, HorizontalCenter, Right, Top, VerticalCenter, Bottom }
public enum CanvasDistribution { Horizontal, Vertical }
public readonly record struct CanvasBounds(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

public sealed partial class CanvasInteractionController
{
    private static readonly JsonSerializerOptions ClipboardJson = new(JsonSerializerDefaults.Web);
    private readonly HashSet<Guid> _selectedObjectIds = [];
    private List<NotesCanvasObject> _objectClipboard = [];

    public IReadOnlyCollection<Guid> SelectedObjectIds => SelectionIds().ToArray();
    public IReadOnlyList<NotesCanvasObject> SelectedObjects
    {
        get
        {
            var ids = SelectionIds();
            return Board.Objects.Where(value => ids.Contains(value.Id)).OrderBy(value => value.ZIndex).ToArray();
        }
    }

    public void SetSelection(IEnumerable<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var valid = Board.Objects.Where(value => value.Kind != NotesCanvasObjectKind.Connector).Select(value => value.Id).ToHashSet();
        _selectedObjectIds.Clear();
        foreach (var id in ids.Distinct()) if (valid.Contains(id)) _selectedObjectIds.Add(id);
        SelectedObjectId = _selectedObjectIds.LastOrDefault();
        if (SelectedObjectId == Guid.Empty) SelectedObjectId = null;
    }

    public void ToggleSelection(Guid id)
    {
        if (Board.Objects.All(value => value.Id != id || value.Kind == NotesCanvasObjectKind.Connector)) return;
        if (!_selectedObjectIds.Add(id)) _selectedObjectIds.Remove(id);
        SelectedObjectId = _selectedObjectIds.LastOrDefault();
        if (SelectedObjectId == Guid.Empty) SelectedObjectId = null;
    }

    public void ClearSelection()
    {
        _selectedObjectIds.Clear();
        SelectedObjectId = null;
    }

    public NotesCanvasObject? HitObjectAtViewport(double viewportX, double viewportY)
    {
        var point = ToCanvas(new CanvasPointerSample(viewportX, viewportY));
        return HitTest(point.X, point.Y);
    }

    public IReadOnlyCollection<Guid> SelectViewportRectangle(double x, double y, double width, double height, bool additive = false)
    {
        var first = ToCanvas(new CanvasPointerSample(x, y));
        var second = ToCanvas(new CanvasPointerSample(x + width, y + height));
        var rect = NormalizeRect(first.X, first.Y, second.X, second.Y);
        var hits = Board.Objects
            .Where(value => value.Kind != NotesCanvasObjectKind.Connector && Intersects(rect, RotatedBounds(value)))
            .Select(value => value.Id)
            .ToArray();
        if (additive) SetSelection(SelectionIds().Concat(hits));
        else SetSelection(hits);
        return SelectedObjectIds;
    }

    public CanvasBounds? SelectionBounds()
    {
        var selected = SelectedObjects;
        if (selected.Count == 0) return null;
        var bounds = selected.Select(RotatedBounds).ToArray();
        var left = bounds.Min(value => value.X);
        var top = bounds.Min(value => value.Y);
        var right = bounds.Max(value => value.Right);
        var bottom = bounds.Max(value => value.Bottom);
        return new CanvasBounds(left, top, right - left, bottom - top);
    }

    public bool TranslateSelection(double deltaX, double deltaY, bool snap = true)
    {
        var selected = SelectedObjects.Where(value => !value.Locked).ToArray();
        if (selected.Length == 0 || (Math.Abs(deltaX) < .001 && Math.Abs(deltaY) < .001)) return false;
        History.Capture(Board);
        foreach (var value in selected) NotesCanvasOperations.Move(value, value.X + deltaX, value.Y + deltaY, snap ? GridSize : 0);
        return true;
    }

    public bool TransformSelection(double deltaX, double deltaY, double deltaWidth, double deltaHeight, double deltaRotation)
    {
        var selected = SelectedObjects.Where(value => !value.Locked).ToArray();
        var bounds = SelectionBounds();
        if (selected.Length == 0 || bounds is null) return false;
        var original = bounds.Value;
        var newWidth = Math.Max(8, original.Width + deltaWidth);
        var newHeight = Math.Max(8, original.Height + deltaHeight);
        var newX = original.X + deltaX;
        var newY = original.Y + deltaY;
        var scaleX = original.Width <= .001 ? 1 : newWidth / original.Width;
        var scaleY = original.Height <= .001 ? 1 : newHeight / original.Height;
        History.Capture(Board);
        foreach (var value in selected)
        {
            var x = newX + (value.X - original.X) * scaleX;
            var y = newY + (value.Y - original.Y) * scaleY;
            NotesCanvasOperations.Move(value, x, y, GridSize);
            NotesCanvasOperations.Resize(value, Math.Max(8, value.Width * scaleX), Math.Max(8, value.Height * scaleY), GridSize);
            if (Math.Abs(deltaRotation) > .001) NotesCanvasOperations.Rotate(value, value.Rotation + deltaRotation);
        }
        return true;
    }

    public bool AlignSelection(CanvasAlignment alignment)
    {
        var selected = SelectedObjects.Where(value => !value.Locked).ToArray();
        if (selected.Length < 2) return false;
        var bounds = SelectionBounds()!.Value;
        History.Capture(Board);
        foreach (var value in selected)
        {
            var x = value.X; var y = value.Y;
            switch (alignment)
            {
                case CanvasAlignment.Left: x = bounds.X; break;
                case CanvasAlignment.HorizontalCenter: x = bounds.X + (bounds.Width - value.Width) / 2; break;
                case CanvasAlignment.Right: x = bounds.Right - value.Width; break;
                case CanvasAlignment.Top: y = bounds.Y; break;
                case CanvasAlignment.VerticalCenter: y = bounds.Y + (bounds.Height - value.Height) / 2; break;
                case CanvasAlignment.Bottom: y = bounds.Bottom - value.Height; break;
            }
            NotesCanvasOperations.Move(value, x, y, GridSize);
        }
        return true;
    }

    public bool DistributeSelection(CanvasDistribution distribution)
    {
        var selected = SelectedObjects.Where(value => !value.Locked).ToArray();
        if (selected.Length < 3) return false;
        History.Capture(Board);
        if (distribution == CanvasDistribution.Horizontal)
        {
            var ordered = selected.OrderBy(value => value.X).ToArray();
            var left = ordered[0].X; var right = ordered[^1].X + ordered[^1].Width;
            var totalWidth = ordered.Sum(value => value.Width);
            var gap = (right - left - totalWidth) / (ordered.Length - 1);
            var x = left;
            foreach (var value in ordered) { NotesCanvasOperations.Move(value, x, value.Y, GridSize); x += value.Width + gap; }
        }
        else
        {
            var ordered = selected.OrderBy(value => value.Y).ToArray();
            var top = ordered[0].Y; var bottom = ordered[^1].Y + ordered[^1].Height;
            var totalHeight = ordered.Sum(value => value.Height);
            var gap = (bottom - top - totalHeight) / (ordered.Length - 1);
            var y = top;
            foreach (var value in ordered) { NotesCanvasOperations.Move(value, value.X, y, GridSize); y += value.Height + gap; }
        }
        return true;
    }

    public bool ClearBoard()
    {
        if (Board.Objects.Count == 0 && Board.Strokes.Count == 0) return false;
        History.Capture(Board);
        Board.Objects.Clear();
        Board.Strokes.Clear();
        Board.GhostLayers.Clear();
        ClearSelection();
        return true;
    }

    public bool DeleteSelection()
    {
        var ids = SelectionIds();
        if (ids.Count == 0) return false;
        var removable = Board.Objects.Where(value => ids.Contains(value.Id) && !value.Locked).Select(value => value.Id).ToHashSet();
        if (removable.Count == 0) return false;
        History.Capture(Board);
        Board.Objects.RemoveAll(value => removable.Contains(value.Id) || (value.Kind == NotesCanvasObjectKind.Connector && ((value.FromObjectId is { } from && removable.Contains(from)) || (value.ToObjectId is { } to && removable.Contains(to)))));
        ClearSelection();
        return true;
    }

    public bool CopySelection()
    {
        var selected = SelectedObjects.Where(value => value.Kind != NotesCanvasObjectKind.Connector).ToArray();
        if (selected.Length == 0) return false;
        _objectClipboard = selected.Select(CloneObject).ToList();
        return true;
    }

    public bool PasteSelection(double offset = 24)
    {
        if (_objectClipboard.Count == 0) return false;
        History.Capture(Board);
        var map = new Dictionary<Guid, Guid>();
        var pasted = new List<NotesCanvasObject>();
        foreach (var source in _objectClipboard)
        {
            var value = CloneObject(source);
            var oldId = value.Id; value.Id = Guid.NewGuid(); map[oldId] = value.Id;
            value.X += offset; value.Y += offset; value.GroupId = null;
            value.ZIndex = Board.Objects.Count == 0 ? 0 : Board.Objects.Max(candidate => candidate.ZIndex) + 1 + pasted.Count;
            pasted.Add(value);
        }
        Board.Objects.AddRange(pasted);
        SetSelection(pasted.Select(value => value.Id));
        _objectClipboard = pasted.Select(CloneObject).ToList();
        return true;
    }

    public bool DuplicateSelection()
    {
        if (!CopySelection()) return false;
        return PasteSelection();
    }

    public Guid? GroupSelection()
    {
        var selected = SelectedObjects.Where(value => !value.Locked && value.Kind != NotesCanvasObjectKind.Connector).ToArray();
        if (selected.Length < 2) return null;
        History.Capture(Board);
        var group = NotesCanvasOperations.Group(selected);
        SetSelection(selected.Select(value => value.Id));
        return group;
    }

    public bool UngroupSelection()
    {
        var groups = SelectedObjects.Where(value => value.GroupId is not null).Select(value => value.GroupId!.Value).Distinct().ToArray();
        if (groups.Length == 0) return false;
        History.Capture(Board);
        foreach (var group in groups) NotesCanvasOperations.Ungroup(Board.Objects.Where(value => value.GroupId == group).ToArray());
        return true;
    }

    public bool BringSelectionToFront()
    {
        var selected = SelectedObjects.Where(value => !value.Locked).ToArray();
        if (selected.Length == 0) return false;
        History.Capture(Board);
        var top = Board.Objects.Count == 0 ? 0 : Board.Objects.Max(value => value.ZIndex);
        for (var index = 0; index < selected.Length; index++) selected[index].ZIndex = top + index + 1;
        return true;
    }

    public bool SendSelectionToBack()
    {
        var selected = SelectedObjects.Where(value => !value.Locked).ToArray();
        if (selected.Length == 0) return false;
        History.Capture(Board);
        var bottom = Board.Objects.Count == 0 ? 0 : Board.Objects.Min(value => value.ZIndex);
        for (var index = 0; index < selected.Length; index++) selected[index].ZIndex = bottom - selected.Length + index;
        return true;
    }

    public NotesCanvasObject AddObjectAt(NotesCanvasObjectKind kind, double x, double y, double width, double height, string? text = null, string? styleJson = null)
    {
        History.Capture(Board);
        var value = new NotesCanvasObject
        {
            Kind = kind, Text = text ?? kind.ToString(), X = x, Y = y, Width = Math.Max(8, width), Height = Math.Max(8, height),
            StyleJson = styleJson ?? string.Empty, ZIndex = Board.Objects.Count == 0 ? 0 : Board.Objects.Max(candidate => candidate.ZIndex) + 1
        };
        Board.Objects.Add(value);
        SetSelection([value.Id]);
        return value;
    }

    public (double X, double Y) ViewportToCanvas(double viewportX, double viewportY) => ToCanvas(new CanvasPointerSample(viewportX, viewportY));
    public (double X, double Y) CanvasToViewport(double canvasX, double canvasY) => (canvasX * Board.Zoom + Board.OffsetX, canvasY * Board.Zoom + Board.OffsetY);

    private HashSet<Guid> SelectionIds()
    {
        _selectedObjectIds.RemoveWhere(id => Board.Objects.All(value => value.Id != id));
        if (_selectedObjectIds.Count == 0 && SelectedObjectId is { } primary && Board.Objects.Any(value => value.Id == primary)) _selectedObjectIds.Add(primary);
        return _selectedObjectIds;
    }

    private static NotesCanvasObject CloneObject(NotesCanvasObject value) =>
        JsonSerializer.Deserialize<NotesCanvasObject>(JsonSerializer.Serialize(value, ClipboardJson), ClipboardJson) ?? throw new InvalidDataException("Canvas object could not be cloned.");

    private static bool ContainsRotated(NotesCanvasObject value, double x, double y)
    {
        if (Math.Abs(value.Rotation) < .001) return x >= value.X && x <= value.X + value.Width && y >= value.Y && y <= value.Y + value.Height;
        var centerX = value.X + value.Width / 2; var centerY = value.Y + value.Height / 2;
        var radians = -value.Rotation * Math.PI / 180; var cos = Math.Cos(radians); var sin = Math.Sin(radians);
        var dx = x - centerX; var dy = y - centerY;
        var localX = dx * cos - dy * sin + centerX; var localY = dx * sin + dy * cos + centerY;
        return localX >= value.X && localX <= value.X + value.Width && localY >= value.Y && localY <= value.Y + value.Height;
    }

    private static CanvasBounds RotatedBounds(NotesCanvasObject value)
    {
        if (Math.Abs(value.Rotation) < .001) return new CanvasBounds(value.X, value.Y, value.Width, value.Height);
        var centerX = value.X + value.Width / 2; var centerY = value.Y + value.Height / 2; var radians = value.Rotation * Math.PI / 180; var cos = Math.Cos(radians); var sin = Math.Sin(radians);
        var corners = new[] { (value.X, value.Y), (value.X + value.Width, value.Y), (value.X + value.Width, value.Y + value.Height), (value.X, value.Y + value.Height) }
            .Select(point => { var dx = point.Item1 - centerX; var dy = point.Item2 - centerY; return (X: dx * cos - dy * sin + centerX, Y: dx * sin + dy * cos + centerY); }).ToArray();
        var left = corners.Min(point => point.X); var top = corners.Min(point => point.Y); var right = corners.Max(point => point.X); var bottom = corners.Max(point => point.Y);
        return new CanvasBounds(left, top, right - left, bottom - top);
    }

    private static CanvasBounds NormalizeRect(double x1, double y1, double x2, double y2) => new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
    private static bool Intersects(CanvasBounds left, CanvasBounds right) => left.X <= right.Right && left.Right >= right.X && left.Y <= right.Bottom && left.Bottom >= right.Y;
}
