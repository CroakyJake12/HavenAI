using Haven.Core;

namespace Haven.Application;

public sealed partial class BoardsWorkspaceService
{
    public async Task RenameNotebookAsync(
        NotesDocument notebook,
        string title,
        CancellationToken cancellationToken)
    {
        EnsureBoards(notebook);
        var stableId = notebook.Id;
        notebook.Title = string.IsNullOrWhiteSpace(title) ? notebook.Title : title.Trim();
        notebook.UpdatedAt = DateTimeOffset.UtcNow;
        await repository.SaveAsync(notebook, "Rename Boards notebook", cancellationToken).ConfigureAwait(false);
        if (notebook.Id != stableId)
            throw new InvalidOperationException("Renaming changed the Boards notebook identity.");
    }

    public NotesSection AddSection(NotesDocument notebook, string? title = null)
    {
        EnsureBoards(notebook);
        var section = NotesSection.CreateDefault();
        section.Title = string.IsNullOrWhiteSpace(title)
            ? $"Section {notebook.Sections.Count + 1}"
            : title.Trim();
        section.Pages[0].Title = "New page";
        section.Pages[0].Order = 0;
        notebook.Sections.Add(section);
        TouchRecovery(notebook);
        return section;
    }

    public NotesPage AddPage(NotesDocument notebook, Guid sectionId, string? title = null)
    {
        var section = RequireRecoverySection(notebook, sectionId);
        var page = NotesPage.CreateDefault();
        page.Title = string.IsNullOrWhiteSpace(title)
            ? $"Page {section.Pages.Count + 1}"
            : title.Trim();
        page.Order = section.Pages.Count;
        section.Pages.Add(page);
        NormalizePages(section);
        TouchRecovery(notebook);
        return page;
    }

    public void MoveSection(NotesDocument notebook, Guid sectionId, int targetIndex)
    {
        EnsureBoards(notebook);
        var index = notebook.Sections.FindIndex(section => section.Id == sectionId);
        if (index < 0)
            throw new KeyNotFoundException("The requested Boards section was not found.");

        var section = notebook.Sections[index];
        notebook.Sections.RemoveAt(index);
        notebook.Sections.Insert(Math.Clamp(targetIndex, 0, notebook.Sections.Count), section);
        TouchRecovery(notebook);
    }

    public void MovePage(NotesDocument notebook, Guid pageId, Guid targetSectionId, int targetIndex)
    {
        EnsureBoards(notebook);
        var source = notebook.Sections.FirstOrDefault(section => section.Pages.Any(page => page.Id == pageId))
            ?? throw new KeyNotFoundException("The requested Boards page was not found.");
        var target = RequireRecoverySection(notebook, targetSectionId);
        var page = source.Pages.Single(value => value.Id == pageId);

        source.Pages.Remove(page);
        target.Pages.Insert(Math.Clamp(targetIndex, 0, target.Pages.Count), page);
        NormalizePages(source);
        if (!ReferenceEquals(source, target))
            NormalizePages(target);
        TouchRecovery(notebook);
    }

    public void MoveBlock(NotesDocument notebook, Guid pageId, Guid blockId, int targetIndex)
    {
        var page = RequireRecoveryPage(notebook, pageId);
        var index = page.Blocks.FindIndex(block => block.Id == blockId);
        if (index < 0)
            throw new KeyNotFoundException("The requested Boards block was not found.");

        var block = page.Blocks[index];
        page.Blocks.RemoveAt(index);
        page.Blocks.Insert(Math.Clamp(targetIndex, 0, page.Blocks.Count), block);
        for (var order = 0; order < page.Blocks.Count; order++)
            page.Blocks[order].Order = order;
        TouchRecovery(notebook);
    }

