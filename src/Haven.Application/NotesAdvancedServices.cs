using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Haven.Core;

namespace Haven.Application;

public sealed class NotesAdvancedDocumentState
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public NotesDocumentViewState View { get; set; } = new();
    public NotesExtendedPageLayout PageLayout { get; set; } = new();
    public Dictionary<Guid, NotesSectionHeaderFooterState> SectionHeaders { get; set; } = [];
    public List<NotesAutocorrectEntry> AutocorrectEntries { get; set; } = [];
    public List<NotesEquationLibraryEntry> EquationLibrary { get; set; } = [];
    public List<NotesCanvasBookmarkEntry> CanvasBookmarks { get; set; } = [];
    public List<NotesStudyAttempt> StudyAttempts { get; set; } = [];
    public List<NotesCrossReference> CrossReferences { get; set; } = [];
    public List<NotesTrackedChange> TrackedChanges { get; set; } = [];
    public NotesPrivacyState Privacy { get; set; } = new();
    public NotesStudyPreferences Study { get; set; } = new();
}

public sealed class NotesDocumentViewState
{
    public bool IsPinned { get; set; }
    public DateTimeOffset? LastOpenedAt { get; set; }
    public bool IsFocusMode { get; set; }
    public bool IsFullscreen { get; set; }
    public bool ShowLibrary { get; set; } = true;
    public bool ShowOutline { get; set; } = true;
    public bool ShowFormattingSidebar { get; set; } = true;
    public bool ShowStatusBar { get; set; } = true;
    public bool ShowFormattingMarks { get; set; }
    public double InterfaceScale { get; set; } = 1;
    public List<string> ToolbarItems { get; set; } =
    [
        "new", "save", "undo", "redo", "import", "export", "print"
    ];
}

public sealed class NotesExtendedPageLayout
{
    public bool MirrorMargins { get; set; }
    public double GutterPoints { get; set; }
    public int Columns { get; set; } = 1;
    public double ColumnSpacingPoints { get; set; } = 18;
    public string PageBorder { get; set; } = string.Empty;
    public string Watermark { get; set; } = string.Empty;
    public bool LineNumbering { get; set; }
    public bool Hyphenation { get; set; }
    public string VerticalAlignment { get; set; } = "Top";
    public bool DifferentFirstPage { get; set; }
    public bool DifferentOddEvenPages { get; set; }
    public string PageNumberFormat { get; set; } = "1, 2, 3";
    public int PageNumberStart { get; set; } = 1;
}

public sealed class NotesSectionHeaderFooterState
{
    public string FirstPageHeader { get; set; } = string.Empty;
    public string FirstPageFooter { get; set; } = string.Empty;
    public string OddPageHeader { get; set; } = string.Empty;
    public string OddPageFooter { get; set; } = string.Empty;
    public string EvenPageHeader { get; set; } = string.Empty;
    public string EvenPageFooter { get; set; } = string.Empty;
    public int? RestartPageNumberAt { get; set; }
}

