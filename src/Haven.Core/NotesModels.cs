namespace Haven.Core;

public enum NotesExperienceKind { Notes = 0, Present = 1, Data = 2, Tasks = 3, Imagine = 4 }
public enum NotesLayoutMode { Paginated = 0, Continuous = 1, Freeform = 2, InfiniteCanvas = 3 }
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
public enum NotesTextAlignment { Left = 0, Center = 1, Right = 2, Justify = 3 }
public enum NotesListKind { Bulleted = 0, Numbered = 1, Checklist = 2 }
public enum NotesCanvasObjectKind { Text = 0, Shape = 1, Image = 2, Connector = 3, Frame = 4, Ink = 5 }
public enum NotesEquationViewMode { Visual = 0, Source = 1, Split = 2 }
public enum NotesHtmlViewMode { Visual = 0, Source = 1, Split = 2 }
public enum NotesGhostRevealMode { Tap = 0, Hold = 1, Scratch = 2, StudyAnswer = 3 }
public enum NotesAiChangeStatus { Proposed = 0, Approved = 1, Rejected = 2, Applied = 3, Cancelled = 4, Failed = 5 }
public enum NotesRevisionKind { Created = 0, Edited = 1, Imported = 2, AiApplied = 3, Restored = 4, ConflictResolved = 5 }
public enum NotesFlashcardRating { Again = 0, Hard = 1, Good = 2, Easy = 3 }
public enum NotesCommentState { Open = 0, Resolved = 1, Reopened = 2 }
public enum NotesConflictState { None = 0, LocalAhead = 1, RemoteAhead = 2, Diverged = 3, Resolved = 4 }

public sealed class NotesDocument
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Untitled note";
    public string Language { get; set; } = "en-GB";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public long Version { get; set; }
    public NotesLayoutMode LayoutMode { get; set; } = NotesLayoutMode.Continuous;
    public NotesPageSetup PageSetup { get; set; } = new();
    public List<NotesSection> Sections { get; set; } = [NotesSection.CreateDefault()];
    public List<NotesNamedStyle> Styles { get; set; } = NotesNamedStyle.CreateDefaults();
    public List<NotesField> Fields { get; set; } = [];
    public List<NotesBookmark> Bookmarks { get; set; } = [];
    public List<NotesCitation> Citations { get; set; } = [];
    public List<NotesComment> Comments { get; set; } = [];
    public List<NotesRevision> Revisions { get; set; } = [];
    public List<NotesAiChange> AiChanges { get; set; } = [];
    public List<NotesFlashcardReview> FlashcardReviews { get; set; } = [];
    public NotesCollaborationState Collaboration { get; set; } = new();
    public NotesRecoveryState Recovery { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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

public sealed class NotesPageSetup
{
    public double WidthPoints { get; set; } = 595;
    public double HeightPoints { get; set; } = 842;
    public double MarginTopPoints { get; set; } = 72;
    public double MarginRightPoints { get; set; } = 72;
    public double MarginBottomPoints { get; set; } = 72;
    public double MarginLeftPoints { get; set; } = 72;
    public string Orientation { get; set; } = "Portrait";
    public string Background { get; set; } = "#FFFFFFFF";
    public bool ShowPageNumbers { get; set; } = true;
}

public sealed class NotesSection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Section 1";
    public string Header { get; set; } = string.Empty;
    public string Footer { get; set; } = string.Empty;
    public bool StartOnNewPage { get; set; }
    public List<NotesPage> Pages { get; set; } = [NotesPage.CreateDefault()];

    public static NotesSection CreateDefault() => new();
}

public sealed class NotesPage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Page 1";
    public int Order { get; set; }
    public double CanvasWidth { get; set; } = 1200;
    public double CanvasHeight { get; set; } = 900;
    public List<NotesBlock> Blocks { get; set; } = [NotesBlock.CreateParagraph()];
    public List<NotesCanvasObject> CanvasObjects { get; set; } = [];

    public static NotesPage CreateDefault() => new();
}

