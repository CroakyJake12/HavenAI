/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/NotesAdvancedServices.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns NotesAdvancedDocumentState, NotesDocumentViewState, NotesExtendedPageLayout, NotesSectionHeaderFooterState, NotesAutocorrectEntry, NotesEquationLibraryEntry, NotesCanvasBookmarkEntry, NotesStudyAttempt, NotesCrossReference, NotesTrackedChange, NotesPrivacyState, NotesStudyPreferences, NotesAdvancedStateStore, NotesFindOptions, NotesFindMatch, NotesDocumentSearch, NotesFieldEvaluator, NotesDiffKind, NotesDiffLine, NotesVersionComparison, NotesVersionComparer, NotesStyleSetService, NotesTableOperations, NaturalStringComparer, NotesEquationTemplate, NotesEquationSymbol, NotesEquationTools, NotesCanvasOperations, RectD, NotesStudyTools, NotesCollaborationTools. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents notes advanced document state and keeps its related state and behavior together.
/// </summary>
public sealed class NotesAdvancedDocumentState
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
    /// Gets or updates view, the bindable or domain state represented by this property.
    /// </summary>
    public NotesDocumentViewState View { get; set; } = new();
    /// <summary>
    /// Gets or updates page layout, the bindable or domain state represented by this property.
    /// </summary>
    public NotesExtendedPageLayout PageLayout { get; set; } = new();
    /// <summary>
    /// Gets or updates section headers, the bindable or domain state represented by this property.
    /// </summary>
    public Dictionary<Guid, NotesSectionHeaderFooterState> SectionHeaders { get; set; } = [];
    /// <summary>
    /// Gets or updates autocorrect entries, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesAutocorrectEntry> AutocorrectEntries { get; set; } = [];
    /// <summary>
    /// Gets or updates equation library, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesEquationLibraryEntry> EquationLibrary { get; set; } = [];
    /// <summary>
    /// Reports whether canvas bookmarks is true for the current state.
    /// </summary>
    public List<NotesCanvasBookmarkEntry> CanvasBookmarks { get; set; } = [];
    /// <summary>
    /// Gets or updates study attempts, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesStudyAttempt> StudyAttempts { get; set; } = [];
    /// <summary>
    /// Gets or updates cross references, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesCrossReference> CrossReferences { get; set; } = [];
    /// <summary>
    /// Gets or updates tracked changes, the bindable or domain state represented by this property.
    /// </summary>
    public List<NotesTrackedChange> TrackedChanges { get; set; } = [];
    /// <summary>
    /// Gets or updates privacy, the bindable or domain state represented by this property.
    /// </summary>
    public NotesPrivacyState Privacy { get; set; } = new();
    /// <summary>
    /// Gets or updates study, the bindable or domain state represented by this property.
    /// </summary>
    public NotesStudyPreferences Study { get; set; } = new();
}

/// <summary>
/// Represents notes document view state and keeps its related state and behavior together.
/// </summary>
public sealed class NotesDocumentViewState
{
    /// <summary>
    /// Reports whether pinned applies to the current state.
    /// </summary>
    public bool IsPinned { get; set; }
    /// <summary>
    /// Gets or updates last opened at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset? LastOpenedAt { get; set; }
    /// <summary>
    /// Reports whether focus mode applies to the current state.
    /// </summary>
    public bool IsFocusMode { get; set; }
    /// <summary>
    /// Reports whether fullscreen applies to the current state.
    /// </summary>
    public bool IsFullscreen { get; set; }
    /// <summary>
    /// Gets or updates show library, the bindable or domain state represented by this property.
    /// </summary>
    public bool ShowLibrary { get; set; } = true;
    /// <summary>
    /// Gets or updates show outline, the bindable or domain state represented by this property.
    /// </summary>
    public bool ShowOutline { get; set; } = true;
    /// <summary>
    /// Gets or updates show formatting sidebar, the bindable or domain state represented by this property.
    /// </summary>
    public bool ShowFormattingSidebar { get; set; } = true;
    /// <summary>
    /// Gets or updates show status bar, the bindable or domain state represented by this property.
    /// </summary>
    public bool ShowStatusBar { get; set; } = true;
    /// <summary>
    /// Gets or updates show formatting marks, the bindable or domain state represented by this property.
    /// </summary>
    public bool ShowFormattingMarks { get; set; }
    /// <summary>
    /// Gets or updates interface scale, the bindable or domain state represented by this property.
    /// </summary>
    public double InterfaceScale { get; set; } = 1;
    /// <summary>
    /// Gets or updates toolbar items, the bindable or domain state represented by this property.
    /// </summary>
    public List<string> ToolbarItems { get; set; } =
    [
        "new", "save", "undo", "redo", "import", "export", "print"
    ];
}

