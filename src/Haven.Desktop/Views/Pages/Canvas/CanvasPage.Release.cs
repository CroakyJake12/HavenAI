using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Canvas;

public sealed partial class CanvasPage
{
    private int _boardIndex;

    private void ShowLibrary()
    {
        Document = null;
        _controller = null;
        _boardIndex = 0;
        _dirty = false;
        _route.SetLibrary(_documents);
        _bus.Fire("Canvas.Library.Opened");
    }

    private async void OnLibraryRequested(object? sender, EventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            if (Document is not null && _dirty && !await SaveAsync("Autosave before opening Canvas home")) return;
            await RefreshDocumentsAsync(CancellationToken.None);
            ShowLibrary();
        }, "open Canvas home");
    }

    private async void OnDocumentOpenRequested(Guid documentId)
    {
        await RunBusyAsync(async () =>
        {
            await RefreshDocumentsAsync(CancellationToken.None);
            var index = IndexOf(documentId);
            if (index < 0 || index >= _documents.Count || _documents[index].Id != documentId)
            {
                _route.SetStatus("That local Canvas no longer exists.");
                return;
            }
            await OpenDocumentAtAsync(index, CancellationToken.None, true);
        }, "open this canvas");
    }

    private void OnBoardRequested(int index)
    {
        if (Document is null || _controller is null) return;
        var count = CanvasDocumentModel.GetBoardCount(Document);
        if (index < 0 || index >= count || index == _boardIndex) return;
        SyncBoardReference();
        _boardIndex = index;
        _controller = new CanvasInteractionController(CanvasDocumentModel.GetBoard(Document, _boardIndex));
        RefreshScene();
        _route.SetStatus($"Board {_boardIndex + 1} of {count} · saved locally with this canvas");
    }

    private void OnAddBoardRequested(object? sender, EventArgs e)
    {
        if (Document is null) return;
        SyncBoardReference();
        _boardIndex = CanvasDocumentModel.AddBoard(Document);
        _controller = new CanvasInteractionController(CanvasDocumentModel.GetBoard(Document, _boardIndex));
        MarkDirty();
        RefreshScene();
        _route.SetStatus($"Added Board {_boardIndex + 1} · autosave is on");
    }

    private void OnBoardRenameRequested(int index, string title)
    {
        if (Document is null || !CanvasDocumentModel.RenameBoard(Document, index, title)) return;
        MarkDirty();
        RefreshScene();
    }

    private void OnSpecialInsertRequested(string command)
    {
        if (Document is null || _controller is null || !command.StartsWith("delete-board:", StringComparison.Ordinal)) return;
        if (!int.TryParse(command.AsSpan(13), out var index)) return;
        SyncBoardReference();
        if (!CanvasDocumentModel.RemoveBoard(Document, index))
        {
            _route.SetStatus("A canvas must keep at least one board.");
            return;
        }
        _boardIndex = Math.Clamp(_boardIndex > index ? _boardIndex - 1 : _boardIndex, 0, CanvasDocumentModel.GetBoardCount(Document) - 1);
        _controller = new CanvasInteractionController(CanvasDocumentModel.GetBoard(Document, _boardIndex));
        MarkDirty();
        RefreshScene();
        _route.SetStatus("Board deleted · autosave is on");
    }
}
