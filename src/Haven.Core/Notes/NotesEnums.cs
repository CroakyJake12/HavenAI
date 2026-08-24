namespace Haven.Core;

/// <summary>
/// Supported notes experience kinds.
/// </summary>
public enum NotesExperienceKind { Notes = 0, Present = 1, Data = 2, Tasks = 3, Imagine = 4 }
/// <summary>
/// Supported notes layout modes.
/// </summary>
public enum NotesLayoutMode { Paginated = 0, Continuous = 1, Freeform = 2, InfiniteCanvas = 3 }
/// <summary>
/// Supported notes block kinds.
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
    Divider = 13,
    Shape = 14
}
/// <summary>
/// Supported notes text alignments.
/// </summary>
public enum NotesTextAlignment { Left = 0, Center = 1, Right = 2, Justify = 3 }
/// <summary>
/// Supported notes list kinds.
/// </summary>
public enum NotesListKind { Bulleted = 0, Numbered = 1, Checklist = 2 }
/// <summary>
/// Supported notes canvas object kinds.
/// </summary>
public enum NotesCanvasObjectKind { Text = 0, Shape = 1, Image = 2, Connector = 3, Frame = 4, Ink = 5 }
/// <summary>
/// Supported notes equation view modes.
/// </summary>
public enum NotesEquationViewMode { Visual = 0, Source = 1, Split = 2 }
/// <summary>
/// Supported notes HTML view modes.
/// </summary>
public enum NotesHtmlViewMode { Visual = 0, Source = 1, Split = 2 }
/// <summary>
/// Supported notes ghost reveal modes.
/// </summary>
public enum NotesGhostRevealMode { Tap = 0, Hold = 1, Scratch = 2, StudyAnswer = 3 }
/// <summary>
/// Supported notes AI change statuses.
/// </summary>
public enum NotesAiChangeStatus { Proposed = 0, Approved = 1, Rejected = 2, Applied = 3, Cancelled = 4, Failed = 5 }
/// <summary>
/// Supported notes revision kinds.
/// </summary>
public enum NotesRevisionKind { Created = 0, Edited = 1, Imported = 2, AiApplied = 3, Restored = 4, ConflictResolved = 5 }
/// <summary>
/// Supported notes flashcard ratings.
/// </summary>
public enum NotesFlashcardRating { Again = 0, Hard = 1, Good = 2, Easy = 3 }
/// <summary>
/// Supported notes comment states.
/// </summary>
public enum NotesCommentState { Open = 0, Resolved = 1, Reopened = 2 }
/// <summary>
/// Supported notes conflict states.
/// </summary>
public enum NotesConflictState { None = 0, LocalAhead = 1, RemoteAhead = 2, Diverged = 3, Resolved = 4 }
