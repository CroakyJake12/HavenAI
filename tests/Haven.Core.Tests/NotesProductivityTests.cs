/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/NotesProductivityTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns NotesProductivityTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>
/// Represents notes productivity tests and keeps its related state and behavior together.
/// </summary>
public sealed class NotesProductivityTests
{
    /// <summary>
    /// Performs the every built in template produces a valid native document step owned by this component.
    /// </summary>
    [Fact]
    public void EveryBuiltInTemplateProducesAValidNativeDocument()
    {
        var validator = new NotesDocumentValidator();

        foreach (var descriptor in NotesTemplateCatalog.Templates)
        {
            var document = NotesTemplateCatalog.Create(descriptor.Id);
            var result = validator.Validate(document);

            Assert.True(
                result.IsValid,
                descriptor.Id + Environment.NewLine + string.Join(
                    Environment.NewLine,
                    result.Issues.Select(issue => issue.Path + ": " + issue.Message)));
            Assert.Equal(descriptor.Name, document.Title);
            Assert.Single(document.Sections);
            Assert.Single(document.Sections[0].Pages);
            Assert.NotEmpty(document.Sections[0].Pages[0].Blocks);
            Assert.Contains(
                document.Revisions,
                revision => revision.Kind == NotesRevisionKind.Created
                            && revision.Summary.Contains(descriptor.Name, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Performs the revision template contains real study and worked example structures step owned by this component.
    /// </summary>
    [Fact]
    public void RevisionTemplateContainsRealStudyAndWorkedExampleStructures()
    {
        var document = NotesTemplateCatalog.Create("revision", "Biology revision");
        var blocks = document.Sections[0].Pages[0].Blocks;

        Assert.Equal("Biology revision", document.Title);
        Assert.Contains(blocks, block => block.Kind == NotesBlockKind.List);
        Assert.Contains(blocks, block => block.Kind == NotesBlockKind.Flashcard);
        Assert.Contains(
            blocks,
            block => block.Kind == NotesBlockKind.Heading
                     && block.PlainText.Contains("Worked example", StringComparison.Ordinal));
    }

    /// <summary>
    /// Performs the find replace preserves formatted runs and creates one revision step owned by this component.
    /// </summary>
    [Fact]
    public void FindReplacePreservesFormattedRunsAndCreatesOneRevision()
    {
        var document = NotesDocument.Create("Replace test");
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.PlainText = "Haven keeps Haven edits reviewed.";
        block.Runs =
        [
            new NotesTextRun { Text = "Haven keeps ", Bold = true },
            new NotesTextRun { Text = "Haven edits reviewed.", Italic = true }
        ];
        var revisionsBefore = document.Revisions.Count;

        var result = NotesFindReplace.Replace(
            document,
            "Haven",
            "Notes",
            matchCase: true,
            wholeWord: true);

        Assert.Equal(2, result.Replacements);
        Assert.Equal(1, result.BlocksChanged);
        Assert.Equal("Notes keeps Notes edits reviewed.", block.PlainText);
        Assert.Equal("Notes keeps ", block.Runs[0].Text);
        Assert.True(block.Runs[0].Bold);
        Assert.Equal("Notes edits reviewed.", block.Runs[1].Text);
        Assert.True(block.Runs[1].Italic);
        Assert.Equal(revisionsBefore + 1, document.Revisions.Count);
    }

    /// <summary>
    /// Performs the whole word replace does not change longer words step owned by this component.
    /// </summary>
    [Fact]
    public void WholeWordReplaceDoesNotChangeLongerWords()
    {
        var document = NotesDocument.Create("Whole word");
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.PlainText = "plan planner planning PLAN";
        block.Runs = [new NotesTextRun { Text = block.PlainText }];

        var result = NotesFindReplace.Replace(
            document,
            "plan",
            "task",
            matchCase: false,
            wholeWord: true);

        Assert.Equal(2, result.Replacements);
        Assert.Equal("task planner planning task", block.PlainText);
    }

    /// <summary>
    /// Performs the language checks return exact block ranges and suggestions step owned by this component.
    /// </summary>
    [Fact]
    public void LanguageChecksReturnExactBlockRangesAndSuggestions()
    {
        var document = NotesDocument.Create("Language checks");
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.PlainText = "this this sentence has a space before punctuation ! next sentence.";
        block.Runs = [new NotesTextRun { Text = block.PlainText }];

        var issues = NotesLanguageChecks.Check(document);

        Assert.Contains(
            issues,
            issue => issue.BlockId == block.Id
                     && issue.Kind == "Repeated word"
                     && issue.Start == 0
                     && issue.Length == "this this".Length
                     && issue.Suggestions.Contains("this"));
        Assert.Contains(
            issues,
            issue => issue.BlockId == block.Id
                     && issue.Kind == "Punctuation spacing"
                     && issue.Message.Contains("space", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            issues,
            issue => issue.BlockId == block.Id
                     && issue.Kind == "Sentence capitalisation"
                     && issue.Suggestions.Count == 1);
    }

    /// <summary>
    /// Performs the no match does not create revision or dirty document timestamp step owned by this component.
    /// </summary>
    [Fact]
    public void NoMatchDoesNotCreateRevisionOrDirtyDocumentTimestamp()
    {
        var document = NotesDocument.Create("No match");
        var updatedAt = document.UpdatedAt;
        var revisions = document.Revisions.Count;

        var result = NotesFindReplace.Replace(
            document,
            "missing phrase",
            "replacement",
            matchCase: false,
            wholeWord: false);

        Assert.Equal(0, result.Replacements);
        Assert.Equal(revisions, document.Revisions.Count);
        Assert.Equal(updatedAt, document.UpdatedAt);
    }
}
