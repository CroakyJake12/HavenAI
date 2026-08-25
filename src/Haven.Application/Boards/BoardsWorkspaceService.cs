using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public interface IBoardsWorkspaceService
{
    Task<IReadOnlyList<NotesDocumentSummary>> ListNotebooksAsync(CancellationToken cancellationToken);
    Task<NotesDocument> CreateNotebookAsync(string title, CancellationToken cancellationToken);
    Task<NotesDocument?> OpenNotebookAsync(Guid notebookId, CancellationToken cancellationToken);
    Task SaveAsync(NotesDocument notebook, string reason, CancellationToken cancellationToken);
    Task RenameNotebookAsync(NotesDocument notebook, string title, CancellationToken cancellationToken);
    NotesSection AddSection(NotesDocument notebook, string? title = null);
    NotesPage AddPage(NotesDocument notebook, Guid sectionId, string? title = null);
    void MoveSection(NotesDocument notebook, Guid sectionId, int targetIndex);
    void MovePage(NotesDocument notebook, Guid pageId, Guid targetSectionId, int targetIndex);
    void MoveBlock(NotesDocument notebook, Guid pageId, Guid blockId, int targetIndex);
    NotesBlock AddBlock(NotesDocument notebook, Guid pageId, NotesBlockKind kind, string? text = null);
    bool RenamePage(NotesDocument notebook, Guid pageId, string? title);
    bool UpdateListItem(NotesDocument notebook, Guid pageId, Guid blockId, Guid itemId, string? text = null, bool? isChecked = null);
    bool UpdateTableCell(NotesDocument notebook, Guid pageId, Guid blockId, Guid cellId, string? text);
    bool IsPinned(NotesDocument notebook);
    void SetPinned(NotesDocument notebook, bool pinned);
    NotesCanvasObject AddCanvasObject(NotesDocument notebook, Guid pageId, NotesCanvasObjectKind kind, string? text, double x, double y, double width = 260, double height = 160);
    bool MoveCanvasObject(NotesDocument notebook, Guid pageId, Guid objectId, double x, double y);
    bool ResizeCanvasObject(NotesDocument notebook, Guid pageId, Guid objectId, double width, double height);
    bool UpdateCanvasObjectText(NotesDocument notebook, Guid pageId, Guid objectId, string? text);
    Task<NotesBlock> AttachAsync(NotesDocument notebook, Guid pageId, string sourcePath, CancellationToken cancellationToken);
    Task<BoardsAttachmentResolution> ResolveAttachmentAsync(NotesMediaData media, CancellationToken cancellationToken);
    bool UpdateComponentSource(NotesDocument notebook, Guid componentId, Action<BoardsLiveComponentSource> update);
    IReadOnlyList<BoardsLiveComponentPlacement> GetPlacements(NotesDocument notebook);
    BoardsLiveComponent AddComponent(NotesDocument notebook, NotesPage page, BoardsLiveComponentKind kind);
    BoardsLiveComponentPlacement PlaceComponent(NotesDocument notebook, NotesPage page, Guid componentId);
    bool UpdateComponentItem(NotesDocument notebook, Guid componentId, Guid itemId, Action<BoardsLiveComponentItem> update);
    IReadOnlyList<BoardsLiveComponent> GetComponents(NotesDocument notebook);
}

public sealed partial class BoardsWorkspaceService(INotesRepository repository, INotesAttachmentStore? attachments = null) : IBoardsWorkspaceService
{
    public const string ProductKey = "haven.product";
    public const string ComponentsKey = "boards.live-components";
    public const string PlacementsKey = "boards.component-placements";
    public const string ComponentIdKey = "boards.component-id";
    public const string PlacementIdKey = "boards.placement-id";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<NotesDocumentSummary>> ListNotebooksAsync(CancellationToken cancellationToken)
    {
        var result = new List<NotesDocumentSummary>();
        foreach (var summary in await repository.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            var document = await repository.LoadAsync(summary.Id, cancellationToken).ConfigureAwait(false);
            if (document is not null && IsBoardsNotebook(document))
                result.Add(summary);
        }

        return result;
    }

    public async Task<NotesDocument> CreateNotebookAsync(string title, CancellationToken cancellationToken)
    {
        var document = NotesDocument.Create(string.IsNullOrWhiteSpace(title) ? "New board" : title.Trim());
        document.Metadata[ProductKey] = "boards";
        document.Metadata["boards.pinned"] = bool.FalseString;
        document.Sections[0].Title = "Notes";
        document.Sections[0].Pages[0].Title = "Start here";
        document.Sections[0].Pages[0].Blocks =
        [
            NotesBlock.Heading(document.Title),
            NotesBlock.CreateParagraph("Capture ideas, arrange content freely, and reuse live components across pages.")
        ];
        await repository.SaveAsync(document, "Created Boards notebook", cancellationToken).ConfigureAwait(false);
        return document;
    }

