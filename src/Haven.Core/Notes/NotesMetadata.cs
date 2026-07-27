using System.Text.Json;

namespace Haven.Core;

/// <summary>
/// A named style definition.
/// </summary>
public sealed class NotesNamedStyle
{
    /// <summary>
    /// Gets or sets the style id.
    /// </summary>
    public string Id { get; set; } = "normal";
    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = "Normal";
    /// <summary>
    /// Gets or sets the base style id.
    /// </summary>
    public string BasedOn { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the character formatting.
    /// </summary>
    public NotesTextRun Character { get; set; } = new();
    /// <summary>
    /// Gets or sets the paragraph formatting.
    /// </summary>
    public NotesParagraphFormat Paragraph { get; set; } = new();

    /// <summary>
    /// Creates the default style set.
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
/// A custom field within a document.
/// </summary>
public sealed class NotesField
{
    /// <summary>
    /// Gets or sets the field id.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the field name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the field value.
    /// </summary>
    public string Value { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the value format.
    /// </summary>
    public string Format { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets whether the field is computed.
    /// </summary>
    public bool IsComputed { get; set; }
}

/// <summary>
/// A bookmark within a document.
/// </summary>
public sealed class NotesBookmark
{
    /// <summary>
    /// Gets or sets the bookmark id.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the bookmark name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the target block id.
    /// </summary>
    public Guid BlockId { get; set; }
    /// <summary>
    /// Gets or sets the character offset.
    /// </summary>
    public int Offset { get; set; }
}

/// <summary>
/// A citation reference.
/// </summary>
public sealed class NotesCitation
{
    /// <summary>
    /// Gets or sets the citation id.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the citation key.
    /// </summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the authors.
    /// </summary>
    public string Authors { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the publication year.
    /// </summary>
    public int? Year { get; set; }
    /// <summary>
    /// Gets or sets the publisher.
    /// </summary>
    public string Publisher { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the DOI.
    /// </summary>
    public string Doi { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the URL.
    /// </summary>
    public string Url { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the access date.
    /// </summary>
    public DateTimeOffset? AccessedAt { get; set; }
    /// <summary>
    /// Gets or sets the evidence excerpt.
    /// </summary>
    public string EvidenceExcerpt { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the source location.
    /// </summary>
    public string SourceLocation { get; set; } = string.Empty;
}

/// <summary>
/// A comment on a block.
/// </summary>
public sealed class NotesComment
{
    /// <summary>
    /// Gets or sets the comment id.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the target block id.
    /// </summary>
    public Guid BlockId { get; set; }
    /// <summary>
    /// Gets or sets the start offset.
    /// </summary>
    public int StartOffset { get; set; }
    /// <summary>
    /// Gets or sets the end offset.
    /// </summary>
    public int EndOffset { get; set; }
    /// <summary>
    /// Gets or sets the author.
    /// </summary>
    public string Author { get; set; } = Environment.UserName;
    /// <summary>
    /// Gets or sets the comment text.
    /// </summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the comment state.
    /// </summary>
    public NotesCommentState State { get; set; }
    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or sets the resolved timestamp.
    /// </summary>
    public DateTimeOffset? ResolvedAt { get; set; }
    /// <summary>
    /// Gets or sets the replies.
    /// </summary>
    public List<NotesCommentReply> Replies { get; set; } = [];
}

/// <summary>
/// A reply to a comment.
/// </summary>
public sealed class NotesCommentReply
{
    /// <summary>
    /// Gets or sets the reply id.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the author.
    /// </summary>
    public string Author { get; set; } = Environment.UserName;
    /// <summary>
    /// Gets or sets the reply text.
    /// </summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A revision entry in the document history.
/// </summary>
public sealed class NotesRevision
{
    /// <summary>
    /// Gets or sets the revision id.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the revision kind.
    /// </summary>
    public NotesRevisionKind Kind { get; set; }
    /// <summary>
    /// Gets or sets the target block id.
    /// </summary>
    public Guid? BlockId { get; set; }
    /// <summary>
    /// Gets or sets the author.
    /// </summary>
    public string Author { get; set; } = Environment.UserName;
    /// <summary>
    /// Gets or sets the revision summary.
    /// </summary>
    public string Summary { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the content hash before the change.
    /// </summary>
    public string BeforeHash { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the content hash after the change.
    /// </summary>
    public string AfterHash { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// An AI-proposed change to the document.
/// </summary>
public sealed class NotesAiChange
{
    /// <summary>
    /// Gets or sets the change id.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the target block id.
    /// </summary>
    public Guid? BlockId { get; set; }
    /// <summary>
    /// Gets or sets the user instruction.
    /// </summary>
    public string Instruction { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the original content.
    /// </summary>
    public string OriginalContent { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the proposed content.
    /// </summary>
    public string ProposedContent { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the explanation.
    /// </summary>
    public string Explanation { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the citation ids.
    /// </summary>
    public List<Guid> CitationIds { get; set; } = [];
    /// <summary>
    /// Gets or sets the provider id.
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the model name.
    /// </summary>
    public string ModelName { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the change status.
    /// </summary>
    public NotesAiChangeStatus Status { get; set; } = NotesAiChangeStatus.Proposed;
    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or sets the review timestamp.
    /// </summary>
    public DateTimeOffset? ReviewedAt { get; set; }
    /// <summary>
    /// Gets or sets the reviewer name.
    /// </summary>
    public string ReviewedBy { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets whether user consent was recorded.
    /// </summary>
    public bool UserConsentRecorded { get; set; }
    /// <summary>
    /// Gets or sets whether document context was sent.
    /// </summary>
    public bool SentDocumentContext { get; set; }
}

/// <summary>
/// Request for an AI proposal.
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
/// Result of an AI proposal.
/// </summary>
public sealed record NotesAiProposalResult(
    string ProposedContent,
    string Explanation,
    IReadOnlyList<Guid> CitationIds,
    string ProviderId,
    string ModelName);
