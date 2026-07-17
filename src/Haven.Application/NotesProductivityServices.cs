using System.Globalization;
using System.Text.RegularExpressions;
using Haven.Core;

namespace Haven.Application;

public sealed record NotesTemplateDescriptor(
    string Id,
    string Name,
    string Description,
    string Category);

public static class NotesTemplateCatalog
{
    public static IReadOnlyList<NotesTemplateDescriptor> Templates { get; } =
    [
        new("blank", "Blank document", "A clean document with one page and paragraph.", "General"),
        new("cornell", "Cornell notes", "Cue column, detailed notes and a summary section.", "Study"),
        new("revision", "Revision guide", "Topics, key facts, worked examples and flashcards.", "Study"),
        new("report", "Structured report", "Executive summary, findings, evidence and recommendations.", "Writing"),
        new("lab", "Lab report", "Aim, hypothesis, method, results table, analysis and conclusion.", "Science"),
        new("meeting", "Meeting notes", "Agenda, attendees, decisions and follow-up actions.", "Planning")
    ];

    public static NotesDocument Create(string templateId, string? title = null)
    {
        var normalized = templateId?.Trim().ToLowerInvariant() ?? string.Empty;
        var descriptor = Templates.FirstOrDefault(item => item.Id == normalized) ?? Templates[0];
        var document = NotesDocument.Create(string.IsNullOrWhiteSpace(title) ? descriptor.Name : title.Trim());
        var page = document.Sections[0].Pages[0];
        page.Blocks.Clear();

        switch (normalized)
        {
            case "cornell":
                page.Blocks.Add(Heading("Cornell notes", 0));
                page.Blocks.Add(Table(1, 2, 1, ["Cues / questions", "Detailed notes"]));
                page.Blocks.Add(Heading("Summary", 2, 2));
                page.Blocks.Add(Paragraph("Summarise the most important ideas in your own words.", 3));
                break;
            case "revision":
                page.Blocks.Add(Heading("Revision guide", 0));
                page.Blocks.Add(Heading("Key ideas", 1, 2));
                page.Blocks.Add(List(2, "Add the essential facts", "Connect each fact to an example"));
                page.Blocks.Add(Heading("Worked example", 3, 2));
                page.Blocks.Add(Paragraph("Show each step and explain why it is valid.", 4));
                page.Blocks.Add(WithOrder(NotesBlock.FlashcardBlock(), 5));
                break;
            case "report":
                AddSections(page,
                    ("Structured report", NotesBlockKind.Heading),
                    ("Executive summary", NotesBlockKind.Heading),
                    ("State the purpose, approach and main conclusion.", NotesBlockKind.Paragraph),
                    ("Findings", NotesBlockKind.Heading),
                    ("Present each finding with its supporting evidence.", NotesBlockKind.Paragraph),
                    ("Recommendations", NotesBlockKind.Heading),
                    ("List practical, evidence-based recommendations.", NotesBlockKind.Paragraph));
                break;
            case "lab":
                AddSections(page,
                    ("Lab report", NotesBlockKind.Heading),
                    ("Aim and hypothesis", NotesBlockKind.Heading),
                    ("State the investigation and predicted outcome.", NotesBlockKind.Paragraph),
                    ("Method", NotesBlockKind.Heading),
                    ("Record a reproducible method and safety controls.", NotesBlockKind.Paragraph));
                page.Blocks.Add(Table(2, 4, page.Blocks.Count, ["Trial", "Independent variable", "Dependent variable", "Notes"]));
                AddSections(page,
                    ("Analysis", NotesBlockKind.Heading),
                    ("Interpret patterns, uncertainty and anomalies.", NotesBlockKind.Paragraph),
                    ("Conclusion", NotesBlockKind.Heading),
                    ("Answer the aim using the collected evidence.", NotesBlockKind.Paragraph));
                break;
            case "meeting":
                AddSections(page,
                    ("Meeting notes", NotesBlockKind.Heading),
                    ("Agenda", NotesBlockKind.Heading));
                page.Blocks.Add(List(page.Blocks.Count, "Agenda item"));
                AddSections(page, ("Decisions", NotesBlockKind.Heading));
                page.Blocks.Add(List(page.Blocks.Count, "Decision and owner"));
                AddSections(page, ("Actions", NotesBlockKind.Heading));
                page.Blocks.Add(new NotesBlock
                {
                    Kind = NotesBlockKind.List,
                    Order = page.Blocks.Count,
                    List = new NotesListData
                    {
                        Kind = NotesListKind.Checklist,
                        Items = [new NotesListItem { Text = "Action · owner · due date" }]
                    }
                });
                break;
            default:
                page.Blocks.Add(Paragraph(string.Empty, 0));
                break;
        }

        Normalize(page);
        document.Revisions.Add(new NotesRevision
        {
            Kind = NotesRevisionKind.Created,
            Summary = "Created from template " + descriptor.Name,
            Author = Environment.UserName
        });
        return document;
    }

