using Haven.Core;

namespace Haven.Application;

public enum BoardsOperationKind
{
    AddSection = 0,
    AddPage = 1,
    AddBlock = 2,
    AddComponent = 3,
    PlaceComponent = 4,
    UpdateComponentItem = 5,
    SetPinned = 6
}

public sealed record BoardsOperation(
    BoardsOperationKind Kind,
    Guid? SectionId = null,
    Guid? PageId = null,
    Guid? ComponentId = null,
    Guid? ItemId = null,
    string? Text = null,
    NotesBlockKind? BlockKind = null,
    BoardsLiveComponentKind? ComponentKind = null,
    bool? Checked = null,
    int? Votes = null,
    string? Status = null,
    bool? Pinned = null);

public sealed record BoardsOperationResult(
    Guid NotebookId,
    Guid? SectionId,
    Guid? PageId,
    Guid? BlockId,
    Guid? ComponentId,
    Guid? ItemId,
    string ActivityTarget);

public interface IBoardsOperationExecutor
{
    Task<BoardsOperationResult> ExecuteAsync(
        Guid notebookId,
        BoardsOperation operation,
        CancellationToken cancellationToken);
}

public sealed class BoardsOperationExecutor(IBoardsWorkspaceService boards) : IBoardsOperationExecutor
{
    public async Task<BoardsOperationResult> ExecuteAsync(
        Guid notebookId,
        BoardsOperation operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var notebook = await boards.OpenNotebookAsync(notebookId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Boards notebook {notebookId:D} was not found.");

        var section = ResolveSection(notebook, operation.SectionId);
        var page = ResolvePage(section, operation.PageId);
        Guid? blockId = null;
        Guid? componentId = operation.ComponentId;
        Guid? itemId = operation.ItemId;

        switch (operation.Kind)
        {
            case BoardsOperationKind.AddSection:
                section = boards.AddSection(notebook, operation.Text);
                page = section.Pages[0];
                break;
            case BoardsOperationKind.AddPage:
                section ??= RequireSection(notebook, operation.SectionId);
                page = boards.AddPage(notebook, section.Id, operation.Text);
                break;
            case BoardsOperationKind.AddBlock:
                page ??= RequirePage(notebook, operation.SectionId, operation.PageId);
                var block = boards.AddBlock(notebook, page.Id, operation.BlockKind ?? NotesBlockKind.Paragraph, operation.Text);
                blockId = block.Id;
                break;
            case BoardsOperationKind.AddComponent:
                page ??= RequirePage(notebook, operation.SectionId, operation.PageId);
                var component = boards.AddComponent(
                    notebook,
                    page,
                    operation.ComponentKind ?? BoardsLiveComponentKind.TaskList);
                componentId = component.Id;
                var placement = boards.PlaceComponent(notebook, page, component.Id);
                blockId = FindPlacementBlock(page, placement.Id)?.Id;
                break;

            case BoardsOperationKind.PlaceComponent:
                page ??= RequirePage(notebook, operation.SectionId, operation.PageId);
                componentId = operation.ComponentId
                    ?? throw new ArgumentException("PlaceComponent requires ComponentId.", nameof(operation));
                var placed = boards.PlaceComponent(notebook, page, componentId.Value);
                blockId = FindPlacementBlock(page, placed.Id)?.Id;
                break;

            case BoardsOperationKind.UpdateComponentItem:
                componentId = operation.ComponentId
                    ?? throw new ArgumentException("UpdateComponentItem requires ComponentId.", nameof(operation));
                itemId = operation.ItemId
                    ?? throw new ArgumentException("UpdateComponentItem requires ItemId.", nameof(operation));
                var updated = boards.UpdateComponentItem(notebook, componentId.Value, itemId.Value, item =>
                {
                    if (operation.Text is not null) item.Text = operation.Text;
                    if (operation.Checked is bool check) item.Checked = check;
                    if (operation.Votes is int votes) item.Votes = votes;
                    if (operation.Status is not null) item.Status = operation.Status;
                });
                if (!updated)
                    throw new KeyNotFoundException("The requested Boards component item was not found.");
                break;

            case BoardsOperationKind.SetPinned:
                boards.SetPinned(notebook, operation.Pinned ?? true);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation.Kind, "Unknown Boards operation.");
        }

        notebook.UpdatedAt = DateTimeOffset.UtcNow;
        await boards.SaveAsync(notebook, $"Boards operation: {operation.Kind}", cancellationToken).ConfigureAwait(false);

        section ??= ResolveSection(notebook, operation.SectionId);
        page ??= ResolvePage(section, operation.PageId);
        var target = new BoardsDeepLink(notebook.Id, section?.Id, page?.Id).ToString();
        return new BoardsOperationResult(
            notebook.Id,
            section?.Id,
            page?.Id,
            blockId,
            componentId,
            itemId,
            target);
    }

    private static NotesSection? ResolveSection(NotesDocument notebook, Guid? id) =>
        id is Guid sectionId
            ? notebook.Sections.FirstOrDefault(item => item.Id == sectionId)
            : notebook.Sections.FirstOrDefault();

    private static NotesPage? ResolvePage(NotesSection? section, Guid? id) =>
        section is null ? null :
        id is Guid pageId
            ? section.Pages.FirstOrDefault(item => item.Id == pageId)
            : section.Pages.OrderBy(item => item.Order).FirstOrDefault();

    private static NotesSection RequireSection(NotesDocument notebook, Guid? id) =>
        ResolveSection(notebook, id)
        ?? throw new KeyNotFoundException("The requested Boards section was not found.");

    private static NotesPage RequirePage(NotesDocument notebook, Guid? sectionId, Guid? pageId)
    {
        var section = RequireSection(notebook, sectionId);
        return ResolvePage(section, pageId)
            ?? throw new KeyNotFoundException("The requested Boards page was not found.");
    }

    private static string Clean(string? text, string fallback) =>
        string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();

    private static NotesBlock CreateBlock(NotesBlockKind kind, string? text) => kind switch
    {
        NotesBlockKind.Heading => NotesBlock.Heading(Clean(text, "Heading")),
        NotesBlockKind.List => new NotesBlock
        {
            Kind = NotesBlockKind.List,
            List = new NotesListData
            {
                Kind = NotesListKind.Checklist,
                Items = [new NotesListItem { Text = Clean(text, "New task") }]
            }
        },
        NotesBlockKind.Table => NotesBlock.TableBlock(3, 3),
        NotesBlockKind.Canvas => NotesBlock.CanvasBlock(),
        NotesBlockKind.HtmlWidget => CreateHtmlBlock(text),
        _ => NotesBlock.CreateParagraph(text ?? string.Empty)
    };

    private static NotesBlock CreateHtmlBlock(string? text)
    {
        var block = NotesBlock.HtmlBlock();
        block.Html!.HtmlSource = text ?? string.Empty;
        block.Html.FallbackText = string.IsNullOrWhiteSpace(text) ? "Embedded content" : text;
        return block;
    }

    private static NotesBlock? FindPlacementBlock(NotesPage page, Guid placementId) =>
        page.Blocks.FirstOrDefault(block =>
            block.Metadata.TryGetValue(BoardsWorkspaceService.PlacementIdKey, out var raw)
            && Guid.TryParse(raw, out var id)
            && id == placementId);
}
