using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public enum PresentAlignment
{
    Left = 0,
    HorizontalCenter = 1,
    Right = 2,
    Top = 3,
    VerticalCenter = 4,
    Bottom = 5
}

public enum PresentDistribution
{
    Horizontal = 0,
    Vertical = 1
}

public sealed record PresentSelection(
    Guid SlideId,
    IReadOnlyList<Guid> ElementIds);

public sealed record PresentSnapGuide(
    string Axis,
    double Position,
    string Source);

public sealed record PresentSnapResult(
    double X,
    double Y,
    IReadOnlyList<PresentSnapGuide> Guides);

public sealed partial class PresentEditor
{
    private const int HistoryLimit = 100;
    private static readonly JsonSerializerOptions SnapshotOptions = new(JsonSerializerDefaults.Web);
    private readonly List<string> _undo = [];
    private readonly List<string> _redo = [];
    private readonly TimeProvider _timeProvider;
    private Guid _selectedSlideId;
    private readonly List<Guid> _selectedElementIds = [];

    public PresentEditor(PresentDocument document, TimeProvider? timeProvider = null)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        _timeProvider = timeProvider ?? TimeProvider.System;
        Document.Normalize();
        _selectedSlideId = Document.Slides[0].Id;
    }

    public event EventHandler? Changed;
    public PresentDocument Document { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public PresentSelection Selection => new(_selectedSlideId, _selectedElementIds.ToArray());

    public PresentSlide SelectedSlide =>
        Document.Slides.FirstOrDefault(slide => slide.Id == _selectedSlideId) ?? Document.Slides[0];

    public IReadOnlyList<PresentElement> SelectedElements
    {
        get
        {
            var ids = _selectedElementIds.ToHashSet();
            return SelectedSlide.Elements.Where(element => ids.Contains(element.Id)).ToArray();
        }
    }

    public bool SelectSlide(Guid slideId)
    {
        if (Document.Slides.All(slide => slide.Id != slideId)) return false;
        if (_selectedSlideId == slideId) return true;
        _selectedSlideId = slideId;
        _selectedElementIds.Clear();
        return true;
    }

    public void SelectElements(IEnumerable<Guid> elementIds)
    {
        ArgumentNullException.ThrowIfNull(elementIds);
        var validIds = SelectedSlide.Elements.Select(element => element.Id).ToHashSet();
        _selectedElementIds.Clear();
        _selectedElementIds.AddRange(elementIds.Where(validIds.Contains).Distinct());
    }

    public bool SetDocumentTitle(string? title)
    {
        var value = title ?? string.Empty;
        if (string.Equals(Document.Title, value, StringComparison.Ordinal)) return false;
        Mutate(() => Document.Title = value);
        return true;
    }

    public bool SetSlideTitle(Guid slideId, string? title)
    {
        var slide = RequireSlide(slideId);
        var value = title ?? string.Empty;
        if (string.Equals(slide.Title, value, StringComparison.Ordinal)) return false;
        Mutate(() => slide.Title = value);
        return true;
    }

    public bool SetSpeakerNotes(Guid slideId, string? notes)
    {
        var slide = RequireSlide(slideId);
        var value = notes ?? string.Empty;
        if (string.Equals(slide.SpeakerNotes, value, StringComparison.Ordinal)) return false;
        Mutate(() => slide.SpeakerNotes = value);
        return true;
    }

    public bool SetElementText(Guid slideId, Guid elementId, string? text)
    {
        var slide = RequireSlide(slideId);
        var element = slide.Elements.FirstOrDefault(item => item.Id == elementId)
            ?? throw new ArgumentOutOfRangeException(nameof(elementId));
        var value = text ?? string.Empty;
        if (string.Equals(element.Text, value, StringComparison.Ordinal)) return false;
        Mutate(() => element.Text = value);
        return true;
    }

    public bool SetSelectedTextStyle(PresentTextStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        var selected = EditableSelection().Where(element => element.Kind == PresentElementKind.Text).ToArray();
        if (selected.Length == 0) return false;
        Mutate(() =>
        {
            foreach (var element in selected)
            {
                element.TextStyle = new PresentTextStyle
                {
                    FontFamily = style.FontFamily,
                    FontSizePoints = style.FontSizePoints,
                    Bold = style.Bold,
                    Italic = style.Italic,
                    Underline = style.Underline,
                    Color = style.Color,
                    HorizontalAlignment = style.HorizontalAlignment,
                    VerticalAlignment = style.VerticalAlignment
                };
            }
        });
        return true;
    }

    public bool SetSelectedElementStyle(PresentElementStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        var selected = EditableSelection();
        if (selected.Count == 0) return false;
        Mutate(() =>
        {
            foreach (var element in selected)
            {
                element.Style = new PresentElementStyle
                {
                    FillColor = style.FillColor,
                    StrokeColor = style.StrokeColor,
                    StrokeWidth = style.StrokeWidth,
                    CornerRadius = style.CornerRadius,
                    Shadow = style.Shadow
                };
            }
        });
        return true;
    }

    public PresentSlide AddSlide(Guid? afterSlideId = null, Guid? layoutId = null)
    {
        PresentSlide? created = null;
        Mutate(() =>
        {
            var insertionIndex = ResolveInsertionIndex(afterSlideId);
            created = PresentSlide.Create(insertionIndex);
            created.LayoutId = ResolveLayoutId(layoutId);
            created.Title = $"Slide {Document.Slides.Count + 1}";
            Document.Slides.Insert(insertionIndex, created);
            _selectedSlideId = created.Id;
            _selectedElementIds.Clear();
        });
        return created!;
    }

    public PresentSlide DuplicateSlide(Guid slideId)
    {
        var source = RequireSlide(slideId);
        PresentSlide? duplicate = null;
        Mutate(() =>
        {
            var sourceIndex = Document.Slides.IndexOf(source);
            duplicate = CloneSlide(source);
            duplicate.Title = string.IsNullOrWhiteSpace(source.Title)
                ? "Slide copy"
                : source.Title + " copy";
            Document.Slides.Insert(sourceIndex + 1, duplicate);
            _selectedSlideId = duplicate.Id;
            _selectedElementIds.Clear();
        });
        return duplicate!;
    }

    public bool DeleteSlide(Guid slideId)
    {
        var index = Document.Slides.FindIndex(slide => slide.Id == slideId);
        if (index < 0) return false;
        Mutate(() =>
        {
            if (Document.Slides.Count == 1)
            {
                var replacement = PresentSlide.Create(0);
                replacement.LayoutId = ResolveLayoutId(null);
                Document.Slides[0] = replacement;
                _selectedSlideId = replacement.Id;
            }
            else
            {
                Document.Slides.RemoveAt(index);
                _selectedSlideId = Document.Slides[Math.Min(index, Document.Slides.Count - 1)].Id;
            }
            _selectedElementIds.Clear();
        });
        return true;
    }

    public bool MoveSlide(Guid slideId, int targetIndex)
    {
        var currentIndex = Document.Slides.FindIndex(slide => slide.Id == slideId);
        if (currentIndex < 0) return false;
        targetIndex = Math.Clamp(targetIndex, 0, Document.Slides.Count - 1);
        if (targetIndex == currentIndex) return false;
        Mutate(() =>
        {
            var slide = Document.Slides[currentIndex];
            Document.Slides.RemoveAt(currentIndex);
            Document.Slides.Insert(targetIndex, slide);
        });
        return true;
    }

    public PresentElement AddText(Guid slideId, string? text = null) => AddElement(slideId, new PresentElement
    {
        Kind = PresentElementKind.Text,
        Text = text ?? string.Empty,
        X = 0.12, Y = 0.18, Width = 0.36, Height = 0.18
    });

    public PresentElement AddShape(Guid slideId, string shapeType = "rect") => AddElement(slideId, new PresentElement
    {
        Kind = PresentElementKind.Shape,
        ShapeType = string.IsNullOrWhiteSpace(shapeType) ? "rect" : shapeType.Trim(),
        X = 0.18, Y = 0.22, Width = 0.24, Height = 0.20
    });

    public PresentElement AddCustomShape(Guid slideId, DocumentVectorShape shape, Guid? gallerySourceId = null)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var inserted = DocumentVectorShapes.CloneForInsertion(shape, gallerySourceId);
        return AddElement(slideId, new PresentElement
        {
            Kind = PresentElementKind.Shape,
            ShapeType = "custom-vector",
            VectorShape = inserted,
            AlternativeText = inserted.AccessibilityDescription,
            X = 0.18, Y = 0.22, Width = 0.24, Height = 0.20
        });
    }

    public bool UpdateCustomShape(Guid slideId, Guid elementId, Action<DocumentVectorShapeEditor> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var slide = RequireSlide(slideId);
        var element = slide.Elements.FirstOrDefault(item => item.Id == elementId && item.Kind == PresentElementKind.Shape && item.VectorShape is not null);
        if (element?.VectorShape is null || element.Locked) return false;
        var working = DocumentVectorShapes.Clone(element.VectorShape);
        var vectorEditor = new DocumentVectorShapeEditor(working);
        update(vectorEditor);
        var updated = DocumentVectorShapes.Clone(vectorEditor.Shape);
        Mutate(() => { element.VectorShape = updated; element.ShapeType = "custom-vector"; element.AlternativeText = updated.AccessibilityDescription; });
        return true;
    }

    public PresentElement AddImage(Guid slideId, string assetId, string? alternativeText = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        return AddElement(slideId, new PresentElement
        {
            Kind = PresentElementKind.Image,
            AssetId = assetId.Trim(),
            AlternativeText = alternativeText?.Trim() ?? string.Empty,
            X = 0.18, Y = 0.18, Width = 0.40, Height = 0.40
        });
    }

    public PresentElement AddMedia(Guid slideId, string assetId, string mimeType, string? alternativeText = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        return AddElement(slideId, new PresentElement
        {
            Kind = PresentElementKind.Media,
            AssetId = assetId.Trim(),
            AlternativeText = alternativeText?.Trim() ?? string.Empty,
            Media = new PresentMediaSettings { MimeType = mimeType?.Trim() ?? string.Empty },
            X = 0.15, Y = 0.16, Width = 0.50, Height = 0.48
        });
    }

    public bool RemoveSelectedElements()
    {
        var selectedIds = _selectedElementIds.ToHashSet();
        if (selectedIds.Count == 0) return false;
        Mutate(() =>
        {
            var slide = SelectedSlide;
            var removedGroupIds = slide.Elements
                .Where(element => selectedIds.Contains(element.Id) && element.Kind == PresentElementKind.Group)
                .Select(element => element.Id)
                .ToHashSet();
            foreach (var element in slide.Elements)
            {
                if (element.ParentGroupId is { } groupId && removedGroupIds.Contains(groupId))
                    element.ParentGroupId = null;
            }
            slide.Elements.RemoveAll(element => selectedIds.Contains(element.Id));
            slide.Animations.RemoveAll(cue => selectedIds.Contains(cue.TargetElementId));
            _selectedElementIds.Clear();
        });
        return true;
    }

    public bool MoveSelection(double deltaX, double deltaY, bool snap = false, double snapTolerance = 0.008)
    {
        var elements = EditableSelection();
        if (elements.Count == 0) return false;
        Mutate(() =>
        {
            foreach (var element in elements)
            {
                var proposedX = element.X + deltaX;
                var proposedY = element.Y + deltaY;
                if (snap)
                {
                    var result = PresentSnapEngine.Snap(SelectedSlide, element.Id, proposedX, proposedY, snapTolerance);
                    proposedX = result.X;
                    proposedY = result.Y;
                }
                element.X = Math.Clamp(proposedX, 0, Math.Max(0, 1 - element.Width));
                element.Y = Math.Clamp(proposedY, 0, Math.Max(0, 1 - element.Height));
            }
        });
        return true;
    }

    public bool ResizeSelection(double deltaWidth, double deltaHeight)
    {
        var elements = EditableSelection();
        if (elements.Count == 0) return false;
        Mutate(() =>
        {
            foreach (var element in elements)
            {
                element.Width = Math.Clamp(element.Width + deltaWidth, 0.01, 1 - element.X);
                element.Height = Math.Clamp(element.Height + deltaHeight, 0.01, 1 - element.Y);
            }
        });
        return true;
    }

    public bool RotateSelection(double deltaDegrees)
    {
        var elements = EditableSelection();
        if (elements.Count == 0 || !double.IsFinite(deltaDegrees)) return false;
        Mutate(() =>
        {
            foreach (var element in elements) element.RotationDegrees += deltaDegrees;
        });
        return true;
    }

    public bool TransformSelection(double deltaX, double deltaY, double deltaWidth, double deltaHeight, double deltaRotationDegrees)
    {
        var elements = EditableSelection();
        if (elements.Count == 0 || !double.IsFinite(deltaX) || !double.IsFinite(deltaY)
            || !double.IsFinite(deltaWidth) || !double.IsFinite(deltaHeight) || !double.IsFinite(deltaRotationDegrees)) return false;
        Mutate(() =>
        {
            foreach (var element in elements)
            {
                var x = Math.Clamp(element.X + deltaX, 0, .99);
                var y = Math.Clamp(element.Y + deltaY, 0, .99);
                var width = Math.Clamp(element.Width + deltaWidth, .01, 1 - x);
                var height = Math.Clamp(element.Height + deltaHeight, .01, 1 - y);
                element.X = Math.Min(x, 1 - width);
                element.Y = Math.Min(y, 1 - height);
                element.Width = width;
                element.Height = height;
                element.RotationDegrees += deltaRotationDegrees;
            }
        });
        return true;
    }

    public bool BringForward() => MoveSelectionInZOrder(1);
    public bool SendBackward() => MoveSelectionInZOrder(-1);

    public bool BringToFront()
    {
        var slide = SelectedSlide;
        var selected = _selectedElementIds.ToHashSet();
        if (selected.Count == 0) return false;
        Mutate(() =>
        {
            var moving = slide.Elements.Where(element => selected.Contains(element.Id)).ToArray();
            slide.Elements.RemoveAll(element => selected.Contains(element.Id));
            slide.Elements.AddRange(moving);
        });
        return true;
    }

    public bool SendToBack()
    {
        var slide = SelectedSlide;
        var selected = _selectedElementIds.ToHashSet();
        if (selected.Count == 0) return false;
        Mutate(() =>
        {
            var moving = slide.Elements.Where(element => selected.Contains(element.Id)).ToArray();
            slide.Elements.RemoveAll(element => selected.Contains(element.Id));
            slide.Elements.InsertRange(0, moving);
        });
        return true;
    }

    public Guid? GroupSelection()
    {
        var elements = EditableSelection().Where(element => element.ParentGroupId is null).ToArray();
        if (elements.Length < 2) return null;
        PresentElement? group = null;
        Mutate(() =>
        {
            var slide = SelectedSlide;
            group = PresentElement.CreateGroup(elements);
            slide.Elements.Add(group);
            _selectedElementIds.Clear();
            _selectedElementIds.Add(group.Id);
        });
        return group!.Id;
    }

    public bool UngroupSelection()
    {
        var slide = SelectedSlide;
        var groupIds = _selectedElementIds
            .Where(id => slide.Elements.Any(element => element.Id == id && element.Kind == PresentElementKind.Group))
            .ToHashSet();
        if (groupIds.Count == 0) return false;
        Mutate(() =>
        {
            foreach (var groupId in groupIds)
            {
                var group = slide.Elements.First(element => element.Id == groupId);
                foreach (var child in slide.Elements.Where(element => element.ParentGroupId == groupId))
                    child.ParentGroupId = group.ParentGroupId;
            }
            slide.Elements.RemoveAll(element => groupIds.Contains(element.Id));
            _selectedElementIds.Clear();
        });
        return true;
    }

    public bool AlignSelection(PresentAlignment alignment)
    {
        var elements = EditableSelection();
        if (elements.Count < 2) return false;
        Mutate(() =>
        {
            var left = elements.Min(element => element.X);
            var right = elements.Max(element => element.X + element.Width);
            var top = elements.Min(element => element.Y);
            var bottom = elements.Max(element => element.Y + element.Height);
            var horizontalCenter = (left + right) / 2;
            var verticalCenter = (top + bottom) / 2;
            foreach (var element in elements)
            {
                switch (alignment)
                {
                    case PresentAlignment.Left: element.X = left; break;
                    case PresentAlignment.HorizontalCenter: element.X = horizontalCenter - element.Width / 2; break;
                    case PresentAlignment.Right: element.X = right - element.Width; break;
                    case PresentAlignment.Top: element.Y = top; break;
                    case PresentAlignment.VerticalCenter: element.Y = verticalCenter - element.Height / 2; break;
                    case PresentAlignment.Bottom: element.Y = bottom - element.Height; break;
                }
            }
        });
        return true;
    }

    public bool DistributeSelection(PresentDistribution distribution)
    {
        var elements = EditableSelection();
        if (elements.Count < 3) return false;
        Mutate(() =>
        {
            var ordered = distribution == PresentDistribution.Horizontal
                ? elements.OrderBy(element => element.X).ToArray()
                : elements.OrderBy(element => element.Y).ToArray();
            if (distribution == PresentDistribution.Horizontal)
            {
                var left = ordered[0].X;
                var right = ordered[^1].X + ordered[^1].Width;
                var occupied = ordered.Sum(element => element.Width);
                var gap = Math.Max(0, (right - left - occupied) / (ordered.Length - 1));
                var cursor = left;
                foreach (var element in ordered)
                {
                    element.X = cursor;
                    cursor += element.Width + gap;
                }
            }
            else
            {
                var top = ordered[0].Y;
                var bottom = ordered[^1].Y + ordered[^1].Height;
                var occupied = ordered.Sum(element => element.Height);
                var gap = Math.Max(0, (bottom - top - occupied) / (ordered.Length - 1));
                var cursor = top;
                foreach (var element in ordered)
                {
                    element.Y = cursor;
                    cursor += element.Height + gap;
                }
            }
        });
        return true;
    }

    public string CopySelection()
    {
        var selected = SelectedElements;
        return selected.Count == 0 ? string.Empty : JsonSerializer.Serialize(selected, SnapshotOptions);
    }

    public IReadOnlyList<Guid> Paste(string clipboardJson, Guid? slideId = null, double offset = 0.02)
    {
        if (string.IsNullOrWhiteSpace(clipboardJson)) return [];
        var source = JsonSerializer.Deserialize<List<PresentElement>>(clipboardJson, SnapshotOptions) ?? [];
        if (source.Count == 0) return [];
        var target = slideId is { } id ? RequireSlide(id) : SelectedSlide;
        var pastedIds = new List<Guid>();
        Mutate(() =>
        {
            var idMap = source.ToDictionary(element => element.Id, _ => Guid.NewGuid());
            var sourceIds = idMap.Keys.ToHashSet();
            foreach (var element in source)
            {
                var oldId = element.Id;
                element.Id = idMap[oldId];
                element.ParentGroupId = element.ParentGroupId is { } parent && sourceIds.Contains(parent)
                    ? idMap[parent]
                    : null;
                element.X = Math.Clamp(element.X + offset, 0, Math.Max(0, 1 - element.Width));
                element.Y = Math.Clamp(element.Y + offset, 0, Math.Max(0, 1 - element.Height));
                target.Elements.Add(element);
                pastedIds.Add(element.Id);
            }
            _selectedSlideId = target.Id;
            _selectedElementIds.Clear();
            _selectedElementIds.AddRange(pastedIds);
        });
        return pastedIds;
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var current = Snapshot(Document);
        var previous = Pop(_undo);
        Push(_redo, current);
        Restore(previous);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        var current = Snapshot(Document);
        var next = Pop(_redo);
        Push(_undo, current);
        Restore(next);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private PresentElement AddElement(Guid slideId, PresentElement element)
    {
        var slide = RequireSlide(slideId);
        Mutate(() =>
        {
            slide.Elements.Add(element);
            _selectedSlideId = slide.Id;
            _selectedElementIds.Clear();
            _selectedElementIds.Add(element.Id);
        });
        return element;
    }

    private bool MoveSelectionInZOrder(int delta)
    {
        var slide = SelectedSlide;
        var selected = _selectedElementIds.ToHashSet();
        if (selected.Count == 0) return false;
        var moved = false;
        Mutate(() =>
        {
            if (delta > 0)
            {
                for (var index = slide.Elements.Count - 2; index >= 0; index--)
                {
                    if (!selected.Contains(slide.Elements[index].Id) || selected.Contains(slide.Elements[index + 1].Id)) continue;
                    (slide.Elements[index], slide.Elements[index + 1]) = (slide.Elements[index + 1], slide.Elements[index]);
                    moved = true;
                }
            }
            else
            {
                for (var index = 1; index < slide.Elements.Count; index++)
                {
                    if (!selected.Contains(slide.Elements[index].Id) || selected.Contains(slide.Elements[index - 1].Id)) continue;
                    (slide.Elements[index], slide.Elements[index - 1]) = (slide.Elements[index - 1], slide.Elements[index]);
                    moved = true;
                }
            }
        });
        return moved;
    }

    private IReadOnlyList<PresentElement> EditableSelection() =>
        SelectedElements.Where(element => !element.Locked).ToArray();

    private int ResolveInsertionIndex(Guid? afterSlideId)
    {
        if (afterSlideId is null) return Document.Slides.Count;
        var index = Document.Slides.FindIndex(slide => slide.Id == afterSlideId.Value);
        return index < 0 ? Document.Slides.Count : index + 1;
    }

    private Guid ResolveLayoutId(Guid? requested)
    {
        if (requested is { } layoutId && Document.Layouts.Any(layout => layout.Id == layoutId)) return layoutId;
        return Document.Layouts[0].Id;
    }

    private PresentSlide RequireSlide(Guid slideId) =>
        Document.Slides.FirstOrDefault(slide => slide.Id == slideId)
        ?? throw new ArgumentOutOfRangeException(nameof(slideId), "The slide does not exist in this presentation.");

    private void Mutate(Action mutation)
    {
        if (_liveTextEditBefore is not null) CommitLiveTextEdit();
        var before = Snapshot(Document);
        mutation();
        Document.Normalize();
        var structuralAfter = Snapshot(Document);
        if (string.Equals(before, structuralAfter, StringComparison.Ordinal)) return;
        Push(_undo, before);
        _redo.Clear();
        Document.UpdatedAt = _timeProvider.GetUtcNow();
        EnsureSelectionIsValid();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Restore(string snapshot)
    {
        Document = JsonSerializer.Deserialize<PresentDocument>(snapshot, SnapshotOptions)
            ?? throw new InvalidDataException("The presentation history snapshot could not be restored.");
        Document.Normalize();
        Document.UpdatedAt = _timeProvider.GetUtcNow();
        EnsureSelectionIsValid();
    }

    private void EnsureSelectionIsValid()
    {
        if (Document.Slides.All(slide => slide.Id != _selectedSlideId)) _selectedSlideId = Document.Slides[0].Id;
        var validIds = SelectedSlide.Elements.Select(element => element.Id).ToHashSet();
        _selectedElementIds.RemoveAll(id => !validIds.Contains(id));
    }

    private static PresentSlide CloneSlide(PresentSlide source)
    {
        var json = JsonSerializer.Serialize(source, SnapshotOptions);
        var clone = JsonSerializer.Deserialize<PresentSlide>(json, SnapshotOptions)
            ?? throw new InvalidDataException("The slide could not be duplicated.");
        clone.Id = Guid.NewGuid();
        var idMap = clone.Elements.ToDictionary(element => element.Id, _ => Guid.NewGuid());
        foreach (var element in clone.Elements)
        {
            var oldId = element.Id;
            element.Id = idMap[oldId];
            if (element.ParentGroupId is { } parent && idMap.TryGetValue(parent, out var newParent))
                element.ParentGroupId = newParent;
            else if (element.ParentGroupId is not null)
                element.ParentGroupId = null;
        }
        foreach (var cue in clone.Animations)
        {
            cue.Id = Guid.NewGuid();
            if (idMap.TryGetValue(cue.TargetElementId, out var target)) cue.TargetElementId = target;
        }
        return clone;
    }

    private static string Snapshot(PresentDocument document) => JsonSerializer.Serialize(document, SnapshotOptions);

    private static string Pop(List<string> stack)
    {
        var index = stack.Count - 1;
        var value = stack[index];
        stack.RemoveAt(index);
        return value;
    }

    private static void Push(List<string> stack, string snapshot)
    {
        stack.Add(snapshot);
        if (stack.Count > HistoryLimit) stack.RemoveAt(0);
    }
}

public static class PresentSnapEngine
{
    private static readonly double[] CanvasGuides = [0, 0.5, 1];

    public static PresentSnapResult Snap(
        PresentSlide slide, Guid movingElementId, double proposedX, double proposedY, double tolerance = 0.008)
    {
        ArgumentNullException.ThrowIfNull(slide);
        tolerance = double.IsFinite(tolerance) ? Math.Clamp(tolerance, 0.0001, 0.1) : 0.008;
        var moving = slide.Elements.FirstOrDefault(element => element.Id == movingElementId)
            ?? throw new ArgumentOutOfRangeException(nameof(movingElementId));
        var xCandidates = new List<(double Position, string Source)>();
        var yCandidates = new List<(double Position, string Source)>();
        foreach (var guide in CanvasGuides)
        {
            xCandidates.Add((guide, guide == 0.5 ? "slide-center" : "slide-edge"));
            yCandidates.Add((guide, guide == 0.5 ? "slide-center" : "slide-edge"));
        }
        foreach (var element in slide.Elements.Where(element => element.Id != movingElementId && element.Visible))
        {
            xCandidates.Add((element.X, "object-left"));
            xCandidates.Add((element.X + element.Width / 2, "object-center"));
            xCandidates.Add((element.X + element.Width, "object-right"));
            yCandidates.Add((element.Y, "object-top"));
            yCandidates.Add((element.Y + element.Height / 2, "object-middle"));
            yCandidates.Add((element.Y + element.Height, "object-bottom"));
        }

        var guides = new List<PresentSnapGuide>();
        var x = SnapAxis(proposedX, moving.Width, xCandidates, tolerance, "x", guides);
        var y = SnapAxis(proposedY, moving.Height, yCandidates, tolerance, "y", guides);
        return new PresentSnapResult(x, y, guides);
    }

    private static double SnapAxis(
        double proposedStart, double size, IReadOnlyList<(double Position, string Source)> candidates,
        double tolerance, string axis, List<PresentSnapGuide> guides)
    {
        var points = new[]
        {
            (Offset: 0d, Position: proposedStart),
            (Offset: size / 2, Position: proposedStart + size / 2),
            (Offset: size, Position: proposedStart + size)
        };
        var bestDistance = double.MaxValue;
        var bestStart = proposedStart;
        (double Position, string Source)? best = null;
        foreach (var point in points)
        foreach (var candidate in candidates)
        {
            var distance = Math.Abs(point.Position - candidate.Position);
            if (distance > tolerance || distance >= bestDistance) continue;
            bestDistance = distance;
            bestStart = candidate.Position - point.Offset;
            best = candidate;
        }
        if (best is { } match) guides.Add(new PresentSnapGuide(axis, match.Position, match.Source));
        return bestStart;
    }
}