    private static NotesBlock Heading(string text, int order, int level = 1)
    {
        var block = NotesBlock.Heading(text);
        block.Order = order;
        block.StyleId = "heading-" + Math.Clamp(level, 1, 6);
        block.Runs =
        [
            new NotesTextRun
            {
                Text = text,
                FontSize = level == 1 ? 28 : 20,
                Bold = true
            }
        ];
        return block;
    }

    private static NotesBlock Paragraph(string text, int order)
    {
        var block = NotesBlock.CreateParagraph(text);
        block.Order = order;
        block.Runs = [new NotesTextRun { Text = text }];
        return block;
    }

    private static NotesBlock List(int order, params string[] values) => new()
    {
        Kind = NotesBlockKind.List,
        Order = order,
        List = new NotesListData
        {
            Kind = NotesListKind.Bulleted,
            Items = values.Select(value => new NotesListItem { Text = value }).ToList()
        }
    };

    private static NotesBlock Table(int rows, int columns, int order, IReadOnlyList<string> headers)
    {
        var table = NotesTableData.Create(rows, columns);
        table.HeaderRow = true;
        for (var column = 0; column < Math.Min(columns, headers.Count); column++)
            table.Rows[0].Cells[column].Text = headers[column];
        return new NotesBlock { Kind = NotesBlockKind.Table, Order = order, Table = table };
    }

    private static void AddSections(NotesPage page, params (string Text, NotesBlockKind Kind)[] items)
    {
        foreach (var item in items)
        {
            page.Blocks.Add(item.Kind == NotesBlockKind.Heading
                ? Heading(item.Text, page.Blocks.Count, page.Blocks.Count == 0 ? 1 : 2)
                : Paragraph(item.Text, page.Blocks.Count));
        }
    }

    private static void Normalize(NotesPage page)
    {
        for (var index = 0; index < page.Blocks.Count; index++) page.Blocks[index].Order = index;
    }

    private static NotesBlock WithOrder(NotesBlock block, int order)
    {
        block.Order = order;
        return block;
    }
}

public sealed record NotesReplaceResult(
    int DocumentsChanged,
    int BlocksChanged,
    int Replacements);

public static class NotesFindReplace
{
    public static NotesReplaceResult Replace(
        NotesDocument document,
        string find,
        string replacement,
        bool matchCase,
        bool wholeWord)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(find)) throw new ArgumentException("Find text is required.", nameof(find));

        var options = matchCase
            ? RegexOptions.CultureInvariant
            : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        var pattern = wholeWord
            ? $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(find)}(?![\p{{L}}\p{{N}}_])"
            : Regex.Escape(find);
        var regex = new Regex(pattern, options, TimeSpan.FromSeconds(2));
        var blocksChanged = 0;
        var replacements = 0;

        foreach (var block in document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks))
        {
            var changed = false;
            foreach (var run in block.Runs)
            {
                var count = regex.Matches(run.Text).Count;
                if (count == 0) continue;
                run.Text = regex.Replace(run.Text, replacement ?? string.Empty);
                replacements += count;
                changed = true;
            }

            if (block.Runs.Count > 0)
            {
                block.PlainText = string.Concat(block.Runs.Select(run => run.Text));
            }
            else
            {
                var count = regex.Matches(block.PlainText).Count;
                if (count > 0)
                {
                    block.PlainText = regex.Replace(block.PlainText, replacement ?? string.Empty);
                    replacements += count;
                    changed = true;
                }
            }

            if (block.List is not null)
            {
                foreach (var item in block.List.Items)
                    replacements += ReplaceString(regex, item.Text, replacement, value => item.Text = value, ref changed);
            }

            if (block.Table is not null)
            {
                foreach (var cell in block.Table.Rows.SelectMany(row => row.Cells))
                    replacements += ReplaceString(regex, cell.Text, replacement, value => cell.Text = value, ref changed);
            }

            if (block.Equation is not null)
                replacements += ReplaceString(regex, block.Equation.Source, replacement, value => block.Equation.Source = value, ref changed);

            if (block.Html is not null)
                replacements += ReplaceString(regex, block.Html.FallbackText, replacement, value => block.Html.FallbackText = value, ref changed);

            if (block.Flashcard is not null)
            {
                replacements += ReplaceString(regex, block.Flashcard.Front, replacement, value => block.Flashcard.Front = value, ref changed);
                replacements += ReplaceString(regex, block.Flashcard.Back, replacement, value => block.Flashcard.Back = value, ref changed);
                replacements += ReplaceString(regex, block.Flashcard.Hint, replacement, value => block.Flashcard.Hint = value, ref changed);
            }

            if (changed) blocksChanged++;
        }

        if (replacements > 0)
        {
            document.UpdatedAt = DateTimeOffset.UtcNow;
            document.Revisions.Add(new NotesRevision
            {
                Kind = NotesRevisionKind.Edited,
                Summary = $"Replaced {replacements} occurrence{(replacements == 1 ? string.Empty : "s")} of “{find}”.",
                Author = Environment.UserName
            });
        }

        return new NotesReplaceResult(replacements > 0 ? 1 : 0, blocksChanged, replacements);
    }

    private static int ReplaceString(
        Regex regex,
        string value,
        string replacement,
        Action<string> assign,
        ref bool changed)
    {
        var count = regex.Matches(value).Count;
        if (count == 0) return 0;
        assign(regex.Replace(value, replacement ?? string.Empty));
        changed = true;
        return count;
    }
}

