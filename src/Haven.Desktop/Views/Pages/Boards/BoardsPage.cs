using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Button = Avalonia.Controls.Button;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI.Components;
using DomainPage = Haven.Core.NotesPage;
using DomainSection = Haven.Core.NotesSection;
using NativeTabStrip = Haven.UI.Components.TabStrip;

namespace Haven.Desktop.Views.Pages.Boards;

public sealed partial class BoardsPage : UserControl, IDisposable
{
    private readonly HavenEventBus _bus;
    private readonly IBoardsWorkspaceService _boards;
    private readonly INotesAttachmentStore? _attachments;
    private readonly Grid _root = new();
    private readonly StackPanel _library = new() { Spacing = 6 };
    private readonly StackPanel _sections = new() { Spacing = 5 };
    private readonly StackPanel _editor = new() { Spacing = 10 };
    private readonly TextBox _search = new() { PlaceholderText = "Search notebooks..." };
    private readonly TextBlock _status = new() { Text = "Opening Boards...", FontSize = 11 };
    private readonly TextBox _notebookTitle = new() { Text = "Boards", FontSize = 24, FontWeight = Avalonia.Media.FontWeight.SemiBold };
    private readonly NativeTabStrip _pageTabs = new() { Name = "Boards.PageTabs" };
    private readonly HavenSceneControl _pageTabHost;
    private readonly Haven.UI.Components.Page _pageTabScene = new() { Name = "Boards.PageTabs.Scene" };
    private IReadOnlyList<NotesDocumentSummary> _notebooks = [];
    private NotesDocument? _document;
    private DomainSection? _section;
    private DomainPage? _page;
    private WriteDocumentEditor? _documentEditor;
    private bool _disposed;

    public BoardsPage(HavenEventBus bus, IBoardsWorkspaceService boards, INotesAttachmentStore? attachments = null)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _boards = boards ?? throw new ArgumentNullException(nameof(boards));
        _attachments = attachments;
        Focusable = true;
        AutomationProperties.SetName(this, "Haven Boards");

        _pageTabScene.Layout = HavenLayout.Vertical;
        _pageTabScene.Add(_pageTabs);
        _pageTabHost = new HavenSceneControl { Root = _pageTabScene };
        _pageTabs.ItemInvoked += OnPageTabInvoked;
        _notebookTitle.LostFocus += async (_, _) => await RenameCurrentNotebookAsync();

