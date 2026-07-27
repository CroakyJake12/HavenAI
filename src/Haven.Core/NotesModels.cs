/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Core/NotesModels.cs, in the dependency-free Core layer, where shared domain models and rules live.
 * What: This file owns NotesExperienceKind, NotesLayoutMode, NotesBlockKind, NotesTextAlignment, NotesListKind, NotesCanvasObjectKind, NotesEquationViewMode, NotesHtmlViewMode, NotesGhostRevealMode, NotesAiChangeStatus, NotesRevisionKind, NotesFlashcardRating, NotesCommentState, NotesConflictState, NotesDocument, NotesPageSetup, NotesSection, NotesPage, NotesBlock, NotesTextRun, NotesParagraphFormat, NotesListData, NotesListItem, NotesTableData, NotesTableRow, NotesTableCell, NotesMediaData, NotesEquationData, NotesHtmlData, NotesCanvasData, NotesCanvasObject, NotesInkStroke, NotesInkPoint, NotesGhostLayer, NotesOcclusionMask, NotesFlashcardData, NotesFlashcardSchedule, NotesFlashcardReview, NotesNamedStyle, NotesField, NotesBookmark, NotesCitation, NotesComment, NotesCommentReply, NotesRevision, NotesAiChange, NotesCollaborationState, NotesCollaborator, NotesConflict, NotesRecoveryState, NotesDocumentSummary, NotesVersionInfo, NotesSaveResult, NotesSearchHit, NotesAiProposalRequest, NotesAiProposalResult. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: This code stays free of UI and storage dependencies so the same rule or data shape can be reused and tested everywhere.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

namespace Haven.Core;

/// <summary>
/// Lists the supported notes experience kind values used to make state explicit and type-safe.
/// </summary>
public enum NotesExperienceKind { Notes = 0, Present = 1, Data = 2, Tasks = 3, Imagine = 4 }
/// <summary>
/// Lists the supported notes layout mode values used to make state explicit and type-safe.
/// </summary>
public enum NotesLayoutMode { Paginated = 0, Continuous = 1, Freeform = 2, InfiniteCanvas = 3 }
/// <summary>
/// Lists the supported notes block kind values used to make state explicit and type-safe.
/// </summary>
public enum NotesBlockKind
{
    Paragraph = 0,
    Heading = 1,
    Quote = 2,
    Code = 3,
    List = 4,
    Table = 5,
    Image = 6,
    Audio = 7,
    Video = 8,
    Equation = 9,
    HtmlWidget = 10,
    Canvas = 11,
    Flashcard = 12,
    Divider = 13
}
/// <summary>
/// Lists the supported notes text alignment values used to make state explicit and type-safe.
/// </summary>
public enum NotesTextAlignment { Left = 0, Center = 1, Right = 2, Justify = 3 }
/// <summary>
/// Lists the supported notes list kind values used to make state explicit and type-safe.
/// </summary>
public enum NotesListKind { Bulleted = 0, Numbered = 1, Checklist = 2 }
/// <summary>
/// Lists the supported notes canvas object kind values used to make state explicit and type-safe.
/// </summary>
public enum NotesCanvasObjectKind { Text = 0, Shape = 1, Image = 2, Connector = 3, Frame = 4, Ink = 5 }
/// <summary>
/// Lists the supported notes equation view mode values used to make state explicit and type-safe.
/// </summary>
public enum NotesEquationViewMode { Visual = 0, Source = 1, Split = 2 }
/// <summary>
/// Lists the supported notes html view mode values used to make state explicit and type-safe.
/// </summary>
public enum NotesHtmlViewMode { Visual = 0, Source = 1, Split = 2 }
/// <summary>
/// Lists the supported notes ghost reveal mode values used to make state explicit and type-safe.
/// </summary>
public enum NotesGhostRevealMode { Tap = 0, Hold = 1, Scratch = 2, StudyAnswer = 3 }
/// <summary>
/// Lists the supported notes ai change status values used to make state explicit and type-safe.
/// </summary>
public enum NotesAiChangeStatus { Proposed = 0, Approved = 1, Rejected = 2, Applied = 3, Cancelled = 4, Failed = 5 }
/// <summary>
/// Lists the supported notes revision kind values used to make state explicit and type-safe.
/// </summary>
public enum NotesRevisionKind { Created = 0, Edited = 1, Imported = 2, AiApplied = 3, Restored = 4, ConflictResolved = 5 }
/// <summary>
/// Lists the supported notes flashcard rating values used to make state explicit and type-safe.
/// </summary>
public enum NotesFlashcardRating { Again = 0, Hard = 1, Good = 2, Easy = 3 }
/// <summary>
/// Lists the supported notes comment state values used to make state explicit and type-safe.
/// </summary>
public enum NotesCommentState { Open = 0, Resolved = 1, Reopened = 2 }
/// <summary>
/// Lists the supported notes conflict state values used to make state explicit and type-safe.
/// </summary>
public enum NotesConflictState { None = 0, LocalAhead = 1, RemoteAhead = 2, Diverged = 3, Resolved = 4 }

