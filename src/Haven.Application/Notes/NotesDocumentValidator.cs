/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/NotesDocumentValidator.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns NotesDocumentValidator. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.RegularExpressions;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents notes document validator and keeps its related state and behavior together.
/// </summary>
public sealed partial class NotesDocumentValidator : INotesDocumentValidator
{
    /// <summary>
    /// Stores maximum sections locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int MaximumSections = 500;
    /// <summary>
    /// Stores maximum pages per section locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int MaximumPagesPerSection = 2_000;
    /// <summary>
    /// Stores maximum blocks per page locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int MaximumBlocksPerPage = 20_000;
    /// <summary>
    /// Stores maximum strokes per canvas locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int MaximumStrokesPerCanvas = 100_000;
    /// <summary>
    /// Stores maximum points per stroke locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int MaximumPointsPerStroke = 200_000;

    /// <summary>
    /// Validates this member before it crosses the next trust or persistence boundary.
    /// </summary>
    public NotesValidationResult Validate(NotesDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var issues = new List<NotesValidationIssue>();

        if (document.SchemaVersion != NotesDocument.CurrentSchemaVersion)
            Error("schemaVersion", $"Unsupported Notes schema version {document.SchemaVersion}.");
        if (document.Id == Guid.Empty) Error("id", "Document ID cannot be empty.");
        if (string.IsNullOrWhiteSpace(document.Title)) Error("title", "A document title is required.");
        if (document.Title.Length > 240) Error("title", "Document titles are limited to 240 characters.");
        if (document.Sections.Count is 0 or > MaximumSections)
            Error("sections", $"A document requires 1–{MaximumSections} sections.");
        if (document.Version < 0) Error("version", "Document version cannot be negative.");
        if (document.CreatedAt > document.UpdatedAt) Error("updatedAt", "Updated time cannot precede creation time.");
        ValidatePageSetup(document.PageSetup);

        var identifiers = new HashSet<Guid>();
        AddId(document.Id, "id");
        foreach (var section in document.Sections)
        {
            AddId(section.Id, $"sections[{section.Title}].id");
            if (string.IsNullOrWhiteSpace(section.Title)) Error($"sections[{section.Id}].title", "Section titles are required.");
            if (section.Pages.Count is 0 or > MaximumPagesPerSection)
                Error($"sections[{section.Id}].pages", $"Sections require 1–{MaximumPagesPerSection} pages.");

            var pageOrders = new HashSet<int>();
            foreach (var page in section.Pages)
            {
                AddId(page.Id, $"pages[{page.Title}].id");
                if (page.Order < 0 || !pageOrders.Add(page.Order))
                    Error($"pages[{page.Id}].order", "Page order must be unique and non-negative within its section.");
                if (page.CanvasWidth is < 100 or > 1_000_000 || page.CanvasHeight is < 100 or > 1_000_000)
                    Error($"pages[{page.Id}].canvas", "Canvas dimensions must be between 100 and 1,000,000 units.");
                if (page.Blocks.Count > MaximumBlocksPerPage)
                    Error($"pages[{page.Id}].blocks", $"Pages support at most {MaximumBlocksPerPage} blocks.");

                var blockOrders = new HashSet<int>();
                foreach (var block in page.Blocks)
                {
                    AddId(block.Id, $"blocks[{block.Id}].id");
                    if (!blockOrders.Add(block.Order))
                        Error($"blocks[{block.Id}].order", "Block order must be unique within its page.");
                    ValidateBlock(block, page.Id);
                }

                foreach (var canvasObject in page.CanvasObjects)
                {
                    AddId(canvasObject.Id, $"canvasObjects[{canvasObject.Id}].id");
                    ValidateCanvasObject(canvasObject, page.Id);
                }
            }
        }

        foreach (var citation in document.Citations)
        {
            AddId(citation.Id, $"citations[{citation.Key}].id");
            if (string.IsNullOrWhiteSpace(citation.Key)) Error($"citations[{citation.Id}].key", "Citation keys are required.");
            if (!string.IsNullOrWhiteSpace(citation.Url))
            {
                if (!Uri.TryCreate(citation.Url, UriKind.Absolute, out var citationUri))
                    Error($"citations[{citation.Id}].url", "Citation URLs must be absolute.");
                else if (citationUri.Scheme is not "http" and not "https")
                    Error($"citations[{citation.Id}].url", "Citation URLs must use HTTP or HTTPS.");
            }
        }

        foreach (var comment in document.Comments)
        {
            AddId(comment.Id, $"comments[{comment.Id}].id");
            if (!identifiers.Contains(comment.BlockId))
                Error($"comments[{comment.Id}].blockId", "Comment target does not exist in this document.");
            if (comment.StartOffset < 0 || comment.EndOffset < comment.StartOffset)
                Error($"comments[{comment.Id}].range", "Comment ranges are invalid.");
            if (string.IsNullOrWhiteSpace(comment.Text)) Error($"comments[{comment.Id}].text", "Comments cannot be empty.");
            foreach (var reply in comment.Replies) AddId(reply.Id, $"comments[{comment.Id}].replies[{reply.Id}]");
        }

        foreach (var revision in document.Revisions) AddId(revision.Id, $"revisions[{revision.Id}].id");
        foreach (var change in document.AiChanges)
        {
            AddId(change.Id, $"aiChanges[{change.Id}].id");
            if (change.Status == NotesAiChangeStatus.Applied && change.ReviewedAt is null)
                Error($"aiChanges[{change.Id}].reviewedAt", "Applied AI changes require a recorded review time.");
            if (change.Status == NotesAiChangeStatus.Applied && !change.UserConsentRecorded)
                Error($"aiChanges[{change.Id}].userConsentRecorded", "Applied AI changes require explicit user consent.");
            if (change.BlockId is { } blockId && !identifiers.Contains(blockId))
                Error($"aiChanges[{change.Id}].blockId", "AI change target does not exist.");
        }

        foreach (var bookmark in document.Bookmarks)
        {
            AddId(bookmark.Id, $"bookmarks[{bookmark.Id}].id");
            if (!identifiers.Contains(bookmark.BlockId))
                Error($"bookmarks[{bookmark.Id}].blockId", "Bookmark target does not exist.");
        }

        foreach (var conflict in document.Collaboration.Conflicts) AddId(conflict.Id, $"conflicts[{conflict.Id}].id");
        return new NotesValidationResult(!issues.Any(issue => issue.IsError), issues);

        void AddId(Guid id, string path)
        {
            if (id == Guid.Empty) { Error(path, "IDs cannot be empty."); return; }
            if (!identifiers.Add(id)) Error(path, "IDs must be unique across the document graph.");
        }

        void Error(string path, string message) => issues.Add(new NotesValidationIssue(path, message, true));

        void ValidatePageSetup(NotesPageSetup setup)
        {
            if (setup.WidthPoints is < 72 or > 5_000 || setup.HeightPoints is < 72 or > 5_000)
                Error("pageSetup.dimensions", "Page dimensions must be between 1 and roughly 69 inches.");
            if (new[] { setup.MarginTopPoints, setup.MarginRightPoints, setup.MarginBottomPoints, setup.MarginLeftPoints }.Any(value => value is < 0 or > 1_000))
                Error("pageSetup.margins", "Page margins must be between 0 and 1,000 points.");
            if (setup.MarginLeftPoints + setup.MarginRightPoints >= setup.WidthPoints)
                Error("pageSetup.margins", "Horizontal margins leave no writable page width.");
            if (setup.MarginTopPoints + setup.MarginBottomPoints >= setup.HeightPoints)
                Error("pageSetup.margins", "Vertical margins leave no writable page height.");
            if (!ColourPattern().IsMatch(setup.Background))
                Error("pageSetup.background", "Page background must use #RRGGBB or #AARRGGBB.");
        }

        void ValidateBlock(NotesBlock block, Guid pageId)
        {
            if (!Enum.IsDefined(block.Kind)) { Error($"pages[{pageId}].blocks[{block.Id}].kind", "Unknown block kind."); return; }
            if (block.Order < 0) Error($"blocks[{block.Id}].order", "Block order cannot be negative.");
            if (block.PlainText.Length > 10_000_000) Error($"blocks[{block.Id}].plainText", "A single text block is limited to ten million characters.");
            if (block.Paragraph.LineSpacing is < 0.5 or > 10) Error($"blocks[{block.Id}].paragraph.lineSpacing", "Line spacing must be between 0.5 and 10.");
            foreach (var run in block.Runs)
            {
                AddId(run.Id, $"blocks[{block.Id}].runs[{run.Id}].id");
                if (run.FontSize is < 4 or > 300) Error($"blocks[{block.Id}].runs[{run.Id}].fontSize", "Font size must be between 4 and 300.");
                if (!ColourPattern().IsMatch(run.Foreground) || !ColourPattern().IsMatch(run.Background))
                    Error($"blocks[{block.Id}].runs[{run.Id}].colour", "Text colours must use #RRGGBB or #AARRGGBB.");
                if (!string.IsNullOrWhiteSpace(run.Link)
                    && (!Uri.TryCreate(run.Link, UriKind.Absolute, out var link)
                        || link.Scheme is not "http" and not "https" and not "mailto"))
                    Error($"blocks[{block.Id}].runs[{run.Id}].link", "Links must use HTTP, HTTPS or mailto.");
            }

            switch (block.Kind)
            {
                case NotesBlockKind.List:
                    if (block.List is null) Error($"blocks[{block.Id}].list", "List blocks require list data.");
                    else foreach (var item in block.List.Items) AddId(item.Id, $"blocks[{block.Id}].list.items[{item.Id}]");
                    break;
                case NotesBlockKind.Table:
                    ValidateTable(block);
                    break;
                case NotesBlockKind.Image:
                case NotesBlockKind.Audio:
                case NotesBlockKind.Video:
                    ValidateMedia(block);
                    break;
                case NotesBlockKind.Equation:
                    ValidateEquation(block);
                    break;
                case NotesBlockKind.HtmlWidget:
                    ValidateHtml(block);
                    break;
                case NotesBlockKind.Canvas:
                    ValidateCanvas(block, pageId);
                    break;
                case NotesBlockKind.Flashcard:
                    ValidateFlashcard(block);
                    break;
                case NotesBlockKind.Shape:
                    ValidateVectorShape(block.VectorShape, $"blocks[{block.Id}].vectorShape");
                    break;
            }
        }

        void ValidateTable(NotesBlock block)
        {
            if (block.Table is null) { Error($"blocks[{block.Id}].table", "Table blocks require table data."); return; }
            if (block.Table.Rows.Count is 0 or > 10_000) Error($"blocks[{block.Id}].table.rows", "Tables require 1–10,000 rows.");
            var columnCount = block.Table.Rows.FirstOrDefault()?.Cells.Count ?? 0;
            if (columnCount is 0 or > 500) Error($"blocks[{block.Id}].table.columns", "Tables require 1–500 columns.");
            foreach (var row in block.Table.Rows)
            {
                AddId(row.Id, $"blocks[{block.Id}].table.rows[{row.Id}]");
                if (row.Cells.Count != columnCount) Error($"blocks[{block.Id}].table.rows[{row.Id}]", "Every table row must have the same number of cells.");
                foreach (var cell in row.Cells)
                {
                    AddId(cell.Id, $"blocks[{block.Id}].table.cells[{cell.Id}]");
                    if (cell.RowSpan is < 1 or > 10_000 || cell.ColumnSpan is < 1 or > 500)
                        Error($"blocks[{block.Id}].table.cells[{cell.Id}].span", "Table spans are outside supported bounds.");
                }
            }
        }

        void ValidateMedia(NotesBlock block)
        {
            if (block.Media is null) { Error($"blocks[{block.Id}].media", "Media blocks require media metadata."); return; }
            if (block.Media.AttachmentId == Guid.Empty) Error($"blocks[{block.Id}].media.attachmentId", "Media attachment IDs are required.");
            if (block.Media.SizeBytes < 0) Error($"blocks[{block.Id}].media.sizeBytes", "Media size cannot be negative.");
            if (block.Media.Width <= 0 || block.Media.Height <= 0) Error($"blocks[{block.Id}].media.dimensions", "Media dimensions must be positive.");
            if (block.Media.StoredPath.Contains("..", StringComparison.Ordinal)) Error($"blocks[{block.Id}].media.storedPath", "Stored media paths cannot contain traversal segments.");
        }

        void ValidateEquation(NotesBlock block)
        {
            if (block.Equation is null) { Error($"blocks[{block.Id}].equation", "Equation blocks require equation data."); return; }
            if (string.IsNullOrWhiteSpace(block.Equation.Source)) Error($"blocks[{block.Id}].equation.source", "Equation source is required.");
            if (block.Equation.Source.Length > 1_000_000) Error($"blocks[{block.Id}].equation.source", "Equation source is limited to one million characters.");
            if (block.Equation.Numbered && block.Equation.Number is < 1) Error($"blocks[{block.Id}].equation.number", "Numbered equations require a positive number.");
            foreach (var stroke in block.Equation.SourceStrokes) ValidateStroke(stroke, $"blocks[{block.Id}].equation.sourceStrokes");
        }

        void ValidateHtml(NotesBlock block)
        {
            if (block.Html is null) { Error($"blocks[{block.Id}].html", "HTML blocks require source and sandbox data."); return; }
            if (block.Html.HtmlSource.Length + block.Html.CssSource.Length + block.Html.JavaScriptSource.Length > 5_000_000)
                Error($"blocks[{block.Id}].html", "A widget's combined source is limited to five million characters.");
            if (!block.Html.AllowScripts && !string.IsNullOrWhiteSpace(block.Html.JavaScriptSource))
                Error($"blocks[{block.Id}].html.allowScripts", "JavaScript source requires explicit script permission.");
            if (!block.Html.AllowNetwork && NetworkReferencePattern().IsMatch(block.Html.HtmlSource + block.Html.CssSource + block.Html.JavaScriptSource))
                Error($"blocks[{block.Id}].html.allowNetwork", "Network references require explicit network permission.");
            if (!block.Html.AllowForms && FormPattern().IsMatch(block.Html.HtmlSource))
                Error($"blocks[{block.Id}].html.allowForms", "Forms require explicit form permission.");
            if (block.Html.AllowPopups)
                Error($"blocks[{block.Id}].html.allowPopups", "Popups are not supported by the Notes sandbox.");
            if (block.Html.Width is < 64 or > 10_000 || block.Html.Height is < 64 or > 10_000)
                Error($"blocks[{block.Id}].html.dimensions", "Widget dimensions must be between 64 and 10,000 pixels.");
        }

        void ValidateCanvas(NotesBlock block, Guid pageId)
        {
            if (block.Canvas is null) { Error($"blocks[{block.Id}].canvas", "Canvas blocks require canvas data."); return; }
            if (block.Canvas.Width is < 100 or > 1_000_000 || block.Canvas.Height is < 100 or > 1_000_000)
                Error($"blocks[{block.Id}].canvas.dimensions", "Canvas dimensions are outside supported bounds.");
            if (block.Canvas.Zoom is < 0.05 or > 100) Error($"blocks[{block.Id}].canvas.zoom", "Canvas zoom must be between 5% and 10,000%.");
            if (block.Canvas.Strokes.Count > MaximumStrokesPerCanvas)
                Error($"blocks[{block.Id}].canvas.strokes", $"Canvas supports at most {MaximumStrokesPerCanvas} strokes.");
            foreach (var canvasObject in block.Canvas.Objects)
            {
                AddId(canvasObject.Id, $"blocks[{block.Id}].canvas.objects[{canvasObject.Id}]");
                ValidateCanvasObject(canvasObject, pageId);
            }
            foreach (var stroke in block.Canvas.Strokes) ValidateStroke(stroke, $"blocks[{block.Id}].canvas.strokes");
            foreach (var layer in block.Canvas.GhostLayers)
            {
                AddId(layer.Id, $"blocks[{block.Id}].canvas.ghostLayers[{layer.Id}]");
                foreach (var mask in layer.Masks)
                {
                    AddId(mask.Id, $"blocks[{block.Id}].canvas.ghostLayers[{layer.Id}].masks[{mask.Id}]");
                    if (mask.Width <= 0 || mask.Height <= 0) Error($"masks[{mask.Id}].dimensions", "Occlusion masks require positive dimensions.");
                }
            }
        }

        void ValidateFlashcard(NotesBlock block)
        {
            if (block.Flashcard is null) { Error($"blocks[{block.Id}].flashcard", "Flashcard blocks require flashcard data."); return; }
            if (block.Flashcard.CardId == Guid.Empty) Error($"blocks[{block.Id}].flashcard.cardId", "Flashcard IDs are required.");
            if (string.IsNullOrWhiteSpace(block.Flashcard.Front)) Error($"blocks[{block.Id}].flashcard.front", "Flashcard fronts cannot be empty.");
            if (string.IsNullOrWhiteSpace(block.Flashcard.Back)) Error($"blocks[{block.Id}].flashcard.back", "Flashcard backs cannot be empty.");
            if (block.Flashcard.Schedule.EaseFactor is < 1.3 or > 3.2) Error($"blocks[{block.Id}].flashcard.schedule.easeFactor", "Ease factor must be between 1.3 and 3.2.");
            foreach (var mask in block.Flashcard.OcclusionMasks)
            {
                AddId(mask.Id, $"blocks[{block.Id}].flashcard.masks[{mask.Id}]");
                if (mask.Width <= 0 || mask.Height <= 0) Error($"blocks[{block.Id}].flashcard.masks[{mask.Id}]", "Occlusion masks require positive dimensions.");
            }
        }

        void ValidateCanvasObject(NotesCanvasObject canvasObject, Guid pageId)
        {
            if (!Enum.IsDefined(canvasObject.Kind)) Error($"pages[{pageId}].canvasObjects[{canvasObject.Id}].kind", "Unknown canvas object kind.");
            if (canvasObject.Width <= 0 || canvasObject.Height <= 0) Error($"pages[{pageId}].canvasObjects[{canvasObject.Id}].dimensions", "Canvas objects require positive dimensions.");
            if (canvasObject.StyleJson.Length > 100_000) Error($"pages[{pageId}].canvasObjects[{canvasObject.Id}].styleJson", "Canvas object style data is too large.");
            if (canvasObject.VectorShape is not null) ValidateVectorShape(canvasObject.VectorShape, $"pages[{pageId}].canvasObjects[{canvasObject.Id}].vectorShape");
        }

        void ValidateVectorShape(DocumentVectorShape? shape, string path)
        {
            if (shape is null) { Error(path, "Native shape objects require editable vector geometry."); return; }
            var result = DocumentVectorShapeValidator.Validate(shape);
            foreach (var issue in result.Issues.Where(issue => issue.Severity == DocumentValidationSeverity.Error))
                Error(path + "." + issue.Code, issue.Message);
        }

        void ValidateStroke(NotesInkStroke stroke, string path)
        {
            AddId(stroke.Id, $"{path}[{stroke.Id}].id");
            if (stroke.Points.Count is 0 or > MaximumPointsPerStroke)
                Error($"{path}[{stroke.Id}].points", $"Ink strokes require 1–{MaximumPointsPerStroke} points.");
            if (stroke.BaseWidth is <= 0 or > 500) Error($"{path}[{stroke.Id}].baseWidth", "Ink width must be between 0 and 500.");
            if (stroke.Opacity is < 0 or > 1) Error($"{path}[{stroke.Id}].opacity", "Ink opacity must be between 0 and 1.");
            if (!ColourPattern().IsMatch(stroke.Colour)) Error($"{path}[{stroke.Id}].colour", "Ink colours must use #RRGGBB or #AARRGGBB.");
            foreach (var point in stroke.Points)
            {
                if (double.IsNaN(point.X) || double.IsNaN(point.Y) || double.IsInfinity(point.X) || double.IsInfinity(point.Y))
                    Error($"{path}[{stroke.Id}].points", "Ink coordinates must be finite.");
                if (point.Pressure is < 0 or > 1) Error($"{path}[{stroke.Id}].pressure", "Ink pressure must be between 0 and 1.");
                if (point.TiltX is < -90 or > 90 || point.TiltY is < -90 or > 90)
                    Error($"{path}[{stroke.Id}].tilt", "Ink tilt must be between -90 and 90 degrees.");
            }
        }
    }

    /// <summary>
    /// Performs the colour pattern step owned by this component.
    /// </summary>
    [GeneratedRegex("^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$")]
    private static partial Regex ColourPattern();

    /// <summary>
    /// Performs the network reference pattern step owned by this component.
    /// </summary>
    [GeneratedRegex("(?:https?:)?//|url\\s*\\(|@import", RegexOptions.IgnoreCase)]
    private static partial Regex NetworkReferencePattern();

    /// <summary>
    /// Performs the form pattern step owned by this component.
    /// </summary>
    [GeneratedRegex("<\\s*form\\b", RegexOptions.IgnoreCase)]
    private static partial Regex FormPattern();
}