public sealed class NotesBlock
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public NotesBlockKind Kind { get; set; } = NotesBlockKind.Paragraph;
    public int Order { get; set; }
    public string StyleId { get; set; } = "normal";
    public string PlainText { get; set; } = string.Empty;
    public List<NotesTextRun> Runs { get; set; } = [];
    public NotesParagraphFormat Paragraph { get; set; } = new();
    public NotesListData? List { get; set; }
    public NotesTableData? Table { get; set; }
    public NotesMediaData? Media { get; set; }
    public NotesEquationData? Equation { get; set; }
    public NotesHtmlData? Html { get; set; }
    public NotesCanvasData? Canvas { get; set; }
    public NotesFlashcardData? Flashcard { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static NotesBlock CreateParagraph(string text = "") => new() { PlainText = text };
    public static NotesBlock Heading(string text = "Heading") => new() { Kind = NotesBlockKind.Heading, PlainText = text, StyleId = "heading-1" };
    public static NotesBlock EquationBlock() => new()
    {
        Kind = NotesBlockKind.Equation,
        Equation = new NotesEquationData { Source = "x^2 + y^2 = z^2", AccessibleAlternative = "x squared plus y squared equals z squared" }
    };
    public static NotesBlock HtmlBlock() => new()
    {
        Kind = NotesBlockKind.HtmlWidget,
        Html = new NotesHtmlData { HtmlSource = "<section><h2>Interactive note</h2><p>Edit the source safely.</p></section>" }
    };
    public static NotesBlock TableBlock(int rows = 3, int columns = 3) => new()
    {
        Kind = NotesBlockKind.Table,
        Table = NotesTableData.Create(rows, columns)
    };
    public static NotesBlock CanvasBlock() => new()
    {
        Kind = NotesBlockKind.Canvas,
        Canvas = new NotesCanvasData()
    };
    public static NotesBlock FlashcardBlock() => new()
    {
        Kind = NotesBlockKind.Flashcard,
        Flashcard = new NotesFlashcardData { Front = "Question", Back = "Answer" }
    };
}

public sealed class NotesTextRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = string.Empty;
    public string FontFamily { get; set; } = "Inter";
    public double FontSize { get; set; } = 14;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool StrikeThrough { get; set; }
    public string Foreground { get; set; } = "#FFEEEEEE";
    public string Background { get; set; } = "#00000000";
    public string? Link { get; set; }
    public string? Language { get; set; }
}

public sealed class NotesParagraphFormat
{
    public NotesTextAlignment Alignment { get; set; }
    public double LineSpacing { get; set; } = 1.25;
    public double SpaceBefore { get; set; }
    public double SpaceAfter { get; set; } = 8;
    public double IndentLeft { get; set; }
    public double IndentRight { get; set; }
    public double FirstLineIndent { get; set; }
    public bool KeepWithNext { get; set; }
    public bool PageBreakBefore { get; set; }
}

public sealed class NotesListData
{
    public NotesListKind Kind { get; set; }
    public int StartNumber { get; set; } = 1;
    public List<NotesListItem> Items { get; set; } = [];
}

public sealed class NotesListItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = string.Empty;
    public bool Checked { get; set; }
    public int Level { get; set; }
}

public sealed class NotesTableData
{
    public List<NotesTableRow> Rows { get; set; } = [];
    public bool HeaderRow { get; set; } = true;
    public bool RepeatHeader { get; set; }
    public string Style { get; set; } = "grid";

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

public sealed class NotesTableRow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public List<NotesTableCell> Cells { get; set; } = [];
    public bool IsHeader { get; set; }
}

public sealed class NotesTableCell
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = string.Empty;
    public int RowSpan { get; set; } = 1;
    public int ColumnSpan { get; set; } = 1;
    public string Background { get; set; } = "#00000000";
    public string VerticalAlignment { get; set; } = "Top";
}