/// <summary>
/// Represents notes document and keeps its related state and behavior together.
/// </summary>
public sealed class NotesDocument
{
    /// <summary>
    /// Stores current schema version locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public const int CurrentSchemaVersion = 1;
    /// <summary>
    /// Gets or updates schema version, the bindable or domain state represented by this property.
    /// </summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title { get; set; } = "Untitled note";
    /// <summary>
    /// Gets or updates language, the bindable or domain state represented by this property.
    /// </summary>
    public string Language { get; set; } = "en-GB";
    /// <summary>
    /// Creates d at with the invariants required by its callers.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or updates updated at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or updates version, the bindable or domain state represented by this property.
    /// </summary>
    public long Version { get; set; }
    /// <summary>
    /// Gets or updates layout mode, the bindable or domain state represented by this property.
    /// </summary>
    public NotesLayoutMode LayoutMode { get; set; } = NotesLayoutMode.Continuous;
    /// <summary>
    /// Gets or updates page setup, the bindable or domain state represented by this property.
    /// </summary>
    public NotesPageSetup PageSetup { get; set; } = new();
    /// <summary>
    /// Gets or updates sections, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesSection> Sections { get; set; } = [NotesSection.CreateDefault()];
    /// <summary>
    /// Gets or updates styles, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesNamedStyle> Styles { get; set; } = NotesNamedStyle.CreateDefaults();
    /// <summary>
    /// Gets or updates fields, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesField> Fields { get; set; } = [];
    /// <summary>
    /// Gets or updates bookmarks, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesBookmark> Bookmarks { get; set; } = [];
    /// <summary>
    /// Gets or updates citations, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesCitation> Citations { get; set; } = [];
    /// <summary>
    /// Gets or updates comments, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesComment> Comments { get; set; } = [];
    /// <summary>
    /// Gets or updates revisions, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesRevision> Revisions { get; set; } = [];
    /// <summary>
    /// Gets or updates ai changes, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesAiChange> AiChanges { get; set; } = [];
    /// <summary>
    /// Gets or updates flashcard reviews, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesFlashcardReview> FlashcardReviews { get; set; } = [];
    /// <summary>
    /// Gets or updates collaboration, the bindable or domain state represented by this property.
    /// </summary>
    public NotesCollaborationState Collaboration { get; set; } = new();
    /// <summary>
    /// Gets or updates recovery, the bindable or domain state represented by this property.
    /// </summary>
    public NotesRecoveryState Recovery { get; set; } = new();
    /// <summary>
    /// Gets or updates metadata, the bindable or domain state represented by this property.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates this member with the invariants required by its callers.
    /// </summary>
    public static NotesDocument Create(string? title = null)
    {
        var document = new NotesDocument();
        if (!string.IsNullOrWhiteSpace(title)) document.Title = title.Trim();
        document.Revisions.Add(new NotesRevision
        {
            Kind = NotesRevisionKind.Created,
            Summary = "Document created",
            CreatedAt = document.CreatedAt,
            Author = Environment.UserName
        });
        return document;
    }
}

/// <summary>
/// Represents notes page setup and keeps its related state and behavior together.
/// </summary>
public sealed class NotesPageSetup
{
    /// <summary>
    /// Gets or updates width points, the bindable or domain state represented by this property.
    /// </summary>
    public double WidthPoints { get; set; } = 595;
    /// <summary>
    /// Gets or updates height points, the bindable or domain state represented by this property.
    /// </summary>
    public double HeightPoints { get; set; } = 842;
    /// <summary>
    /// Gets or updates margin top points, the bindable or domain state represented by this property.
    /// </summary>
    public double MarginTopPoints { get; set; } = 72;
    /// <summary>
    /// Gets or updates margin right points, the bindable or domain state represented by this property.
    /// </summary>
    public double MarginRightPoints { get; set; } = 72;
    /// <summary>
    /// Gets or updates margin bottom points, the bindable or domain state represented by this property.
    /// </summary>
    public double MarginBottomPoints { get; set; } = 72;
    /// <summary>
    /// Gets or updates margin left points, the bindable or domain state represented by this property.
    /// </summary>
    public double MarginLeftPoints { get; set; } = 72;
    /// <summary>
    /// Gets or updates orientation, the bindable or domain state represented by this property.
    /// </summary>
    public string Orientation { get; set; } = "Portrait";
    /// <summary>
    /// Gets or updates background, the bindable or domain state represented by this property.
    /// </summary>
    public string Background { get; set; } = "#FFFFFFFF";
    /// <summary>
    /// Gets or updates show page numbers, the bindable or domain state represented by this property.
    /// </summary>
    public bool ShowPageNumbers { get; set; } = true;
}

/// <summary>
/// Represents notes section and keeps its related state and behavior together.
/// </summary>
public sealed class NotesSection
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title { get; set; } = "Section 1";
    /// <summary>
    /// Gets or updates header, the bindable or domain state represented by this property.
    /// </summary>
    public string Header { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates footer, the bindable or domain state represented by this property.
    /// </summary>
    public string Footer { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates start on new page, the bindable or domain state represented by this property.
    /// </summary>
    public bool StartOnNewPage { get; set; }
    /// <summary>
    /// Gets or updates pages, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesPage> Pages { get; set; } = [NotesPage.CreateDefault()];

    /// <summary>
    /// Creates default with the invariants required by its callers.
    /// </summary>
    public static NotesSection CreateDefault() => new();
}

/// <summary>
/// Represents notes page and keeps its related state and behavior together.
/// </summary>
public sealed class NotesPage
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title { get; set; } = "Page 1";
    /// <summary>
    /// Gets or updates order, the bindable or domain state represented by this property.
    /// </summary>
    public int Order { get; set; }
    /// <summary>
    /// Reports whether canvas width is true for the current state.
    /// </summary>
    public double CanvasWidth { get; set; } = 1200;
    /// <summary>
    /// Reports whether canvas height is true for the current state.
    /// </summary>
    public double CanvasHeight { get; set; } = 900;
    /// <summary>
    /// Gets or updates blocks, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesBlock> Blocks { get; set; } = [NotesBlock.CreateParagraph()];
    /// <summary>
    /// Reports whether canvas objects is true for the current state.
    /// </summary>
    public List<NotesCanvasObject> CanvasObjects { get; set; } = [];

    /// <summary>
    /// Creates default with the invariants required by its callers.
    /// </summary>
    public static NotesPage CreateDefault() => new();
}

