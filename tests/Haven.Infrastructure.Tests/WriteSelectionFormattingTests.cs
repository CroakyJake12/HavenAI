using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

public sealed class WriteSelectionFormattingTests
{
    [Fact]
    public void Selection_formatting_only_changes_highlighted_text()
    {
        var document = NotesDocument.Create();
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.PlainText = "Alpha beta gamma";
        block.Runs = [new NotesTextRun { Text = block.PlainText }];
        var editor = new WriteDocumentEditor(document);

        editor.SelectBlock(block.Id, 10, 6, 10);
        editor.ToggleSelectionCharacter(WriteCharacterFormat.Bold);

        Assert.Equal(3, block.Runs.Count);
        Assert.Equal("Alpha ", block.Runs[0].Text);
        Assert.False(block.Runs[0].Bold);
        Assert.Equal("beta", block.Runs[1].Text);
        Assert.True(block.Runs[1].Bold);
        Assert.Equal(" gamma", block.Runs[2].Text);
        Assert.False(block.Runs[2].Bold);
    }

    [Fact]
    public void Selection_formatting_spans_existing_runs_without_touching_outer_text()
    {
        var document = NotesDocument.Create();
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.PlainText = "One Two Three";
        block.Runs =
        [
            new NotesTextRun { Text = "One ", Italic = true },
            new NotesTextRun { Text = "Two ", Bold = true },
            new NotesTextRun { Text = "Three", Underline = true }
        ];
        var editor = new WriteDocumentEditor(document);

        editor.SelectBlock(block.Id, 9, 2, 9);
        editor.SetFontFamily("Aptos");

        Assert.Equal("On", block.Runs[0].Text);
        Assert.NotEqual("Aptos", block.Runs[0].FontFamily);
        Assert.Equal("e ", block.Runs[1].Text);
        Assert.Equal("Aptos", block.Runs[1].FontFamily);
        Assert.Equal("Two ", block.Runs[2].Text);
        Assert.Equal("Aptos", block.Runs[2].FontFamily);
        Assert.Equal("T", block.Runs[3].Text);
        Assert.Equal("Aptos", block.Runs[3].FontFamily);
        Assert.Equal("hree", block.Runs[4].Text);
        Assert.NotEqual("Aptos", block.Runs[4].FontFamily);
    }

    [Fact]
    public void Comment_targets_selected_text_range()
    {
        var document = NotesDocument.Create();
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.PlainText = "Alpha beta";
        block.Runs = [new NotesTextRun { Text = block.PlainText }];
        var editor = new WriteDocumentEditor(document);

        editor.SelectBlock(block.Id, 10, 6, 10);
        editor.AddComment("Review");

        var comment = Assert.Single(document.Comments);
        Assert.Equal(6, comment.StartOffset);
        Assert.Equal(10, comment.EndOffset);
        Assert.Equal("Review", comment.Text);
    }
}
