/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/NotesDocumentMigrationService.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns NotesMigrationResult, INotesDocumentMigrator, NotesDocumentMigrator. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents notes migration result and keeps its related state and behavior together.
/// </summary>
public sealed record NotesMigrationResult(
    NotesDocument Document,
    int SourceSchemaVersion,
    int TargetSchemaVersion,
    IReadOnlyList<string> Changes);

/// <summary>
/// Defines the notes document migrator contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface INotesDocumentMigrator
{
    Task<NotesMigrationResult> ReadAndMigrateAsync(string path, CancellationToken cancellationToken);
    NotesMigrationResult Migrate(NotesDocument document, int sourceSchemaVersion);
}

/// <summary>
/// Represents notes document migrator and keeps its related state and behavior together.
/// </summary>
public sealed class NotesDocumentMigrator : INotesDocumentMigrator
{
    /// <summary>
    /// Stores maximum native document bytes locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const long MaximumNativeDocumentBytes = 256L * 1024 * 1024;
    /// <summary>
    /// Stores json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>
    /// Performs read and migrate asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<NotesMigrationResult> ReadAndMigrateAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("The native Notes file does not exist.", path);
        var length = new FileInfo(path).Length;
        if (length is <= 0 or > MaximumNativeDocumentBytes)
            throw new InvalidDataException("Native Notes files must be between 1 byte and 256 MB.");

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var node = await JsonNode.ParseAsync(
            stream,
            new JsonNodeOptions { PropertyNameCaseInsensitive = true },
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 256
            },
            cancellationToken).ConfigureAwait(false)
                   ?? throw new InvalidDataException("The native Notes file was empty.");
        if (node is not JsonObject root)
            throw new InvalidDataException("A native Notes file must contain a JSON object.");

        var sourceVersion = ReadSchemaVersion(root);
        if (sourceVersion > NotesDocument.CurrentSchemaVersion)
            throw new InvalidDataException(
                $"This Notes file uses schema {sourceVersion}, but this Haven build supports up to {NotesDocument.CurrentSchemaVersion}.");
        if (sourceVersion < 0)
            throw new InvalidDataException("The native Notes schema version cannot be negative.");

        var changes = new List<string>();
        while (sourceVersion < NotesDocument.CurrentSchemaVersion)
        {
            sourceVersion = sourceVersion switch
            {
                0 => MigrateZeroToOne(root, changes),
                _ => throw new InvalidDataException($"No Notes migration is registered from schema {sourceVersion}.")
            };
        }

        var document = root.Deserialize<NotesDocument>(JsonOptions)
                       ?? throw new InvalidDataException("The native Notes file could not be read.");
        var result = Migrate(document, ReadOriginalSchemaVersion(root, changes));
        return result with { Changes = changes.Concat(result.Changes).Distinct(StringComparer.Ordinal).ToArray() };
    }

    /// <summary>
    /// Performs the migrate step owned by this component.
    /// </summary>
    public NotesMigrationResult Migrate(NotesDocument document, int sourceSchemaVersion)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (sourceSchemaVersion > NotesDocument.CurrentSchemaVersion)
            throw new InvalidDataException("The Notes document was created by a newer incompatible schema.");
        var changes = new List<string>();
        document.SchemaVersion = NotesDocument.CurrentSchemaVersion;
        NormalizeDocument(document, changes);
        return new NotesMigrationResult(
            document,
            sourceSchemaVersion,
            NotesDocument.CurrentSchemaVersion,
            changes);
    }

    /// <summary>
    /// Performs the read schema version step owned by this component.
    /// </summary>
    private static int ReadSchemaVersion(JsonObject root)
    {
        if (!root.TryGetPropertyValue("schemaVersion", out var value) || value is null) return 0;
        try { return value.GetValue<int>(); }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("The native Notes schemaVersion must be an integer.", ex);
        }
    }

    /// <summary>
    /// Performs the read original schema version step owned by this component.
    /// </summary>
    private static int ReadOriginalSchemaVersion(JsonObject root, IReadOnlyCollection<string> changes)
    {
        if (changes.Any(change => change.StartsWith("Migrated schema 0", StringComparison.Ordinal))) return 0;
        return ReadSchemaVersion(root);
    }

    /// <summary>
    /// Performs the migrate zero to one step owned by this component.
    /// </summary>
    private static int MigrateZeroToOne(JsonObject root, ICollection<string> changes)
    {
        root["schemaVersion"] = 1;
        if (!root.ContainsKey("metadata")) root["metadata"] = new JsonObject();
        if (!root.ContainsKey("fields")) root["fields"] = new JsonArray();
        if (!root.ContainsKey("bookmarks")) root["bookmarks"] = new JsonArray();
        if (!root.ContainsKey("citations")) root["citations"] = new JsonArray();
        if (!root.ContainsKey("comments")) root["comments"] = new JsonArray();
        if (!root.ContainsKey("revisions")) root["revisions"] = new JsonArray();
        if (!root.ContainsKey("aiChanges")) root["aiChanges"] = new JsonArray();
        if (!root.ContainsKey("flashcardReviews")) root["flashcardReviews"] = new JsonArray();
        changes.Add("Migrated schema 0 to schema 1 and added durable document collections.");
        return 1;
    }

    /// <summary>
    /// Performs the normalize document step owned by this component.
    /// </summary>
    private static void NormalizeDocument(NotesDocument document, ICollection<string> changes)
    {
        var usedIds = new HashSet<Guid>();
        document.Id = Unique(document.Id, usedIds, changes, "document");
        document.Title = string.IsNullOrWhiteSpace(document.Title) ? "Untitled note" : document.Title.Trim();
        document.Language = string.IsNullOrWhiteSpace(document.Language) ? "en-GB" : document.Language.Trim();
        document.CreatedAt = document.CreatedAt == default ? DateTimeOffset.UtcNow : document.CreatedAt;
        document.UpdatedAt = document.UpdatedAt == default || document.UpdatedAt < document.CreatedAt
            ? document.CreatedAt
            : document.UpdatedAt;
        document.Version = Math.Max(0, document.Version);
        document.PageSetup ??= new NotesPageSetup();
        document.Sections ??= [];
        document.Styles ??= [];
        document.Fields ??= [];
        document.Bookmarks ??= [];
        document.Citations ??= [];
        document.Comments ??= [];
        document.Revisions ??= [];
        document.AiChanges ??= [];
        document.FlashcardReviews ??= [];
        document.Collaboration ??= new NotesCollaborationState();
        document.Recovery ??= new NotesRecoveryState();
        document.Metadata = document.Metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(document.Metadata, StringComparer.OrdinalIgnoreCase);
        if (document.Sections.Count == 0)
        {
            document.Sections.Add(NotesSection.CreateDefault());
            changes.Add("Added a required default section.");
        }
        if (document.Styles.Count == 0)
        {
            document.Styles = NotesNamedStyle.CreateDefaults();
            changes.Add("Restored built-in named styles.");
        }

        foreach (var section in document.Sections)
        {
            section.Id = Unique(section.Id, usedIds, changes, "section");
            section.Title = string.IsNullOrWhiteSpace(section.Title) ? "Section" : section.Title.Trim();
            section.Header ??= string.Empty;
            section.Footer ??= string.Empty;
            section.Pages ??= [];
            if (section.Pages.Count == 0)
            {
                section.Pages.Add(NotesPage.CreateDefault());
                changes.Add("Added a required default page.");
            }
            for (var pageIndex = 0; pageIndex < section.Pages.Count; pageIndex++)
            {
                var page = section.Pages[pageIndex];
                page.Id = Unique(page.Id, usedIds, changes, "page");
                page.Title = string.IsNullOrWhiteSpace(page.Title) ? "Page " + (pageIndex + 1) : page.Title.Trim();
                page.Order = pageIndex;
                page.CanvasWidth = ClampFinite(page.CanvasWidth, 1200, 100, 1_000_000);
                page.CanvasHeight = ClampFinite(page.CanvasHeight, 900, 100, 1_000_000);
                page.Blocks ??= [];
                page.CanvasObjects ??= [];
                if (page.Blocks.Count == 0) page.Blocks.Add(NotesBlock.CreateParagraph());
                for (var blockIndex = 0; blockIndex < page.Blocks.Count; blockIndex++)
                {
                    var block = page.Blocks[blockIndex];
                    block.Id = Unique(block.Id, usedIds, changes, "block");
                    block.Order = blockIndex;
                    NormalizeBlock(block, usedIds, changes);
                }
                foreach (var value in page.CanvasObjects)
                {
                    value.Id = Unique(value.Id, usedIds, changes, "canvas object");
                    NormalizeCanvasObject(value);
                }
            }
        }

        foreach (var style in document.Styles)
        {
            style.Id = string.IsNullOrWhiteSpace(style.Id) ? "style-" + Guid.NewGuid().ToString("N") : style.Id.Trim();
            style.Name = string.IsNullOrWhiteSpace(style.Name) ? style.Id : style.Name.Trim();
            style.BasedOn ??= string.Empty;
            style.Character ??= new NotesTextRun();
            style.Paragraph ??= new NotesParagraphFormat();
            NormalizeRun(style.Character, usedIds, changes);
        }
        foreach (var field in document.Fields) field.Id = Unique(field.Id, usedIds, changes, "field");
        foreach (var bookmark in document.Bookmarks) bookmark.Id = Unique(bookmark.Id, usedIds, changes, "bookmark");
        foreach (var citation in document.Citations) citation.Id = Unique(citation.Id, usedIds, changes, "citation");
        foreach (var comment in document.Comments)
        {
            comment.Id = Unique(comment.Id, usedIds, changes, "comment");
            comment.Replies ??= [];
            foreach (var reply in comment.Replies) reply.Id = Unique(reply.Id, usedIds, changes, "comment reply");
        }
        foreach (var revision in document.Revisions) revision.Id = Unique(revision.Id, usedIds, changes, "revision");
        foreach (var change in document.AiChanges) change.Id = Unique(change.Id, usedIds, changes, "AI change");
        foreach (var review in document.FlashcardReviews) review.Id = Unique(review.Id, usedIds, changes, "flashcard review");
        document.Collaboration.Collaborators ??= [];
        document.Collaboration.Conflicts ??= [];
        foreach (var conflict in document.Collaboration.Conflicts) conflict.Id = Unique(conflict.Id, usedIds, changes, "conflict");
    }

    /// <summary>
    /// Performs the normalize block step owned by this component.
    /// </summary>
    private static void NormalizeBlock(NotesBlock block, HashSet<Guid> usedIds, ICollection<string> changes)
    {
        block.StyleId = string.IsNullOrWhiteSpace(block.StyleId) ? "normal" : block.StyleId.Trim();
        block.PlainText ??= string.Empty;
        block.Runs ??= [];
        block.Paragraph ??= new NotesParagraphFormat();
        block.Metadata = block.Metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(block.Metadata, StringComparer.OrdinalIgnoreCase);
        foreach (var run in block.Runs) NormalizeRun(run, usedIds, changes);
        switch (block.Kind)
        {
            case NotesBlockKind.List:
                block.List ??= new NotesListData();
                block.List.Items ??= [];
                if (block.List.Items.Count == 0) block.List.Items.Add(new NotesListItem());
                foreach (var item in block.List.Items) item.Id = Unique(item.Id, usedIds, changes, "list item");
                break;
            case NotesBlockKind.Table:
                block.Table ??= NotesTableData.Create(1, 1);
                block.Table.Rows ??= [];
                if (block.Table.Rows.Count == 0) block.Table.Rows.Add(NotesTableData.Create(1, 1).Rows[0]);
                var columns = Math.Max(1, block.Table.Rows.Max(row => row.Cells?.Count ?? 0));
                foreach (var row in block.Table.Rows)
                {
                    row.Id = Unique(row.Id, usedIds, changes, "table row");
                    row.Cells ??= [];
                    while (row.Cells.Count < columns) row.Cells.Add(new NotesTableCell());
                    foreach (var cell in row.Cells) cell.Id = Unique(cell.Id, usedIds, changes, "table cell");
                }
                break;
            case NotesBlockKind.Image:
            case NotesBlockKind.Audio:
            case NotesBlockKind.Video:
                block.Media ??= new NotesMediaData();
                block.Media.AttachmentId = block.Media.AttachmentId == Guid.Empty ? Guid.NewGuid() : block.Media.AttachmentId;
                block.Media.Width = ClampFinite(block.Media.Width, 400, 1, 10_000);
                block.Media.Height = ClampFinite(block.Media.Height, 300, 1, 10_000);
                break;
            case NotesBlockKind.Equation:
                block.Equation ??= new NotesEquationData { Source = "x = x" };
                block.Equation.Source ??= string.Empty;
                block.Equation.Macros ??= new Dictionary<string, string>(StringComparer.Ordinal);
                block.Equation.References ??= [];
                block.Equation.SourceStrokes ??= [];
                foreach (var stroke in block.Equation.SourceStrokes) NormalizeStroke(stroke, usedIds, changes);
                break;
            case NotesBlockKind.HtmlWidget:
                block.Html ??= new NotesHtmlData();
                block.Html.AllowPopups = false;
                block.Html.Width = ClampFinite(block.Html.Width, 640, 64, 10_000);
                block.Html.Height = ClampFinite(block.Html.Height, 360, 64, 10_000);
                break;
            case NotesBlockKind.Canvas:
                block.Canvas ??= new NotesCanvasData();
                block.Canvas.Objects ??= [];
                block.Canvas.Strokes ??= [];
                block.Canvas.GhostLayers ??= [];
                block.Canvas.Width = ClampFinite(block.Canvas.Width, 1200, 100, 1_000_000);
                block.Canvas.Height = ClampFinite(block.Canvas.Height, 900, 100, 1_000_000);
                block.Canvas.Zoom = ClampFinite(block.Canvas.Zoom, 1, 0.05, 100);
                foreach (var value in block.Canvas.Objects)
                {
                    value.Id = Unique(value.Id, usedIds, changes, "canvas object");
                    NormalizeCanvasObject(value);
                }
                foreach (var stroke in block.Canvas.Strokes) NormalizeStroke(stroke, usedIds, changes);
                foreach (var layer in block.Canvas.GhostLayers)
                {
                    layer.Id = Unique(layer.Id, usedIds, changes, "ghost layer");
                    layer.StrokeIds ??= [];
                    layer.ObjectIds ??= [];
                    layer.Masks ??= [];
                    foreach (var mask in layer.Masks)
                    {
                        mask.Id = Unique(mask.Id, usedIds, changes, "ghost mask");
                        mask.Width = ClampFinite(mask.Width, 120, 1, 1_000_000);
                        mask.Height = ClampFinite(mask.Height, 60, 1, 1_000_000);
                    }
                }
                break;
            case NotesBlockKind.Flashcard:
                block.Flashcard ??= new NotesFlashcardData { Front = "Question", Back = "Answer" };
                block.Flashcard.CardId = block.Flashcard.CardId == Guid.Empty ? Guid.NewGuid() : block.Flashcard.CardId;
                block.Flashcard.Schedule ??= new NotesFlashcardSchedule();
                block.Flashcard.OcclusionMasks ??= [];
                block.Flashcard.Tags ??= [];
                foreach (var mask in block.Flashcard.OcclusionMasks)
                {
                    mask.Id = Unique(mask.Id, usedIds, changes, "flashcard mask");
                    mask.Width = ClampFinite(mask.Width, 120, 1, 1_000_000);
                    mask.Height = ClampFinite(mask.Height, 60, 1, 1_000_000);
                }
                break;
        }
    }

    /// <summary>
    /// Performs the normalize run step owned by this component.
    /// </summary>
    private static void NormalizeRun(NotesTextRun run, HashSet<Guid> usedIds, ICollection<string> changes)
    {
        run.Id = Unique(run.Id, usedIds, changes, "text run");
        run.Text ??= string.Empty;
        run.FontFamily = string.IsNullOrWhiteSpace(run.FontFamily) || run.FontFamily.Equals("Inter", StringComparison.OrdinalIgnoreCase)
            ? "Montserrat"
            : run.FontFamily.Trim();
        run.FontSize = ClampFinite(run.FontSize, 14, 4, 300);
        run.Foreground = ValidColour(run.Foreground) ? run.Foreground : "#FFEEEEEE";
        run.Background = ValidColour(run.Background) ? run.Background : "#00000000";
    }

    /// <summary>
    /// Performs the normalize canvas object step owned by this component.
    /// </summary>
    private static void NormalizeCanvasObject(NotesCanvasObject value)
    {
        value.Text ??= string.Empty;
        value.Width = ClampFinite(value.Width, 160, 1, 1_000_000);
        value.Height = ClampFinite(value.Height, 100, 1, 1_000_000);
        value.X = ClampFinite(value.X, 0, -1_000_000, 1_000_000);
        value.Y = ClampFinite(value.Y, 0, -1_000_000, 1_000_000);
        value.Rotation = ClampFinite(value.Rotation, 0, -360_000, 360_000);
        value.StyleJson = string.IsNullOrWhiteSpace(value.StyleJson) ? "{}" : value.StyleJson;
    }

    /// <summary>
    /// Performs the normalize stroke step owned by this component.
    /// </summary>
    private static void NormalizeStroke(NotesInkStroke stroke, HashSet<Guid> usedIds, ICollection<string> changes)
    {
        stroke.Id = Unique(stroke.Id, usedIds, changes, "ink stroke");
        stroke.Tool = string.IsNullOrWhiteSpace(stroke.Tool) ? "pen" : stroke.Tool.Trim();
        stroke.Colour = ValidColour(stroke.Colour) ? stroke.Colour : "#FF2F80ED";
        stroke.BaseWidth = ClampFinite(stroke.BaseWidth, 2.5, 0.1, 500);
        stroke.Opacity = ClampFinite(stroke.Opacity, 1, 0, 1);
        stroke.Points ??= [];
        if (stroke.Points.Count == 0) stroke.Points.Add(new NotesInkPoint());
        foreach (var point in stroke.Points)
        {
            point.X = ClampFinite(point.X, 0, -1_000_000, 1_000_000);
            point.Y = ClampFinite(point.Y, 0, -1_000_000, 1_000_000);
            point.Pressure = ClampFinite(point.Pressure, 0.5, 0, 1);
            point.TiltX = ClampFinite(point.TiltX, 0, -90, 90);
            point.TiltY = ClampFinite(point.TiltY, 0, -90, 90);
            point.TimestampMilliseconds = Math.Max(0, point.TimestampMilliseconds);
        }
    }

    /// <summary>
    /// Performs the unique step owned by this component.
    /// </summary>
    private static Guid Unique(Guid candidate, ISet<Guid> used, ICollection<string> changes, string kind)
    {
        if (candidate != Guid.Empty && used.Add(candidate)) return candidate;
        Guid replacement;
        do replacement = Guid.NewGuid(); while (!used.Add(replacement));
        changes.Add("Repaired a missing or duplicate " + kind + " ID.");
        return replacement;
    }

    /// <summary>
    /// Performs the clamp finite step owned by this component.
    /// </summary>
    private static double ClampFinite(double value, double fallback, double minimum, double maximum) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    /// <summary>
    /// Performs the valid colour step owned by this component.
    /// </summary>
    private static bool ValidColour(string? value) =>
        value is { Length: 7 or 9 }
        && value[0] == '#'
        && value[1..].All(character => Uri.IsHexDigit(character));
}