public sealed class NotesMediaData
{
    public Guid AttachmentId { get; set; } = Guid.NewGuid();
    public string OriginalName { get; set; } = string.Empty;
    public string StoredPath { get; set; } = string.Empty;
    public string MediaType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string AltText { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string Wrapping { get; set; } = "Inline";
    public double Width { get; set; } = 400;
    public double Height { get; set; } = 300;
    public double Rotation { get; set; }
    public double CropLeft { get; set; }
    public double CropTop { get; set; }
    public double CropRight { get; set; }
    public double CropBottom { get; set; }
}

public sealed class NotesEquationData
{
    public NotesEquationViewMode ViewMode { get; set; } = NotesEquationViewMode.Split;
    public string Source { get; set; } = string.Empty;
    public string VisualStructureJson { get; set; } = "{}";
    public string RenderedText { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public string AccessibleAlternative { get; set; } = string.Empty;
    public bool Numbered { get; set; }
    public int? Number { get; set; }
    public string Label { get; set; } = string.Empty;
    public Dictionary<string, string> Macros { get; set; } = new(StringComparer.Ordinal);
    public List<string> References { get; set; } = [];
    public List<NotesInkStroke> SourceStrokes { get; set; } = [];
}

public sealed class NotesHtmlData
{
    public NotesHtmlViewMode ViewMode { get; set; } = NotesHtmlViewMode.Split;
    public string HtmlSource { get; set; } = string.Empty;
    public string CssSource { get; set; } = string.Empty;
    public string JavaScriptSource { get; set; } = string.Empty;
    public bool AllowScripts { get; set; }
    public bool AllowNetwork { get; set; }
    public bool AllowForms { get; set; }
    public bool AllowPopups { get; set; }
    public string FallbackText { get; set; } = string.Empty;
    public string SnapshotPath { get; set; } = string.Empty;
    public double Width { get; set; } = 640;
    public double Height { get; set; } = 360;
    public string LastSecurityError { get; set; } = string.Empty;
}

public sealed class NotesCanvasData
{
    public double Width { get; set; } = 1200;
    public double Height { get; set; } = 900;
    public double Zoom { get; set; } = 1;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public bool Infinite { get; set; }
    public List<NotesCanvasObject> Objects { get; set; } = [];
    public List<NotesInkStroke> Strokes { get; set; } = [];
    public List<NotesGhostLayer> GhostLayers { get; set; } = [];
}

public sealed class NotesCanvasObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public NotesCanvasObjectKind Kind { get; set; }
    public string Text { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 160;
    public double Height { get; set; } = 100;
    public double Rotation { get; set; }
    public int ZIndex { get; set; }
    public bool Locked { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? FromObjectId { get; set; }
    public Guid? ToObjectId { get; set; }
    public string StyleJson { get; set; } = "{}";
}

public sealed class NotesInkStroke
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Tool { get; set; } = "pen";
    public string Colour { get; set; } = "#FF2F80ED";
    public double BaseWidth { get; set; } = 2.5;
    public double Opacity { get; set; } = 1;
    public bool IsGhost { get; set; }
    public Guid? GhostLayerId { get; set; }
    public List<NotesInkPoint> Points { get; set; } = [];
    public string RecognitionText { get; set; } = string.Empty;
    public double RecognitionConfidence { get; set; }
}

public sealed class NotesInkPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Pressure { get; set; } = 0.5;
    public double TiltX { get; set; }
    public double TiltY { get; set; }
    public long TimestampMilliseconds { get; set; }
}

public sealed class NotesGhostLayer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Answer";
    public NotesGhostRevealMode RevealMode { get; set; } = NotesGhostRevealMode.Tap;
    public bool IsRevealed { get; set; }
    public string Hint { get; set; } = string.Empty;
    public Guid? AnswerGroupId { get; set; }
    public List<Guid> StrokeIds { get; set; } = [];
    public List<Guid> ObjectIds { get; set; } = [];
    public List<NotesOcclusionMask> Masks { get; set; } = [];
    public bool IncludeWhenExporting { get; set; }
}

public sealed class NotesOcclusionMask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 120;
    public double Height { get; set; } = 60;
    public string Label { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public bool Revealed { get; set; }
}

public sealed class NotesFlashcardData
{
    public Guid CardId { get; set; } = Guid.NewGuid();
    public string Front { get; set; } = string.Empty;
    public string Back { get; set; } = string.Empty;
    public string Hint { get; set; } = string.Empty;
    public Guid? SourceBlockId { get; set; }
    public string SourceAnchor { get; set; } = string.Empty;
    public List<NotesOcclusionMask> OcclusionMasks { get; set; } = [];
    public NotesFlashcardSchedule Schedule { get; set; } = new();
    public List<string> Tags { get; set; } = [];
}

public sealed class NotesFlashcardSchedule
{
    public DateTimeOffset DueAt { get; set; } = DateTimeOffset.UtcNow;
    public int IntervalDays { get; set; }
    public double EaseFactor { get; set; } = 2.5;
    public int Repetitions { get; set; }
    public int Lapses { get; set; }
    public double LastConfidence { get; set; }
}

public sealed class NotesFlashcardReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CardId { get; set; }
    public DateTimeOffset ReviewedAt { get; set; } = DateTimeOffset.UtcNow;
    public NotesFlashcardRating Rating { get; set; }
    public double Confidence { get; set; }
    public int PreviousIntervalDays { get; set; }
    public int NewIntervalDays { get; set; }
    public TimeSpan ResponseTime { get; set; }
}

public sealed class NotesNamedStyle
{
    public string Id { get; set; } = "normal";
    public string Name { get; set; } = "Normal";
    public string BasedOn { get; set; } = string.Empty;
    public NotesTextRun Character { get; set; } = new();
    public NotesParagraphFormat Paragraph { get; set; } = new();

