using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.WorkspaceEditor;

namespace Haven.Desktop.Tests;

public sealed class WorkspaceEditorLanguageServerTests
{
    [Fact]
    public void Completion_prefers_protocol_replace_range_and_applies_additional_edits()
    {
        const string original = "using X;\nConso";
        var completion = new LanguageServerCompletion(
            "Console",
            "System.Console",
            "Console",
            new CodeRange(new CodePosition(1, 5), new CodePosition(1, 5)),
            new CodeRange(new CodePosition(1, 0), new CodePosition(1, 5)),
            [new LanguageServerTextEdit(new CodeRange(new CodePosition(0, 6), new CodePosition(0, 7)), "System")]);

        var updated = WorkspaceEditorPage.ApplyCompletionToText(original, original.Length, completion);

        Assert.Equal("using System;\nConsole", updated);
    }

    [Fact]
    public void Completion_rejects_overlapping_protocol_edits()
    {
        const string original = "alpha beta";
        var completion = new LanguageServerCompletion(
            "replacement", null, "replacement", null,
            new CodeRange(new CodePosition(0, 0), new CodePosition(0, 5)),
            [new LanguageServerTextEdit(new CodeRange(new CodePosition(0, 3), new CodePosition(0, 6)), "other")]);

        Assert.Throws<InvalidOperationException>(() => WorkspaceEditorPage.ApplyCompletionToText(original, 5, completion));
    }
}
