namespace Haven.Core;

/// <summary>
/// An ink stroke drawn on a canvas or equation.
/// </summary>
public sealed class NotesInkStroke
{
    /// <summary>
    /// Gets or sets the stroke id.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the drawing tool.
    /// </summary>
    public string Tool { get; set; } = "pen";
    /// <summary>
    /// Gets or sets the stroke colour.
    /// </summary>
    public string Colour { get; set; } = "#FF2F80ED";
    /// <summary>
    /// Gets or sets the base width.
    /// </summary>
    public double BaseWidth { get; set; } = 2.5;
    /// <summary>
    /// Gets or sets the opacity.
    /// </summary>
    public double Opacity { get; set; } = 1;
    /// <summary>
    /// Gets or sets whether this is a ghost stroke.
    /// </summary>
    public bool IsGhost { get; set; }
    /// <summary>
    /// Gets or sets the ghost layer id.
    /// </summary>
    public Guid? GhostLayerId { get; set; }
    /// <summary>
    /// Gets or sets the ink points.
    /// </summary>
    public List<NotesInkPoint> Points { get; set; } = [];
    /// <summary>
    /// Gets or sets the recognition text.
    /// </summary>
    public string RecognitionText { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the recognition confidence.
    /// </summary>
    public double RecognitionConfidence { get; set; }
}

/// <summary>
/// A single point within an ink stroke.
/// </summary>
public sealed class NotesInkPoint
{
    /// <summary>
    /// Gets or sets the X coordinate.
    /// </summary>
    public double X { get; set; }
    /// <summary>
    /// Gets or sets the Y coordinate.
    /// </summary>
    public double Y { get; set; }
    /// <summary>
    /// Gets or sets the pressure.
    /// </summary>
    public double Pressure { get; set; } = 0.5;
    /// <summary>
    /// Gets or sets the X tilt.
    /// </summary>
    public double TiltX { get; set; }
    /// <summary>
    /// Gets or sets the Y tilt.
    /// </summary>
    public double TiltY { get; set; }
    /// <summary>
    /// Gets or sets the timestamp in milliseconds.
    /// </summary>
    public long TimestampMilliseconds { get; set; }
}

/// <summary>
/// A ghost layer for reveal-based study content.
/// </summary>
public sealed class NotesGhostLayer
{
    /// <summary>
    /// Gets or sets the layer id.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the layer name.
    /// </summary>
    public string Name { get; set; } = "Answer";
    /// <summary>
    /// Gets or sets the reveal mode.
    /// </summary>
    public NotesGhostRevealMode RevealMode { get; set; } = NotesGhostRevealMode.Tap;
    /// <summary>
    /// Gets or sets whether the layer is revealed.
    /// </summary>
    public bool IsRevealed { get; set; }
    /// <summary>
    /// Gets or sets the hint text.
    /// </summary>
    public string Hint { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the answer group id.
    /// </summary>
    public Guid? AnswerGroupId { get; set; }
    /// <summary>
    /// Gets or sets the associated stroke ids.
    /// </summary>
    public List<Guid> StrokeIds { get; set; } = [];
    /// <summary>
    /// Gets or sets the associated object ids.
    /// </summary>
    public List<Guid> ObjectIds { get; set; } = [];
    /// <summary>
    /// Gets or sets the occlusion masks.
    /// </summary>
    public List<NotesOcclusionMask> Masks { get; set; } = [];
    /// <summary>
    /// Gets or sets whether to include when exporting.
    /// </summary>
    public bool IncludeWhenExporting { get; set; }
}

/// <summary>
/// An occlusion mask within a ghost layer.
/// </summary>
public sealed class NotesOcclusionMask
{
    /// <summary>
    /// Gets or sets the mask id.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the X position.
    /// </summary>
    public double X { get; set; }
    /// <summary>
    /// Gets or sets the Y position.
    /// </summary>
    public double Y { get; set; }
    /// <summary>
    /// Gets or sets the width.
    /// </summary>
    public double Width { get; set; } = 120;
    /// <summary>
    /// Gets or sets the height.
    /// </summary>
    public double Height { get; set; } = 60;
    /// <summary>
    /// Gets or sets the label.
    /// </summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the answer text.
    /// </summary>
    public string Answer { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets whether the mask is revealed.
    /// </summary>
    public bool Revealed { get; set; }
}

/// <summary>
/// Flashcard data for a block.
/// </summary>
public sealed class NotesFlashcardData
{
    /// <summary>
    /// Gets or sets the card id.
    /// </summary>
    public Guid CardId { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the front text.
    /// </summary>
    public string Front { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the back text.
    /// </summary>
    public string Back { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the hint text.
    /// </summary>
    public string Hint { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the source block id.
    /// </summary>
    public Guid? SourceBlockId { get; set; }
    /// <summary>
    /// Gets or sets the source anchor.
    /// </summary>
    public string SourceAnchor { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the occlusion masks.
    /// </summary>
    public List<NotesOcclusionMask> OcclusionMasks { get; set; } = [];
    /// <summary>
    /// Gets or sets the flashcard schedule.
    /// </summary>
    public NotesFlashcardSchedule Schedule { get; set; } = new();
    /// <summary>
    /// Gets or sets the tags.
    /// </summary>
    public List<string> Tags { get; set; } = [];
}

/// <summary>
/// Spaced repetition schedule for a flashcard.
/// </summary>
public sealed class NotesFlashcardSchedule
{
    /// <summary>
    /// Gets or sets the due date.
    /// </summary>
    public DateTimeOffset DueAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or sets the interval in days.
    /// </summary>
    public int IntervalDays { get; set; }
    /// <summary>
    /// Gets or sets the ease factor.
    /// </summary>
    public double EaseFactor { get; set; } = 2.5;
    /// <summary>
    /// Gets or sets the repetition count.
    /// </summary>
    public int Repetitions { get; set; }
    /// <summary>
    /// Gets or sets the lapse count.
    /// </summary>
    public int Lapses { get; set; }
    /// <summary>
    /// Gets or sets the last confidence score.
    /// </summary>
    public double LastConfidence { get; set; }
}

/// <summary>
/// A flashcard review event.
/// </summary>
public sealed class NotesFlashcardReview
{
    /// <summary>
    /// Gets or sets the review id.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the card id.
    /// </summary>
    public Guid CardId { get; set; }
    /// <summary>
    /// Gets or sets the review timestamp.
    /// </summary>
    public DateTimeOffset ReviewedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or sets the rating.
    /// </summary>
    public NotesFlashcardRating Rating { get; set; }
    /// <summary>
    /// Gets or sets the confidence score.
    /// </summary>
    public double Confidence { get; set; }
    /// <summary>
    /// Gets or sets the previous interval in days.
    /// </summary>
    public int PreviousIntervalDays { get; set; }
    /// <summary>
    /// Gets or sets the new interval in days.
    /// </summary>
    public int NewIntervalDays { get; set; }
    /// <summary>
    /// Gets or sets the response time.
    /// </summary>
    public TimeSpan ResponseTime { get; set; }
}
