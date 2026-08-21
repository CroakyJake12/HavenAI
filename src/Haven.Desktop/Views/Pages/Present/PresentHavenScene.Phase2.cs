using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Present;

internal sealed partial class PresentHavenScene
{
    public event EventHandler? ImportRequested;
    public event EventHandler? PresentRequested;
    public event EventHandler? UndoRequested;
    public event EventHandler? RedoRequested;
    public event EventHandler? DuplicateSlideRequested;
    public event EventHandler? MoveSlideEarlierRequested;
    public event EventHandler? MoveSlideLaterRequested;
    public event EventHandler? AddTextRequested;
    public event EventHandler? AddShapeRequested;
    public event EventHandler? CopyRequested;
    public event EventHandler? PasteRequested;
    public event EventHandler? DeleteObjectRequested;
    public event EventHandler? GroupRequested;
    public event EventHandler? UngroupRequested;
    public event EventHandler? BringFrontRequested;
    public event EventHandler? SendBackRequested;
    public event EventHandler? BoldRequested;
    public event EventHandler? ItalicRequested;
    public event EventHandler? MoveObjectLeftRequested;
    public event EventHandler? MoveObjectRightRequested;
    public event EventHandler? MoveObjectUpRequested;
    public event EventHandler? MoveObjectDownRequested;
    public event EventHandler? GrowObjectRequested;
    public event EventHandler? ShrinkObjectRequested;
    public event EventHandler? RotateLeftRequested;
    public event EventHandler? RotateRightRequested;
    public event EventHandler? AlignLeftRequested;
    public event EventHandler? AlignCenterRequested;
    public event EventHandler? AlignTopRequested;
    public event EventHandler? AlignMiddleRequested;
    public event Action<int>? SlideSelected;
    public event Action<Guid>? ObjectSelectionToggled;
    public event Action<Guid?>? CanvasSelectionRequested;
    public event Action<double, double>? CanvasMoveSelectionRequested;
    public event Action<Guid, Guid, PresentVectorHandleKind, double, double>? CanvasVectorHandleMoveRequested;

    public HavenButton ImportButton { get; private set; } = null!;
    public HavenButton PresentButton { get; private set; } = null!;
    public HavenButton UndoButton { get; private set; } = null!;
    public HavenButton RedoButton { get; private set; } = null!;
    public HavenButton DuplicateSlideButton { get; private set; } = null!;
    public HavenButton MoveSlideEarlierButton { get; private set; } = null!;
    public HavenButton MoveSlideLaterButton { get; private set; } = null!;
    public Container SlideNavigator { get; private set; } = null!;
    public Container ObjectNavigator { get; private set; } = null!;
    public Container ObjectToolbar { get; private set; } = null!;
    public Container TransformToolbar { get; private set; } = null!;
    public HavenText InspectorText { get; private set; } = null!;

    private HavenButton[] _phase2Buttons = [];

