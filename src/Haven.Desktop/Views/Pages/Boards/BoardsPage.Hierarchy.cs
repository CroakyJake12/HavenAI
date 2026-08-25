using Avalonia.Controls;
using Avalonia.Layout;
using Haven.Core;
using Haven.UI.Components;
using DomainPage = Haven.Core.NotesPage;
using DomainSection = Haven.Core.NotesSection;

namespace Haven.Desktop.Views.Pages.Boards;

public sealed partial class BoardsPage
{
    private void RebuildHierarchy()
    {
        _sections.Children.Clear();
        if (_document is null) return;

        foreach (var section in _document.Sections)
        {
            var local = section;
            var button = ActionButton(
                (ReferenceEquals(section, _section) ? "• " : "") + section.Title,
                async () =>
                {
                    _section = local;
                    _page = local.Pages.OrderBy(item => item.Order).FirstOrDefault();
                    RebuildHierarchy();
                    RebuildPageTabs();
                    RebuildEditor();
                    await Task.CompletedTask;
                });
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            _sections.Children.Add(button);
        }
    }

    private void RebuildPageTabs()
    {
        var pages = _section?.Pages.OrderBy(item => item.Order).ToArray() ?? [];
        _pageTabs.SetItems(pages.Select(item =>
            new Haven.UI.Components.TabStripItem(item.Id.ToString("D"), item.Title, item.Id == _page?.Id, false)).ToArray());
    }

    private void OnPageTabInvoked(object? sender, string key)
    {
        if (_section is null || !Guid.TryParse(key, out var id)) return;
        var page = _section.Pages.FirstOrDefault(item => item.Id == id);
        if (page is null) return;
        _page = page;
        RebuildPageTabs();
        RebuildEditor();
        SetStatus($"{_section.Title} · {page.Title}");
    }

    private async Task AddSectionAsync()
    {
        if (_document is null) return;
        var section = _boards.AddSection(_document);
        _section = section;
        _page = section.Pages[0];
        RebuildHierarchy();
        RebuildPageTabs();
        RebuildEditor();
        await SaveAsync("Added Boards section");
    }

    private async Task AddPageAsync()
    {
        if (_section is null || _document is null) return;
        var page = _boards.AddPage(_document, _section.Id);
        _page = page;
        RebuildPageTabs();
        RebuildEditor();
        await SaveAsync("Added Boards page");
    }

    private async Task AddBlockAsync(NotesBlockKind kind)
    {
        if (_page is null || _document is null) return;
        _boards.AddBlock(_document, _page.Id, kind);
        RebuildEditor();
        await SaveAsync($"Added {kind} block");
    }

    private async Task AddInkAsync()
    {
        if (_page is null || _document is null) return;
        _boards.AddBlock(_document, _page.Id, NotesBlockKind.Canvas);
        RebuildEditor();
        await SaveAsync("Added Boards ink canvas");
    }

    private async Task AddLiveComponentAsync(BoardsLiveComponentKind kind)
    {
        if (_document is null || _page is null) return;
        var component = _boards.AddComponent(_document, _page, kind);
        _boards.PlaceComponent(_document, _page, component.Id);
        RebuildEditor();
        await SaveAsync($"Added Boards {kind} live component");
    }
}
