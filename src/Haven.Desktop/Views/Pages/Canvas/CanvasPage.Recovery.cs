using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Canvas;

public sealed partial class CanvasPage
{
    private UserPreferencesService _preferences = null!;
    private int _boardIndex;

    private void InitializeRecovery(UserPreferencesService preferences)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _route.BoardRequested += OnBoardRequested;
        _route.AddBoardRequested += OnAddBoardRequested;
        _route.BoardRenameRequested += OnBoardRenameRequested;
        _route.DeleteBoardRequested += OnDeleteBoardRequested;
        _route.SavePenPresetRequested += OnSavePenPresetRequested;
    }

    private void UnwireRecovery()
    {
        _route.BoardRequested -= OnBoardRequested;
        _route.AddBoardRequested -= OnAddBoardRequested;
        _route.BoardRenameRequested -= OnBoardRenameRequested;
        _route.DeleteBoardRequested -= OnDeleteBoardRequested;
        _route.SavePenPresetRequested -= OnSavePenPresetRequested;
    }

    private void SetRecoveryDocument(NotesDocument document, int documentIndex)
    {
        Document = document;
        _documentIndex = Math.Clamp(documentIndex, 0, Math.Max(0, _documents.Count - 1));
        _boardIndex = 0;
        _controller = CreateBoardController(CanvasDocumentModel.GetBoard(document, _boardIndex), null);
        _dirty = false;
        RefreshScene();
    }

    private static CanvasInteractionController CreateBoardController(NotesCanvasData board, CanvasInteractionController? previous)
    {
        var controller = new CanvasInteractionController(board);
        if (previous is null) return controller;
        controller.Tool = previous.Tool;
        controller.PenColour = previous.PenColour;
        controller.PenWidth = previous.PenWidth;
        controller.PenOpacity = previous.PenOpacity;
        controller.PenEffect = previous.PenEffect;
        controller.EraserMode = previous.EraserMode;
        controller.GridSize = previous.GridSize;
        return controller;
    }

    private void OnBoardRequested(int index) => SwitchBoard(index);

    private void SwitchBoard(int index)
    {
        if (Document is null || _controller is null) return;
        var count = CanvasDocumentModel.GetBoardCount(Document);
        if (count == 0) return;
        index = Math.Clamp(index, 0, count - 1);
        if (index == _boardIndex) return;
        ReleaseInteractionForPersistence();
        SyncBoardReference();
        var previous = _controller;
        _boardIndex = index;
        _controller = CreateBoardController(CanvasDocumentModel.GetBoard(Document, _boardIndex), previous);
        RefreshScene();
    }

    private void OnAddBoardRequested(object? sender, EventArgs e)
    {
        if (Document is null || _controller is null) return;
        ReleaseInteractionForPersistence();
        SyncBoardReference();
        var previous = _controller;
        _boardIndex = CanvasDocumentModel.AddBoard(Document);
        _controller = CreateBoardController(CanvasDocumentModel.GetBoard(Document, _boardIndex), previous);
        MarkDirty();
        RefreshScene();
    }

    private void OnBoardRenameRequested(int index, string title)
    {
        if (Document is null) return;
        var documentId = Document.Id;
        if (!CanvasDocumentModel.RenameBoard(Document, index, title)) return;
        if (Document.Id != documentId) throw new InvalidOperationException("Canvas board rename changed document identity.");
        MarkDirty();
        RefreshScene();
    }

    private void OnDeleteBoardRequested(int index)
    {
        if (Document is null || _controller is null) return;
        ReleaseInteractionForPersistence();
        SyncBoardReference();
        if (!CanvasDocumentModel.RemoveBoard(Document, index)) return;
        var previous = _controller;
        _boardIndex = Math.Clamp(_boardIndex > index ? _boardIndex - 1 : _boardIndex, 0, CanvasDocumentModel.GetBoardCount(Document) - 1);
        _controller = CreateBoardController(CanvasDocumentModel.GetBoard(Document, _boardIndex), previous);
        MarkDirty();
        RefreshScene();
    }

    private void OnSavePenPresetRequested(CanvasPenPresetPreference requested)
    {
        var saved = _preferences.SaveCanvasPenPreset(requested);
        if (saved is null)
        {
            _route.SetStatus("Canvas pen presets were created by a newer Haven version, so this version left them unchanged.");
            return;
        }
        _route.SetPenPresets(_preferences.CanvasPenPresets);
        _route.SetStatus($"Saved pen preset ‘{saved.Name}’.");
    }

    private void RefreshRecoveryChrome()
    {
        if (Document is null) return;
        _route.SetBoards(CanvasDocumentModel.GetBoardTitles(Document), _boardIndex);
        _route.SetPenPresets(_preferences.CanvasPenPresets);
    }

    private void ReleaseInteractionForPersistence()
    {
        if (_controller is null) return;
        var committedMutation = _route.ReleaseInteraction();
        SyncBoardReference();
        if (committedMutation) MarkDirty();
    }
}
