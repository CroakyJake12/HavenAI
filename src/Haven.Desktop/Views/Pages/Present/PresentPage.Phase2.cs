using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Present;

public sealed partial class PresentPage
{
    private IPresentImportService? _importer;
    private PresentEditor? _editor;
    private string _objectClipboard = string.Empty;
    private PresentPlaybackSession? _playback;

    internal PresentEditor? Editor => _editor;
    internal PresentPlaybackSession? PlaybackSession => _playback;

    private void InitializePhase2(IPresentImportService? importer)
    {
        _importer = importer;
        _route.ImportRequested += OnImportRequested;
        _route.PresentRequested += OnPresentRequested;
        _route.UndoRequested += OnUndoRequested;
        _route.RedoRequested += OnRedoRequested;
        _route.DuplicateSlideRequested += OnDuplicateSlideRequested;
        _route.MoveSlideEarlierRequested += OnMoveSlideEarlierRequested;
        _route.MoveSlideLaterRequested += OnMoveSlideLaterRequested;
        _route.AddTextRequested += OnAddTextRequested;
        _route.AddShapeRequested += OnAddShapeRequested;
        _route.CopyRequested += OnCopyRequested;
        _route.PasteRequested += OnPasteRequested;
        _route.DeleteObjectRequested += OnDeleteObjectRequested;
        _route.GroupRequested += OnGroupRequested;
        _route.UngroupRequested += OnUngroupRequested;
        _route.BringFrontRequested += OnBringFrontRequested;
        _route.SendBackRequested += OnSendBackRequested;
        _route.BoldRequested += OnBoldRequested;
        _route.ItalicRequested += OnItalicRequested;
        _route.MoveObjectLeftRequested += OnMoveObjectLeftRequested;
        _route.MoveObjectRightRequested += OnMoveObjectRightRequested;
        _route.MoveObjectUpRequested += OnMoveObjectUpRequested;
        _route.MoveObjectDownRequested += OnMoveObjectDownRequested;
        _route.GrowObjectRequested += OnGrowObjectRequested;
        _route.ShrinkObjectRequested += OnShrinkObjectRequested;
        _route.RotateLeftRequested += OnRotateLeftRequested;
        _route.RotateRightRequested += OnRotateRightRequested;
        _route.AlignLeftRequested += OnAlignLeftRequested;
        _route.AlignCenterRequested += OnAlignCenterRequested;
        _route.AlignTopRequested += OnAlignTopRequested;
        _route.AlignMiddleRequested += OnAlignMiddleRequested;
        _route.SlideSelected += OnSlideSelected;
        _route.ObjectSelectionToggled += OnObjectSelectionToggled;
        _route.CanvasSelectionRequested += OnCanvasSelectionRequested;
        _route.CanvasMoveSelectionRequested += OnCanvasMoveSelectionRequested;
        _route.CanvasVectorHandleMoveRequested += OnCanvasVectorHandleMoveRequested;
    }

