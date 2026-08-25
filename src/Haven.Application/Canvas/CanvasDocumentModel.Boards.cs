using Haven.Core;

namespace Haven.Application;

public static partial class CanvasDocumentModel
{
    public static int GetBoardCount(NotesDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsureCanvasPages(document);
        return document.Sections[0].Pages.Count;
    }

    public static IReadOnlyList<string> GetBoardTitles(NotesDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsureCanvasPages(document);
        return document.Sections[0].Pages
            .Select((page, index) => string.IsNullOrWhiteSpace(page.Title) ? $"Board {index + 1}" : page.Title)
            .ToArray();
    }

    public static NotesCanvasData GetBoard(NotesDocument document, int boardIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (boardIndex == 0) return GetBoard(document);
        EnsureCanvasPages(document);
        boardIndex = Math.Clamp(boardIndex, 0, document.Sections[0].Pages.Count - 1);
        return EnsureBoardOnPage(document.Sections[0].Pages[boardIndex]);
    }

    public static int AddBoard(NotesDocument document, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsureCanvasPages(document);
        var section = document.Sections[0];
        var page = NotesPage.CreateDefault();
        page.Order = section.Pages.Count;
        page.Title = string.IsNullOrWhiteSpace(title) ? $"Board {section.Pages.Count + 1}" : title.Trim();
        page.Blocks.Clear();
        page.CanvasObjects.Clear();

        var block = NotesBlock.CanvasBlock();
        block.Order = 0;
        block.Canvas!.Infinite = true;
        page.Blocks.Add(block);

        section.Pages.Add(page);
        document.UpdatedAt = DateTimeOffset.UtcNow;
        return section.Pages.Count - 1;
    }

    public static bool RenameBoard(NotesDocument document, int boardIndex, string? title)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsureCanvasPages(document);
        if (boardIndex < 0 || boardIndex >= document.Sections[0].Pages.Count) return false;
        var next = string.IsNullOrWhiteSpace(title) ? $"Board {boardIndex + 1}" : title.Trim();
        if (string.Equals(document.Sections[0].Pages[boardIndex].Title, next, StringComparison.Ordinal)) return false;
        document.Sections[0].Pages[boardIndex].Title = next;
        document.UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    public static bool RemoveBoard(NotesDocument document, int boardIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsureCanvasPages(document);
        var pages = document.Sections[0].Pages;
        if (pages.Count <= 1 || boardIndex < 0 || boardIndex >= pages.Count) return false;
        pages.RemoveAt(boardIndex);
        for (var index = 0; index < pages.Count; index++) pages[index].Order = index;
        document.UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    public static void ReplaceBoard(NotesDocument document, NotesCanvasData replacement, int boardIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(replacement);
        if (boardIndex == 0)
        {
            ReplaceBoard(document, replacement);
            return;
        }

        EnsureCanvasPages(document);
        boardIndex = Math.Clamp(boardIndex, 0, document.Sections[0].Pages.Count - 1);
        var page = document.Sections[0].Pages[boardIndex];
        var current = EnsureBoardOnPage(page);
        var block = page.Blocks.First(candidate =>
            candidate.Kind == NotesBlockKind.Canvas && ReferenceEquals(candidate.Canvas, current));
        block.Canvas = replacement;
        document.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void EnsureCanvasPages(NotesDocument document)
    {
        if (!IsCanvasDocument(document))
            throw new InvalidOperationException("This Notes document is not a Haven Canvas board.");

        document.LayoutMode = NotesLayoutMode.InfiniteCanvas;
        document.Metadata[SchemaMetadataKey] = SchemaVersion;
        document.Sections ??= [NotesSection.CreateDefault()];
        if (document.Sections.Count == 0) document.Sections.Add(NotesSection.CreateDefault());
        document.Sections[0].Pages ??= [NotesPage.CreateDefault()];
        if (document.Sections[0].Pages.Count == 0) document.Sections[0].Pages.Add(NotesPage.CreateDefault());
    }

    private static NotesCanvasData EnsureBoardOnPage(NotesPage page)
    {
        page.Blocks ??= [];
        page.CanvasObjects ??= [];
        var block = page.Blocks.FirstOrDefault(candidate =>
            candidate.Kind == NotesBlockKind.Canvas && candidate.Canvas is not null);

        if (block is null)
        {
            block = NotesBlock.CanvasBlock();
            block.Order = page.Blocks.Count == 0 ? 0 : page.Blocks.Max(candidate => candidate.Order) + 1;
            page.Blocks.Add(block);
        }

        var board = block.Canvas ??= new NotesCanvasData { Infinite = true };
        board.Objects ??= [];
        board.Strokes ??= [];
        board.GhostLayers ??= [];

        if (page.CanvasObjects.Count > 0)
        {
            var ids = board.Objects.Select(value => value.Id).ToHashSet();
            foreach (var value in page.CanvasObjects)
                if (ids.Add(value.Id)) board.Objects.Add(value);
            page.CanvasObjects.Clear();
        }

        return board;
    }
}