/// <summary>
/// Represents notes block and keeps its related state and behavior together.
/// </summary>
public sealed class NotesBlock
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public NotesBlockKind Kind { get; set; } = NotesBlockKind.Paragraph;
    /// <summary>
    /// Gets or updates order, the bindable or domain state represented by this property.
    /// </summary>
    public int Order { get; set; }
    /// <summary>
    /// Gets or updates style id, the bindable or domain state represented by this property.
    /// </summary>
    public string StyleId { get; set; } = "normal";
    /// <summary>
    /// Gets or updates plain text, the bindable or domain state represented by this property.
    /// </summary>
    public string PlainText { get; set; } = string.Empty;
    /// <summary>
    /// Runs runs while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    public List<NotesTextRun> Runs { get; set; } = [];
    /// <summary>
    /// Gets or updates paragraph, the bindable or domain state represented by this property.
    /// </summary>
    public NotesParagraphFormat Paragraph { get; set; } = new();
    /// <summary>
    /// Gets or updates list, the bindable or domain state represented by this property.
    /// </summary>
    public NotesListData? List { get; set; }
    /// <summary>
    /// Gets or updates table, the bindable or domain state represented by this property.
    /// </summary>
    public NotesTableData? Table { get; set; }
    /// <summary>
    /// Gets or updates media, the bindable or domain state represented by this property.
    /// </summary>
    public NotesMediaData? Media { get; set; }
    /// <summary>
    /// Gets or updates equation, the bindable or domain state represented by this property.
    /// </summary>
    public NotesEquationData? Equation { get; set; }
    /// <summary>
    /// Gets or updates html, the bindable or domain state represented by this property.
    /// </summary>
    public NotesHtmlData? Html { get; set; }
    /// <summary>
    /// Reports whether canvas is true for the current state.
    /// </summary>
    public NotesCanvasData? Canvas { get; set; }
    /// <summary>
    /// Gets or updates flashcard, the bindable or domain state represented by this property.
    /// </summary>
    public NotesFlashcardData? Flashcard { get; set; }
    /// <summary>
    /// Gets or updates metadata, the bindable or domain state represented by this property.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates paragraph with the invariants required by its callers.
    /// </summary>
    public static NotesBlock CreateParagraph(string text = "") => new() { PlainText = text };
    /// <summary>
    /// Performs the heading step owned by this component.
    /// </summary>
    public static NotesBlock Heading(string text = "Heading") => new() { Kind = NotesBlockKind.Heading, PlainText = text, StyleId = "heading-1" };
    /// <summary>
    /// Performs the equation block step owned by this component.
    /// </summary>
    public static NotesBlock EquationBlock() => new()
    {
        Kind = NotesBlockKind.Equation,
        Equation = new NotesEquationData { Source = "x^2 + y^2 = z^2", AccessibleAlternative = "x squared plus y squared equals z squared" }
    };
    /// <summary>
    /// Performs the html block step owned by this component.
    /// </summary>
    public static NotesBlock HtmlBlock() => new()
    {
        Kind = NotesBlockKind.HtmlWidget,
        Html = new NotesHtmlData { HtmlSource = "<section><h2>Interactive note</h2><p>Edit the source safely.</p></section>" }
    };
    /// <summary>
    /// Performs the table block step owned by this component.
    /// </summary>
    public static NotesBlock TableBlock(int rows = 3, int columns = 3) => new()
    {
        Kind = NotesBlockKind.Table,
        Table = NotesTableData.Create(rows, columns)
    };
    /// <summary>
    /// Reports whether canvas block is true for the current state.
    /// </summary>
    public static NotesBlock CanvasBlock() => new()
    {
        Kind = NotesBlockKind.Canvas,
        Canvas = new NotesCanvasData()
    };
    /// <summary>
    /// Performs the flashcard block step owned by this component.
    /// </summary>
    public static NotesBlock FlashcardBlock() => new()
    {
        Kind = NotesBlockKind.Flashcard,
        Flashcard = new NotesFlashcardData { Front = "Question", Back = "Answer" }
    };
}

/// <summary>
/// Represents notes text run and keeps its related state and behavior together.
/// </summary>
public sealed class NotesTextRun
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates text, the bindable or domain state represented by this property.
    /// </summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates font family, the bindable or domain state represented by this property.
    /// </summary>
    public string FontFamily { get; set; } = "Inter";
    /// <summary>
    /// Gets or updates font size, the bindable or domain state represented by this property.
    /// </summary>
    public double FontSize { get; set; } = 14;
    /// <summary>
    /// Gets or updates bold, the bindable or domain state represented by this property.
    /// </summary>
    public bool Bold { get; set; }
    /// <summary>
    /// Gets or updates italic, the bindable or domain state represented by this property.
    /// </summary>
    public bool Italic { get; set; }
    /// <summary>
    /// Gets or updates underline, the bindable or domain state represented by this property.
    /// </summary>
    public bool Underline { get; set; }
    /// <summary>
    /// Gets or updates strike through, the bindable or domain state represented by this property.
    /// </summary>
    public bool StrikeThrough { get; set; }
    /// <summary>
    /// Gets or updates foreground, the bindable or domain state represented by this property.
    /// </summary>
    public string Foreground { get; set; } = "#FFEEEEEE";
    /// <summary>
    /// Gets or updates background, the bindable or domain state represented by this property.
    /// </summary>
    public string Background { get; set; } = "#00000000";
    /// <summary>
    /// Gets or updates link, the bindable or domain state represented by this property.
    /// </summary>
    public string? Link { get; set; }
    /// <summary>
    /// Gets or updates language, the bindable or domain state represented by this property.
    /// </summary>
    public string? Language { get; set; }
}

/// <summary>
/// Represents notes paragraph format and keeps its related state and behavior together.
/// </summary>
public sealed class NotesParagraphFormat
{
    /// <summary>
    /// Gets or updates alignment, the bindable or domain state represented by this property.
    /// </summary>
    public NotesTextAlignment Alignment { get; set; }
    /// <summary>
    /// Gets or updates line spacing, the bindable or domain state represented by this property.
    /// </summary>
    public double LineSpacing { get; set; } = 1.25;
    /// <summary>
    /// Gets or updates space before, the bindable or domain state represented by this property.
    /// </summary>
    public double SpaceBefore { get; set; }
    /// <summary>
    /// Gets or updates space after, the bindable or domain state represented by this property.
    /// </summary>
    public double SpaceAfter { get; set; } = 8;
    /// <summary>
    /// Gets or updates indent left, the bindable or domain state represented by this property.
    /// </summary>
    public double IndentLeft { get; set; }
    /// <summary>
    /// Gets or updates indent right, the bindable or domain state represented by this property.
    /// </summary>
    public double IndentRight { get; set; }
    /// <summary>
    /// Gets or updates first line indent, the bindable or domain state represented by this property.
    /// </summary>
    public double FirstLineIndent { get; set; }
    /// <summary>
    /// Gets or updates keep with next, the bindable or domain state represented by this property.
    /// </summary>
    public bool KeepWithNext { get; set; }
    /// <summary>
    /// Gets or updates page break before, the bindable or domain state represented by this property.
    /// </summary>
    public bool PageBreakBefore { get; set; }
}

/// <summary>
/// Represents notes list data and keeps its related state and behavior together.
/// </summary>
public sealed class NotesListData
{
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public NotesListKind Kind { get; set; }
    /// <summary>
    /// Gets or updates start number, the bindable or domain state represented by this property.
    /// </summary>
    public int StartNumber { get; set; } = 1;
    /// <summary>
    /// Gets or updates items, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesListItem> Items { get; set; } = [];
}