    public async Task<NotesDocument?> OpenNotebookAsync(Guid notebookId, CancellationToken cancellationToken)
    {
        var document = await repository.LoadAsync(notebookId, cancellationToken).ConfigureAwait(false);
        return document is not null && IsBoardsNotebook(document) ? document : null;
    }

    public async Task SaveAsync(NotesDocument notebook, string reason, CancellationToken cancellationToken)
    {
        EnsureBoards(notebook);
        await repository.SaveAsync(notebook, reason, cancellationToken).ConfigureAwait(false);
    }

    public BoardsLiveComponent AddComponent(NotesDocument notebook, NotesPage page, BoardsLiveComponentKind kind)
    {
        EnsurePage(notebook, page);
        var component = new BoardsLiveComponent
        {
            Kind = kind,
            Title = kind switch
            {
                BoardsLiveComponentKind.TaskList => "Task list",
                BoardsLiveComponentKind.Poll => "Poll",
                BoardsLiveComponentKind.Status => "Status",
                BoardsLiveComponentKind.Table => "Shared table",
                _ => "Shared list"
            },
            Items = Defaults(kind)
        };

        var components = Read<List<BoardsLiveComponent>>(notebook, ComponentsKey) ?? [];
        components.Add(component);
        Write(notebook, ComponentsKey, components);
        return component;
    }

    public BoardsLiveComponentPlacement PlaceComponent(NotesDocument notebook, NotesPage page, Guid componentId)
    {
        EnsurePage(notebook, page);
        var component = GetComponents(notebook).Single(item => item.Id == componentId);
        var placements = Read<List<BoardsLiveComponentPlacement>>(notebook, PlacementsKey) ?? [];
        var placement = new BoardsLiveComponentPlacement
        {
            ComponentId = componentId,
            PageId = page.Id,
            X = 40 + placements.Count(item => item.PageId == page.Id) * 24,
            Y = 80 + placements.Count(item => item.PageId == page.Id) * 24
        };
        placements.Add(placement);
        Write(notebook, PlacementsKey, placements);

        var block = NotesBlock.CreateParagraph(string.Empty);
        block.Metadata[ComponentIdKey] = component.Id.ToString("D");
        block.Metadata[PlacementIdKey] = placement.Id.ToString("D");
        RefreshBlock(block, component);
        block.Order = page.Blocks.Count;
        page.Blocks.Add(block);
        return placement;
    }

    public bool UpdateComponentItem(NotesDocument notebook, Guid componentId, Guid itemId, Action<BoardsLiveComponentItem> update)
    {
        var components = Read<List<BoardsLiveComponent>>(notebook, ComponentsKey) ?? [];
        var component = components.SingleOrDefault(item => item.Id == componentId);
        var item = component?.Items.SingleOrDefault(candidate => candidate.Id == itemId);
        if (component is null || item is null)
            return false;

        update(item);
        component.Version++;
        component.UpdatedAt = DateTimeOffset.UtcNow;
        Write(notebook, ComponentsKey, components);

        foreach (var block in notebook.Sections.SelectMany(s => s.Pages).SelectMany(p => p.Blocks))
        {
            if (block.Metadata.TryGetValue(ComponentIdKey, out var raw) &&
                Guid.TryParse(raw, out var placedId) &&
                placedId == component.Id)
            {
                RefreshBlock(block, component);
            }
        }

        return true;
    }

    public IReadOnlyList<BoardsLiveComponent> GetComponents(NotesDocument notebook)
    {
        EnsureBoards(notebook);
        return Read<List<BoardsLiveComponent>>(notebook, ComponentsKey) ?? [];
    }

    public static bool IsBoardsNotebook(NotesDocument document) =>
        document.Metadata.TryGetValue(ProductKey, out var value) &&
        value.Equals("boards", StringComparison.OrdinalIgnoreCase);

    private static void EnsureBoards(NotesDocument document)
    {
        if (!IsBoardsNotebook(document))
            throw new InvalidOperationException("Document is not a Boards notebook.");
    }

