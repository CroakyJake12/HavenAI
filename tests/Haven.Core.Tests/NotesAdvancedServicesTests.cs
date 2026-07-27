/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/NotesAdvancedServicesTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns NotesAdvancedServicesTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>
/// Represents notes advanced services tests and keeps its related state and behavior together.
/// </summary>
public sealed class NotesAdvancedServicesTests
{
    /// <summary>
    /// Performs the advanced state round trips inside native document metadata step owned by this component.
    /// </summary>
    [Fact]
    public void AdvancedStateRoundTripsInsideNativeDocumentMetadata()
    {
        var document = NotesDocument.Create("Advanced state");
        var state = new NotesAdvancedDocumentState
        {
            View = new NotesDocumentViewState { IsPinned = true, InterfaceScale = 1.25 },
            PageLayout = new NotesExtendedPageLayout
            {
                Columns = 2,
                GutterPoints = 18,
                DifferentFirstPage = true,
                Watermark = "Draft"
            },
            Privacy = new NotesPrivacyState
            {
                AiEnabled = true,
                AllowExternalProviders = false,
                AllowDocumentContext = true
            }
        };
        state.StudyAttempts.Add(new NotesStudyAttempt
        {
            CardId = Guid.NewGuid(),
            AttemptText = "An answer",
            Correctness = "Partly correct"
        });

        NotesAdvancedStateStore.Save(document, state);
        var loaded = NotesAdvancedStateStore.Load(document);

        Assert.True(loaded.View.IsPinned);
        Assert.Equal(1.25, loaded.View.InterfaceScale);
        Assert.Equal(2, loaded.PageLayout.Columns);
        Assert.Equal("Draft", loaded.PageLayout.Watermark);
        Assert.True(loaded.Privacy.AllowDocumentContext);
        Assert.False(loaded.Privacy.AllowExternalProviders);
        Assert.Single(loaded.StudyAttempts);
    }

    /// <summary>
    /// Performs the advanced state clamps unsafe preference values step owned by this component.
    /// </summary>
    [Fact]
    public void AdvancedStateClampsUnsafePreferenceValues()
    {
        var document = NotesDocument.Create("Clamp");
        var state = new NotesAdvancedDocumentState
        {
            View = new NotesDocumentViewState { InterfaceScale = 99 },
            PageLayout = new NotesExtendedPageLayout { Columns = 99, GutterPoints = -100 },
            Study = new NotesStudyPreferences { DailyTarget = 0, MaximumCardsPerSession = 100_001 }
        };

        NotesAdvancedStateStore.Save(document, state);
        var loaded = NotesAdvancedStateStore.Load(document);

        Assert.Equal(3, loaded.View.InterfaceScale);
        Assert.Equal(12, loaded.PageLayout.Columns);
        Assert.Equal(0, loaded.PageLayout.GutterPoints);
        Assert.Equal(1, loaded.Study.DailyTarget);
        Assert.Equal(10_000, loaded.Study.MaximumCardsPerSession);
    }

    /// <summary>
    /// Performs the regex find and replace honours page and whole word scope step owned by this component.
    /// </summary>
    [Fact]
    public void RegexFindAndReplaceHonoursPageAndWholeWordScope()
    {
        var document = NotesDocument.Create("Search");
        var firstPage = document.Sections[0].Pages[0];
        firstPage.Blocks[0].PlainText = "cat category CAT";
        firstPage.Blocks[0].Runs = [new NotesTextRun { Text = firstPage.Blocks[0].PlainText }];
        var secondPage = NotesPage.CreateDefault();
        secondPage.Order = 1;
        secondPage.Blocks[0].PlainText = "cat elsewhere";
        secondPage.Blocks[0].Runs = [new NotesTextRun { Text = secondPage.Blocks[0].PlainText }];
        document.Sections[0].Pages.Add(secondPage);
        var options = new NotesFindOptions(
            UseRegularExpression: true,
            MatchCase: false,
            WholeWord: true,
            PageId: firstPage.Id);

        var matches = NotesDocumentSearch.Find(document, "cat", options);
        var result = NotesDocumentSearch.Replace(document, "cat", "dog", options);

        Assert.Equal(2, matches.Count);
        Assert.Equal(2, result.Replacements);
        Assert.Equal("dog category dog", firstPage.Blocks[0].PlainText);
        Assert.Equal("cat elsewhere", secondPage.Blocks[0].PlainText);
    }

