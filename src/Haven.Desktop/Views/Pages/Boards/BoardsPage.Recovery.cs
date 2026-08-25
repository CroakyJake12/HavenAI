namespace Haven.Desktop.Views.Pages.Boards;

public sealed partial class BoardsPage
{
    private async Task RenameCurrentNotebookAsync()
    {
        if (_document is null) return;
        var proposed = _notebookTitle.Text?.Trim();
        if (string.IsNullOrWhiteSpace(proposed) || proposed == _document.Title)
        {
            _notebookTitle.Text = _document.Title;
            return;
        }

        var stableId = _document.Id;
        await _boards.RenameNotebookAsync(_document, proposed, CancellationToken.None);
        if (_document.Id != stableId)
            throw new InvalidOperationException("Boards rename changed notebook identity.");
        _notebookTitle.Text = _document.Title;
        await RefreshLibraryAsync();
        SetStatus("Renamed board");
    }

    private async Task MoveCurrentSectionAsync(int delta)
    {
        if (_document is null || _section is null) return;
        var index = _document.Sections.FindIndex(item => item.Id == _section.Id);
        if (index < 0) return;
        var target = Math.Clamp(index + delta, 0, _document.Sections.Count - 1);
        if (target == index) return;
        _boards.MoveSection(_document, _section.Id, target);
        RebuildHierarchy();
        await SaveAsync("Reordered Boards section");
    }

    private async Task MoveCurrentPageAsync(int delta)
    {
        if (_document is null || _section is null || _page is null) return;
        var ordered = _section.Pages.OrderBy(item => item.Order).ToArray();
        var index = Array.FindIndex(ordered, item => item.Id == _page.Id);
        if (index < 0) return;
        var target = Math.Clamp(index + delta, 0, ordered.Length - 1);
        if (target == index) return;
        _boards.MovePage(_document, _page.Id, _section.Id, target);
        RebuildPageTabs();
        await SaveAsync("Reordered Boards page");
    }
}