    private static void EnsurePage(NotesDocument document, NotesPage page)
    {
        EnsureBoards(document);
        if (!document.Sections.SelectMany(s => s.Pages).Any(p => p.Id == page.Id))
            throw new InvalidOperationException("Page does not belong to the Boards notebook.");
    }

    private static T? Read<T>(NotesDocument document, string key)
    {
        if (!document.Metadata.TryGetValue(key, out var value))
            return default;
        try { return JsonSerializer.Deserialize<T>(value, Json); }
        catch (JsonException) { return default; }
    }

    private static void Write<T>(NotesDocument document, string key, T value) =>
        document.Metadata[key] = JsonSerializer.Serialize(value, Json);

    private static List<BoardsLiveComponentItem> Defaults(BoardsLiveComponentKind kind) => kind switch
    {
        BoardsLiveComponentKind.TaskList => [new() { Text = "First task" }, new() { Text = "Second task" }],
        BoardsLiveComponentKind.Poll => [new() { Text = "Option A" }, new() { Text = "Option B" }],
        BoardsLiveComponentKind.Status => [new() { Text = "Overall", Status = "On track" }],
        BoardsLiveComponentKind.Table => [new() { Cells = ["Item", "Owner", "Status"] }, new() { Cells = ["Example", "", "Not started"] }],
        _ => [new() { Text = "First item" }, new() { Text = "Second item" }]
    };

    private static void RefreshBlock(NotesBlock block, BoardsLiveComponent component)
    {
        if (component.Kind == BoardsLiveComponentKind.TaskList)
        {
            block.Kind = NotesBlockKind.List;
            block.PlainText = LiveTitle(component);
            block.Table = null;
            block.List = new NotesListData
            {
                Kind = NotesListKind.Checklist,
                Items = component.Items.Select(item => new NotesListItem
                {
                    Id = item.Id,
                    Text = item.Text,
                    Checked = item.Checked
                }).ToList()
            };
            return;
        }

        if (component.Kind == BoardsLiveComponentKind.Table)
        {
            var rows = Math.Max(1, component.Items.Count);
            var cols = Math.Max(1, component.Items.Select(i => i.Cells.Count).DefaultIfEmpty(1).Max());
            var table = NotesBlock.TableBlock(rows, cols);
            for (var r = 0; r < component.Items.Count; r++)
                for (var c = 0; c < component.Items[r].Cells.Count; c++)
                    table.Table!.Rows[r].Cells[c].Text = component.Items[r].Cells[c];
            block.Kind = NotesBlockKind.Table;
            block.Table = table.Table;
            block.List = null;
            block.PlainText = LiveTitle(component);
            return;
        }

        var lines = component.Kind switch
        {
            BoardsLiveComponentKind.Poll => component.Items.Select(i => $"{i.Text} ÃƒÂ¢Ã¢â‚¬ÂÃ‚Â¬ÃƒÆ’Ã¢â€šÂ¬ {i.Votes} votes"),
            BoardsLiveComponentKind.Status => component.Items.Select(i => $"{i.Text}: {i.Status}"),
            _ => component.Items.Select(i => $"ÃƒÆ’Ã¢â‚¬ÂÃƒÆ’Ã¢â‚¬Â¡ÃƒÆ’Ã‚Â³ {i.Text}")
        };
        block.Kind = NotesBlockKind.Paragraph;
        block.List = null;
        block.Table = null;
        block.PlainText = LiveTitle(component) + Environment.NewLine + string.Join(Environment.NewLine, lines) + LiveUnavailableReason(component);
        block.Runs = [new NotesTextRun { Text = block.PlainText }];
    }
}

public readonly record struct BoardsDeepLink(Guid NotebookId, Guid? SectionId = null, Guid? PageId = null)
{
    public override string ToString() =>
        $"haven://boards/{NotebookId:D}" +
        (SectionId is Guid section ? $"/section/{section:D}" : "") +
        (PageId is Guid page ? $"/page/{page:D}" : "");

    public static bool TryParse(string? value, out BoardsDeepLink link)
    {
        link = default;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals("haven", StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals("boards", StringComparison.OrdinalIgnoreCase))
            return false;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || !Guid.TryParse(segments[0], out var notebookId))
            return false;

        Guid? sectionId = null, pageId = null;
        for (var i = 1; i + 1 < segments.Length; i += 2)
        {
            if (segments[i].Equals("section", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(segments[i + 1], out var section))
                sectionId = section;
            else if (segments[i].Equals("page", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(segments[i + 1], out var page))
                pageId = page;
        }

        link = new BoardsDeepLink(notebookId, sectionId, pageId);
        return true;
    }
}