        BuildShell();
        Content = _root;
        Loaded += OnLoaded;
        DetachedFromVisualTree += OnDetached;
    }

    internal NotesDocument? Document => _document;
    internal DomainSection? CurrentSection => _section;
    internal DomainPage? CurrentPage => _page;
    internal NativeTabStrip PageTabs => _pageTabs;

    private void BuildShell()
    {
        _root.ColumnDefinitions = new ColumnDefinitions("270,220,*");
        _root.RowDefinitions = new RowDefinitions("*");
        _root.ColumnSpacing = 1;

        var libraryPane = Pane("Notebooks");
        Grid.SetColumn(libraryPane, 0);
        var newNotebook = ActionButton("+ New notebook", async () => await CreateNotebookAsync());
        _search.TextChanged += (_, _) => RebuildLibrary();
        libraryPane.Children.Add(newNotebook);
        libraryPane.Children.Add(_search);
        libraryPane.Children.Add(new ScrollViewer { Content = _library });
        _root.Children.Add(libraryPane);

        var hierarchyPane = Pane("Sections");
        Grid.SetColumn(hierarchyPane, 1);
        hierarchyPane.Children.Add(ActionButton("+ Section", async () => await AddSectionAsync()));
        hierarchyPane.Children.Add(new ScrollViewer { Content = _sections });
        _root.Children.Add(hierarchyPane);

        var workspace = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
            RowSpacing = 6,
            Margin = new Thickness(14, 10, 14, 10)
        };
        Grid.SetColumn(workspace, 2);
        workspace.Children.Add(_notebookTitle);

        Grid.SetRow(_pageTabHost, 1);
        workspace.Children.Add(_pageTabHost);

        var toolbar = new WrapPanel();
        foreach (var button in new[]
        {
            ActionButton("+ Page", async () => await AddPageAsync()),
            ActionButton("Section ↑", async () => await MoveCurrentSectionAsync(-1)),
            ActionButton("Section ↓", async () => await MoveCurrentSectionAsync(1)),
            ActionButton("Page ↑", async () => await MoveCurrentPageAsync(-1)),
            ActionButton("Page ↓", async () => await MoveCurrentPageAsync(1)),
            ActionButton("Text", async () => await AddBlockAsync(NotesBlockKind.Paragraph)),
            ActionButton("Checklist", async () => await AddBlockAsync(NotesBlockKind.List)),
            ActionButton("Table", async () => await AddBlockAsync(NotesBlockKind.Table)),
            ActionButton("Ink canvas", async () => await AddInkAsync()),
            ActionButton("Freeform", () => { ToggleFreeform(); return Task.CompletedTask; }),
            ActionButton("+ Freeform card", async () => await AddFreeformCardAsync()),
            ActionButton("Attach", async () => await AddAttachmentAsync()),
            ActionButton("Embed", async () => await AddEmbedAsync()),
            ActionButton("Task component", async () => await AddLiveComponentAsync(BoardsLiveComponentKind.TaskList)),
            ActionButton("Poll", async () => await AddLiveComponentAsync(BoardsLiveComponentKind.Poll)),
            ActionButton("Status", async () => await AddLiveComponentAsync(BoardsLiveComponentKind.Status)),
            ActionButton("Shared list", async () => await AddLiveComponentAsync(BoardsLiveComponentKind.List)),
            ActionButton("Shared table", async () => await AddLiveComponentAsync(BoardsLiveComponentKind.Table)),
            ActionButton("Save", async () => await SaveAsync("Manual Boards save"))
        }) toolbar.Children.Add(button);
        Grid.SetRow(toolbar, 2);
        workspace.Children.Add(toolbar);

        var editorScroll = new ScrollViewer { Content = _editor };
        Grid.SetRow(editorScroll, 3);
        workspace.Children.Add(editorScroll);

        Grid.SetRow(_status, 4);
        workspace.Children.Add(_status);
        _root.Children.Add(workspace);
    }

    private static StackPanel Pane(string title) => new()
    {
        Spacing = 8,
        Margin = new Thickness(10),
        Children =
        {
            new TextBlock { Text = title, FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold }
        }
    };

    private static Button ActionButton(string label, Func<Task> action)
    {
        var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(button, label);
        button.Click += async (_, _) => await action();
        return button;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (!_disposed) await RefreshLibraryAsync();
    }

    private async void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_document is not null) await SaveAsync("Autosave on leaving Boards");
    }

    private async Task RefreshLibraryAsync()
    {
        SetStatus("Loading notebooks...");
        try
        {
            _notebooks = await _boards.ListNotebooksAsync(CancellationToken.None);
            await RefreshPinnedAsync();
            RebuildLibrary();
            if (_document is null && _notebooks.Count > 0)
                await OpenNotebookAsync(_notebooks[0].Id);
            else if (_notebooks.Count == 0)
                ShowEmptyState();
        }
        catch (Exception)
        {
            SetStatus("Couldn’t load Boards.");
        }
    }

    private void RebuildLibrary()
    {
        _library.Children.Clear();
        var filter = _search.Text?.Trim() ?? string.Empty;
        var visible = _notebooks
            .Where(item => filter.Length == 0 || item.Title.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => _pinnedNotebookIds.Contains(item.Id))
            .ThenByDescending(item => item.UpdatedAt)
            .ToArray();

        if (visible.Length == 0)
        {
            _library.Children.Add(new TextBlock
            {
                Text = filter.Length == 0 ? "No notebooks yet." : "No matching notebooks.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
            return;
        }

        foreach (var summary in visible)
        {
            var local = summary;
            var button = ActionButton(local.Title, async () => await OpenNotebookAsync(local.Id));
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            _library.Children.Add(BuildNotebookLibraryRow(local));
        }
    }

    private async Task CreateNotebookAsync()
    {
        SetStatus("Creating notebook...");
        var created = await _boards.CreateNotebookAsync("New board", CancellationToken.None);
        await RefreshLibraryAsync();
        await OpenNotebookAsync(created.Id);
    }

    public async Task<bool> OpenDeepLinkAsync(string value, CancellationToken cancellationToken = default)
    {
        if (!BoardsDeepLink.TryParse(value, out var link)) return false;
        var notebook = await _boards.OpenNotebookAsync(link.NotebookId, cancellationToken);
        if (notebook is null) return false;
        ActivateNotebook(notebook, link.SectionId, link.PageId);
        return true;
    }

    private async Task OpenNotebookAsync(Guid id)
    {
        SetStatus("Opening notebook...");
        var notebook = await _boards.OpenNotebookAsync(id, CancellationToken.None);
        if (notebook is null)
        {
            SetStatus("That Boards notebook is unavailable or no longer exists.");
            return;
        }
        ActivateNotebook(notebook, null, null);
    }

    private void ActivateNotebook(NotesDocument notebook, Guid? sectionId, Guid? pageId)
    {
        if (_documentEditor is not null) _documentEditor.Changed -= OnDocumentChanged;
        _document = notebook;
        _documentEditor = new WriteDocumentEditor(notebook);
        _documentEditor.Changed += OnDocumentChanged;
        _notebookTitle.Text = notebook.Title;

        _section = notebook.Sections.FirstOrDefault(item => item.Id == sectionId) ?? notebook.Sections.FirstOrDefault();
        _page = _section?.Pages.FirstOrDefault(item => item.Id == pageId) ?? _section?.Pages.OrderBy(item => item.Order).FirstOrDefault();
        RebuildHierarchy();
        RebuildPageTabs();
        RebuildEditor();
        SetStatus($"Local notebook · v{notebook.Version}");
        _bus.Fire("Boards.Opened");
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        RebuildEditor();
        SetStatus("Unsaved changes");
    }

    private void ShowEmptyState()
    {
        _sections.Children.Clear();
        _editor.Children.Clear();
        _pageTabs.SetItems([]);
        _editor.Children.Add(new TextBlock
        {
            Text = "Create your first notebook to start collecting pages, ink and live components.",
            FontSize = 17,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        SetStatus("Boards is ready.");
    }

    private void SetStatus(string text) => _status.Text = text;

    private async Task SaveAsync(string reason)
    {
        if (_document is null) return;
        try
        {
            await _boards.SaveAsync(_document, reason, CancellationToken.None);
            SetStatus($"Saved locally · {DateTimeOffset.Now:t}");
            _bus.Fire("Boards.Saved");
        }
        catch (Exception)
        {
            SetStatus("Couldn’t save this board.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Loaded -= OnLoaded;
        DetachedFromVisualTree -= OnDetached;
        _pageTabs.ItemInvoked -= OnPageTabInvoked;
        if (_documentEditor is not null) _documentEditor.Changed -= OnDocumentChanged;
    }
}
