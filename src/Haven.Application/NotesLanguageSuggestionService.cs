using Haven.Core;

namespace Haven.Application;

public static class NotesLanguageSuggestionService
{
    public static bool Apply(
        NotesDocument document,
        NotesLanguageIssue issue,
        int suggestionIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(issue);
        if (suggestionIndex < 0 || suggestionIndex >= issue.Suggestions.Count) return false;
        var block = document.Sections
            .SelectMany(section => section.Pages)
            .SelectMany(page => page.Blocks)
            .FirstOrDefault(candidate => candidate.Id == issue.BlockId);
        if (block is null) return false;
        var replacement = issue.Suggestions[suggestionIndex];
        if (issue.Start < 0 || issue.Length < 0) return false;

        if (block.Runs.Count == 0)
        {
            if (issue.Start + issue.Length > block.PlainText.Length) return false;
            block.PlainText = block.PlainText.Remove(issue.Start, issue.Length).Insert(issue.Start, replacement);
        }
        else
        {
            var totalLength = block.Runs.Sum(run => run.Text.Length);
            if (issue.Start + issue.Length > totalLength) return false;
            ReplaceAcrossRuns(block.Runs, issue.Start, issue.Length, replacement);
            block.PlainText = string.Concat(block.Runs.Select(run => run.Text));
        }

        document.UpdatedAt = DateTimeOffset.UtcNow;
        document.Revisions.Add(new NotesRevision
        {
            Kind = NotesRevisionKind.Edited,
            BlockId = block.Id,
            Summary = "Applied language suggestion: " + issue.Kind,
            Author = Environment.UserName
        });
        return true;
    }

    private static void ReplaceAcrossRuns(
        IReadOnlyList<NotesTextRun> runs,
        int start,
        int length,
        string replacement)
    {
        var end = start + length;
        var cursor = 0;
        var inserted = false;
        foreach (var run in runs)
        {
            var runStart = cursor;
            var runEnd = cursor + run.Text.Length;
            cursor = runEnd;
            if (runEnd <= start || runStart >= end) continue;

            var localStart = Math.Max(0, start - runStart);
            var localEnd = Math.Min(run.Text.Length, end - runStart);
            var prefix = run.Text[..localStart];
            var suffix = run.Text[localEnd..];
            run.Text = prefix + (inserted ? string.Empty : replacement) + suffix;
            inserted = true;
        }
    }
}