    private void AttachEditor(PresentDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_editor is not null) _editor.Changed -= OnEditorChanged;
        _editor = new PresentEditor(document);
        _slideIndex = Math.Clamp(_slideIndex, 0, document.Slides.Count - 1);
        if (_slideIndex > 0) _editor.SelectSlide(document.Slides[_slideIndex].Id);
        _editor.Changed += OnEditorChanged;
        _playback = null;
        _objectClipboard = string.Empty;
    }

    private void OnEditorChanged(object? sender, EventArgs e)
    {
        if (_editor is null) return;
        Document = _editor.Document;
        _slideIndex = IndexOfSlide(_editor.Selection.SlideId);
        _dirty = true;
        _route.SetStatus("Unsaved changes · autosave is on");
        RenderCurrent();
    }

    private int IndexOfSlide(Guid slideId)
    {
        if (Document is null) return 0;
        for (var index = 0; index < Document.Slides.Count; index++)
            if (Document.Slides[index].Id == slideId) return index;
        return Math.Clamp(_slideIndex, 0, Math.Max(0, Document.Slides.Count - 1));
    }

    private void EditDeckTitle(string value)
    {
        if (_editor is not null) _editor.SetDocumentTitle(value);
        else if (Document is not null && Document.Title != value) { Document.Title = value; MarkDirty(); RenderCurrent(); }
    }

    private void EditSlideTitle(string value)
    {
        var slide = CurrentSlide;
        if (slide is null) return;
        if (_editor is not null) _editor.SetSlideTitle(slide.Id, value);
        else if (slide.Title != value) { slide.Title = value; MarkDirty(); RenderCurrent(); }
    }

    private void EditBody(string value)
    {
        var slide = CurrentSlide;
        if (slide is null) return;
        var body = slide.GetOrCreateBodyText();
        if (_editor is not null) _editor.SetElementText(slide.Id, body.Id, value);
        else if (body.Text != value) { body.Text = value; MarkDirty(); RenderCurrent(); }
    }

    private void EditNotes(string value)
    {
        var slide = CurrentSlide;
        if (slide is null) return;
        if (_editor is not null) _editor.SetSpeakerNotes(slide.Id, value);
        else if (slide.SpeakerNotes != value) { slide.SpeakerNotes = value; MarkDirty(); RenderCurrent(); }
    }

    private void MoveSlideSelection(int offset)
    {
        if (Document is null || Document.Slides.Count <= 1) return;
        _slideIndex = (_slideIndex + offset + Document.Slides.Count) % Document.Slides.Count;
        _editor?.SelectSlide(Document.Slides[_slideIndex].Id);
        RenderCurrent();
    }

    private void AddSlideWithEditor()
    {
        if (Document is null) return;
        if (_editor is null) { AttachEditor(Document); }
        var created = _editor!.AddSlide(CurrentSlide?.Id);
        _slideIndex = IndexOfSlide(created.Id);
        _bus.Fire("Present.Slide.Added");
    }

    private void DeleteSlideWithEditor()
    {
        var slide = CurrentSlide;
        if (slide is null) return;
        if (_editor is null) AttachEditor(Document!);
        if (_editor!.DeleteSlide(slide.Id)) _bus.Fire("Present.Slide.Deleted");
    }

    private void RenderPhase2()
    {
        if (Document is null) return;
        if (_editor is null || !ReferenceEquals(_editor.Document, Document)) AttachEditor(Document);
        _route.SetPhase2Document(Document, _slideIndex, _editor!.Selection.ElementIds, _editor.CanUndo, _editor.CanRedo);
    }

    private async void OnImportRequested(object? sender, EventArgs e) =>
        await RunBusyAsync(PickImportAsync, "import this PowerPoint presentation");

    private void OnPresentRequested(object? sender, EventArgs e)
    {
        if (Document is null) return;
        _playback = new PresentPlaybackSession(Document);
        if (_slideIndex > 0) _playback.GoTo(_slideIndex);
        var frame = _playback.Frame;
        _route.SetStatus($"Presentation ready · slide {frame.SlideNumber} of {frame.SlideCount} · speaker notes and animation timing loaded");
        _bus.Fire("Present.Playback.Started");
    }

    internal bool AdvancePlayback()
    {
        if (_playback is null || !_playback.Advance()) return false;
        var frame = _playback.Frame;
        _route.SetStatus($"Presenting slide {frame.SlideNumber} of {frame.SlideCount} · {frame.Elapsed.ToString(@"mm\:ss")}");
        return true;
    }

    internal bool PreviousPlayback()
    {
        if (_playback is null || !_playback.Previous()) return false;
        var frame = _playback.Frame;
        _route.SetStatus($"Presenting slide {frame.SlideNumber} of {frame.SlideCount} · {frame.Elapsed.ToString(@"mm\:ss")}");
        return true;
    }

    private void OnUndoRequested(object? sender, EventArgs e) => _editor?.Undo();
    private void OnRedoRequested(object? sender, EventArgs e) => _editor?.Redo();

    private void OnDuplicateSlideRequested(object? sender, EventArgs e)
    {
        var slide = CurrentSlide;
        if (slide is null || _editor is null) return;
        var duplicate = _editor.DuplicateSlide(slide.Id);
        _slideIndex = IndexOfSlide(duplicate.Id);
        _bus.Fire("Present.Slide.Duplicated");
    }

    private void OnMoveSlideEarlierRequested(object? sender, EventArgs e) => MoveCurrentSlide(-1);
    private void OnMoveSlideLaterRequested(object? sender, EventArgs e) => MoveCurrentSlide(1);

    private void MoveCurrentSlide(int offset)
    {
        var slide = CurrentSlide;
        if (slide is null || _editor is null) return;
        var target = Math.Clamp(_slideIndex + offset, 0, Document!.Slides.Count - 1);
        if (_editor.MoveSlide(slide.Id, target)) _slideIndex = target;
    }

    private void OnAddTextRequested(object? sender, EventArgs e)
    {
        if (_editor is null) return;
        _editor.AddText(_editor.Selection.SlideId, "Text box");
        _bus.Fire("Present.Object.Added");
    }

    private void OnAddShapeRequested(object? sender, EventArgs e)
    {
        if (_editor is null) return;
        _editor.AddCustomShape(_editor.Selection.SlideId, DocumentVectorShapes.CreateEditableStarter());
        _bus.Fire("Present.Object.Added");
    }

    private void OnCopyRequested(object? sender, EventArgs e)
    {
        if (_editor is null) return;
        _objectClipboard = _editor.CopySelection();
        _route.SetStatus(string.IsNullOrEmpty(_objectClipboard) ? "Select an object to copy." : "Copied selected presentation object(s).");
    }

    private void OnPasteRequested(object? sender, EventArgs e)
    {
        if (_editor is null || string.IsNullOrWhiteSpace(_objectClipboard)) return;
        _editor.Paste(_objectClipboard);
        _bus.Fire("Present.Object.Pasted");
    }

    private void OnDeleteObjectRequested(object? sender, EventArgs e) => _editor?.RemoveSelectedElements();
    private void OnGroupRequested(object? sender, EventArgs e) => _editor?.GroupSelection();
    private void OnUngroupRequested(object? sender, EventArgs e) => _editor?.UngroupSelection();
    private void OnBringFrontRequested(object? sender, EventArgs e) => _editor?.BringToFront();
    private void OnSendBackRequested(object? sender, EventArgs e) => _editor?.SendToBack();
    private void OnBoldRequested(object? sender, EventArgs e) => ToggleSelectedTextStyle(toggleBold: true);
    private void OnItalicRequested(object? sender, EventArgs e) => ToggleSelectedTextStyle(toggleBold: false);

    private void ToggleSelectedTextStyle(bool toggleBold)
    {
        if (_editor is null) return;
        var text = _editor.SelectedElements.FirstOrDefault(element => element.Kind == PresentElementKind.Text);
        if (text is null) return;
        var source = text.TextStyle;
        _editor.SetSelectedTextStyle(new PresentTextStyle
        {
            FontFamily = source.FontFamily, FontSizePoints = source.FontSizePoints,
            Bold = toggleBold ? !source.Bold : source.Bold,
            Italic = toggleBold ? source.Italic : !source.Italic,
            Underline = source.Underline, Color = source.Color,
            HorizontalAlignment = source.HorizontalAlignment, VerticalAlignment = source.VerticalAlignment
        });
    }

    private void OnMoveObjectLeftRequested(object? sender, EventArgs e) => _editor?.MoveSelection(-0.01, 0, snap: true);
    private void OnMoveObjectRightRequested(object? sender, EventArgs e) => _editor?.MoveSelection(0.01, 0, snap: true);
    private void OnMoveObjectUpRequested(object? sender, EventArgs e) => _editor?.MoveSelection(0, -0.01, snap: true);
    private void OnMoveObjectDownRequested(object? sender, EventArgs e) => _editor?.MoveSelection(0, 0.01, snap: true);
    private void OnGrowObjectRequested(object? sender, EventArgs e) => _editor?.ResizeSelection(0.02, 0.02);
    private void OnShrinkObjectRequested(object? sender, EventArgs e) => _editor?.ResizeSelection(-0.02, -0.02);
    private void OnRotateLeftRequested(object? sender, EventArgs e) => _editor?.RotateSelection(-15);
    private void OnRotateRightRequested(object? sender, EventArgs e) => _editor?.RotateSelection(15);
    private void OnAlignLeftRequested(object? sender, EventArgs e) => _editor?.AlignSelection(PresentAlignment.Left);
    private void OnAlignCenterRequested(object? sender, EventArgs e) => _editor?.AlignSelection(PresentAlignment.HorizontalCenter);
    private void OnAlignTopRequested(object? sender, EventArgs e) => _editor?.AlignSelection(PresentAlignment.Top);
    private void OnAlignMiddleRequested(object? sender, EventArgs e) => _editor?.AlignSelection(PresentAlignment.VerticalCenter);

    private void OnSlideSelected(int index)
    {
        if (Document is null || _editor is null) return;
        _slideIndex = Math.Clamp(index, 0, Document.Slides.Count - 1);
        _editor.SelectSlide(Document.Slides[_slideIndex].Id);
        RenderCurrent();
    }

    private void OnObjectSelectionToggled(Guid elementId)
    {
        if (_editor is null) return;
        var selected = _editor.Selection.ElementIds.ToHashSet();
        if (!selected.Add(elementId)) selected.Remove(elementId);
        _editor.SelectElements(selected);
        RenderCurrent();
    }

    private void OnCanvasSelectionRequested(Guid? elementId)
    {
        if (_editor is null) return; _editor.SelectElements(elementId is { } id ? [id] : []); RenderCurrent();
    }

    private void OnCanvasMoveSelectionRequested(double deltaX, double deltaY)
    {
        if (_editor is null) return; _editor.MoveSelection(deltaX, deltaY, snap: true);
    }

    private void OnCanvasVectorHandleMoveRequested(Guid elementId, Guid nodeId, PresentVectorHandleKind kind, double x, double y)
    {
        if (_editor is null) return;
        _editor.UpdateCustomShape(_editor.Selection.SlideId, elementId, vectorEditor =>
        {
            if (kind == PresentVectorHandleKind.Node) vectorEditor.MoveNode(nodeId, x, y);
            else vectorEditor.MoveControlPoint(nodeId, kind == PresentVectorHandleKind.Control1 ? 1 : 2, x, y);
        });
    }

    internal async Task<bool> ImportFromPathAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (_importer is null) { _route.SetStatus("PPTX import service is unavailable."); return false; }
        if (Document is not null && _dirty && !await SaveAsync("Autosave before PPTX import", cancellationToken)) return false;
        try
        {
            var imported = await _importer.ImportAsync(sourcePath, cancellationToken);
            var saved = await _repository.SaveAsync(imported, "Imported PPTX", cancellationToken);
            imported.Version = saved.Version;
            await RefreshDocumentsAsync(cancellationToken);
            Document = imported; _deckIndex = IndexOfDocument(imported.Id); _slideIndex = 0; _dirty = false;
            AttachEditor(imported); RenderCurrent();
            _route.SetStatus("Imported " + Path.GetFileName(sourcePath) + " · " + _importer.Support.Description);
            _bus.Fire("Present.Document.Imported");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { _route.SetStatus("Couldn’t import this presentation: " + ex.Message); return false; }
    }

    private async Task PickImportAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null) { _route.SetStatus("Import isn’t available from this platform surface."); return; }
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import PowerPoint presentation", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PowerPoint presentation") { Patterns = ["*.pptx"] }]
        });
        if (files.Count == 0) return;
        var file = files[0]; var localPath = file.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath)) { await ImportFromPathAsync(localPath); return; }
        var temporary = Path.Combine(Path.GetTempPath(), $"haven-present-import-{Guid.NewGuid():N}.pptx");
        try
        {
            await using var source = await file.OpenReadAsync();
            await using (var destination = File.Create(temporary)) await source.CopyToAsync(destination);
            await ImportFromPathAsync(temporary);
        }
        finally { TryDeleteTemporary(temporary); }
    }

    private void DisposePhase2()
    {
        if (_editor is not null) _editor.Changed -= OnEditorChanged;
        _route.ImportRequested -= OnImportRequested;
        _route.PresentRequested -= OnPresentRequested;
        _route.UndoRequested -= OnUndoRequested;
        _route.RedoRequested -= OnRedoRequested;
        _route.DuplicateSlideRequested -= OnDuplicateSlideRequested;
        _route.MoveSlideEarlierRequested -= OnMoveSlideEarlierRequested;
        _route.MoveSlideLaterRequested -= OnMoveSlideLaterRequested;
        _route.AddTextRequested -= OnAddTextRequested; _route.AddShapeRequested -= OnAddShapeRequested;
        _route.CopyRequested -= OnCopyRequested; _route.PasteRequested -= OnPasteRequested; _route.DeleteObjectRequested -= OnDeleteObjectRequested;
        _route.GroupRequested -= OnGroupRequested; _route.UngroupRequested -= OnUngroupRequested;
        _route.BringFrontRequested -= OnBringFrontRequested; _route.SendBackRequested -= OnSendBackRequested;
        _route.BoldRequested -= OnBoldRequested; _route.ItalicRequested -= OnItalicRequested;
        _route.MoveObjectLeftRequested -= OnMoveObjectLeftRequested; _route.MoveObjectRightRequested -= OnMoveObjectRightRequested;
        _route.MoveObjectUpRequested -= OnMoveObjectUpRequested; _route.MoveObjectDownRequested -= OnMoveObjectDownRequested;
        _route.GrowObjectRequested -= OnGrowObjectRequested; _route.ShrinkObjectRequested -= OnShrinkObjectRequested;
        _route.RotateLeftRequested -= OnRotateLeftRequested; _route.RotateRightRequested -= OnRotateRightRequested;
        _route.AlignLeftRequested -= OnAlignLeftRequested; _route.AlignCenterRequested -= OnAlignCenterRequested;
        _route.AlignTopRequested -= OnAlignTopRequested; _route.AlignMiddleRequested -= OnAlignMiddleRequested;
        _route.SlideSelected -= OnSlideSelected; _route.ObjectSelectionToggled -= OnObjectSelectionToggled;
        _route.CanvasSelectionRequested -= OnCanvasSelectionRequested; _route.CanvasMoveSelectionRequested -= OnCanvasMoveSelectionRequested;
        _route.CanvasVectorHandleMoveRequested -= OnCanvasVectorHandleMoveRequested;
    }
}