/// <summary>
/// Represents notes list item and keeps its related state and behavior together.
/// </summary>
public sealed class NotesListItem
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates text, the bindable or domain state represented by this property.
    /// </summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates checked, the bindable or domain state represented by this property.
    /// </summary>
    public bool Checked { get; set; }
    /// <summary>
    /// Gets or updates level, the bindable or domain state represented by this property.
    /// </summary>
    public int Level { get; set; }
}

/// <summary>
/// Represents notes table data and keeps its related state and behavior together.
/// </summary>
public sealed class NotesTableData
{
    /// <summary>
    /// Gets or updates rows, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesTableRow> Rows { get; set; } = [];
    /// <summary>
    /// Gets or updates header row, the bindable or domain state represented by this property.
    /// </summary>
    public bool HeaderRow { get; set; } = true;
    /// <summary>
    /// Gets or updates repeat header, the bindable or domain state represented by this property.
    /// </summary>
    public bool RepeatHeader { get; set; }
    /// <summary>
    /// Gets or updates style, the bindable or domain state represented by this property.
    /// </summary>
    public string Style { get; set; } = "grid";

    /// <summary>
    /// Creates this member with the invariants required by its callers.
    /// </summary>
    public static NotesTableData Create(int rows, int columns)
    {
        var table = new NotesTableData();
        for (var row = 0; row < Math.Clamp(rows, 1, 100); row++)
        {
            var value = new NotesTableRow();
            for (var column = 0; column < Math.Clamp(columns, 1, 50); column++)
                value.Cells.Add(new NotesTableCell { Text = row == 0 ? $"Column {column + 1}" : string.Empty });
            table.Rows.Add(value);
        }
        return table;
    }
}

/// <summary>
/// Represents notes table row and keeps its related state and behavior together.
/// </summary>
public sealed class NotesTableRow
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates cells, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesTableCell> Cells { get; set; } = [];
    /// <summary>
    /// Reports whether header applies to the current state.
    /// </summary>
    public bool IsHeader { get; set; }
}

/// <summary>
/// Represents notes table cell and keeps its related state and behavior together.
/// </summary>
public sealed class NotesTableCell
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates text, the bindable or domain state represented by this property.
    /// </summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates row span, the bindable or domain state represented by this property.
    /// </summary>
    public int RowSpan { get; set; } = 1;
    /// <summary>
    /// Gets or updates column span, the bindable or domain state represented by this property.
    /// </summary>
    public int ColumnSpan { get; set; } = 1;
    /// <summary>
    /// Gets or updates background, the bindable or domain state represented by this property.
    /// </summary>
    public string Background { get; set; } = "#00000000";
    /// <summary>
    /// Gets or updates vertical alignment, the bindable or domain state represented by this property.
    /// </summary>
    public string VerticalAlignment { get; set; } = "Top";
}