public sealed record NotesLanguageIssue(
    Guid BlockId,
    int Start,
    int Length,
    string Kind,
    string Message,
    IReadOnlyList<string> Suggestions);

public static class NotesLanguageChecks
{
    private static readonly Regex RepeatedWord = new(
        @"\b(?<word>[\p{L}\p{N}']+)\s+\k<word>\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));
    private static readonly Regex SpacingBeforePunctuation = new(
        @"\s+([,.;:!?])",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));
    private static readonly Regex SentenceStart = new(
        @"(?:^|[.!?]\s+)(?<letter>[a-z])",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    public static IReadOnlyList<NotesLanguageIssue> Check(NotesDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var issues = new List<NotesLanguageIssue>();

        foreach (var block in document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks))
        {
            var text = NotesProductivityText.Enumerate(block).FirstOrDefault() ?? block.PlainText;
            foreach (Match match in RepeatedWord.Matches(text))
            {
                issues.Add(new NotesLanguageIssue(
                    block.Id,
                    match.Index,
                    match.Length,
                    "Repeated word",
                    "The same word appears twice in succession.",
                    [match.Groups["word"].Value]));
            }

            foreach (Match match in SpacingBeforePunctuation.Matches(text))
            {
                issues.Add(new NotesLanguageIssue(
                    block.Id,
                    match.Index,
                    match.Length,
                    "Punctuation spacing",
                    "Remove the space before punctuation.",
                    [match.Groups[1].Value]));
            }

            foreach (Match match in SentenceStart.Matches(text))
            {
                var letter = match.Groups["letter"];
                issues.Add(new NotesLanguageIssue(
                    block.Id,
                    letter.Index,
                    letter.Length,
                    "Sentence capitalisation",
                    "Sentences normally begin with a capital letter.",
                    [letter.Value.ToUpper(CultureInfo.CurrentCulture)]));
            }
        }

        return issues.Take(1_000).ToArray();
    }
}

internal static class NotesProductivityText
{
    public static IEnumerable<string> Enumerate(NotesBlock block)
    {
        if (!string.IsNullOrWhiteSpace(block.PlainText)) yield return block.PlainText;
        if (block.Runs.Count > 0) yield return string.Concat(block.Runs.Select(run => run.Text));
        if (block.List is not null)
            foreach (var item in block.List.Items) yield return item.Text;
        if (block.Table is not null)
            foreach (var cell in block.Table.Rows.SelectMany(row => row.Cells)) yield return cell.Text;
        if (block.Equation is not null) yield return block.Equation.Source;
        if (block.Html is not null) yield return block.Html.FallbackText;
        if (block.Flashcard is not null)
        {
            yield return block.Flashcard.Front;
            yield return block.Flashcard.Back;
            yield return block.Flashcard.Hint;
        }
    }
}