    public NotesBlock AddBlock(NotesDocument notebook, Guid pageId, NotesBlockKind kind, string? text = null)
    {
        var page = RequireRecoveryPage(notebook, pageId);
        var block = kind switch
        {
            NotesBlockKind.List => new NotesBlock { Kind = NotesBlockKind.List, List = new NotesListData { Kind = NotesListKind.Checklist, Items = [new NotesListItem { Text = string.IsNullOrWhiteSpace(text) ? "New task" : text.Trim() }] } },
            NotesBlockKind.Table => NotesBlock.TableBlock(3, 3),
            NotesBlockKind.Heading => NotesBlock.Heading(string.IsNullOrWhiteSpace(text) ? "Heading" : text.Trim()),
            NotesBlockKind.Canvas => NotesBlock.CanvasBlock(),
            NotesBlockKind.HtmlWidget => NotesBlock.HtmlBlock(),
            _ => NotesBlock.CreateParagraph(text ?? string.Empty)
        };
        if (block.Canvas is not null) block.Canvas.Infinite = true;
        if (block.Html is not null)
        {
            block.Html.HtmlSource = text ?? string.Empty;
            block.Html.FallbackText = string.IsNullOrWhiteSpace(text) ? "Embedded content" : text.Trim();
        }
        block.Order = page.Blocks.Count;
        page.Blocks.Add(block);
        TouchRecovery(notebook);
        return block;
    }

    public bool RenamePage(NotesDocument notebook, Guid pageId, string? title)
    {
        var page = RequireRecoveryPage(notebook, pageId);
        page.Title = string.IsNullOrWhiteSpace(title) ? "Untitled page" : title.Trim();
        TouchRecovery(notebook);
        return true;
    }

    public bool UpdateListItem(NotesDocument notebook, Guid pageId, Guid blockId, Guid itemId, string? text = null, bool? isChecked = null)
    {
        var page = RequireRecoveryPage(notebook, pageId);
        var block = page.Blocks.FirstOrDefault(value => value.Id == blockId);
        var item = block?.List?.Items.FirstOrDefault(value => value.Id == itemId);
        if (item is null) return false;
        if (text is not null) item.Text = text;
        if (isChecked is bool check) item.Checked = check;
        TouchRecovery(notebook);
        return true;
    }

    public bool UpdateTableCell(NotesDocument notebook, Guid pageId, Guid blockId, Guid cellId, string? text)
    {
        var page = RequireRecoveryPage(notebook, pageId);
        var block = page.Blocks.FirstOrDefault(value => value.Id == blockId);
        var cell = block?.Table?.Rows.SelectMany(row => row.Cells).FirstOrDefault(value => value.Id == cellId);
        if (cell is null) return false;
        cell.Text = text ?? string.Empty;
        TouchRecovery(notebook);
        return true;
    }

    public bool IsPinned(NotesDocument notebook)
    {
        EnsureBoards(notebook);
        return notebook.Metadata.TryGetValue("boards.pinned", out var raw)
            && bool.TryParse(raw, out var pinned)
            && pinned;
    }

    public void SetPinned(NotesDocument notebook, bool pinned)
    {
        EnsureBoards(notebook);
        notebook.Metadata["boards.pinned"] = pinned.ToString();
        TouchRecovery(notebook);
    }

    public NotesCanvasObject AddCanvasObject(
        NotesDocument notebook,
        Guid pageId,
        NotesCanvasObjectKind kind,
        string? text,
        double x,
        double y,
        double width = 260,
        double height = 160)
    {
        var page = RequireRecoveryPage(notebook, pageId);
        var value = new NotesCanvasObject
        {
            Kind = kind,
            Text = text?.Trim() ?? string.Empty,
            X = Math.Max(0, x),
            Y = Math.Max(0, y),
            Width = Math.Clamp(width, 24, 5000),
            Height = Math.Clamp(height, 24, 5000),
            ZIndex = page.CanvasObjects.Select(item => item.ZIndex).DefaultIfEmpty(-1).Max() + 1
        };
        page.CanvasObjects.Add(value);
        GrowCanvas(page, value);
        TouchRecovery(notebook);
        return value;
    }

