using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

public sealed class WriteWordProcessorTests
{
    [Fact]
    public void Editor_supports_word_processor_formatting_structure_layout_review_and_history()
    {
        var document = NotesDocument.Create("Coursework"); var block = document.Sections[0].Pages[0].Blocks[0]; block.PlainText = "Alpha beta"; block.Runs = [new NotesTextRun { Text = "Alpha ", Bold = true }, new NotesTextRun { Text = "beta", Italic = true }]; var editor = new WriteDocumentEditor(document); editor.SelectBlock(block.Id, 8); editor.ToggleCharacter(WriteCharacterFormat.Underline); editor.SetFontFamily("Aptos"); editor.SetFontSize(12); editor.SetAlignment(NotesTextAlignment.Justify); editor.SetLineSpacing(1.5); editor.SetLeftIndent(24); editor.SetFirstLineIndent(18);
        Assert.True(block.Runs[1].Underline); Assert.Equal("Aptos", block.Runs[1].FontFamily); Assert.Equal(12, block.Runs[1].FontSize); Assert.Equal(NotesTextAlignment.Justify, block.Paragraph.Alignment); Assert.Equal(1.5, block.Paragraph.LineSpacing); Assert.Equal(24, block.Paragraph.IndentLeft); Assert.Equal(18, block.Paragraph.FirstLineIndent);
        editor.SelectBlock(block.Id, 8); Assert.True(editor.SplitRunAtCaret()); Assert.Equal(3, block.Runs.Count); Assert.True(editor.MergeRunWithPrevious()); Assert.Equal(2, block.Runs.Count); var list = editor.InsertBlock(NotesBlockKind.List, NotesListKind.Checklist); Assert.Equal(NotesListKind.Checklist, list.List!.Kind); editor.AddListItem(); Assert.Equal(2, list.List.Items.Count); var table = editor.InsertBlock(NotesBlockKind.Table); editor.SelectBlock(table.Id); editor.AddTableRow(); editor.AddTableColumn(); Assert.Equal(4, table.Table!.Rows.Count); Assert.All(table.Table.Rows, row => Assert.Equal(4, row.Cells.Count));
        editor.SetPagePreset("Letter"); editor.SetOrientation("Landscape"); editor.SetMargins(54); editor.SetPageNumbers(false); Assert.Equal("Landscape", document.PageSetup.Orientation); Assert.True(document.PageSetup.WidthPoints > document.PageSetup.HeightPoints); Assert.Equal(54, document.PageSetup.MarginLeftPoints); Assert.False(document.PageSetup.ShowPageNumbers); editor.SelectBlock(block.Id); editor.AddComment("Check this wording"); editor.AddCitation("Source", "Author", "https://example.test"); Assert.Single(document.Comments); Assert.Single(document.Citations);
        Assert.Single(editor.Find("beta")); Assert.Equal(1, editor.ReplaceAll("beta", "gamma")); Assert.Contains("gamma", block.PlainText); Assert.True(editor.CanUndo); Assert.True(editor.Undo()); Assert.Contains("beta", document.Sections[0].Pages[0].Blocks[0].PlainText); Assert.True(editor.Redo()); Assert.Contains("gamma", document.Sections[0].Pages[0].Blocks[0].PlainText);
    }

    [Fact]
    public void Text_editing_preserves_existing_run_formatting()
    {
        var document = NotesDocument.Create(); var block = document.Sections[0].Pages[0].Blocks[0]; block.Runs = [new NotesTextRun { Text = "Bold ", Bold = true }, new NotesTextRun { Text = "italic", Italic = true }]; block.PlainText = "Bold italic"; var editor = new WriteDocumentEditor(document); editor.SelectBlock(block.Id, 5); editor.ReplaceSelectedText("Bold stronger italic", 12); Assert.Equal("Bold stronger ", block.Runs[0].Text); Assert.True(block.Runs[0].Bold); Assert.Equal("italic", block.Runs[1].Text); Assert.True(block.Runs[1].Italic); Assert.Equal("Bold stronger italic", block.PlainText);
    }
}
