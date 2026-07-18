/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/NotesProductionTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns NotesProductionTests, FakeModelClient, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents notes production tests and keeps its related state and behavior together.
/// </summary>
public sealed class NotesProductionTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the complete mixed document passes deep validation step owned by this component.
    /// </summary>
    [Fact]
    public void CompleteMixedDocumentPassesDeepValidation()
    {
        var document = CompleteDocument();
        var result = new NotesDocumentValidator().Validate(document);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Path + ": " + issue.Message)));
        Assert.Contains(document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks), block => block.Canvas?.Strokes.Count == 1);
        Assert.Contains(document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks), block => block.Html is not null);
        Assert.Contains(document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks), block => block.Flashcard is not null);
    }

    /// <summary>
    /// Performs the invalid html permissions and duplicate orders fail closed step owned by this component.
    /// </summary>
    [Fact]
    public void InvalidHtmlPermissionsAndDuplicateOrdersFailClosed()
    {
        var document = CompleteDocument();
        var page = document.Sections[0].Pages[0];
        page.Blocks[1].Order = page.Blocks[0].Order;
        var html = page.Blocks.Single(block => block.Html is not null).Html!;
        html.HtmlSource = "<img src=\"https://tracker.example/pixel.png\"><form></form>";
        html.AllowNetwork = false;
        html.AllowForms = false;
        html.AllowPopups = true;

        var result = new NotesDocumentValidator().Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Path.Contains("order", StringComparison.Ordinal));
        Assert.Contains(result.Issues, issue => issue.Path.EndsWith("allowNetwork", StringComparison.Ordinal));
        Assert.Contains(result.Issues, issue => issue.Path.EndsWith("allowForms", StringComparison.Ordinal));
        Assert.Contains(result.Issues, issue => issue.Path.EndsWith("allowPopups", StringComparison.Ordinal));
    }

    /// <summary>
    /// Performs the atomic repository versions searches and recovers corrupt current file step owned by this component.
    /// </summary>
    [Fact]
    public async Task AtomicRepositoryVersionsSearchesAndRecoversCorruptCurrentFile()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var repository = new NotesRepository(_paths, new NotesDocumentValidator(), diagnostics);
        var document = CompleteDocument();

        var first = await repository.SaveAsync(document, "Initial complete document", CancellationToken.None);
        document.Sections[0].Pages[0].Blocks[0].PlainText = "Second version with a uniquely searchable narwhal phrase.";
        var second = await repository.SaveAsync(document, "Second version", CancellationToken.None);

        Assert.Equal(1, first.Version);
        Assert.Equal(2, second.Version);
        Assert.True(File.Exists(second.CurrentPath));
        Assert.True(File.Exists(second.VersionPath));
        var versions = await repository.GetVersionsAsync(document.Id, CancellationToken.None);
        Assert.Contains(versions, version => version.Version == 1);
        Assert.Contains(versions, version => version.Version == 2);
        var hits = await repository.SearchAsync("uniquely searchable narwhal", CancellationToken.None);
        Assert.Single(hits);
        Assert.Equal(document.Id, hits[0].DocumentId);

        await File.WriteAllTextAsync(second.CurrentPath, "{ corrupt current document", CancellationToken.None);
        var recovered = await repository.LoadAsync(document.Id, CancellationToken.None);

        Assert.NotNull(recovered);
        Assert.True(recovered!.Recovery.HasUnsavedRecovery);
        Assert.Contains("Recovered", recovered.Recovery.RecoveryReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            Directory.EnumerateFiles(Path.GetDirectoryName(second.CurrentPath)!, "current.haven-notes.json.corrupt-current-*"),
            File.Exists);
    }

    /// <summary>
    /// Performs the verified repository rejects valid json tampering and returns previous version step owned by this component.
    /// </summary>
    [Fact]
    public async Task VerifiedRepositoryRejectsValidJsonTamperingAndReturnsPreviousVersion()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var inner = new NotesRepository(_paths, new NotesDocumentValidator(), diagnostics);
        var verified = new VerifiedNotesRepository(inner, _paths, diagnostics);
        var document = CompleteDocument();
        await verified.SaveAsync(document, "Initial", CancellationToken.None);
        document.Title = "Trusted second version";
        var second = await verified.SaveAsync(document, "Trusted second version", CancellationToken.None);

        var tampered = await inner.LoadAsync(document.Id, CancellationToken.None);
        Assert.NotNull(tampered);
        tampered!.Title = "Tampered but syntactically valid";
        await File.WriteAllTextAsync(
            second.CurrentPath,
            JsonSerializer.Serialize(tampered, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }),
            CancellationToken.None);

        var loaded = await verified.LoadAsync(document.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.NotEqual("Tampered but syntactically valid", loaded!.Title);
        Assert.True(loaded.Recovery.HasUnsavedRecovery);
        var events = await diagnostics.ReadRecentAsync(30, CancellationToken.None);
        Assert.Contains(events, item => item.EventName == "integrity-mismatch");
    }

    /// <summary>
    /// Performs the native round trip preserves ink html flashcards comments and ai provenance step owned by this component.
    /// </summary>
    [Fact]
    public async Task NativeRoundTripPreservesInkHtmlFlashcardsCommentsAndAiProvenance()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var formats = new NotesImportExportService(new NotesDocumentValidator(), diagnostics);
        var document = CompleteDocument();
        var destination = Path.Combine(_paths.DataDirectory, "roundtrip.haven-notes.json");

        await formats.ExportAsync(document, destination, CancellationToken.None);
        var imported = await formats.ImportAsync(destination, CancellationToken.None);

        var blocks = imported.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks).ToArray();
        Assert.Contains(blocks, block => block.Canvas?.Strokes.Single().Points.Count == 2);
        Assert.Contains(blocks, block => block.Canvas?.GhostLayers.Single().Masks.Single().Answer == "42");
        Assert.Contains(blocks, block => block.Html?.JavaScriptSource.Contains("textContent", StringComparison.Ordinal) == true);
        Assert.Contains(blocks, block => block.Flashcard?.OcclusionMasks.Single().Answer == "mitochondrion");
        Assert.Single(imported.Comments);
        Assert.Single(imported.Citations);
        Assert.Single(imported.AiChanges);
        Assert.Contains(imported.Revisions, revision => revision.Kind == NotesRevisionKind.Imported);
    }

    /// <summary>
    /// Performs the pdf and html exports use truthful fallbacks for interactive content step owned by this component.
    /// </summary>
    [Fact]
    public async Task PdfAndHtmlExportsUseTruthfulFallbacksForInteractiveContent()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var formats = new NotesImportExportService(new NotesDocumentValidator(), diagnostics);
        var document = CompleteDocument();
        var pdf = Path.Combine(_paths.DataDirectory, "notes.pdf");
        var html = Path.Combine(_paths.DataDirectory, "notes.html");

        await formats.ExportAsync(document, pdf, CancellationToken.None);
        await formats.ExportAsync(document, html, CancellationToken.None);

        var pdfHeader = new byte[8];
        await using (var stream = File.OpenRead(pdf))
            _ = await stream.ReadAsync(pdfHeader, CancellationToken.None);
        Assert.StartsWith("%PDF-1.", System.Text.Encoding.ASCII.GetString(pdfHeader), StringComparison.Ordinal);
        var htmlText = await File.ReadAllTextAsync(html, CancellationToken.None);
        Assert.Contains("Interactive widget fallback", htmlText, StringComparison.Ordinal);
        Assert.Contains("Interactive canvas data remains in the native file", htmlText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Performs the notes ai requires consent and rejects invented citation ids step owned by this component.
    /// </summary>
    [Fact]
    public async Task NotesAiRequiresConsentAndRejectsInventedCitationIds()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var citation = new NotesCitation
        {
            Key = "source-1",
            Title = "Primary evidence",
            EvidenceExcerpt = "Haven Notes keeps reviewed AI proposals separate from applied edits."
        };
        var fake = new FakeModelClient("""
            {
              "proposedContent": "A clearer evidence-based sentence.",
              "explanation": "Uses the supplied source.",
              "citationIds": ["00000000-0000-0000-0000-000000000001"]
            }
            """);
        var service = new NotesAiService(fake, diagnostics);
        var withoutContext = new NotesAiProposalRequest(
            Guid.NewGuid(),
            null,
            "Rewrite this",
            string.Empty,
            "private document context",
            "ollama:test",
            false,
            [citation]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProposeAsync(withoutContext, CancellationToken.None));

        var withSelection = withoutContext with { SelectedText = "Original sentence" };
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ProposeAsync(withSelection, CancellationToken.None));
        Assert.Equal(1, fake.CompleteCalls);
    }

    /// <summary>
    /// Performs the notes ai accepts only supplied evidence and returns review proposal step owned by this component.
    /// </summary>
    [Fact]
    public async Task NotesAiAcceptsOnlySuppliedEvidenceAndReturnsReviewProposal()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var citation = new NotesCitation
        {
            Key = "source-1",
            Title = "Primary evidence",
            Authors = "Haven team",
            EvidenceExcerpt = "Proposals require explicit review."
        };
        var response = JsonSerializer.Serialize(new
        {
            proposedContent = "Proposals require explicit review before application.",
            explanation = "Clarifies the supplied evidence without adding facts.",
            citationIds = new[] { citation.Id }
        });
        var fake = new FakeModelClient(response);
        var service = new NotesAiService(fake, diagnostics);

        var result = await service.ProposeAsync(new NotesAiProposalRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Clarify",
            "Proposals require review.",
            string.Empty,
            "openai:test-model",
            false,
            [citation]), CancellationToken.None);

        Assert.Equal("Proposals require explicit review before application.", result.ProposedContent);
        Assert.Equal([citation.Id], result.CitationIds);
        Assert.Equal("openai", result.ProviderId);
        Assert.Equal("openai:test-model", result.ModelName);
    }

    /// <summary>
    /// Performs the flashcard scheduler records lapses and expands successful intervals step owned by this component.
    /// </summary>
    [Fact]
    public void FlashcardSchedulerRecordsLapsesAndExpandsSuccessfulIntervals()
    {
        var card = new NotesFlashcardData
        {
            Front = "Question",
            Back = "Answer",
            Schedule = new NotesFlashcardSchedule { IntervalDays = 12, Repetitions = 4, EaseFactor = 2.5 }
        };
        var now = DateTimeOffset.UtcNow;

        var failed = NotesFlashcardScheduler.Review(card, NotesFlashcardRating.Again, 0.1, TimeSpan.FromSeconds(8), now);
        Assert.Equal(1, failed.NewIntervalDays);
        Assert.Equal(1, card.Schedule.Lapses);
        Assert.Equal(0, card.Schedule.Repetitions);

        var successful = NotesFlashcardScheduler.Review(card, NotesFlashcardRating.Easy, 0.95, TimeSpan.FromSeconds(2), now.AddDays(1));
        Assert.True(successful.NewIntervalDays >= 1);
        Assert.Equal(1, card.Schedule.Repetitions);
        Assert.True(card.Schedule.DueAt > now);
    }

    /// <summary>
    /// Performs the secure attachment store resolves files without extensions inside managed root step owned by this component.
    /// </summary>
    [Fact]
    public async Task SecureAttachmentStoreResolvesFilesWithoutExtensionsInsideManagedRoot()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var inner = new NotesAttachmentStore(_paths, diagnostics);
        var secure = new SecureNotesAttachmentStore(inner, _paths);
        var source = Path.Combine(_paths.DataDirectory, "source-without-extension");
        await File.WriteAllTextAsync(source, "attachment", CancellationToken.None);

        var imported = await secure.ImportAsync(source, CancellationToken.None);
        var resolved = await secure.ResolvePathAsync(imported.AttachmentId, CancellationToken.None);

        Assert.True(File.Exists(resolved));
        Assert.StartsWith(
            Path.Combine(_paths.DataDirectory, "Notes", "Attachments"),
            resolved,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        RuntimeSafetyState.DisableSafeMode();
        _paths.Dispose();
    }

    /// <summary>
    /// Performs the complete document step owned by this component.
    /// </summary>
    private static NotesDocument CompleteDocument()
    {
        var document = NotesDocument.Create("Complete Notes document");
        var page = document.Sections[0].Pages[0];
        page.Blocks.Clear();
        page.Blocks.Add(new NotesBlock
        {
            Kind = NotesBlockKind.Heading,
            StyleId = "heading-1",
            PlainText = "Research heading",
            Runs = [new NotesTextRun { Text = "Research heading", Bold = true, FontSize = 24 }],
            Order = 0
        });
        page.Blocks.Add(new NotesBlock
        {
            Kind = NotesBlockKind.Paragraph,
            PlainText = "A paragraph with structured rich text.",
            Runs =
            [
                new NotesTextRun { Text = "A paragraph with ", FontSize = 14 },
                new NotesTextRun { Text = "structured", Bold = true, Foreground = "#FF2F80ED", FontSize = 14 },
                new NotesTextRun { Text = " rich text.", Italic = true, FontSize = 14 }
            ],
            Order = 1
        });
        page.Blocks.Add(new NotesBlock
        {
            Kind = NotesBlockKind.List,
            Order = 2,
            List = new NotesListData
            {
                Kind = NotesListKind.Checklist,
                Items = [new NotesListItem { Text = "Review evidence", Checked = true }]
            }
        });
        page.Blocks.Add(new NotesBlock { Kind = NotesBlockKind.Table, Order = 3, Table = NotesTableData.Create(2, 2) });
        page.Blocks.Add(new NotesBlock
        {
            Kind = NotesBlockKind.Equation,
            Order = 4,
            Equation = new NotesEquationData
            {
                Source = "E = mc^2",
                RenderedText = "E = mc²",
                AccessibleAlternative = "Energy equals mass times the speed of light squared."
            }
        });
        page.Blocks.Add(new NotesBlock
        {
            Kind = NotesBlockKind.HtmlWidget,
            Order = 5,
            Html = new NotesHtmlData
            {
                HtmlSource = "<button id=\"value\">Ready</button>",
                CssSource = "button{font:inherit}",
                JavaScriptSource = "document.getElementById('value').textContent='Interactive';",
                AllowScripts = true,
                FallbackText = "Interactive button"
            }
        });
        var stroke = new NotesInkStroke
        {
            Colour = "#FF2F80ED",
            Points =
            [
                new NotesInkPoint { X = 10, Y = 20, Pressure = 0.4, TiltX = 10, TiltY = -5 },
                new NotesInkPoint { X = 80, Y = 90, Pressure = 0.8, TiltX = 14, TiltY = -8 }
            ]
        };
        var ghost = new NotesGhostLayer
        {
            Name = "Answer",
            StrokeIds = [stroke.Id],
            Masks = [new NotesOcclusionMask { X = 20, Y = 30, Width = 100, Height = 60, Answer = "42" }]
        };
        page.Blocks.Add(new NotesBlock
        {
            Kind = NotesBlockKind.Canvas,
            Order = 6,
            Canvas = new NotesCanvasData
            {
                Infinite = true,
                Strokes = [stroke],
                GhostLayers = [ghost],
                Objects = [new NotesCanvasObject { Kind = NotesCanvasObjectKind.Text, Text = "Canvas note" }]
            }
        });
        page.Blocks.Add(new NotesBlock
        {
            Kind = NotesBlockKind.Flashcard,
            Order = 7,
            Flashcard = new NotesFlashcardData
            {
                Front = "Where is ATP mainly produced?",
                Back = "In mitochondria.",
                OcclusionMasks = [new NotesOcclusionMask { X = 10, Y = 10, Width = 120, Height = 50, Answer = "mitochondrion" }]
            }
        });
        var citation = new NotesCitation
        {
            Key = "source-1",
            Title = "Primary source",
            Authors = "Haven team",
            Url = "https://example.test/source",
            EvidenceExcerpt = "Reviewed proposals require consent."
        };
        document.Citations.Add(citation);
        document.Comments.Add(new NotesComment
        {
            BlockId = page.Blocks[1].Id,
            StartOffset = 0,
            EndOffset = 10,
            Text = "Clarify this paragraph."
        });
        document.AiChanges.Add(new NotesAiChange
        {
            BlockId = page.Blocks[1].Id,
            Instruction = "Clarify",
            OriginalContent = "Original",
            ProposedContent = "Proposed",
            Explanation = "Uses supplied evidence.",
            CitationIds = [citation.Id],
            ProviderId = "ollama",
            ModelName = "test",
            Status = NotesAiChangeStatus.Proposed,
            UserConsentRecorded = true
        });
        return document;
    }

    /// <summary>
    /// Represents fake model client and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeModelClient(string response) : IOllamaClient
    {
        /// <summary>
        /// Gets or updates complete calls, the bindable or domain state represented by this property.
        /// </summary>
        public int CompleteCalls { get; private set; }
        /// <summary>
        /// Reports whether is available async is true for the current state.
        /// </summary>
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        /// <summary>
        /// Retrieves models async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        /// <summary>
        /// Performs stream chat async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public async IAsyncEnumerable<string> StreamChatAsync(
            OllamaChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
        /// <summary>
        /// Performs complete async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
        {
            CompleteCalls++;
            return Task.FromResult(response);
        }
        /// <summary>
        /// Performs chat with tools async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(string.Empty, []));
        /// <summary>
        /// Performs pull model async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        /// <summary>
        /// Performs delete model async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task DeleteModelAsync(string model, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// Represents test paths and keeps its related state and behavior together.
    /// </summary>
    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-notes-production-tests-" + Guid.NewGuid().ToString("N"));
            DatabasePath = Path.Combine(DataDirectory, "haven.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(LogsDirectory);
        }
        /// <summary>
        /// Gets or updates data directory, the bindable or domain state represented by this property.
        /// </summary>
        public string DataDirectory { get; }
        /// <summary>
        /// Gets or updates database path, the bindable or domain state represented by this property.
        /// </summary>
        public string DatabasePath { get; }
        /// <summary>
        /// Gets or updates browser profile directory, the bindable or domain state represented by this property.
        /// </summary>
        public string BrowserProfileDirectory { get; }
        /// <summary>
        /// Gets or updates attachments directory, the bindable or domain state represented by this property.
        /// </summary>
        public string AttachmentsDirectory { get; }
        /// <summary>
        /// Gets or updates logs directory, the bindable or domain state represented by this property.
        /// </summary>
        public string LogsDirectory { get; }
        /// <summary>
        /// Gets or updates legacy state path, the bindable or domain state represented by this property.
        /// </summary>
        public string LegacyStatePath { get; }
        /// <summary>
        /// Performs the dispose step owned by this component.
        /// </summary>
        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