    public bool MoveCanvasObject(
        NotesDocument notebook,
        Guid pageId,
        Guid objectId,
        double x,
        double y)
    {
        var page = RequireRecoveryPage(notebook, pageId);
        var value = page.CanvasObjects.FirstOrDefault(item => item.Id == objectId);
        if (value is null)
            return false;

        NotesCanvasOperations.Move(value, Math.Max(0, x), Math.Max(0, y), 0);
        GrowCanvas(page, value);
        TouchRecovery(notebook);
        return true;
    }

    public bool ResizeCanvasObject(
        NotesDocument notebook,
        Guid pageId,
        Guid objectId,
        double width,
        double height)
    {
        var page = RequireRecoveryPage(notebook, pageId);
        var value = page.CanvasObjects.FirstOrDefault(item => item.Id == objectId);
        if (value is null)
            return false;

        NotesCanvasOperations.Resize(
            value,
            Math.Clamp(width, 24, 5000),
            Math.Clamp(height, 24, 5000),
            0);
        GrowCanvas(page, value);
        TouchRecovery(notebook);
        return true;
    }

    public bool UpdateCanvasObjectText(NotesDocument notebook, Guid pageId, Guid objectId, string? text)
    {
        var page = RequireRecoveryPage(notebook, pageId);
        var value = page.CanvasObjects.FirstOrDefault(item => item.Id == objectId);
        if (value is null)
            return false;
        value.Text = text?.Trim() ?? string.Empty;
        TouchRecovery(notebook);
        return true;
    }

    public async Task<NotesBlock> AttachAsync(
        NotesDocument notebook,
        Guid pageId,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        EnsureBoards(notebook);
        if (attachments is null)
            throw new InvalidOperationException("The shared Haven attachment store is unavailable.");

        var page = RequireRecoveryPage(notebook, pageId);
        var media = await attachments.ImportAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var block = new NotesBlock
        {
            Kind = media.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                ? NotesBlockKind.Image
                : media.MediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
                    ? NotesBlockKind.Audio
                    : media.MediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                        ? NotesBlockKind.Video
                        : NotesBlockKind.Paragraph,
            Media = media,
            PlainText = media.OriginalName,
            Order = page.Blocks.Count
        };
        page.Blocks.Add(block);
        TouchRecovery(notebook);
        return block;
    }

    public async Task<BoardsAttachmentResolution> ResolveAttachmentAsync(
        NotesMediaData media,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(media);
        if (attachments is null)
        {
            return new BoardsAttachmentResolution(
                media.AttachmentId,
                BoardsAttachmentStatus.Unavailable,
                null,
                "The shared Haven attachment store is unavailable.");
        }

        try
        {
            var path = await attachments.ResolvePathAsync(media.AttachmentId, cancellationToken)
                .ConfigureAwait(false);
            return File.Exists(path)
                ? new BoardsAttachmentResolution(
                    media.AttachmentId,
                    BoardsAttachmentStatus.Available,
                    path,
                    $"Attachment '{media.OriginalName}' is available.")
                : new BoardsAttachmentResolution(
                    media.AttachmentId,
                    BoardsAttachmentStatus.Missing,
                    null,
                    $"Attachment '{media.OriginalName}' is missing.");
        }
        catch (FileNotFoundException)
        {
            return new BoardsAttachmentResolution(
                media.AttachmentId,
                BoardsAttachmentStatus.Missing,
                null,
                $"Attachment '{media.OriginalName}' is missing.");
        }
        catch (UnauthorizedAccessException)
        {
            return new BoardsAttachmentResolution(
                media.AttachmentId,
                BoardsAttachmentStatus.Unavailable,
                null,
                $"Attachment '{media.OriginalName}' cannot be accessed.");
        }
        catch (IOException)
        {
            return new BoardsAttachmentResolution(
                media.AttachmentId,
                BoardsAttachmentStatus.Unavailable,
                null,
                $"Attachment '{media.OriginalName}' is temporarily unavailable.");
        }
    }