    /// <summary>
    /// Performs the invalid regular expression fails without changing document step owned by this component.
    /// </summary>
    [Fact]
    public void InvalidRegularExpressionFailsWithoutChangingDocument()
    {
        var document = NotesDocument.Create("Invalid regex");
        var before = document.Sections[0].Pages[0].Blocks[0].PlainText;

        Assert.Throws<InvalidDataException>(() => NotesDocumentSearch.Replace(
            document,
            "[",
            "x",
            new NotesFindOptions(UseRegularExpression: true)));

        Assert.Equal(before, document.Sections[0].Pages[0].Blocks[0].PlainText);
    }

    /// <summary>
    /// Performs the computed fields refresh from current document truth step owned by this component.
    /// </summary>
    [Fact]
    public void ComputedFieldsRefreshFromCurrentDocumentTruth()
    {
        var document = NotesDocument.Create("Field document");
        document.Sections[0].Pages[0].Blocks[0].PlainText = "one two three";
        document.Fields =
        [
            new NotesField { Name = "title", IsComputed = true },
            new NotesField { Name = "word-count", IsComputed = true },
            new NotesField { Name = "page-count", IsComputed = true },
            new NotesField { Name = "date", IsComputed = true, Format = "yyyy-MM-dd" }
        ];

        NotesFieldEvaluator.Refresh(document, new DateTimeOffset(2026, 7, 17, 10, 30, 0, TimeSpan.Zero));

        Assert.Equal("Field document", document.Fields[0].Value);
        Assert.Equal("3", document.Fields[1].Value);
        Assert.Equal("1", document.Fields[2].Value);
        Assert.Equal("2026-07-17", document.Fields[3].Value);
    }

    /// <summary>
    /// Performs the version comparison reports added and removed lines step owned by this component.
    /// </summary>
    [Fact]
    public void VersionComparisonReportsAddedAndRemovedLines()
    {
        var previous = NotesDocument.Create("Version");
        previous.Version = 3;
        previous.Sections[0].Pages[0].Blocks[0].PlainText = "First line\nOld line";
        var current = NotesDocument.Create("Version");
        current.Version = 4;
        current.Sections[0].Pages[0].Blocks[0].PlainText = "First line\nNew line";

        var comparison = NotesVersionComparer.Compare(current, previous);

        Assert.Equal(4, comparison.CurrentVersion);
        Assert.Equal(3, comparison.ComparedVersion);
        Assert.Contains(comparison.Lines, line => line.Kind == NotesDiffKind.Removed && line.Text == "Old line");
        Assert.Contains(comparison.Lines, line => line.Kind == NotesDiffKind.Added && line.Text == "New line");
    }

    /// <summary>
    /// Performs the table operations sort sum and round trip delimited text step owned by this component.
    /// </summary>
    [Fact]
    public void TableOperationsSortSumAndRoundTripDelimitedText()
    {
        var table = NotesTableData.Create(4, 2);
        table.Rows[0].Cells[0].Text = "Name";
        table.Rows[0].Cells[1].Text = "Score";
        table.Rows[1].Cells[0].Text = "B";
        table.Rows[1].Cells[1].Text = "2";
        table.Rows[2].Cells[0].Text = "A";
        table.Rows[2].Cells[1].Text = "10";
        table.Rows[3].Cells[0].Text = "C";
        table.Rows[3].Cells[1].Text = "3";

        NotesTableOperations.Sort(table, 1, descending: true);
        var sum = NotesTableOperations.Sum(table, 1);
        var text = NotesTableOperations.ToDelimitedText(table);
        var roundTrip = NotesTableOperations.FromDelimitedText(text);

        Assert.Equal("10", table.Rows[1].Cells[1].Text);
        Assert.Equal(15, sum);
        Assert.Equal(table.Rows.Count, roundTrip.Rows.Count);
        Assert.Equal(table.Rows[0].Cells.Count, roundTrip.Rows[0].Cells.Count);
    }