/// <summary>
/// Represents notes extended page layout and keeps its related state and behavior together.
/// </summary>
public sealed class NotesExtendedPageLayout
{
    /// <summary>
    /// Gets or updates mirror margins, the bindable or domain state represented by this property.
    /// </summary>
    public bool MirrorMargins { get; set; }
    /// <summary>
    /// Gets or updates gutter points, the bindable or domain state represented by this property.
    /// </summary>
    public double GutterPoints { get; set; }
    /// <summary>
    /// Gets or updates columns, the bindable or domain state represented by this property.
    /// </summary>
    public int Columns { get; set; } = 1;
    /// <summary>
    /// Gets or updates column spacing points, the bindable or domain state represented by this property.
    /// </summary>
    public double ColumnSpacingPoints { get; set; } = 18;
    /// <summary>
    /// Gets or updates page border, the bindable or domain state represented by this property.
    /// </summary>
    public string PageBorder { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates watermark, the bindable or domain state represented by this property.
    /// </summary>
    public string Watermark { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates line numbering, the bindable or domain state represented by this property.
    /// </summary>
    public bool LineNumbering { get; set; }
    /// <summary>
    /// Gets or updates hyphenation, the bindable or domain state represented by this property.
    /// </summary>
    public bool Hyphenation { get; set; }
    /// <summary>
    /// Gets or updates vertical alignment, the bindable or domain state represented by this property.
    /// </summary>
    public string VerticalAlignment { get; set; } = "Top";
    /// <summary>
    /// Gets or updates different first page, the bindable or domain state represented by this property.
    /// </summary>
    public bool DifferentFirstPage { get; set; }
    /// <summary>
    /// Gets or updates different odd even pages, the bindable or domain state represented by this property.
    /// </summary>
    public bool DifferentOddEvenPages { get; set; }
    /// <summary>
    /// Gets or updates page number format, the bindable or domain state represented by this property.
    /// </summary>
    public string PageNumberFormat { get; set; } = "1, 2, 3";
    /// <summary>
    /// Gets or updates page number start, the bindable or domain state represented by this property.
    /// </summary>
    public int PageNumberStart { get; set; } = 1;
}

/// <summary>
/// Represents notes section header footer state and keeps its related state and behavior together.
/// </summary>
public sealed class NotesSectionHeaderFooterState
{
    /// <summary>
    /// Gets or updates first page header, the bindable or domain state represented by this property.
    /// </summary>
    public string FirstPageHeader { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates first page footer, the bindable or domain state represented by this property.
    /// </summary>
    public string FirstPageFooter { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates odd page header, the bindable or domain state represented by this property.
    /// </summary>
    public string OddPageHeader { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates odd page footer, the bindable or domain state represented by this property.
    /// </summary>
    public string OddPageFooter { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates even page header, the bindable or domain state represented by this property.
    /// </summary>
    public string EvenPageHeader { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates even page footer, the bindable or domain state represented by this property.
    /// </summary>
    public string EvenPageFooter { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates restart page number at, the bindable or domain state represented by this property.
    /// </summary>
    public int? RestartPageNumberAt { get; set; }
}

/// <summary>
/// Represents notes autocorrect entry and keeps its related state and behavior together.
/// </summary>
public sealed class NotesAutocorrectEntry
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates input, the bindable or domain state represented by this property.
    /// </summary>
    public string Input { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates replacement, the bindable or domain state represented by this property.
    /// </summary>
    public string Replacement { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates match case, the bindable or domain state represented by this property.
    /// </summary>
    public bool MatchCase { get; set; }
    /// <summary>
    /// Reports whether enabled applies to the current state.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Represents notes equation library entry and keeps its related state and behavior together.
/// </summary>
public sealed class NotesEquationLibraryEntry
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
    /// Gets or updates description, the bindable or domain state represented by this property.
    /// </summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates category, the bindable or domain state represented by this property.
    /// </summary>
    public string Category { get; set; } = "General";
    /// <summary>
    /// Gets or updates latex, the bindable or domain state represented by this property.
    /// </summary>
    public string Latex { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates tags, the bindable or domain state represented by this property.
    /// </summary>
    public List<string> Tags { get; set; } = [];
    /// <summary>
    /// Reports whether favourite applies to the current state.
    /// </summary>
    public bool IsFavourite { get; set; }
    /// <summary>
    /// Creates d at with the invariants required by its callers.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Represents notes canvas bookmark entry and keeps its related state and behavior together.
/// </summary>
public sealed class NotesCanvasBookmarkEntry
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates page id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid PageId { get; set; }
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates x, the bindable or domain state represented by this property.
    /// </summary>
    public double X { get; set; }
    /// <summary>
    /// Gets or updates y, the bindable or domain state represented by this property.
    /// </summary>
    public double Y { get; set; }
    /// <summary>
    /// Gets or updates zoom, the bindable or domain state represented by this property.
    /// </summary>
    public double Zoom { get; set; } = 1;
}

/// <summary>
/// Represents notes study attempt and keeps its related state and behavior together.
/// </summary>
public sealed class NotesStudyAttempt
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
    /// Gets or updates source block id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid? SourceBlockId { get; set; }
    /// <summary>
    /// Gets or updates attempt text, the bindable or domain state represented by this property.
    /// </summary>
    public string AttemptText { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates correctness, the bindable or domain state represented by this property.
    /// </summary>
    public string Correctness { get; set; } = "Unmarked";
    /// <summary>
    /// Gets or updates confidence, the bindable or domain state represented by this property.
    /// </summary>
    public double Confidence { get; set; }
    /// <summary>
    /// Gets or updates hints used, the bindable or domain state represented by this property.
    /// </summary>
    public int HintsUsed { get; set; }
    /// <summary>
    /// Gets or updates response time, the bindable or domain state represented by this property.
    /// </summary>
    public TimeSpan ResponseTime { get; set; }
    /// <summary>
    /// Gets or updates started at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or updates completed at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>
/// Represents notes cross reference and keeps its related state and behavior together.
/// </summary>
public sealed class NotesCrossReference
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or updates source block id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid SourceBlockId { get; set; }
    /// <summary>
    /// Gets or updates target block id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid TargetBlockId { get; set; }
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public string Kind { get; set; } = "Reference";
    /// <summary>
    /// Gets or updates label, the bindable or domain state represented by this property.
    /// </summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>
    /// Reports whether broken applies to the current state.
    /// </summary>
    public bool IsBroken { get; set; }
}

/// <summary>
/// Represents notes tracked change and keeps its related state and behavior together.
/// </summary>
public sealed class NotesTrackedChange
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
    /// Gets or updates author, the bindable or domain state represented by this property.
    /// </summary>
    public string Author { get; set; } = Environment.UserName;
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public string Kind { get; set; } = "Edit";
    /// <summary>
    /// Gets or updates before, the bindable or domain state represented by this property.
    /// </summary>
    public string Before { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates after, the bindable or domain state represented by this property.
    /// </summary>
    public string After { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates state, the bindable or domain state represented by this property.
    /// </summary>
    public string State { get; set; } = "Pending";
    /// <summary>
    /// Creates d at with the invariants required by its callers.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or updates reviewed at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset? ReviewedAt { get; set; }
}

/// <summary>
/// Represents notes privacy state and keeps its related state and behavior together.
/// </summary>
public sealed class NotesPrivacyState
{
    /// <summary>
    /// Gets or updates ai enabled, the bindable or domain state represented by this property.
    /// </summary>
    public bool AiEnabled { get; set; } = true;
    /// <summary>
    /// Gets or updates allow external providers, the bindable or domain state represented by this property.
    /// </summary>
    public bool AllowExternalProviders { get; set; }
    /// <summary>
    /// Gets or updates allow document context, the bindable or domain state represented by this property.
    /// </summary>
    public bool AllowDocumentContext { get; set; }
    /// <summary>
    /// Gets or updates allow workspace context, the bindable or domain state represented by this property.
    /// </summary>
    public bool AllowWorkspaceContext { get; set; }
    /// <summary>
    /// Gets or updates allow web research, the bindable or domain state represented by this property.
    /// </summary>
    public bool AllowWebResearch { get; set; }
    /// <summary>
    /// Gets or updates store ai provenance, the bindable or domain state represented by this property.
    /// </summary>
    public bool StoreAiProvenance { get; set; } = true;
    /// <summary>
    /// Gets or updates store research sources, the bindable or domain state represented by this property.
    /// </summary>
    public bool StoreResearchSources { get; set; } = true;
}

/// <summary>
/// Represents notes study preferences and keeps its related state and behavior together.
/// </summary>
public sealed class NotesStudyPreferences
{
    /// <summary>
    /// Gets or updates daily target, the bindable or domain state represented by this property.
    /// </summary>
    public int DailyTarget { get; set; } = 20;
    /// <summary>
    /// Gets or updates new card limit, the bindable or domain state represented by this property.
    /// </summary>
    public int NewCardLimit { get; set; } = 10;
    /// <summary>
    /// Gets or updates maximum cards per session, the bindable or domain state represented by this property.
    /// </summary>
    public int MaximumCardsPerSession { get; set; } = 50;
    /// <summary>
    /// Gets or updates shuffle, the bindable or domain state represented by this property.
    /// </summary>
    public bool Shuffle { get; set; }
    /// <summary>
    /// Gets or updates review mistakes only, the bindable or domain state represented by this property.
    /// </summary>
    public bool ReviewMistakesOnly { get; set; }
    /// <summary>
    /// Gets or updates cram mode, the bindable or domain state represented by this property.
    /// </summary>
    public bool CramMode { get; set; }
    /// <summary>
    /// Gets or updates exam date, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset? ExamDate { get; set; }
}

/// <summary>
/// Represents notes advanced state store and keeps its related state and behavior together.
/// </summary>
public static class NotesAdvancedStateStore
{
    /// <summary>
    /// Stores metadata key locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public const string MetadataKey = "haven.notes.advanced.v1";
    /// <summary>
    /// Stores maximum serialized bytes locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int MaximumSerializedBytes = 4 * 1024 * 1024;
    /// <summary>
    /// Stores json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    /// <summary>
    /// Performs the load step owned by this component.
    /// </summary>
    public static NotesAdvancedDocumentState Load(NotesDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.Metadata.TryGetValue(MetadataKey, out var json) || string.IsNullOrWhiteSpace(json))
            return new NotesAdvancedDocumentState();
        if (Encoding.UTF8.GetByteCount(json) > MaximumSerializedBytes)
            throw new InvalidDataException("The advanced Notes state exceeds the 4 MB safety limit.");
        var state = JsonSerializer.Deserialize<NotesAdvancedDocumentState>(json, JsonOptions)
                    ?? throw new InvalidDataException("The advanced Notes state was empty.");
        if (state.SchemaVersion != NotesAdvancedDocumentState.CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported advanced Notes state schema {state.SchemaVersion}.");
        Normalize(state);
        return state;
    }

    /// <summary>
    /// Performs the save step owned by this component.
    /// </summary>
    public static void Save(NotesDocument document, NotesAdvancedDocumentState state)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(state);
        state.SchemaVersion = NotesAdvancedDocumentState.CurrentSchemaVersion;
        Normalize(state);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaximumSerializedBytes)
            throw new InvalidDataException("The advanced Notes state exceeds the 4 MB safety limit.");
        document.Metadata[MetadataKey] = json;
    }

    /// <summary>
    /// Performs the normalize step owned by this component.
    /// </summary>
    private static void Normalize(NotesAdvancedDocumentState state)
    {
        state.View.InterfaceScale = Math.Clamp(state.View.InterfaceScale, 0.5, 3);
        state.PageLayout.Columns = Math.Clamp(state.PageLayout.Columns, 1, 12);
        state.PageLayout.ColumnSpacingPoints = Math.Clamp(state.PageLayout.ColumnSpacingPoints, 0, 1000);
        state.PageLayout.GutterPoints = Math.Clamp(state.PageLayout.GutterPoints, 0, 1000);
        state.PageLayout.PageNumberStart = Math.Max(1, state.PageLayout.PageNumberStart);
        state.Study.DailyTarget = Math.Clamp(state.Study.DailyTarget, 1, 10_000);
        state.Study.NewCardLimit = Math.Clamp(state.Study.NewCardLimit, 0, 10_000);
        state.Study.MaximumCardsPerSession = Math.Clamp(state.Study.MaximumCardsPerSession, 1, 10_000);
        foreach (var bookmark in state.CanvasBookmarks) bookmark.Zoom = Math.Clamp(bookmark.Zoom, 0.05, 100);
    }
}

/// <summary>
/// Represents notes find options and keeps its related state and behavior together.
/// </summary>
public sealed record NotesFindOptions(
    bool UseRegularExpression = false,
    bool MatchCase = false,
    bool WholeWord = false,
    Guid? SectionId = null,
    Guid? PageId = null,
    Guid? BlockId = null);

/// <summary>
/// Represents notes find match and keeps its related state and behavior together.
/// </summary>
public sealed record NotesFindMatch(
    Guid SectionId,
    Guid PageId,
    Guid BlockId,
    string BlockKind,
    int Start,
    int Length,
    string Value,
    string Context);

/// <summary>
/// Represents notes document search and keeps its related state and behavior together.
/// </summary>
public static class NotesDocumentSearch
{
    /// <summary>
    /// Performs the find step owned by this component.
    /// </summary>
    public static IReadOnlyList<NotesFindMatch> Find(
        NotesDocument document,
        string query,
        NotesFindOptions options,
        int maximumResults = 1_000)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrEmpty(query)) return [];
        maximumResults = Math.Clamp(maximumResults, 1, 100_000);
        var regex = CreateRegex(query, options);
        var results = new List<NotesFindMatch>();
        foreach (var section in document.Sections)
        {
            if (options.SectionId is { } sectionId && section.Id != sectionId) continue;
            foreach (var page in section.Pages)
            {
                if (options.PageId is { } pageId && page.Id != pageId) continue;
                foreach (var block in page.Blocks)
                {
                    if (options.BlockId is { } blockId && block.Id != blockId) continue;
                    var text = SearchableText(block);
                    foreach (Match match in regex.Matches(text))
                    {
                        results.Add(new NotesFindMatch(
                            section.Id,
                            page.Id,
                            block.Id,
                            block.Kind.ToString(),
                            match.Index,
                            match.Length,
                            match.Value,
                            Context(text, match.Index, match.Length)));
                        if (results.Count >= maximumResults) return results;
                    }
                }
            }
        }
        return results;
    }

    /// <summary>
    /// Performs the replace step owned by this component.
    /// </summary>
    public static NotesReplaceResult Replace(
        NotesDocument document,
        string query,
        string replacement,
        NotesFindOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrEmpty(query)) throw new ArgumentException("Find text is required.", nameof(query));
        var regex = CreateRegex(query, options);
        var blocksChanged = 0;
        var replacements = 0;
        foreach (var section in document.Sections)
        {
            if (options.SectionId is { } sectionId && section.Id != sectionId) continue;
            foreach (var page in section.Pages)
            {
                if (options.PageId is { } pageId && page.Id != pageId) continue;
                foreach (var block in page.Blocks)
                {
                    if (options.BlockId is { } blockId && block.Id != blockId) continue;
                    var changed = false;
                    replacements += ReplaceRuns(block, regex, replacement, ref changed);
                    if (block.List is not null)
                        foreach (var item in block.List.Items)
                            replacements += ReplaceValue(item.Text, value => item.Text = value, regex, replacement, ref changed);
                    if (block.Table is not null)
                        foreach (var cell in block.Table.Rows.SelectMany(row => row.Cells))
                            replacements += ReplaceValue(cell.Text, value => cell.Text = value, regex, replacement, ref changed);
                    if (block.Equation is not null)
                        replacements += ReplaceValue(block.Equation.Source, value => block.Equation.Source = value, regex, replacement, ref changed);
                    if (block.Html is not null)
                    {
                        replacements += ReplaceValue(block.Html.HtmlSource, value => block.Html.HtmlSource = value, regex, replacement, ref changed);
                        replacements += ReplaceValue(block.Html.CssSource, value => block.Html.CssSource = value, regex, replacement, ref changed);
                        replacements += ReplaceValue(block.Html.JavaScriptSource, value => block.Html.JavaScriptSource = value, regex, replacement, ref changed);
                        replacements += ReplaceValue(block.Html.FallbackText, value => block.Html.FallbackText = value, regex, replacement, ref changed);
                    }
                    if (block.Flashcard is not null)
                    {
                        replacements += ReplaceValue(block.Flashcard.Front, value => block.Flashcard.Front = value, regex, replacement, ref changed);
                        replacements += ReplaceValue(block.Flashcard.Back, value => block.Flashcard.Back = value, regex, replacement, ref changed);
                        replacements += ReplaceValue(block.Flashcard.Hint, value => block.Flashcard.Hint = value, regex, replacement, ref changed);
                    }
                    if (changed) blocksChanged++;
                }
            }
        }
        if (replacements > 0)
        {
            document.UpdatedAt = DateTimeOffset.UtcNow;
            document.Revisions.Add(new NotesRevision
            {
                Kind = NotesRevisionKind.Edited,
                Summary = $"Replaced {replacements} search match{(replacements == 1 ? string.Empty : "es")}.",
                Author = Environment.UserName
            });
        }
        return new NotesReplaceResult(replacements > 0 ? 1 : 0, blocksChanged, replacements);
    }

    /// <summary>
    /// Creates regex with the invariants required by its callers.
    /// </summary>
    private static Regex CreateRegex(string query, NotesFindOptions options)
    {
        var pattern = options.UseRegularExpression ? query : Regex.Escape(query);
        if (options.WholeWord) pattern = $@"(?<![\p{{L}}\p{{N}}_])(?:{pattern})(?![\p{{L}}\p{{N}}_])";
        var regexOptions = RegexOptions.CultureInvariant;
        if (!options.MatchCase) regexOptions |= RegexOptions.IgnoreCase;
        try { return new Regex(pattern, regexOptions, TimeSpan.FromSeconds(2)); }
        catch (ArgumentException ex) { throw new InvalidDataException("The search expression is invalid: " + ex.Message, ex); }
    }

    /// <summary>
    /// Performs the replace runs step owned by this component.
    /// </summary>
    private static int ReplaceRuns(NotesBlock block, Regex regex, string replacement, ref bool changed)
    {
        var total = 0;
        if (block.Runs.Count == 0)
        {
            total += ReplaceValue(block.PlainText, value => block.PlainText = value, regex, replacement, ref changed);
            return total;
        }
        foreach (var run in block.Runs)
            total += ReplaceValue(run.Text, value => run.Text = value, regex, replacement, ref changed);
        block.PlainText = string.Concat(block.Runs.Select(run => run.Text));
        return total;
    }

    /// <summary>
    /// Performs the replace value step owned by this component.
    /// </summary>
    private static int ReplaceValue(
        string value,
        Action<string> assign,
        Regex regex,
        string replacement,
        ref bool changed)
    {
        var count = regex.Matches(value).Count;
        if (count == 0) return 0;
        assign(regex.Replace(value, replacement ?? string.Empty));
        changed = true;
        return count;
    }

    /// <summary>
    /// Performs the searchable text step owned by this component.
    /// </summary>
    private static string SearchableText(NotesBlock block) => string.Join("\n", NotesProductivityText.Enumerate(block));

    /// <summary>
    /// Performs the context step owned by this component.
    /// </summary>
    private static string Context(string text, int start, int length)
    {
        var from = Math.Max(0, start - 48);
        var to = Math.Min(text.Length, start + length + 72);
        return (from > 0 ? "…" : string.Empty)
               + text[from..to].ReplaceLineEndings(" ")
               + (to < text.Length ? "…" : string.Empty);
    }
}

/// <summary>
/// Represents notes field evaluator and keeps its related state and behavior together.
/// </summary>
public static class NotesFieldEvaluator
{
    /// <summary>
    /// Performs the refresh step owned by this component.
    /// </summary>
    public static void Refresh(NotesDocument document, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);
        var statistics = NotesTextStatistics.Calculate(document);
        var pageCount = document.Sections.Sum(section => section.Pages.Count);
        foreach (var field in document.Fields)
        {
            if (!field.IsComputed) continue;
            field.Value = field.Name.Trim().ToLowerInvariant() switch
            {
                "title" or "document-title" => document.Title,
                "author" => document.Collaboration.OwnerId,
                "date" => now.ToString(string.IsNullOrWhiteSpace(field.Format) ? "d" : field.Format, CultureInfo.CurrentCulture),
                "time" => now.ToString(string.IsNullOrWhiteSpace(field.Format) ? "t" : field.Format, CultureInfo.CurrentCulture),
                "page-count" => pageCount.ToString(CultureInfo.CurrentCulture),
                "word-count" => statistics.Words.ToString(CultureInfo.CurrentCulture),
                "character-count" => statistics.Characters.ToString(CultureInfo.CurrentCulture),
                "file-name" => document.Metadata.TryGetValue("source-file-name", out var fileName) ? fileName : string.Empty,
                _ => field.Value
            };
        }
    }
}

/// <summary>
/// Lists the supported notes diff kind values used to make state explicit and type-safe.
/// </summary>
public enum NotesDiffKind { Unchanged = 0, Added = 1, Removed = 2 }
/// <summary>
/// Represents notes diff line and keeps its related state and behavior together.
/// </summary>
public sealed record NotesDiffLine(NotesDiffKind Kind, string Text, int? OldLine, int? NewLine);
/// <summary>
/// Represents notes version comparison and keeps its related state and behavior together.
/// </summary>
public sealed record NotesVersionComparison(
    long CurrentVersion,
    long ComparedVersion,
    IReadOnlyList<NotesDiffLine> Lines,
    int Added,
    int Removed);

/// <summary>
/// Represents notes version comparer and keeps its related state and behavior together.
/// </summary>
public static class NotesVersionComparer
{
    /// <summary>
    /// Performs the compare step owned by this component.
    /// </summary>
    public static NotesVersionComparison Compare(NotesDocument current, NotesDocument previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);
        var left = DocumentLines(previous).Take(4_000).ToArray();
        var right = DocumentLines(current).Take(4_000).ToArray();
        var lines = Diff(left, right);
        return new NotesVersionComparison(
            current.Version,
            previous.Version,
            lines,
            lines.Count(line => line.Kind == NotesDiffKind.Added),
            lines.Count(line => line.Kind == NotesDiffKind.Removed));
    }

    /// <summary>
    /// Performs the diff step owned by this component.
    /// </summary>
    private static IReadOnlyList<NotesDiffLine> Diff(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if ((long)left.Count * right.Count > 4_000_000)
            return FallbackDiff(left, right);
        var matrix = new int[left.Count + 1, right.Count + 1];
        for (var i = left.Count - 1; i >= 0; i--)
        for (var j = right.Count - 1; j >= 0; j--)
            matrix[i, j] = string.Equals(left[i], right[j], StringComparison.Ordinal)
                ? matrix[i + 1, j + 1] + 1
                : Math.Max(matrix[i + 1, j], matrix[i, j + 1]);
        var result = new List<NotesDiffLine>();
        var oldLine = 0;
        var newLine = 0;
        while (oldLine < left.Count || newLine < right.Count)
        {
            if (oldLine < left.Count && newLine < right.Count
                && string.Equals(left[oldLine], right[newLine], StringComparison.Ordinal))
            {
                result.Add(new NotesDiffLine(NotesDiffKind.Unchanged, left[oldLine], oldLine + 1, newLine + 1));
                oldLine++;
                newLine++;
            }
            else if (newLine < right.Count && (oldLine == left.Count || matrix[oldLine, newLine + 1] >= matrix[oldLine + 1, newLine]))
            {
                result.Add(new NotesDiffLine(NotesDiffKind.Added, right[newLine], null, newLine + 1));
                newLine++;
            }
            else
            {
                result.Add(new NotesDiffLine(NotesDiffKind.Removed, left[oldLine], oldLine + 1, null));
                oldLine++;
            }
        }
        return result;
    }

    /// <summary>
    /// Performs the fallback diff step owned by this component.
    /// </summary>
    private static IReadOnlyList<NotesDiffLine> FallbackDiff(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var result = new List<NotesDiffLine>();
        var count = Math.Max(left.Count, right.Count);
        for (var index = 0; index < count; index++)
        {
            var oldValue = index < left.Count ? left[index] : null;
            var newValue = index < right.Count ? right[index] : null;
            if (oldValue == newValue && oldValue is not null)
                result.Add(new NotesDiffLine(NotesDiffKind.Unchanged, oldValue, index + 1, index + 1));
            else
            {
                if (oldValue is not null) result.Add(new NotesDiffLine(NotesDiffKind.Removed, oldValue, index + 1, null));
                if (newValue is not null) result.Add(new NotesDiffLine(NotesDiffKind.Added, newValue, null, index + 1));
            }
        }
        return result;
    }

    /// <summary>
    /// Performs the document lines step owned by this component.
    /// </summary>
    private static IEnumerable<string> DocumentLines(NotesDocument document)
    {
        yield return "# " + document.Title;
        foreach (var section in document.Sections)
        {
            yield return "## " + section.Title;
            foreach (var page in section.Pages.OrderBy(page => page.Order))
            {
                yield return "### " + page.Title;
                foreach (var block in page.Blocks.OrderBy(block => block.Order))
                    foreach (var line in NotesProductivityText.Enumerate(block).SelectMany(value => value.ReplaceLineEndings("\n").Split('\n')))
                        yield return line;
            }
        }
    }
}

/// <summary>
/// Represents notes style set service and keeps its related state and behavior together.
/// </summary>
public static class NotesStyleSetService
{
    /// <summary>
    /// Stores json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// Performs the export step owned by this component.
    /// </summary>
    public static string Export(IReadOnlyCollection<NotesNamedStyle> styles) =>
        JsonSerializer.Serialize(styles, JsonOptions);

    /// <summary>
    /// Performs the import step owned by this component.
    /// </summary>
    public static IReadOnlyList<NotesNamedStyle> Import(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("The style set is empty.");
        var styles = JsonSerializer.Deserialize<List<NotesNamedStyle>>(json, JsonOptions)
                     ?? throw new InvalidDataException("The style set could not be read.");
        if (styles.Count is 0 or > 500) throw new InvalidDataException("A style set requires 1–500 styles.");
        if (styles.Any(style => string.IsNullOrWhiteSpace(style.Id) || string.IsNullOrWhiteSpace(style.Name)))
            throw new InvalidDataException("Every style requires an ID and name.");
        if (styles.Select(style => style.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != styles.Count)
            throw new InvalidDataException("Style IDs must be unique.");
        return styles;
    }
}

/// <summary>
/// Represents notes table operations and keeps its related state and behavior together.
/// </summary>
public static class NotesTableOperations
{
    /// <summary>
    /// Performs the sort step owned by this component.
    /// </summary>
    public static void Sort(NotesTableData table, int column, bool descending)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (table.Rows.Count <= 1) return;
        var maximumColumns = table.Rows.Max(row => row.Cells.Count);
        if (column < 0 || column >= maximumColumns) throw new ArgumentOutOfRangeException(nameof(column));
        var header = table.HeaderRow ? table.Rows[0] : null;
        var data = (header is null ? table.Rows : table.Rows.Skip(1))
            .OrderBy(row => column < row.Cells.Count ? row.Cells[column].Text : string.Empty, NaturalStringComparer.Instance)
            .ToList();
        if (descending) data.Reverse();
        table.Rows = header is null ? data : [header, .. data];
    }

    /// <summary>
    /// Performs the sum step owned by this component.
    /// </summary>
    public static decimal Sum(NotesTableData table, int column)
    {
        ArgumentNullException.ThrowIfNull(table);
        return table.Rows.Skip(table.HeaderRow ? 1 : 0)
            .Where(row => column >= 0 && column < row.Cells.Count)
            .Select(row => decimal.TryParse(row.Cells[column].Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) ? value : 0)
            .Sum();
    }

    /// <summary>
    /// Performs the to delimited text step owned by this component.
    /// </summary>
    public static string ToDelimitedText(NotesTableData table, char delimiter = '\t')
    {
        ArgumentNullException.ThrowIfNull(table);
        return string.Join(Environment.NewLine, table.Rows.Select(row => string.Join(delimiter, row.Cells.Select(cell => cell.Text))));
    }

    /// <summary>
    /// Performs the from delimited text step owned by this component.
    /// </summary>
    public static NotesTableData FromDelimitedText(string text, char delimiter = '\t')
    {
        var rows = text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (rows.Length == 0) return NotesTableData.Create(1, 1);
        var parsed = rows.Select(row => row.Split(delimiter)).ToArray();
        var columns = parsed.Max(row => row.Length);
        var table = NotesTableData.Create(parsed.Length, columns);
        for (var row = 0; row < parsed.Length; row++)
        for (var column = 0; column < parsed[row].Length; column++)
            table.Rows[row].Cells[column].Text = parsed[row][column];
        return table;
    }

    /// <summary>
    /// Represents natural string comparer and keeps its related state and behavior together.
    /// </summary>
    private sealed class NaturalStringComparer : IComparer<string>
    {
        /// <summary>
        /// Gets or updates instance, the bindable or domain state represented by this property.
        /// </summary>
        public static NaturalStringComparer Instance { get; } = new();
        /// <summary>
        /// Performs the compare step owned by this component.
        /// </summary>
        public int Compare(string? left, string? right)
        {
            if (decimal.TryParse(left, NumberStyles.Number, CultureInfo.CurrentCulture, out var leftNumber)
                && decimal.TryParse(right, NumberStyles.Number, CultureInfo.CurrentCulture, out var rightNumber))
                return leftNumber.CompareTo(rightNumber);
            return StringComparer.CurrentCultureIgnoreCase.Compare(left, right);
        }
    }
}

/// <summary>
/// Represents notes equation template and keeps its related state and behavior together.
/// </summary>
public sealed record NotesEquationTemplate(string Id, string Name, string Category, string Latex, string Description);
/// <summary>
/// Represents notes equation symbol and keeps its related state and behavior together.
/// </summary>
public sealed record NotesEquationSymbol(string Name, string Command, string Glyph, string Category);

/// <summary>
/// Represents notes equation tools and keeps its related state and behavior together.
/// </summary>
public static class NotesEquationTools
{
    /// <summary>
    /// Gets or updates templates, the bindable or domain state represented by this property.
    /// </summary>
    public static IReadOnlyList<NotesEquationTemplate> Templates { get; } =
    [
        new("fraction", "Fraction", "Algebra", @"\frac{numerator}{denominator}", "A numerator over a denominator."),
        new("root", "Square root", "Algebra", @"\sqrt{value}", "A square root."),
        new("indexed-root", "Indexed root", "Algebra", @"\sqrt[index]{value}", "A root with a custom index."),
        new("sum", "Summation", "Calculus", @"\sum_{i=1}^{n} expression", "A finite summation."),
        new("integral", "Integral", "Calculus", @"\int_{a}^{b} f(x)\,dx", "A definite integral."),
        new("limit", "Limit", "Calculus", @"\lim_{x \to a} f(x)", "A limit expression."),
        new("matrix", "Matrix", "Linear algebra", @"\begin{bmatrix} a & b \\ c & d \end{bmatrix}", "A two by two matrix."),
        new("cases", "Cases", "Functions", @"\begin{cases} expression_1 & condition_1 \\ expression_2 & condition_2 \end{cases}", "A piecewise function."),
        new("derivative", "Derivative", "Calculus", @"\frac{d}{dx} f(x)", "A derivative."),
        new("partial", "Partial derivative", "Calculus", @"\frac{\partial f}{\partial x}", "A partial derivative."),
        new("binomial", "Binomial coefficient", "Probability", @"\binom{n}{r}", "A binomial coefficient."),
        new("vector", "Vector", "Geometry", @"\vec{v}", "A vector accent.")
    ];

    /// <summary>
    /// Gets or updates symbols, the bindable or domain state represented by this property.
    /// </summary>
    public static IReadOnlyList<NotesEquationSymbol> Symbols { get; } =
    [
        new("alpha", @"\alpha", "α", "Greek"), new("beta", @"\beta", "β", "Greek"),
        new("gamma", @"\gamma", "γ", "Greek"), new("delta", @"\delta", "δ", "Greek"),
        new("pi", @"\pi", "π", "Greek"), new("theta", @"\theta", "θ", "Greek"),
        new("sum", @"\sum", "∑", "Calculus"), new("integral", @"\int", "∫", "Calculus"),
        new("partial", @"\partial", "∂", "Calculus"), new("infinity", @"\infty", "∞", "Operators"),
        new("less than or equal", @"\leq", "≤", "Relations"), new("greater than or equal", @"\geq", "≥", "Relations"),
        new("not equal", @"\neq", "≠", "Relations"), new("approximately", @"\approx", "≈", "Relations"),
        new("element of", @"\in", "∈", "Set theory"), new("subset", @"\subset", "⊂", "Set theory"),
        new("implies", @"\Rightarrow", "⇒", "Logic"), new("if and only if", @"\Leftrightarrow", "⇔", "Logic")
    ];

    /// <summary>
    /// Performs the search symbols step owned by this component.
    /// </summary>
    public static IReadOnlyList<NotesEquationSymbol> SearchSymbols(string query) =>
        Symbols.Where(symbol => string.IsNullOrWhiteSpace(query)
                                || symbol.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                                || symbol.Command.Contains(query, StringComparison.OrdinalIgnoreCase)
                                || symbol.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    /// <summary>
    /// Performs the expand intelligent input step owned by this component.
    /// </summary>
    public static string ExpandIntelligentInput(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sqrt"] = @"\sqrt{}",
            ["sum"] = @"\sum_{}^{}",
            ["int"] = @"\int_{}^{} \, d{}",
            ["alpha"] = @"\alpha",
            ["beta"] = @"\beta",
            ["gamma"] = @"\gamma",
            ["theta"] = @"\theta",
            ["pi"] = @"\pi"
        };
        var trimmed = value.Trim();
        return replacements.TryGetValue(trimmed, out var expansion) ? expansion : value;
    }

    /// <summary>
    /// Validates macros before it crosses the next trust or persistence boundary.
    /// </summary>
    public static IReadOnlyList<string> ValidateMacros(IReadOnlyDictionary<string, string> macros)
    {
        var errors = new List<string>();
        var commandPattern = new Regex(@"^\\[A-Za-z]+$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        foreach (var macro in macros)
        {
            if (!commandPattern.IsMatch(macro.Key)) errors.Add($"Macro '{macro.Key}' is not a valid command name.");
            if (string.IsNullOrWhiteSpace(macro.Value)) errors.Add($"Macro '{macro.Key}' has no replacement.");
            if (macro.Value.Length > 10_000) errors.Add($"Macro '{macro.Key}' exceeds the 10,000-character limit.");
        }
        return errors;
    }

    /// <summary>
    /// Performs the to math ml step owned by this component.
    /// </summary>
    public static string ToMathMl(NotesEquationData equation)
    {
        ArgumentNullException.ThrowIfNull(equation);
        var spoken = string.IsNullOrWhiteSpace(equation.AccessibleAlternative)
            ? equation.RenderedText
            : equation.AccessibleAlternative;
        return $"<math xmlns=\"http://www.w3.org/1998/Math/MathML\" aria-label=\"{Xml(spoken)}\"><semantics><mtext>{Xml(equation.RenderedText)}</mtext><annotation encoding=\"application/x-tex\">{Xml(equation.Source)}</annotation></semantics></math>";
    }

    /// <summary>
    /// Performs the to svg step owned by this component.
    /// </summary>
    public static string ToSvg(NotesEquationData equation)
    {
        ArgumentNullException.ThrowIfNull(equation);
        var text = string.IsNullOrWhiteSpace(equation.RenderedText) ? equation.Source : equation.RenderedText;
        var width = Math.Clamp(text.Length * 10 + 32, 160, 4000);
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"64\" role=\"img\" aria-label=\"{Xml(equation.AccessibleAlternative)}\"><rect width=\"100%\" height=\"100%\" fill=\"white\"/><text x=\"16\" y=\"40\" font-family=\"serif\" font-size=\"24\" fill=\"black\">{Xml(text)}</text></svg>";
    }

    /// <summary>
    /// Performs the xml step owned by this component.
    /// </summary>
    private static string Xml(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}

/// <summary>
/// Represents notes canvas operations and keeps its related state and behavior together.
/// </summary>
public static class NotesCanvasOperations
{
    /// <summary>
    /// Performs the move step owned by this component.
    /// </summary>
    public static void Move(NotesCanvasObject value, double x, double y, double gridSize = 0)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Locked) return;
        value.X = Snap(x, gridSize);
        value.Y = Snap(y, gridSize);
    }

    /// <summary>
    /// Performs the resize step owned by this component.
    /// </summary>
    public static void Resize(NotesCanvasObject value, double width, double height, double gridSize = 0)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Locked) return;
        value.Width = Math.Max(8, Snap(width, gridSize));
        value.Height = Math.Max(8, Snap(height, gridSize));
    }

    /// <summary>
    /// Performs the rotate step owned by this component.
    /// </summary>
    public static void Rotate(NotesCanvasObject value, double degrees)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.Locked) value.Rotation = NormalizeDegrees(degrees);
    }