    private void BuildPhase2Controls()
    {
        ImportButton = NewButton("Present.Deck.Import", "Import .pptx");
        PresentButton = NewButton("Present.Deck.Present", "Present");
        DeckToolbar.Add(ImportButton);
        DeckToolbar.Add(PresentButton);

        UndoButton = NewButton("Present.Edit.Undo", "Undo");
        RedoButton = NewButton("Present.Edit.Redo", "Redo");
        DuplicateSlideButton = NewButton("Present.Slide.Duplicate", "Duplicate slide");
        MoveSlideEarlierButton = NewButton("Present.Slide.MoveEarlier", "Move earlier");
        MoveSlideLaterButton = NewButton("Present.Slide.MoveLater", "Move later");
        SlideToolbar.Add(UndoButton);
        SlideToolbar.Add(RedoButton);
        SlideToolbar.Add(DuplicateSlideButton);
        SlideToolbar.Add(MoveSlideEarlierButton);
        SlideToolbar.Add(MoveSlideLaterButton);

        EditorHost.Add(Caption("Slides"));
        SlideNavigator = NewToolbar("Present.Slides.Navigator", 0);
        SlideNavigator.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        EditorHost.Add(SlideNavigator);

        EditorHost.Add(Caption("Objects · select multiple objects to group or align"));
        ObjectNavigator = NewToolbar("Present.Objects.Navigator", 0);
        ObjectNavigator.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        EditorHost.Add(ObjectNavigator);

        ObjectToolbar = NewToolbar("Present.Objects.Toolbar", 0);
        var addText = NewButton("Present.Object.AddText", "Text box");
        var addShape = NewButton("Present.Object.AddShape", "Shape");
        var copy = NewButton("Present.Object.Copy", "Copy");
        var paste = NewButton("Present.Object.Paste", "Paste");
        var delete = NewButton("Present.Object.Delete", "Delete object");
        var group = NewButton("Present.Object.Group", "Group");
        var ungroup = NewButton("Present.Object.Ungroup", "Ungroup");
        var front = NewButton("Present.Object.Front", "Bring front");
        var back = NewButton("Present.Object.Back", "Send back");
        var bold = NewButton("Present.Object.Bold", "Bold");
        var italic = NewButton("Present.Object.Italic", "Italic");
        foreach (var button in new[] { addText, addShape, copy, paste, delete, group, ungroup, front, back, bold, italic }) ObjectToolbar.Add(button);
        EditorHost.Add(ObjectToolbar);

        TransformToolbar = NewToolbar("Present.Transform.Toolbar", 0);
        var left = NewButton("Present.Object.Left", "←");
        var right = NewButton("Present.Object.Right", "→");
        var up = NewButton("Present.Object.Up", "↑");
        var down = NewButton("Present.Object.Down", "↓");
        var grow = NewButton("Present.Object.Grow", "Grow");
        var shrink = NewButton("Present.Object.Shrink", "Shrink");
        var rotateLeft = NewButton("Present.Object.RotateLeft", "Rotate −15°");
        var rotateRight = NewButton("Present.Object.RotateRight", "Rotate +15°");
        var alignLeft = NewButton("Present.Object.AlignLeft", "Align left");
        var alignCenter = NewButton("Present.Object.AlignCenter", "Align centre");
        var alignTop = NewButton("Present.Object.AlignTop", "Align top");
        var alignMiddle = NewButton("Present.Object.AlignMiddle", "Align middle");
        foreach (var button in new[] { left, right, up, down, grow, shrink, rotateLeft, rotateRight, alignLeft, alignCenter, alignTop, alignMiddle }) TransformToolbar.Add(button);
        EditorHost.Add(TransformToolbar);

        InspectorText = new HavenText { Name = "Present.Inspector", Level = TextLevel.Caption };
        InspectorText.SetValue(HavenProperties.Foreground, "TextSecondary");
        EditorHost.Add(InspectorText);

        SlideCanvas.SelectionRequested += elementId => CanvasSelectionRequested?.Invoke(elementId);
        SlideCanvas.MoveSelectionRequested += (deltaX, deltaY) => CanvasMoveSelectionRequested?.Invoke(deltaX, deltaY);
        SlideCanvas.VectorHandleMoveRequested += (elementId, nodeId, kind, x, y) => CanvasVectorHandleMoveRequested?.Invoke(elementId, nodeId, kind, x, y);

        ImportButton.Invoked += (_, _) => ImportRequested?.Invoke(this, EventArgs.Empty);
        PresentButton.Invoked += (_, _) => PresentRequested?.Invoke(this, EventArgs.Empty);
        UndoButton.Invoked += (_, _) => UndoRequested?.Invoke(this, EventArgs.Empty);
        RedoButton.Invoked += (_, _) => RedoRequested?.Invoke(this, EventArgs.Empty);
        DuplicateSlideButton.Invoked += (_, _) => DuplicateSlideRequested?.Invoke(this, EventArgs.Empty);
        MoveSlideEarlierButton.Invoked += (_, _) => MoveSlideEarlierRequested?.Invoke(this, EventArgs.Empty);
        MoveSlideLaterButton.Invoked += (_, _) => MoveSlideLaterRequested?.Invoke(this, EventArgs.Empty);
        addText.Invoked += (_, _) => AddTextRequested?.Invoke(this, EventArgs.Empty);
        addShape.Invoked += (_, _) => AddShapeRequested?.Invoke(this, EventArgs.Empty);
        copy.Invoked += (_, _) => CopyRequested?.Invoke(this, EventArgs.Empty);
        paste.Invoked += (_, _) => PasteRequested?.Invoke(this, EventArgs.Empty);
        delete.Invoked += (_, _) => DeleteObjectRequested?.Invoke(this, EventArgs.Empty);
        group.Invoked += (_, _) => GroupRequested?.Invoke(this, EventArgs.Empty);
        ungroup.Invoked += (_, _) => UngroupRequested?.Invoke(this, EventArgs.Empty);
        front.Invoked += (_, _) => BringFrontRequested?.Invoke(this, EventArgs.Empty);
        back.Invoked += (_, _) => SendBackRequested?.Invoke(this, EventArgs.Empty);
        bold.Invoked += (_, _) => BoldRequested?.Invoke(this, EventArgs.Empty);
        italic.Invoked += (_, _) => ItalicRequested?.Invoke(this, EventArgs.Empty);
        left.Invoked += (_, _) => MoveObjectLeftRequested?.Invoke(this, EventArgs.Empty);
        right.Invoked += (_, _) => MoveObjectRightRequested?.Invoke(this, EventArgs.Empty);
        up.Invoked += (_, _) => MoveObjectUpRequested?.Invoke(this, EventArgs.Empty);
        down.Invoked += (_, _) => MoveObjectDownRequested?.Invoke(this, EventArgs.Empty);
        grow.Invoked += (_, _) => GrowObjectRequested?.Invoke(this, EventArgs.Empty);
        shrink.Invoked += (_, _) => ShrinkObjectRequested?.Invoke(this, EventArgs.Empty);
        rotateLeft.Invoked += (_, _) => RotateLeftRequested?.Invoke(this, EventArgs.Empty);
        rotateRight.Invoked += (_, _) => RotateRightRequested?.Invoke(this, EventArgs.Empty);
        alignLeft.Invoked += (_, _) => AlignLeftRequested?.Invoke(this, EventArgs.Empty);
        alignCenter.Invoked += (_, _) => AlignCenterRequested?.Invoke(this, EventArgs.Empty);
        alignTop.Invoked += (_, _) => AlignTopRequested?.Invoke(this, EventArgs.Empty);
        alignMiddle.Invoked += (_, _) => AlignMiddleRequested?.Invoke(this, EventArgs.Empty);

        _phase2Buttons =
        [
            ImportButton, PresentButton, UndoButton, RedoButton, DuplicateSlideButton, MoveSlideEarlierButton, MoveSlideLaterButton,
            addText, addShape, copy, paste, delete, group, ungroup, front, back, bold, italic,
            left, right, up, down, grow, shrink, rotateLeft, rotateRight, alignLeft, alignCenter, alignTop, alignMiddle
        ];
    }

