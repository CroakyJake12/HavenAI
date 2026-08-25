using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Boards;

public sealed partial class BoardsPage
{
    private readonly HashSet<Guid> _pinnedNotebookIds = [];

    private async Task RefreshPinnedAsync()
    {
        _pinnedNotebookIds.Clear();
        foreach (var summary in _notebooks)
        {
            var notebook = await _boards.OpenNotebookAsync(summary.Id, CancellationToken.None);
            if (notebook is not null && _boards.IsPinned(notebook))
            {
                _pinnedNotebookIds.Add(summary.Id);
            }
        }
    }

    private Control BuildNotebookLibraryRow(NotesDocumentSummary summary)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 6
        };
        var open = ActionButton(
            ( _pinnedNotebookIds.Contains(summary.Id) ? "Pinned · " : string.Empty) + summary.Title,
            async () => await OpenNotebookAsync(summary.Id));
        open.HorizontalContentAlignment = HorizontalAlignment.Left;
        var pin = new Button
        {
            Content = _pinnedNotebookIds.Contains(summary.Id) ? "Unpin" : "Pin",
            MinWidth = 58
        };
        AutomationProperties.SetName(pin, $"{pin.Content} {summary.Title}");
        pin.Click += async (_, _) => await TogglePinnedAsync(summary.Id);
        Grid.SetColumn(pin, 1);
        row.Children.Add(open);
        row.Children.Add(pin);
        return row;
    }

    private async Task TogglePinnedAsync(Guid notebookId)
    {
        var notebook = await _boards.OpenNotebookAsync(notebookId, CancellationToken.None);
        if (notebook is null) return;
        var pinned = _boards.IsPinned(notebook);
        _boards.SetPinned(notebook, !pinned);
        await _boards.SaveAsync(notebook, pinned ? "Unpinned Boards notebook" : "Pinned Boards notebook", CancellationToken.None);
        if (_document?.Id == notebookId)
            _boards.SetPinned(_document, !pinned);
        await RefreshLibraryAsync();
    }
}