    public static List<NotesNamedStyle> CreateDefaults() =>
    [
        new NotesNamedStyle(),
        new NotesNamedStyle { Id = "heading-1", Name = "Heading 1", Character = new NotesTextRun { FontSize = 28, Bold = true }, Paragraph = new NotesParagraphFormat { SpaceBefore = 18, SpaceAfter = 10, KeepWithNext = true } },
        new NotesNamedStyle { Id = "heading-2", Name = "Heading 2", Character = new NotesTextRun { FontSize = 22, Bold = true }, Paragraph = new NotesParagraphFormat { SpaceBefore = 14, SpaceAfter = 8, KeepWithNext = true } },
        new NotesNamedStyle { Id = "quote", Name = "Quote", Character = new NotesTextRun { Italic = true }, Paragraph = new NotesParagraphFormat { IndentLeft = 24, IndentRight = 24, SpaceBefore = 8, SpaceAfter = 8 } },
        new NotesNamedStyle { Id = "code", Name = "Code", Character = new NotesTextRun { FontFamily = "Cascadia Mono", FontSize = 13 }, Paragraph = new NotesParagraphFormat { SpaceBefore = 8, SpaceAfter = 8 } }
    ];
}

public sealed class NotesField
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public bool IsComputed { get; set; }
}

public sealed class NotesBookmark
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public Guid BlockId { get; set; }
    public int Offset { get; set; }
}

public sealed class NotesCitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Authors { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string Publisher { get; set; } = string.Empty;
    public string Doi { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTimeOffset? AccessedAt { get; set; }
    public string EvidenceExcerpt { get; set; } = string.Empty;
    public string SourceLocation { get; set; } = string.Empty;
}

public sealed class NotesComment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BlockId { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public string Author { get; set; } = Environment.UserName;
    public string Text { get; set; } = string.Empty;
    public NotesCommentState State { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }
    public List<NotesCommentReply> Replies { get; set; } = [];
}

public sealed class NotesCommentReply
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Author { get; set; } = Environment.UserName;
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class NotesRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public NotesRevisionKind Kind { get; set; }
    public Guid? BlockId { get; set; }
    public string Author { get; set; } = Environment.UserName;
    public string Summary { get; set; } = string.Empty;
    public string BeforeHash { get; set; } = string.Empty;
    public string AfterHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class NotesAiChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? BlockId { get; set; }
    public string Instruction { get; set; } = string.Empty;
    public string OriginalContent { get; set; } = string.Empty;
    public string ProposedContent { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public List<Guid> CitationIds { get; set; } = [];
    public string ProviderId { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public NotesAiChangeStatus Status { get; set; } = NotesAiChangeStatus.Proposed;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAt { get; set; }
    public string ReviewedBy { get; set; } = string.Empty;
    public bool UserConsentRecorded { get; set; }
    public bool SentDocumentContext { get; set; }
}

public sealed class NotesCollaborationState
{
    public string OwnerId { get; set; } = Environment.UserName;
    public List<NotesCollaborator> Collaborators { get; set; } = [];
    public string SyncRevision { get; set; } = string.Empty;
    public string RemoteEtag { get; set; } = string.Empty;
    public NotesConflictState ConflictState { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public List<NotesConflict> Conflicts { get; set; } = [];
}

public sealed class NotesCollaborator
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = "Viewer";
    public DateTimeOffset? LastSeenAt { get; set; }
}

public sealed class NotesConflict
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? BlockId { get; set; }
    public string LocalValue { get; set; } = string.Empty;
    public string RemoteValue { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }
}

public sealed class NotesRecoveryState
{
    public DateTimeOffset? LastAutosaveAt { get; set; }
    public DateTimeOffset? LastBackupAt { get; set; }
    public DateTimeOffset? LastRecoveredAt { get; set; }
    public string LastValidSha256 { get; set; } = string.Empty;
    public bool HasUnsavedRecovery { get; set; }
    public string RecoveryReason { get; set; } = string.Empty;
}

public sealed record NotesDocumentSummary(
    Guid Id,
    string Title,
    DateTimeOffset UpdatedAt,
    long Version,
    int SectionCount,
    int BlockCount,
    int WordCount,
    bool HasRecovery);

public sealed record NotesVersionInfo(
    string VersionId,
    long Version,
    DateTimeOffset CreatedAt,
    string Reason,
    long SizeBytes,
    string Sha256);

public sealed record NotesSaveResult(
    Guid DocumentId,
    long Version,
    DateTimeOffset SavedAt,
    string Sha256,
    string CurrentPath,
    string VersionPath);

public sealed record NotesSearchHit(
    Guid DocumentId,
    string DocumentTitle,
    Guid SectionId,
    Guid PageId,
    Guid BlockId,
    string BlockKind,
    string Snippet,
    int StartOffset);

public sealed record NotesAiProposalRequest(
    Guid DocumentId,
    Guid? BlockId,
    string Instruction,
    string SelectedText,
    string DocumentContext,
    string ModelName,
    bool AllowDocumentContext,
    IReadOnlyList<NotesCitation> Citations);

public sealed record NotesAiProposalResult(
    string ProposedContent,
    string Explanation,
    IReadOnlyList<Guid> CitationIds,
    string ProviderId,
    string ModelName);