    public void SetPhase2Document(
        PresentDocument document, int slideIndex, IReadOnlyCollection<Guid> selectedElementIds, bool canUndo, bool canRedo)
    {
        ArgumentNullException.ThrowIfNull(document);
        selectedElementIds ??= Array.Empty<Guid>();
        document.Normalize();
        slideIndex = Math.Clamp(slideIndex, 0, document.Slides.Count - 1);
        var slide = document.Slides[slideIndex];
        var selected = selectedElementIds.ToHashSet();
        SlideCanvas.SetSlide(document, slide, selected);

        UndoButton.SetValue(HavenProperties.Enabled, canUndo);
        RedoButton.SetValue(HavenProperties.Enabled, canRedo);
        MoveSlideEarlierButton.SetValue(HavenProperties.Enabled, slideIndex > 0);
        MoveSlideLaterButton.SetValue(HavenProperties.Enabled, slideIndex < document.Slides.Count - 1);

        ClearChildren(SlideNavigator);
        for (var index = 0; index < document.Slides.Count; index++)
        {
            var capturedIndex = index;
            var item = document.Slides[index];
            var button = NewButton($"Present.Thumbnail.{item.Id:N}", $"{index + 1} · {DisplayTitle(item.Title)}");
            button.Accessibility.AccessibleName = $"Slide {index + 1}: {DisplayTitle(item.Title)}";
            button.SetState(HavenElementState.Selected, index == slideIndex);
            button.Invoked += (_, _) => SlideSelected?.Invoke(capturedIndex);
            SlideNavigator.Add(button);
        }

        ClearChildren(ObjectNavigator);
        foreach (var element in slide.Elements.OrderBy(item => item.Order))
        {
            var capturedId = element.Id;
            var label = element.Kind switch
            {
                PresentElementKind.Text => string.IsNullOrWhiteSpace(element.Text) ? "Text box" : TrimLabel(element.Text),
                PresentElementKind.Image => "Image · " + TrimLabel(element.AlternativeText),
                PresentElementKind.Media => "Media · " + TrimLabel(element.AlternativeText),
                PresentElementKind.Shape => "Shape · " + (string.IsNullOrWhiteSpace(element.ShapeType) ? "rect" : element.ShapeType),
                PresentElementKind.Group => "Group",
                PresentElementKind.GenUi => "Interactive Haven UI",
                _ => element.Kind.ToString()
            };
            var button = NewButton($"Present.Object.{element.Id:N}", label);
            button.Accessibility.AccessibleName = $"{element.Kind} object: {label}";
            button.SetState(HavenElementState.Selected, selected.Contains(element.Id));
            button.Invoked += (_, _) => ObjectSelectionToggled?.Invoke(capturedId);
            ObjectNavigator.Add(button);
        }

        var selectedElements = slide.Elements.Where(element => selected.Contains(element.Id)).ToArray();
        var selectedDescription = selectedElements.Length == 0
            ? "No object selected"
            : selectedElements.Length == 1
                ? $"1 selected · {selectedElements[0].Kind} · x {selectedElements[0].X:0.###}, y {selectedElements[0].Y:0.###}, {selectedElements[0].Width:0.###} × {selectedElements[0].Height:0.###} · {selectedElements[0].RotationDegrees:0.#}°"
                : $"{selectedElements.Length} objects selected";
        InspectorText.Content =
            $"{document.SlideSize.WidthInches:0.##} × {document.SlideSize.HeightInches:0.##} in · {document.Theme.Name} · {selectedDescription} · transition {slide.Transition.Kind} · {slide.Animations.Count} animation cue(s)";
    }

    public void SetPhase2Busy(bool busy)
    {
        foreach (var button in _phase2Buttons)
        {
            if (ReferenceEquals(button, UndoButton) || ReferenceEquals(button, RedoButton) || ReferenceEquals(button, MoveSlideEarlierButton) || ReferenceEquals(button, MoveSlideLaterButton)) continue;
            button.SetValue(HavenProperties.Enabled, !busy);
        }
    }

    private static void ClearChildren(Container container)
    {
        foreach (var child in container.Children.ToArray()) container.Remove(child);
    }

    private static string DisplayTitle(string? value) => string.IsNullOrWhiteSpace(value) ? "Untitled slide" : value.Trim();

    private static string TrimLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Untitled";
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 30 ? normalized : normalized[..27] + "…";
    }
}
