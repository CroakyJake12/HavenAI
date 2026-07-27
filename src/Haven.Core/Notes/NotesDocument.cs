namespace Haven.Core;

/// <summary>
/// Root notes document with all related state.
/// </summary>
public sealed class NotesDocument
{
    /// <summary>
    /// Current schema version.
    /// </summary>
    public const int CurrentSchemaVersion = 1;
    /// <summary>
    /// Gets or sets schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    /// <summary>
    /// Gets or sets the document id.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the document title.
    /// </summary>
    public string Title { get; set; } = "Untitled note";
    /// <summary>
    /// Gets or sets the document language.
    /// </summary>
    public string Language { get; set; } = "en-GB";
    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or sets the last update timestamp.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or sets the document version counter.
    /// </summary>
    public long Version { get; set; }
    /// <summary>
    /// Gets or sets the layout mode.
    /// </summary>
    public NotesLayoutMode LayoutMode { get; set; } = NotesLayoutMode.Continuous;
    /// <summary>
    /// Gets or sets the page setup.
    /// </summary>
    public NotesPageSetup PageSetup { get; set; } = new();
    /// <summary>
    /// Gets or sets the sections.
    /// </summary>
    public List<NotesSection> Sections { get; set; } = [NotesSection.CreateDefault()];
    /// <summary>
    /// Gets or sets the named styles.
    /// </summary>
    public List<NotesNamedStyle> Styles { get; set; } = NotesNamedStyle.CreateDefaults();
    /// <summary>
    /// Gets or sets the fields.
    /// </summary>
    public List<NotesField> Fields { get; set; } = [];
    /// <summary>
    /// Gets or sets the bookmarks.
    /// </summary>
    public List<NotesBookmark> Bookmarks { get; set; } = [];
    /// <summary>
    /// Gets or sets the citations.
    /// </summary>
    public List<NotesCitation> Citations { get; set; } = [];
    /// <summary>
    /// Gets or sets the comments.
    /// </summary>
    public List<NotesComment> Comments { get; set; } = [];
    /// <summary>
    /// Gets or sets the revisions.
    /// </summary>
    public List<NotesRevision> Revisions { get; set; } = [];
    /// <summary>
    /// Gets or sets the AI changes.
    /// </summary>
    public List<NotesAiChange> AiChanges { get; set; } = [];
    /// <summary>
    /// Gets or sets the flashcard reviews.
    /// </summary>
    public List<NotesFlashcardReview> FlashcardReviews { get; set; } = [];
    /// <summary>
    /// Gets or sets the collaboration state.
    /// </summary>
    public NotesCollaborationState Collaboration { get; set; } = new();
    /// <summary>
    /// Gets or sets the recovery state.
    /// </summary>
    public NotesRecoveryState Recovery { get; set; } = new();
    /// <summary>
    /// Gets or sets additional metadata.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a new document with an optional title.
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
/// Page layout settings for a notes document.
/// </summary>
public sealed class NotesPageSetup
{
    /// <summary>
    /// Gets or sets the page width in points.
    /// </summary>
    public double WidthPoints { get; set; } = 595;
    /// <summary>
    /// Gets or sets the page height in points.
    /// </summary>
    public double HeightPoints { get; set; } = 842;
    /// <summary>
    /// Gets or sets the top margin in points.
    /// </summary>
    public double MarginTopPoints { get; set; } = 72;
    /// <summary>
    /// Gets or sets the right margin in points.
    /// </summary>
    public double MarginRightPoints { get; set; } = 72;
    /// <summary>
    /// Gets or sets the bottom margin in points.
    /// </summary>
    public double MarginBottomPoints { get; set; } = 72;
    /// <summary>
    /// Gets or sets the left margin in points.
    /// </summary>
    public double MarginLeftPoints { get; set; } = 72;
    /// <summary>
    /// Gets or sets the page orientation.
    /// </summary>
    public string Orientation { get; set; } = "Portrait";
    /// <summary>
    /// Gets or sets the background colour.
    /// </summary>
    public string Background { get; set; } = "#FFFFFFFF";
    /// <summary>
    /// Gets or sets whether page numbers are shown.
    /// </summary>
    public bool ShowPageNumbers { get; set; } = true;
}

/// <summary>
/// A section within a notes document.
/// </summary>
public sealed class NotesSection
{
    /// <summary>
    /// Gets or sets the section id.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the section title.
    /// </summary>
    public string Title { get; set; } = "Section 1";
    /// <summary>
    /// Gets or sets the section header.
    /// </summary>
    public string Header { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the section footer.
    /// </summary>
    public string Footer { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets whether the section starts on a new page.
    /// </summary>
    public bool StartOnNewPage { get; set; }
    /// <summary>
    /// Gets or sets the pages.
    /// </summary>
    public List<NotesPage> Pages { get; set; } = [NotesPage.CreateDefault()];

    /// <summary>
    /// Creates a default section.
    /// </summary>
    public static NotesSection CreateDefault() => new();
}

/// <summary>
/// A page within a section.
/// </summary>
public sealed class NotesPage
{
    /// <summary>
    /// Gets or sets the page id.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the page title.
    /// </summary>
    public string Title { get; set; } = "Page 1";
    /// <summary>
    /// Gets or sets the page order.
    /// </summary>
    public int Order { get; set; }
    /// <summary>
    /// Gets or sets the canvas width.
    /// </summary>
    public double CanvasWidth { get; set; } = 1200;
    /// <summary>
    /// Gets or sets the canvas height.
    /// </summary>
    public double CanvasHeight { get; set; } = 900;
    /// <summary>
    /// Gets or sets the blocks.
    /// </summary>
    public List<NotesBlock> Blocks { get; set; } = [NotesBlock.CreateParagraph()];
    /// <summary>
    /// Gets or sets the canvas objects.
    /// </summary>
    public List<NotesCanvasObject> CanvasObjects { get; set; } = [];

    /// <summary>
    /// Creates a default page.
    /// </summary>
    public static NotesPage CreateDefault() => new();
}

/// <summary>
/// Summary of a notes document.
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
/// Version information for a saved document.
/// </summary>
public sealed record NotesVersionInfo(
    string VersionId,
    long Version,
    DateTimeOffset CreatedAt,
    string Reason,
    long SizeBytes,
    string Sha256);

/// <summary>
/// Result of saving a notes document.
/// </summary>
public sealed record NotesSaveResult(
    Guid DocumentId,
    long Version,
    DateTimeOffset SavedAt,
    string Sha256,
    string CurrentPath,
    string VersionPath);

/// <summary>
/// A search hit within a notes document.
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
