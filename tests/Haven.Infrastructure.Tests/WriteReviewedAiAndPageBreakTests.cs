using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

public sealed class WriteReviewedAiAndPageBreakTests
{
    [Fact]
    public void Insert_page_break_is_structural_and_round_trips_through_history()
    {
        var document = NotesDocument.Create("Page break");
        var editor = new WriteDocumentEditor(document);
        var original = document.Sections[0].Pages[0].Blocks[0];
        editor.SelectBlock(original.Id);

        var pageBreak = editor.InsertPageBreak();

        var blocks = document.Sections[0].Pages[0].Blocks;
        Assert.Equal(2, blocks.Count);
        Assert.Equal(pageBreak.Id, blocks[1].Id);
        Assert.True(blocks[1].Paragraph.PageBreakBefore);

        Assert.True(editor.Undo());
        Assert.Single(editor.Document.Sections[0].Pages[0].Blocks);

        Assert.True(editor.Redo());
        blocks = editor.Document.Sections[0].Pages[0].Blocks;
        Assert.Equal(2, blocks.Count);
        Assert.True(blocks[1].Paragraph.PageBreakBefore);
    }

    [Fact]
    public void Applying_reviewed_ai_change_preserves_unaffected_rich_text_runs()
    {
        var document = NotesDocument.Create("AI review");
        var paragraph = document.Sections[0].Pages[0].Blocks[0];
        paragraph.PlainText = "Hello world";
        paragraph.Runs =
        [
            new NotesTextRun { Text = "Hello ", Bold = true },
            new NotesTextRun { Text = "world", Italic = true }
        ];
        var change = new NotesAiChange
        {
            BlockId = paragraph.Id,
            Instruction = "Make the greeting brighter",
            OriginalContent = "Hello world",
            ProposedContent = "Hello brighter world",
            Explanation = "Adds one descriptive word.",
            Status = NotesAiChangeStatus.Proposed
        };
        document.AiChanges.Add(change);
        var editor = new WriteDocumentEditor(document);
        editor.SelectBlock(paragraph.Id);

        Assert.True(editor.ApplyAiChange(change));

        paragraph = editor.SelectedBlock!;
        Assert.Equal("Hello brighter world", string.Concat(paragraph.Runs.Select(run => run.Text)));
        Assert.True(paragraph.Runs[0].Bold);
        Assert.True(paragraph.Runs[^1].Italic);
        Assert.Equal(NotesAiChangeStatus.Applied, change.Status);
        Assert.NotNull(change.ReviewedAt);
        Assert.True(change.UserConsentRecorded);
        Assert.Contains(document.Revisions, revision => revision.Kind == NotesRevisionKind.AiApplied && revision.BlockId == paragraph.Id);

        Assert.True(editor.Undo());
        paragraph = editor.SelectedBlock!;
        Assert.Equal("Hello world", string.Concat(paragraph.Runs.Select(run => run.Text)));

        Assert.True(editor.Redo());
        paragraph = editor.SelectedBlock!;
        Assert.Equal("Hello brighter world", string.Concat(paragraph.Runs.Select(run => run.Text)));
    }

    [Fact]
    public void Rejecting_ai_change_leaves_document_content_unchanged()
    {
        var document = NotesDocument.Create("AI reject");
        var paragraph = document.Sections[0].Pages[0].Blocks[0];
        paragraph.PlainText = "Keep this";
        paragraph.Runs = [new NotesTextRun { Text = "Keep this", Underline = true }];
        var change = new NotesAiChange
        {
            BlockId = paragraph.Id,
            Instruction = "Replace it",
            OriginalContent = "Keep this",
            ProposedContent = "Replace this",
            Status = NotesAiChangeStatus.Proposed
        };
        document.AiChanges.Add(change);
        var editor = new WriteDocumentEditor(document);

        Assert.True(editor.RejectAiChange(change));

        Assert.Equal("Keep this", string.Concat(editor.SelectedBlock!.Runs.Select(run => run.Text)));
        Assert.True(editor.SelectedBlock.Runs[0].Underline);
        Assert.Equal(NotesAiChangeStatus.Rejected, change.Status);
        Assert.NotNull(change.ReviewedAt);
    }
}