    /// <summary>
    /// Performs the group step owned by this component.
    /// </summary>
    public static Guid Group(IEnumerable<NotesCanvasObject> values)
    {
        var group = Guid.NewGuid();
        foreach (var value in values.Where(value => !value.Locked)) value.GroupId = group;
        return group;
    }

    /// <summary>
    /// Performs the ungroup step owned by this component.
    /// </summary>
    public static void Ungroup(IEnumerable<NotesCanvasObject> values)
    {
        foreach (var value in values.Where(value => !value.Locked)) value.GroupId = null;
    }

    /// <summary>
    /// Performs the connect step owned by this component.
    /// </summary>
    public static NotesCanvasObject Connect(NotesCanvasObject from, NotesCanvasObject to, string label = "")
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (from.Id == to.Id) throw new InvalidOperationException("A canvas object cannot connect to itself.");
        return new NotesCanvasObject
        {
            Kind = NotesCanvasObjectKind.Connector,
            FromObjectId = from.Id,
            ToObjectId = to.Id,
            Text = label,
            X = from.X + from.Width / 2,
            Y = from.Y + from.Height / 2,
            Width = to.X + to.Width / 2 - (from.X + from.Width / 2),
            Height = to.Y + to.Height / 2 - (from.Y + from.Height / 2)
        };
    }

    /// <summary>
    /// Performs the transform stroke step owned by this component.
    /// </summary>
    public static void TransformStroke(
        NotesInkStroke stroke,
        double translateX,
        double translateY,
        double scale,
        double rotationDegrees)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        scale = Math.Clamp(scale, 0.01, 100);
        var radians = rotationDegrees * Math.PI / 180;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        foreach (var point in stroke.Points)
        {
            var x = point.X * scale;
            var y = point.Y * scale;
            point.X = x * cosine - y * sine + translateX;
            point.Y = x * sine + y * cosine + translateY;
        }
        stroke.BaseWidth = Math.Clamp(stroke.BaseWidth * scale, 0.1, 500);
    }

    /// <summary>
    /// Performs the bounds step owned by this component.
    /// </summary>
    public static RectD Bounds(IEnumerable<NotesCanvasObject> objects)
    {
        var values = objects.ToArray();
        if (values.Length == 0) return new RectD(0, 0, 0, 0);
        var left = values.Min(value => value.X);
        var top = values.Min(value => value.Y);
        var right = values.Max(value => value.X + value.Width);
        var bottom = values.Max(value => value.Y + value.Height);
        return new RectD(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Performs the snap step owned by this component.
    /// </summary>
    private static double Snap(double value, double gridSize) => gridSize > 0 ? Math.Round(value / gridSize) * gridSize : value;
    /// <summary>
    /// Performs the normalize degrees step owned by this component.
    /// </summary>
    private static double NormalizeDegrees(double value) => ((value % 360) + 360) % 360;
}

/// <summary>
/// Represents rect d and keeps its related state and behavior together.
/// </summary>
public sealed record RectD(double X, double Y, double Width, double Height);

/// <summary>
/// Represents notes study tools and keeps its related state and behavior together.
/// </summary>
public static class NotesStudyTools
{
    /// <summary>
    /// Performs the begin attempt step owned by this component.
    /// </summary>
    public static NotesStudyAttempt BeginAttempt(
        NotesAdvancedDocumentState state,
        NotesFlashcardData card,
        Guid? sourceBlockId,
        double confidence)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(card);
        var attempt = new NotesStudyAttempt
        {
            CardId = card.CardId,
            SourceBlockId = sourceBlockId,
            Confidence = Math.Clamp(confidence, 0, 1)
        };
        state.StudyAttempts.Add(attempt);
        return attempt;
    }

    /// <summary>
    /// Performs the complete attempt step owned by this component.
    /// </summary>
    public static void CompleteAttempt(
        NotesStudyAttempt attempt,
        string answer,
        string correctness,
        int hintsUsed,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        attempt.AttemptText = answer ?? string.Empty;
        attempt.Correctness = correctness is "Correct" or "Partly correct" or "Incorrect"
            ? correctness
            : "Unmarked";
        attempt.HintsUsed = Math.Max(0, hintsUsed);
        attempt.CompletedAt = completedAt;
        attempt.ResponseTime = completedAt >= attempt.StartedAt ? completedAt - attempt.StartedAt : TimeSpan.Zero;
    }

    /// <summary>
    /// Performs the explain due reason step owned by this component.
    /// </summary>
    public static string ExplainDueReason(NotesFlashcardData card, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (card.Schedule.Repetitions == 0) return "New card: it has not yet completed a successful review.";
        if (card.Schedule.DueAt <= now) return $"Due because its {card.Schedule.IntervalDays}-day interval has elapsed.";
        return $"Scheduled for {card.Schedule.DueAt.LocalDateTime:g} after a {card.Schedule.IntervalDays}-day interval at ease {card.Schedule.EaseFactor:0.00}.";
    }
}

/// <summary>
/// Represents notes collaboration tools and keeps its related state and behavior together.
/// </summary>
public static class NotesCollaborationTools
{
    /// <summary>
    /// Performs the resolve conflict step owned by this component.
    /// </summary>
    public static void ResolveConflict(NotesDocument document, NotesConflict conflict, string resolution)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(conflict);
        if (!document.Collaboration.Conflicts.Contains(conflict))
            throw new InvalidOperationException("The conflict does not belong to this document.");
        conflict.Resolution = resolution switch
        {
            "local" => conflict.LocalValue,
            "remote" => conflict.RemoteValue,
            _ => resolution
        };
        conflict.ResolvedAt = DateTimeOffset.UtcNow;
        document.Collaboration.ConflictState = document.Collaboration.Conflicts.Any(item => item.ResolvedAt is null)
            ? NotesConflictState.Diverged
            : NotesConflictState.Resolved;
        document.Revisions.Add(new NotesRevision
        {
            Kind = NotesRevisionKind.ConflictResolved,
            BlockId = conflict.BlockId,
            Summary = "Resolved collaboration conflict",
            Author = Environment.UserName
        });
    }
}