/// <summary>
/// Represents notes media data and keeps its related state and behavior together.
/// </summary>
public sealed class NotesMediaData
{
    /// <summary>
    /// Gets or updates attachment id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid AttachmentId { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates original name, the bindable or domain state represented by this property.
    /// </summary>
    public string OriginalName { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates stored path, the bindable or domain state represented by this property.
    /// </summary>
    public string StoredPath { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates media type, the bindable or domain state represented by this property.
    /// </summary>
    public string MediaType { get; set; } = "application/octet-stream";
    /// <summary>
    /// Gets or updates size bytes, the bindable or domain state represented by this property.
    /// </summary>
    public long SizeBytes { get; set; }
    /// <summary>
    /// Gets or updates sha256, the bindable or domain state represented by this property.
    /// </summary>
    public string Sha256 { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates alt text, the bindable or domain state represented by this property.
    /// </summary>
    public string AltText { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates caption, the bindable or domain state represented by this property.
    /// </summary>
    public string Caption { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates wrapping, the bindable or domain state represented by this property.
    /// </summary>
    public string Wrapping { get; set; } = "Inline";
    /// <summary>
    /// Gets or updates width, the bindable or domain state represented by this property.
    /// </summary>
    public double Width { get; set; } = 400;
    /// <summary>
    /// Gets or updates height, the bindable or domain state represented by this property.
    /// </summary>
    public double Height { get; set; } = 300;
    /// <summary>
    /// Gets or updates rotation, the bindable or domain state represented by this property.
    /// </summary>
    public double Rotation { get; set; }
    /// <summary>
    /// Gets or updates crop left, the bindable or domain state represented by this property.
    /// </summary>
    public double CropLeft { get; set; }
    /// <summary>
    /// Gets or updates crop top, the bindable or domain state represented by this property.
    /// </summary>
    public double CropTop { get; set; }
    /// <summary>
    /// Gets or updates crop right, the bindable or domain state represented by this property.
    /// </summary>
    public double CropRight { get; set; }
    /// <summary>
    /// Gets or updates crop bottom, the bindable or domain state represented by this property.
    /// </summary>
    public double CropBottom { get; set; }
}

/// <summary>
/// Represents notes equation data and keeps its related state and behavior together.
/// </summary>
public sealed class NotesEquationData
{
    /// <summary>
    /// Gets or updates view mode, the bindable or domain state represented by this property.
    /// </summary>
    public NotesEquationViewMode ViewMode { get; set; } = NotesEquationViewMode.Split;
    /// <summary>
    /// Gets or updates source, the bindable or domain state represented by this property.
    /// </summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates visual structure json, the bindable or domain state represented by this property.
    /// </summary>
    public string VisualStructureJson { get; set; } = "{}";
    /// <summary>
    /// Gets or updates rendered text, the bindable or domain state represented by this property.
    /// </summary>
    public string RenderedText { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates error, the bindable or domain state represented by this property.
    /// </summary>
    public string Error { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates accessible alternative, the bindable or domain state represented by this property.
    /// </summary>
    public string AccessibleAlternative { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates numbered, the bindable or domain state represented by this property.
    /// </summary>
    public bool Numbered { get; set; }
    /// <summary>
    /// Gets or updates number, the bindable or domain state represented by this property.
    /// </summary>
    public int? Number { get; set; }
    /// <summary>
    /// Gets or updates label, the bindable or domain state represented by this property.
    /// </summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates macros, the bindable or domain state represented by this property.
    /// </summary>
    public Dictionary<string, string> Macros { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// Gets or updates references, the bindable or domain state represented by this property.
    /// </summary>
    public List<string> References { get; set; } = [];
    /// <summary>
    /// Gets or updates source strokes, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesInkStroke> SourceStrokes { get; set; } = [];
}

/// <summary>
/// Represents notes html data and keeps its related state and behavior together.
/// </summary>
public sealed class NotesHtmlData
{
    /// <summary>
    /// Gets or updates view mode, the bindable or domain state represented by this property.
    /// </summary>
    public NotesHtmlViewMode ViewMode { get; set; } = NotesHtmlViewMode.Split;
    /// <summary>
    /// Gets or updates html source, the bindable or domain state represented by this property.
    /// </summary>
    public string HtmlSource { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates css source, the bindable or domain state represented by this property.
    /// </summary>
    public string CssSource { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates java script source, the bindable or domain state represented by this property.
    /// </summary>
    public string JavaScriptSource { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates allow scripts, the bindable or domain state represented by this property.
    /// </summary>
    public bool AllowScripts { get; set; }
    /// <summary>
    /// Gets or updates allow network, the bindable or domain state represented by this property.
    /// </summary>
    public bool AllowNetwork { get; set; }
    /// <summary>
    /// Gets or updates allow forms, the bindable or domain state represented by this property.
    /// </summary>
    public bool AllowForms { get; set; }
    /// <summary>
    /// Gets or updates allow popups, the bindable or domain state represented by this property.
    /// </summary>
    public bool AllowPopups { get; set; }
    /// <summary>
    /// Gets or updates fallback text, the bindable or domain state represented by this property.
    /// </summary>
    public string FallbackText { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates snapshot path, the bindable or domain state represented by this property.
    /// </summary>
    public string SnapshotPath { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates width, the bindable or domain state represented by this property.
    /// </summary>
    public double Width { get; set; } = 640;
    /// <summary>
    /// Gets or updates height, the bindable or domain state represented by this property.
    /// </summary>
    public double Height { get; set; } = 360;
    /// <summary>
    /// Gets or updates last security error, the bindable or domain state represented by this property.
    /// </summary>
    public string LastSecurityError { get; set; } = string.Empty;
}

/// <summary>
/// Represents notes canvas data and keeps its related state and behavior together.
/// </summary>
public sealed class NotesCanvasData
{
    /// <summary>
    /// Gets or updates width, the bindable or domain state represented by this property.
    /// </summary>
    public double Width { get; set; } = 1200;
    /// <summary>
    /// Gets or updates height, the bindable or domain state represented by this property.
    /// </summary>
    public double Height { get; set; } = 900;
    /// <summary>
    /// Gets or updates zoom, the bindable or domain state represented by this property.
    /// </summary>
    public double Zoom { get; set; } = 1;
    /// <summary>
    /// Gets or updates offset x, the bindable or domain state represented by this property.
    /// </summary>
    public double OffsetX { get; set; }
    /// <summary>
    /// Gets or updates offset y, the bindable or domain state represented by this property.
    /// </summary>
    public double OffsetY { get; set; }
    /// <summary>
    /// Gets or updates infinite, the bindable or domain state represented by this property.
    /// </summary>
    public bool Infinite { get; set; }
    /// <summary>
    /// Gets or updates objects, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesCanvasObject> Objects { get; set; } = [];
    /// <summary>
    /// Gets or updates strokes, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesInkStroke> Strokes { get; set; } = [];
    /// <summary>
    /// Gets or updates ghost layers, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesGhostLayer> GhostLayers { get; set; } = [];
}

/// <summary>
/// Represents notes canvas object and keeps its related state and behavior together.
/// </summary>
public sealed class NotesCanvasObject
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public NotesCanvasObjectKind Kind { get; set; }
    /// <summary>
    /// Gets or updates text, the bindable or domain state represented by this property.
    /// </summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates x, the bindable or domain state represented by this property.
    /// </summary>
    public double X { get; set; }
    /// <summary>
    /// Gets or updates y, the bindable or domain state represented by this property.
    /// </summary>
    public double Y { get; set; }
    /// <summary>
    /// Gets or updates width, the bindable or domain state represented by this property.
    /// </summary>
    public double Width { get; set; } = 160;
    /// <summary>
    /// Gets or updates height, the bindable or domain state represented by this property.
    /// </summary>
    public double Height { get; set; } = 100;
    /// <summary>
    /// Gets or updates rotation, the bindable or domain state represented by this property.
    /// </summary>
    public double Rotation { get; set; }
    /// <summary>
    /// Gets or updates z index, the bindable or domain state represented by this property.
    /// </summary>
    public int ZIndex { get; set; }
    /// <summary>
    /// Gets or updates locked, the bindable or domain state represented by this property.
    /// </summary>
    public bool Locked { get; set; }
    /// <summary>
    /// Gets or updates group id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid? GroupId { get; set; }
    /// <summary>
    /// Gets or updates from object id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid? FromObjectId { get; set; }
    /// <summary>
    /// Gets or updates to object id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid? ToObjectId { get; set; }
    /// <summary>
    /// Gets or updates style json, the bindable or domain state represented by this property.
    /// </summary>
    public string StyleJson { get; set; } = "{}";
}

/// <summary>
/// Represents notes ink stroke and keeps its related state and behavior together.
/// </summary>
public sealed class NotesInkStroke
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates tool, the bindable or domain state represented by this property.
    /// </summary>
    public string Tool { get; set; } = "pen";
    /// <summary>
    /// Gets or updates colour, the bindable or domain state represented by this property.
    /// </summary>
    public string Colour { get; set; } = "#FF2F80ED";
    /// <summary>
    /// Gets or updates base width, the bindable or domain state represented by this property.
    /// </summary>
    public double BaseWidth { get; set; } = 2.5;
    /// <summary>
    /// Gets or updates opacity, the bindable or domain state represented by this property.
    /// </summary>
    public double Opacity { get; set; } = 1;
    /// <summary>
    /// Reports whether ghost applies to the current state.
    /// </summary>
    public bool IsGhost { get; set; }
    /// <summary>
    /// Gets or updates ghost layer id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid? GhostLayerId { get; set; }
    /// <summary>
    /// Gets or updates points, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesInkPoint> Points { get; set; } = [];
    /// <summary>
    /// Gets or updates recognition text, the bindable or domain state represented by this property.
    /// </summary>
    public string RecognitionText { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates recognition confidence, the bindable or domain state represented by this property.
    /// </summary>
    public double RecognitionConfidence { get; set; }
}

/// <summary>
/// Represents notes ink point and keeps its related state and behavior together.
/// </summary>
public sealed class NotesInkPoint
{
    /// <summary>
    /// Gets or updates x, the bindable or domain state represented by this property.
    /// </summary>
    public double X { get; set; }
    /// <summary>
    /// Gets or updates y, the bindable or domain state represented by this property.
    /// </summary>
    public double Y { get; set; }
    /// <summary>
    /// Gets or updates pressure, the bindable or domain state represented by this property.
    /// </summary>
    public double Pressure { get; set; } = 0.5;
    /// <summary>
    /// Gets or updates tilt x, the bindable or domain state represented by this property.
    /// </summary>
    public double TiltX { get; set; }
    /// <summary>
    /// Gets or updates tilt y, the bindable or domain state represented by this property.
    /// </summary>
    public double TiltY { get; set; }
    /// <summary>
    /// Gets or updates timestamp milliseconds, the bindable or domain state represented by this property.
    /// </summary>
    public long TimestampMilliseconds { get; set; }
}

/// <summary>
/// Represents notes ghost layer and keeps its related state and behavior together.
/// </summary>
public sealed class NotesGhostLayer
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name { get; set; } = "Answer";
    /// <summary>
    /// Gets or updates reveal mode, the bindable or domain state represented by this property.
    /// </summary>
    public NotesGhostRevealMode RevealMode { get; set; } = NotesGhostRevealMode.Tap;
    /// <summary>
    /// Reports whether revealed applies to the current state.
    /// </summary>
    public bool IsRevealed { get; set; }
    /// <summary>
    /// Gets or updates hint, the bindable or domain state represented by this property.
    /// </summary>
    public string Hint { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates answer group id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid? AnswerGroupId { get; set; }
    /// <summary>
    /// Gets or updates stroke ids, the bindable or domain state represented by this property.
    /// </summary>
    public List<Guid> StrokeIds { get; set; } = [];
    /// <summary>
    /// Gets or updates object ids, the bindable or domain state represented by this property.
    /// </summary>
    public List<Guid> ObjectIds { get; set; } = [];
    /// <summary>
    /// Gets or updates masks, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesOcclusionMask> Masks { get; set; } = [];
    /// <summary>
    /// Gets or updates include when exporting, the bindable or domain state represented by this property.
    /// </summary>
    public bool IncludeWhenExporting { get; set; }
}

/// <summary>
/// Represents notes occlusion mask and keeps its related state and behavior together.
/// </summary>
public sealed class NotesOcclusionMask
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates x, the bindable or domain state represented by this property.
    /// </summary>
    public double X { get; set; }
    /// <summary>
    /// Gets or updates y, the bindable or domain state represented by this property.
    /// </summary>
    public double Y { get; set; }
    /// <summary>
    /// Gets or updates width, the bindable or domain state represented by this property.
    /// </summary>
    public double Width { get; set; } = 120;
    /// <summary>
    /// Gets or updates height, the bindable or domain state represented by this property.
    /// </summary>
    public double Height { get; set; } = 60;
    /// <summary>
    /// Gets or updates label, the bindable or domain state represented by this property.
    /// </summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates answer, the bindable or domain state represented by this property.
    /// </summary>
    public string Answer { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates revealed, the bindable or domain state represented by this property.
    /// </summary>
    public bool Revealed { get; set; }
}

/// <summary>
/// Represents notes flashcard data and keeps its related state and behavior together.
/// </summary>
public sealed class NotesFlashcardData
{
    /// <summary>
    /// Gets or updates card id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid CardId { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates front, the bindable or domain state represented by this property.
    /// </summary>
    public string Front { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates back, the bindable or domain state represented by this property.
    /// </summary>
    public string Back { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates hint, the bindable or domain state represented by this property.
    /// </summary>
    public string Hint { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates source block id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid? SourceBlockId { get; set; }
    /// <summary>
    /// Gets or updates source anchor, the bindable or domain state represented by this property.
    /// </summary>
    public string SourceAnchor { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates occlusion masks, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesOcclusionMask> OcclusionMasks { get; set; } = [];
    /// <summary>
    /// Gets or updates schedule, the bindable or domain state represented by this property.
    /// </summary>
    public NotesFlashcardSchedule Schedule { get; set; } = new();
    /// <summary>
    /// Gets or updates tags, the bindable or domain state represented by this property.
    /// </summary>
    public List<string> Tags { get; set; } = [];
}

/// <summary>
/// Represents notes flashcard schedule and keeps its related state and behavior together.
/// </summary>
public sealed class NotesFlashcardSchedule
{
    /// <summary>
    /// Gets or updates due at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset DueAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or updates interval days, the bindable or domain state represented by this property.
    /// </summary>
    public int IntervalDays { get; set; }
    /// <summary>
    /// Gets or updates ease factor, the bindable or domain state represented by this property.
    /// </summary>
    public double EaseFactor { get; set; } = 2.5;
    /// <summary>
    /// Gets or updates repetitions, the bindable or domain state represented by this property.
    /// </summary>
    public int Repetitions { get; set; }
    /// <summary>
    /// Gets or updates lapses, the bindable or domain state represented by this property.
    /// </summary>
    public int Lapses { get; set; }
    /// <summary>
    /// Gets or updates last confidence, the bindable or domain state represented by this property.
    /// </summary>
    public double LastConfidence { get; set; }
}

/// <summary>
/// Represents notes flashcard review and keeps its related state and behavior together.
/// </summary>
public sealed class NotesFlashcardReview
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates card id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid CardId { get; set; }
    /// <summary>
    /// Gets or updates reviewed at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset ReviewedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or updates rating, the bindable or domain state represented by this property.
    /// </summary>
    public NotesFlashcardRating Rating { get; set; }
    /// <summary>
    /// Gets or updates confidence, the bindable or domain state represented by this property.
    /// </summary>
    public double Confidence { get; set; }
    /// <summary>
    /// Gets or updates previous interval days, the bindable or domain state represented by this property.
    /// </summary>
    public int PreviousIntervalDays { get; set; }
    /// <summary>
    /// Gets or updates new interval days, the bindable or domain state represented by this property.
    /// </summary>
    public int NewIntervalDays { get; set; }
    /// <summary>
    /// Gets or updates response time, the bindable or domain state represented by this property.
    /// </summary>
    public TimeSpan ResponseTime { get; set; }
}

/// <summary>
/// Represents notes named style and keeps its related state and behavior together.
/// </summary>
public sealed class NotesNamedStyle
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public string Id { get; set; } = "normal";
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name { get; set; } = "Normal";
    /// <summary>
    /// Gets or updates based on, the bindable or domain state represented by this property.
    /// </summary>
    public string BasedOn { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates character, the bindable or domain state represented by this property.
    /// </summary>
    public NotesTextRun Character { get; set; } = new();
    /// <summary>
    /// Gets or updates paragraph, the bindable or domain state represented by this property.
    /// </summary>
    public NotesParagraphFormat Paragraph { get; set; } = new();

    /// <summary>
    /// Creates defaults with the invariants required by its callers.
    /// </summary>
    public static List<NotesNamedStyle> CreateDefaults() =>
    [
        new NotesNamedStyle(),
        new NotesNamedStyle { Id = "heading-1", Name = "Heading 1", Character = new NotesTextRun { FontSize = 28, Bold = true }, Paragraph = new NotesParagraphFormat { SpaceBefore = 18, SpaceAfter = 10, KeepWithNext = true } },
        new NotesNamedStyle { Id = "heading-2", Name = "Heading 2", Character = new NotesTextRun { FontSize = 22, Bold = true }, Paragraph = new NotesParagraphFormat { SpaceBefore = 14, SpaceAfter = 8, KeepWithNext = true } },
        new NotesNamedStyle { Id = "quote", Name = "Quote", Character = new NotesTextRun { Italic = true }, Paragraph = new NotesParagraphFormat { IndentLeft = 24, IndentRight = 24, SpaceBefore = 8, SpaceAfter = 8 } },
        new NotesNamedStyle { Id = "code", Name = "Code", Character = new NotesTextRun { FontFamily = "Cascadia Mono", FontSize = 13 }, Paragraph = new NotesParagraphFormat { SpaceBefore = 8, SpaceAfter = 8 } }
    ];
}

/// <summary>
/// Represents notes field and keeps its related state and behavior together.
/// </summary>
public sealed class NotesField
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates value, the bindable or domain state represented by this property.
    /// </summary>
    public string Value { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates format, the bindable or domain state represented by this property.
    /// </summary>
    public string Format { get; set; } = string.Empty;
    /// <summary>
    /// Reports whether computed applies to the current state.
    /// </summary>
    public bool IsComputed { get; set; }
}

/// <summary>
/// Represents notes bookmark and keeps its related state and behavior together.
/// </summary>
public sealed class NotesBookmark
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates block id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid BlockId { get; set; }
    /// <summary>
    /// Gets or updates offset, the bindable or domain state represented by this property.
    /// </summary>
    public int Offset { get; set; }
}

/// <summary>
/// Represents notes citation and keeps its related state and behavior together.
/// </summary>
public sealed class NotesCitation
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates key, the bindable or domain state represented by this property.
    /// </summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates authors, the bindable or domain state represented by this property.
    /// </summary>
    public string Authors { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates year, the bindable or domain state represented by this property.
    /// </summary>
    public int? Year { get; set; }
    /// <summary>
    /// Gets or updates publisher, the bindable or domain state represented by this property.
    /// </summary>
    public string Publisher { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates doi, the bindable or domain state represented by this property.
    /// </summary>
    public string Doi { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates url, the bindable or domain state represented by this property.
    /// </summary>
    public string Url { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates accessed at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset? AccessedAt { get; set; }
    /// <summary>
    /// Gets or updates evidence excerpt, the bindable or domain state represented by this property.
    /// </summary>
    public string EvidenceExcerpt { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates source location, the bindable or domain state represented by this property.
    /// </summary>
    public string SourceLocation { get; set; } = string.Empty;
}

/// <summary>
/// Represents notes comment and keeps its related state and behavior together.
/// </summary>
public sealed class NotesComment
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates block id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid BlockId { get; set; }
    /// <summary>
    /// Gets or updates start offset, the bindable or domain state represented by this property.
    /// </summary>
    public int StartOffset { get; set; }
    /// <summary>
    /// Gets or updates end offset, the bindable or domain state represented by this property.
    /// </summary>
    public int EndOffset { get; set; }
    /// <summary>
    /// Gets or updates author, the bindable or domain state represented by this property.
    /// </summary>
    public string Author { get; set; } = Environment.UserName;
    /// <summary>
    /// Gets or updates text, the bindable or domain state represented by this property.
    /// </summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates state, the bindable or domain state represented by this property.
    /// </summary>
    public NotesCommentState State { get; set; }
    /// <summary>
    /// Creates d at with the invariants required by its callers.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or updates resolved at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset? ResolvedAt { get; set; }
    /// <summary>
    /// Gets or updates replies, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesCommentReply> Replies { get; set; } = [];
}

/// <summary>
/// Represents notes comment reply and keeps its related state and behavior together.
/// </summary>
public sealed class NotesCommentReply
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates author, the bindable or domain state represented by this property.
    /// </summary>
    public string Author { get; set; } = Environment.UserName;
    /// <summary>
    /// Gets or updates text, the bindable or domain state represented by this property.
    /// </summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>
    /// Creates d at with the invariants required by its callers.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Represents notes revision and keeps its related state and behavior together.
/// </summary>
public sealed class NotesRevision
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public NotesRevisionKind Kind { get; set; }
    /// <summary>
    /// Gets or updates block id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid? BlockId { get; set; }
    /// <summary>
    /// Gets or updates author, the bindable or domain state represented by this property.
    /// </summary>
    public string Author { get; set; } = Environment.UserName;
    /// <summary>
    /// Gets or updates summary, the bindable or domain state represented by this property.
    /// </summary>
    public string Summary { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates before hash, the bindable or domain state represented by this property.
    /// </summary>
    public string BeforeHash { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates after hash, the bindable or domain state represented by this property.
    /// </summary>
    public string AfterHash { get; set; } = string.Empty;
    /// <summary>
    /// Creates d at with the invariants required by its callers.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Represents notes ai change and keeps its related state and behavior together.
/// </summary>
public sealed class NotesAiChange
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates block id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid? BlockId { get; set; }
    /// <summary>
    /// Gets or updates instruction, the bindable or domain state represented by this property.
    /// </summary>
    public string Instruction { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates original content, the bindable or domain state represented by this property.
    /// </summary>
    public string OriginalContent { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates proposed content, the bindable or domain state represented by this property.
    /// </summary>
    public string ProposedContent { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates explanation, the bindable or domain state represented by this property.
    /// </summary>
    public string Explanation { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates citation ids, the bindable or domain state represented by this property.
    /// </summary>
    public List<Guid> CitationIds { get; set; } = [];
    /// <summary>
    /// Gets or updates provider id, the bindable or domain state represented by this property.
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates model name, the bindable or domain state represented by this property.
    /// </summary>
    public string ModelName { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public NotesAiChangeStatus Status { get; set; } = NotesAiChangeStatus.Proposed;
    /// <summary>
    /// Creates d at with the invariants required by its callers.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or updates reviewed at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset? ReviewedAt { get; set; }
    /// <summary>
    /// Gets or updates reviewed by, the bindable or domain state represented by this property.
    /// </summary>
    public string ReviewedBy { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates user consent recorded, the bindable or domain state represented by this property.
    /// </summary>
    public bool UserConsentRecorded { get; set; }
    /// <summary>
    /// Gets or updates sent document context, the bindable or domain state represented by this property.
    /// </summary>
    public bool SentDocumentContext { get; set; }
}

/// <summary>
/// Represents notes collaboration state and keeps its related state and behavior together.
/// </summary>
public sealed class NotesCollaborationState
{
    /// <summary>
    /// Gets or updates owner id, the bindable or domain state represented by this property.
    /// </summary>
    public string OwnerId { get; set; } = Environment.UserName;
    /// <summary>
    /// Gets or updates collaborators, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesCollaborator> Collaborators { get; set; } = [];
    /// <summary>
    /// Gets or updates sync revision, the bindable or domain state represented by this property.
    /// </summary>
    public string SyncRevision { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates remote etag, the bindable or domain state represented by this property.
    /// </summary>
    public string RemoteEtag { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates conflict state, the bindable or domain state represented by this property.
    /// </summary>
    public NotesConflictState ConflictState { get; set; }
    /// <summary>
    /// Gets or updates last synced at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset? LastSyncedAt { get; set; }
    /// <summary>
    /// Gets or updates conflicts, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesConflict> Conflicts { get; set; } = [];
}

/// <summary>
/// Represents notes collaborator and keeps its related state and behavior together.
/// </summary>
public sealed class NotesCollaborator
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates display name, the bindable or domain state represented by this property.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates role, the bindable or domain state represented by this property.
    /// </summary>
    public string Role { get; set; } = "Viewer";
    /// <summary>
    /// Gets or updates last seen at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset? LastSeenAt { get; set; }
}

/// <summary>
/// Represents notes conflict and keeps its related state and behavior together.
/// </summary>
public sealed class NotesConflict
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates block id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid? BlockId { get; set; }
    /// <summary>
    /// Gets or updates local value, the bindable or domain state represented by this property.
    /// </summary>
    public string LocalValue { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates remote value, the bindable or domain state represented by this property.
    /// </summary>
    public string RemoteValue { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates resolution, the bindable or domain state represented by this property.
    /// </summary>
    public string Resolution { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates detected at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or updates resolved at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset? ResolvedAt { get; set; }
}

/// <summary>
/// Represents notes recovery state and keeps its related state and behavior together.
/// </summary>
public sealed class NotesRecoveryState
{
    /// <summary>
    /// Gets or updates last autosave at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset? LastAutosaveAt { get; set; }
    /// <summary>
    /// Gets or updates last backup at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset? LastBackupAt { get; set; }
    /// <summary>
    /// Gets or updates last recovered at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset? LastRecoveredAt { get; set; }
    /// <summary>
    /// Gets or updates last valid sha256, the bindable or domain state represented by this property.
    /// </summary>
    public string LastValidSha256 { get; set; } = string.Empty;
    /// <summary>
    /// Reports whether unsaved recovery applies to the current state.
    /// </summary>
    public bool HasUnsavedRecovery { get; set; }
    /// <summary>
    /// Gets or updates recovery reason, the bindable or domain state represented by this property.
    /// </summary>
    public string RecoveryReason { get; set; } = string.Empty;
}

/// <summary>
/// Represents notes document summary and keeps its related state and behavior together.
/// </summary>
public sealed record NotesDocumentSummary(
    Guid Id,
    string Title,
    DateTimeOffset UpdatedAt,
    long Version,
    int SectionCount,
    int BlockCount,
    int WordCount,
    bool HasRecovery);

/// <summary>
/// Represents notes version info and keeps its related state and behavior together.
/// </summary>
public sealed record NotesVersionInfo(
    string VersionId,
    long Version,
    DateTimeOffset CreatedAt,
    string Reason,
    long SizeBytes,
    string Sha256);

/// <summary>
/// Represents notes save result and keeps its related state and behavior together.
/// </summary>
public sealed record NotesSaveResult(
    Guid DocumentId,
    long Version,
    DateTimeOffset SavedAt,
    string Sha256,
    string CurrentPath,
    string VersionPath);

/// <summary>
/// Represents notes search hit and keeps its related state and behavior together.
/// </summary>
public sealed record NotesSearchHit(
    Guid DocumentId,
    string DocumentTitle,
    Guid SectionId,
    Guid PageId,
    Guid BlockId,
    string BlockKind,
    string Snippet,
    int StartOffset);

/// <summary>
/// Represents notes ai proposal request and keeps its related state and behavior together.
/// </summary>
public sealed record NotesAiProposalRequest(
    Guid DocumentId,
    Guid? BlockId,
    string Instruction,
    string SelectedText,
    string DocumentContext,
    string ModelName,
    bool AllowDocumentContext,
    IReadOnlyList<NotesCitation> Citations);

/// <summary>
/// Represents notes ai proposal result and keeps its related state and behavior together.
/// </summary>
public sealed record NotesAiProposalResult(
    string ProposedContent,
    string Explanation,
    IReadOnlyList<Guid> CitationIds,
    string ProviderId,
    string ModelName);