public sealed class NotesAutocorrectEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Input { get; set; } = string.Empty;
    public string Replacement { get; set; } = string.Empty;
    public bool MatchCase { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public sealed class NotesEquationLibraryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string Latex { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public bool IsFavourite { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class NotesCanvasBookmarkEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PageId { get; set; }
    public string Name { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Zoom { get; set; } = 1;
}

public sealed class NotesStudyAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CardId { get; set; }
    public Guid? SourceBlockId { get; set; }
    public string AttemptText { get; set; } = string.Empty;
    public string Correctness { get; set; } = "Unmarked";
    public double Confidence { get; set; }
    public int HintsUsed { get; set; }
    public TimeSpan ResponseTime { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class NotesCrossReference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceBlockId { get; set; }
    public Guid TargetBlockId { get; set; }
    public string Kind { get; set; } = "Reference";
    public string Label { get; set; } = string.Empty;
    public bool IsBroken { get; set; }
}

public sealed class NotesTrackedChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BlockId { get; set; }
    public string Author { get; set; } = Environment.UserName;
    public string Kind { get; set; } = "Edit";
    public string Before { get; set; } = string.Empty;
    public string After { get; set; } = string.Empty;
    public string State { get; set; } = "Pending";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAt { get; set; }
}

public sealed class NotesPrivacyState
{
    public bool AiEnabled { get; set; } = true;
    public bool AllowExternalProviders { get; set; }
    public bool AllowDocumentContext { get; set; }
    public bool AllowWorkspaceContext { get; set; }
    public bool AllowWebResearch { get; set; }
    public bool StoreAiProvenance { get; set; } = true;
    public bool StoreResearchSources { get; set; } = true;
}

public sealed class NotesStudyPreferences
{
    public int DailyTarget { get; set; } = 20;
    public int NewCardLimit { get; set; } = 10;
    public int MaximumCardsPerSession { get; set; } = 50;
    public bool Shuffle { get; set; }
    public bool ReviewMistakesOnly { get; set; }
    public bool CramMode { get; set; }
    public DateTimeOffset? ExamDate { get; set; }
}

public static class NotesAdvancedStateStore
{
    public const string MetadataKey = "haven.notes.advanced.v1";
    private const int MaximumSerializedBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

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

public sealed record NotesFindOptions(
    bool UseRegularExpression = false,
    bool MatchCase = false,
    bool WholeWord = false,
    Guid? SectionId = null,
    Guid? PageId = null,
    Guid? BlockId = null);

public sealed record NotesFindMatch(
    Guid SectionId,
    Guid PageId,
    Guid BlockId,
    string BlockKind,
    int Start,
    int Length,
    string Value,
    string Context);

public static class NotesDocumentSearch
{
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

    private static Regex CreateRegex(string query, NotesFindOptions options)
    {
        var pattern = options.UseRegularExpression ? query : Regex.Escape(query);
        if (options.WholeWord) pattern = $@"(?<![\p{{L}}\p{{N}}_])(?:{pattern})(?![\p{{L}}\p{{N}}_])";
        var regexOptions = RegexOptions.CultureInvariant;
        if (!options.MatchCase) regexOptions |= RegexOptions.IgnoreCase;
        try { return new Regex(pattern, regexOptions, TimeSpan.FromSeconds(2)); }
        catch (ArgumentException ex) { throw new InvalidDataException("The search expression is invalid: " + ex.Message, ex); }
    }

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

    private static string SearchableText(NotesBlock block) => string.Join("\n", NotesProductivityText.Enumerate(block));

    private static string Context(string text, int start, int length)
    {
        var from = Math.Max(0, start - 48);
        var to = Math.Min(text.Length, start + length + 72);
        return (from > 0 ? "…" : string.Empty)
               + text[from..to].ReplaceLineEndings(" ")
               + (to < text.Length ? "…" : string.Empty);
    }
}

public static class NotesFieldEvaluator
{
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

public enum NotesDiffKind { Unchanged = 0, Added = 1, Removed = 2 }
public sealed record NotesDiffLine(NotesDiffKind Kind, string Text, int? OldLine, int? NewLine);
public sealed record NotesVersionComparison(
    long CurrentVersion,
    long ComparedVersion,
    IReadOnlyList<NotesDiffLine> Lines,
    int Added,
    int Removed);

public static class NotesVersionComparer
{
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

public static class NotesStyleSetService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string Export(IReadOnlyCollection<NotesNamedStyle> styles) =>
        JsonSerializer.Serialize(styles, JsonOptions);

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

public static class NotesTableOperations
{
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

    public static decimal Sum(NotesTableData table, int column)
    {
        ArgumentNullException.ThrowIfNull(table);
        return table.Rows.Skip(table.HeaderRow ? 1 : 0)
            .Where(row => column >= 0 && column < row.Cells.Count)
            .Select(row => decimal.TryParse(row.Cells[column].Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) ? value : 0)
            .Sum();
    }

    public static string ToDelimitedText(NotesTableData table, char delimiter = '\t')
    {
        ArgumentNullException.ThrowIfNull(table);
        return string.Join(Environment.NewLine, table.Rows.Select(row => string.Join(delimiter, row.Cells.Select(cell => cell.Text))));
    }

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

    private sealed class NaturalStringComparer : IComparer<string>
    {
        public static NaturalStringComparer Instance { get; } = new();
        public int Compare(string? left, string? right)
        {
            if (decimal.TryParse(left, NumberStyles.Number, CultureInfo.CurrentCulture, out var leftNumber)
                && decimal.TryParse(right, NumberStyles.Number, CultureInfo.CurrentCulture, out var rightNumber))
                return leftNumber.CompareTo(rightNumber);
            return StringComparer.CurrentCultureIgnoreCase.Compare(left, right);
        }
    }
}

public sealed record NotesEquationTemplate(string Id, string Name, string Category, string Latex, string Description);
public sealed record NotesEquationSymbol(string Name, string Command, string Glyph, string Category);

public static class NotesEquationTools
{
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

    public static IReadOnlyList<NotesEquationSymbol> SearchSymbols(string query) =>
        Symbols.Where(symbol => string.IsNullOrWhiteSpace(query)
                                || symbol.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                                || symbol.Command.Contains(query, StringComparison.OrdinalIgnoreCase)
                                || symbol.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();

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

    public static string ToMathMl(NotesEquationData equation)
    {
        ArgumentNullException.ThrowIfNull(equation);
        var spoken = string.IsNullOrWhiteSpace(equation.AccessibleAlternative)
            ? equation.RenderedText
            : equation.AccessibleAlternative;
        return $"<math xmlns=\"http://www.w3.org/1998/Math/MathML\" aria-label=\"{Xml(spoken)}\"><semantics><mtext>{Xml(equation.RenderedText)}</mtext><annotation encoding=\"application/x-tex\">{Xml(equation.Source)}</annotation></semantics></math>";
    }

    public static string ToSvg(NotesEquationData equation)
    {
        ArgumentNullException.ThrowIfNull(equation);
        var text = string.IsNullOrWhiteSpace(equation.RenderedText) ? equation.Source : equation.RenderedText;
        var width = Math.Clamp(text.Length * 10 + 32, 160, 4000);
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"64\" role=\"img\" aria-label=\"{Xml(equation.AccessibleAlternative)}\"><rect width=\"100%\" height=\"100%\" fill=\"white\"/><text x=\"16\" y=\"40\" font-family=\"serif\" font-size=\"24\" fill=\"black\">{Xml(text)}</text></svg>";
    }

    private static string Xml(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}

public static class NotesCanvasOperations
{
    public static void Move(NotesCanvasObject value, double x, double y, double gridSize = 0)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Locked) return;
        value.X = Snap(x, gridSize);
        value.Y = Snap(y, gridSize);
    }

    public static void Resize(NotesCanvasObject value, double width, double height, double gridSize = 0)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Locked) return;
        value.Width = Math.Max(8, Snap(width, gridSize));
        value.Height = Math.Max(8, Snap(height, gridSize));
    }

    public static void Rotate(NotesCanvasObject value, double degrees)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.Locked) value.Rotation = NormalizeDegrees(degrees);
    }

    public static Guid Group(IEnumerable<NotesCanvasObject> values)
    {
        var group = Guid.NewGuid();
        foreach (var value in values.Where(value => !value.Locked)) value.GroupId = group;
        return group;
    }

    public static void Ungroup(IEnumerable<NotesCanvasObject> values)
    {
        foreach (var value in values.Where(value => !value.Locked)) value.GroupId = null;
    }

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

    private static double Snap(double value, double gridSize) => gridSize > 0 ? Math.Round(value / gridSize) * gridSize : value;
    private static double NormalizeDegrees(double value) => ((value % 360) + 360) % 360;
}

public sealed record RectD(double X, double Y, double Width, double Height);

public static class NotesStudyTools
{
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

    public static string ExplainDueReason(NotesFlashcardData card, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (card.Schedule.Repetitions == 0) return "New card: it has not yet completed a successful review.";
        if (card.Schedule.DueAt <= now) return $"Due because its {card.Schedule.IntervalDays}-day interval has elapsed.";
        return $"Scheduled for {card.Schedule.DueAt.LocalDateTime:g} after a {card.Schedule.IntervalDays}-day interval at ease {card.Schedule.EaseFactor:0.00}.";
    }
}

public static class NotesCollaborationTools
{
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