    public bool UpdateComponentSource(
        NotesDocument notebook,
        Guid componentId,
        Action<BoardsLiveComponentSource> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var components = Read<List<BoardsLiveComponent>>(notebook, ComponentsKey) ?? [];
        var component = components.FirstOrDefault(item => item.Id == componentId);
        if (component is null)
            return false;

        update(component.Source);
        component.Source.Provider = TrimBound(component.Source.Provider, 128, "Haven");
        component.Source.ResourceId = TrimBound(component.Source.ResourceId, 256, string.Empty);
        component.Source.DisplayName = TrimBound(
            component.Source.DisplayName,
            256,
            component.Source.Provider);
        component.Source.UnavailableReason = TrimBound(
            component.Source.UnavailableReason,
            512,
            string.Empty);
        component.Version = checked(component.Version + 1);
        component.UpdatedAt = DateTimeOffset.UtcNow;
        Write(notebook, ComponentsKey, components);
        RefreshRecoveryPlacements(notebook, component);
        TouchRecovery(notebook);
        return true;
    }

    public IReadOnlyList<BoardsLiveComponentPlacement> GetPlacements(NotesDocument notebook)
    {
        EnsureBoards(notebook);
        return Read<List<BoardsLiveComponentPlacement>>(notebook, PlacementsKey) ?? [];
    }

    private static void RefreshRecoveryPlacements(
        NotesDocument notebook,
        BoardsLiveComponent component)
    {
        var id = component.Id.ToString("D");
        foreach (var block in notebook.Sections
                     .SelectMany(section => section.Pages)
                     .SelectMany(page => page.Blocks))
        {
            if (block.Metadata.TryGetValue(ComponentIdKey, out var raw)
                && raw.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                RefreshBlock(block, component);
            }
        }
    }

    private static string LiveTitle(BoardsLiveComponent component) => component.Source.Availability switch
    {
        BoardsLiveAvailability.Stale => component.Title + " ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â· stale",
        BoardsLiveAvailability.Unavailable => component.Title + " ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â· unavailable",
        _ => component.Title
    };

    private static string LiveUnavailableReason(BoardsLiveComponent component) =>
        component.Source.Availability == BoardsLiveAvailability.Unavailable
        && !string.IsNullOrWhiteSpace(component.Source.UnavailableReason)
            ? Environment.NewLine + component.Source.UnavailableReason
            : string.Empty;

    private static NotesSection RequireRecoverySection(NotesDocument notebook, Guid sectionId)
    {
        EnsureBoards(notebook);
        return notebook.Sections.FirstOrDefault(section => section.Id == sectionId)
            ?? throw new KeyNotFoundException("The requested Boards section was not found.");
    }

    private static NotesPage RequireRecoveryPage(NotesDocument notebook, Guid pageId)
    {
        EnsureBoards(notebook);
        return notebook.Sections
            .SelectMany(section => section.Pages)
            .FirstOrDefault(page => page.Id == pageId)
            ?? throw new KeyNotFoundException("The requested Boards page was not found.");
    }

    private static void NormalizePages(NotesSection section)
    {
        for (var index = 0; index < section.Pages.Count; index++)
            section.Pages[index].Order = index;
    }

    private static void GrowCanvas(NotesPage page, NotesCanvasObject value)
    {
        page.CanvasWidth = Math.Max(page.CanvasWidth, value.X + value.Width + 80);
        page.CanvasHeight = Math.Max(page.CanvasHeight, value.Y + value.Height + 80);
    }

    private static void TouchRecovery(NotesDocument notebook) =>
        notebook.UpdatedAt = DateTimeOffset.UtcNow;

    private static string TrimBound(string? value, int maximum, string fallback)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return clean.Length <= maximum ? clean : clean[..maximum];
    }
}
