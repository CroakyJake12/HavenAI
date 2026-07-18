/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/NotesAbstractions.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns INotesDocumentValidator, INotesRepository, INotesImportExportService, INotesAiService, INotesAttachmentStore, NotesValidationIssue, NotesValidationResult, NotesFlashcardScheduler, NotesTextStatistics, NotesStatistics. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Defines the notes document validator contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface INotesDocumentValidator
{
    NotesValidationResult Validate(NotesDocument document);
}

/// <summary>
/// Defines the notes repository contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface INotesRepository
{
    Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken cancellationToken);
    Task<NotesDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken);
    Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken cancellationToken);
    Task DeleteAsync(Guid documentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid documentId, CancellationToken cancellationToken);
    Task<NotesDocument?> LoadVersionAsync(Guid documentId, string versionId, CancellationToken cancellationToken);
    Task<NotesDocument?> RecoverLatestAsync(Guid documentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken cancellationToken);
}

/// <summary>
/// Defines the notes import export service contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface INotesImportExportService
{
    IReadOnlyList<string> ImportExtensions { get; }
    IReadOnlyList<string> ExportExtensions { get; }
    Task<NotesDocument> ImportAsync(string sourcePath, CancellationToken cancellationToken);
    Task<string> ExportAsync(NotesDocument document, string destinationPath, CancellationToken cancellationToken);
    Task PrintAsync(NotesDocument document, CancellationToken cancellationToken);
}

/// <summary>
/// Defines the notes ai service contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface INotesAiService
{
    Task<NotesAiProposalResult> ProposeAsync(NotesAiProposalRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Defines the notes attachment store contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface INotesAttachmentStore
{
    Task<NotesMediaData> ImportAsync(string sourcePath, CancellationToken cancellationToken);
    Task<string> ResolvePathAsync(Guid attachmentId, CancellationToken cancellationToken);
    Task DeleteUnreferencedAsync(IReadOnlyCollection<Guid> referencedAttachmentIds, CancellationToken cancellationToken);
}

/// <summary>
/// Represents notes validation issue and keeps its related state and behavior together.
/// </summary>
public sealed record NotesValidationIssue(string Path, string Message, bool IsError);

/// <summary>
/// Represents notes validation result and keeps its related state and behavior together.
/// </summary>
public sealed record NotesValidationResult(
    bool IsValid,
    IReadOnlyList<NotesValidationIssue> Issues);

/// <summary>
/// Represents notes flashcard scheduler and keeps its related state and behavior together.
/// </summary>
public static class NotesFlashcardScheduler
{
    /// <summary>
    /// Performs the review step owned by this component.
    /// </summary>
    public static NotesFlashcardReview Review(
        NotesFlashcardData card,
        NotesFlashcardRating rating,
        double confidence,
        TimeSpan responseTime,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(card);
        var schedule = card.Schedule;
        var previous = schedule.IntervalDays;
        var quality = rating switch
        {
            NotesFlashcardRating.Again => 1,
            NotesFlashcardRating.Hard => 3,
            NotesFlashcardRating.Good => 4,
            NotesFlashcardRating.Easy => 5,
            _ => 0
        };

        if (quality < 3)
        {
            schedule.Repetitions = 0;
            schedule.IntervalDays = 1;
            schedule.Lapses++;
        }
        else
        {
            schedule.IntervalDays = schedule.Repetitions switch
            {
                0 => 1,
                1 => rating == NotesFlashcardRating.Easy ? 6 : 3,
                _ => Math.Max(1, (int)Math.Round(schedule.IntervalDays * schedule.EaseFactor *
                    (rating == NotesFlashcardRating.Hard ? 0.8 : rating == NotesFlashcardRating.Easy ? 1.3 : 1)))
            };
            schedule.Repetitions++;
            schedule.EaseFactor = Math.Clamp(
                schedule.EaseFactor + (0.1 - (5 - quality) * (0.08 + (5 - quality) * 0.02)),
                1.3,
                3.2);
        }

        schedule.LastConfidence = Math.Clamp(confidence, 0, 1);
        schedule.DueAt = now.AddDays(schedule.IntervalDays);
        return new NotesFlashcardReview
        {
            CardId = card.CardId,
            ReviewedAt = now,
            Rating = rating,
            Confidence = schedule.LastConfidence,
            PreviousIntervalDays = previous,
            NewIntervalDays = schedule.IntervalDays,
            ResponseTime = responseTime
        };
    }
}

/// <summary>
/// Represents notes text statistics and keeps its related state and behavior together.
/// </summary>
public static class NotesTextStatistics
{
    /// <summary>
    /// Performs the calculate step owned by this component.
    /// </summary>
    public static NotesStatistics Calculate(NotesDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var text = EnumerateText(document).ToArray();
        var joined = string.Join("\n", text);
        var words = joined.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var characters = joined.Length;
        var charactersWithoutSpaces = joined.Count(character => !char.IsWhiteSpace(character));
        var paragraphs = document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks)
            .Count(block => block.Kind is NotesBlockKind.Paragraph or NotesBlockKind.Heading or NotesBlockKind.Quote);
        var readingMinutes = words == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(words / 220d));
        return new NotesStatistics(words, characters, charactersWithoutSpaces, paragraphs, readingMinutes);
    }

    /// <summary>
    /// Performs the enumerate text step owned by this component.
    /// </summary>
    public static IEnumerable<string> EnumerateText(NotesDocument document)
    {
        foreach (var section in document.Sections)
        {
            yield return section.Title;
            yield return section.Header;
            yield return section.Footer;
            foreach (var page in section.Pages)
            {
                yield return page.Title;
                foreach (var block in page.Blocks)
                {
                    yield return block.PlainText;
                    if (block.List is not null)
                        foreach (var item in block.List.Items) yield return item.Text;
                    if (block.Table is not null)
                        foreach (var cell in block.Table.Rows.SelectMany(row => row.Cells)) yield return cell.Text;
                    if (block.Media is not null)
                    {
                        yield return block.Media.Caption;
                        yield return block.Media.AltText;
                    }
                    if (block.Equation is not null)
                    {
                        yield return block.Equation.Source;
                        yield return block.Equation.AccessibleAlternative;
                    }
                    if (block.Html is not null)
                    {
                        yield return block.Html.FallbackText;
                        yield return block.Html.HtmlSource;
                    }
                    if (block.Flashcard is not null)
                    {
                        yield return block.Flashcard.Front;
                        yield return block.Flashcard.Back;
                        yield return block.Flashcard.Hint;
                    }
                }
            }
        }
    }
}

/// <summary>
/// Represents notes statistics and keeps its related state and behavior together.
/// </summary>
public sealed record NotesStatistics(
    int Words,
    int Characters,
    int CharactersWithoutSpaces,
    int Paragraphs,
    int ReadingMinutes);