    /// <summary>
    /// Performs the equation tools expose templates search macros and accessible exports step owned by this component.
    /// </summary>
    [Fact]
    public void EquationToolsExposeTemplatesSearchMacrosAndAccessibleExports()
    {
        var symbols = NotesEquationTools.SearchSymbols("alpha");
        var equation = new NotesEquationData
        {
            Source = @"\alpha + \beta",
            RenderedText = "α + β",
            AccessibleAlternative = "alpha plus beta"
        };

        Assert.Contains(NotesEquationTools.Templates, template => template.Id == "fraction");
        Assert.Single(symbols);
        Assert.Equal(@"\sqrt{}", NotesEquationTools.ExpandIntelligentInput("sqrt"));
        Assert.Empty(NotesEquationTools.ValidateMacros(new Dictionary<string, string> { [@"\R"] = @"\mathbb{R}" }));
        Assert.NotEmpty(NotesEquationTools.ValidateMacros(new Dictionary<string, string> { ["R"] = string.Empty }));
        Assert.Contains("application/x-tex", NotesEquationTools.ToMathMl(equation), StringComparison.Ordinal);
        Assert.Contains("alpha plus beta", NotesEquationTools.ToSvg(equation), StringComparison.Ordinal);
    }

    /// <summary>
    /// Reports whether canvas operations respect locks snap and preserve editable points is true for the current state.
    /// </summary>
    [Fact]
    public void CanvasOperationsRespectLocksSnapAndPreserveEditablePoints()
    {
        var first = new NotesCanvasObject { X = 1, Y = 2, Width = 100, Height = 80 };
        var second = new NotesCanvasObject { X = 300, Y = 200, Width = 120, Height = 90 };
        NotesCanvasOperations.Move(first, 47, 53, 10);
        NotesCanvasOperations.Resize(first, 143, 96, 10);
        var group = NotesCanvasOperations.Group([first, second]);
        var connector = NotesCanvasOperations.Connect(first, second, "Link");
        var stroke = new NotesInkStroke
        {
            Points =
            [
                new NotesInkPoint { X = 1, Y = 0, Pressure = 0.5 },
                new NotesInkPoint { X = 2, Y = 0, Pressure = 0.7 }
            ]
        };
        NotesCanvasOperations.TransformStroke(stroke, 10, 20, 2, 90);

        Assert.Equal(50, first.X);
        Assert.Equal(50, first.Y);
        Assert.Equal(140, first.Width);
        Assert.Equal(100, first.Height);
        Assert.Equal(group, first.GroupId);
        Assert.Equal(group, second.GroupId);
        Assert.Equal(first.Id, connector.FromObjectId);
        Assert.Equal(second.Id, connector.ToObjectId);
        Assert.Equal(10, stroke.Points[0].X, 6);
        Assert.Equal(22, stroke.Points[0].Y, 6);
        first.Locked = true;
        NotesCanvasOperations.Move(first, 999, 999);
        Assert.Equal(50, first.X);
    }

    /// <summary>
    /// Performs the study attempts preserve confidence hints timing and mark step owned by this component.
    /// </summary>
    [Fact]
    public void StudyAttemptsPreserveConfidenceHintsTimingAndMark()
    {
        var state = new NotesAdvancedDocumentState();
        var card = new NotesFlashcardData();
        var attempt = NotesStudyTools.BeginAttempt(state, card, Guid.NewGuid(), 0.7);
        var completedAt = attempt.StartedAt.AddSeconds(12);

        NotesStudyTools.CompleteAttempt(attempt, "My answer", "Partly correct", 2, completedAt);

        Assert.Single(state.StudyAttempts);
        Assert.Equal("My answer", attempt.AttemptText);
        Assert.Equal("Partly correct", attempt.Correctness);
        Assert.Equal(2, attempt.HintsUsed);
        Assert.Equal(TimeSpan.FromSeconds(12), attempt.ResponseTime);
        Assert.Contains("New card", NotesStudyTools.ExplainDueReason(card, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Performs the collaboration conflict resolution is versioned step owned by this component.
    /// </summary>
    [Fact]
    public void CollaborationConflictResolutionIsVersioned()
    {
        var document = NotesDocument.Create("Conflict");
        var conflict = new NotesConflict
        {
            BlockId = document.Sections[0].Pages[0].Blocks[0].Id,
            LocalValue = "Local",
            RemoteValue = "Remote"
        };
        document.Collaboration.ConflictState = NotesConflictState.Diverged;
        document.Collaboration.Conflicts.Add(conflict);

        NotesCollaborationTools.ResolveConflict(document, conflict, "local");

        Assert.Equal("Local", conflict.Resolution);
        Assert.NotNull(conflict.ResolvedAt);
        Assert.Equal(NotesConflictState.Resolved, document.Collaboration.ConflictState);
        Assert.Contains(document.Revisions, revision => revision.Kind == NotesRevisionKind.ConflictResolved);
    }
}
