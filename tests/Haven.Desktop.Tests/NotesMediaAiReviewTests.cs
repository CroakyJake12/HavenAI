/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/NotesMediaAiReviewTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns NotesMediaAiReviewTests, RecordingNotesAiService, FakeModelClient, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using Haven.Infrastructure;

namespace Haven.Desktop.Tests;

/// <summary>
/// Represents notes media ai review tests and keeps its related state and behavior together.
/// </summary>
public sealed class NotesMediaAiReviewTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the alt text proposal does not mutate until approved and uses only text evidence step owned by this component.
    /// </summary>
    [Fact]
    public async Task AltTextProposalDoesNotMutateUntilApprovedAndUsesOnlyTextEvidence()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var model = new FakeModelClient();
        var ai = new RecordingNotesAiService("A labelled energy-flow diagram.");
        var workspace = CreateWorkspace(diagnostics, model);
        await workspace.InitializeAsync(CancellationToken.None);
        workspace.SelectedModelName = "ollama:qwen-test";
        var page = Assert.IsType<NotesPage>(workspace.CurrentPage);
        page.Blocks.Clear();
        page.Blocks.Add(NotesBlock.CreateParagraph("The surrounding paragraph describes energy moving from the Sun to a plant."));
        var media = ImageBlock();
        media.Order = 1;
        page.Blocks.Add(media);
        workspace.SelectedBlock = media;

        var change = await NotesMediaAiReview.ProposeAsync(
            ai,
            workspace,
            media,
            NotesMediaAiTarget.AltText,
            "Keep it concise.",
            CancellationToken.None);
        var persistedPending = NotesMediaAiReview.FindPending(workspace.Document!, media.Id, NotesMediaAiTarget.AltText);

        Assert.Equal(string.Empty, media.Media!.AltText);
        Assert.Equal(NotesAiChangeStatus.Proposed, change.Status);
        Assert.NotNull(persistedPending);
        Assert.Equal(change.Id, persistedPending!.Id);
        Assert.Empty(workspace.Document!.AiChanges);
        Assert.NotNull(ai.LastRequest);
        Assert.Contains("diagram.png", ai.LastRequest!.SelectedText, StringComparison.Ordinal);
        Assert.Contains("energy moving", ai.LastRequest.SelectedText, StringComparison.Ordinal);
        Assert.DoesNotContain("raw bytes", ai.LastRequest.SelectedText, StringComparison.OrdinalIgnoreCase);
        Assert.False(ai.LastRequest.AllowDocumentContext);

        await NotesMediaAiReview.ApplyAsync(workspace, media, persistedPending, CancellationToken.None);

        Assert.Equal("A labelled energy-flow diagram.", media.Media.AltText);
        Assert.Equal(NotesAiChangeStatus.Applied, persistedPending.Status);
        Assert.NotNull(persistedPending.ReviewedAt);
        Assert.Null(NotesMediaAiReview.FindPending(workspace.Document, media.Id, NotesMediaAiTarget.AltText));
        Assert.Contains(workspace.Document.AiChanges, value => value.Id == change.Id && value.Status == NotesAiChangeStatus.Applied);
        Assert.Contains(workspace.Document.Revisions, revision =>
            revision.Kind == NotesRevisionKind.AiApplied
            && revision.BlockId == media.Id
            && revision.Summary.Contains("alt text", StringComparison.OrdinalIgnoreCase));
        Assert.False(workspace.IsDirty);
        workspace.Dispose();
    }

    /// <summary>
    /// Performs the transcript proposal can be rejected without changing existing transcript step owned by this component.
    /// </summary>
    [Fact]
    public async Task TranscriptProposalCanBeRejectedWithoutChangingExistingTranscript()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var model = new FakeModelClient();
        var ai = new RecordingNotesAiService("Cleaned transcript that must not be applied.");
        var workspace = CreateWorkspace(diagnostics, model);
        await workspace.InitializeAsync(CancellationToken.None);
        workspace.SelectedModelName = "ollama:qwen-test";
        var block = VideoBlock();
        var transform = NotesMediaTransformStore.Load(block);
        transform.Transcript = "Original rough transcript.";
        NotesMediaTransformStore.Save(block, transform);
        var page = Assert.IsType<NotesPage>(workspace.CurrentPage);
        page.Blocks.Clear();
        page.Blocks.Add(block);
        workspace.SelectedBlock = block;

        await NotesMediaAiReview.ProposeAsync(
            ai,
            workspace,
            block,
            NotesMediaAiTarget.Transcript,
            "Clean punctuation only.",
            CancellationToken.None);
        var persisted = Assert.IsType<NotesAiChange>(
            NotesMediaAiReview.FindPending(workspace.Document!, block.Id, NotesMediaAiTarget.Transcript));
        await NotesMediaAiReview.RejectAsync(workspace, block, persisted);

        Assert.Equal("Original rough transcript.", NotesMediaTransformStore.Load(block).Transcript);
        Assert.Equal(NotesAiChangeStatus.Rejected, persisted.Status);
        Assert.NotNull(persisted.ReviewedAt);
        Assert.Null(NotesMediaAiReview.FindPending(workspace.Document!, block.Id, NotesMediaAiTarget.Transcript));
        Assert.Contains(workspace.Document!.AiChanges, value => value.Id == persisted.Id && value.Status == NotesAiChangeStatus.Rejected);
        Assert.True(workspace.IsDirty);
        workspace.Dispose();
    }

    /// <summary>
    /// Performs the new proposal archives older unreviewed proposal for same media target step owned by this component.
    /// </summary>
    [Fact]
    public async Task NewProposalArchivesOlderUnreviewedProposalForSameMediaTarget()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var model = new FakeModelClient();
        var ai = new RecordingNotesAiService("First value");
        var workspace = CreateWorkspace(diagnostics, model);
        await workspace.InitializeAsync(CancellationToken.None);
        workspace.SelectedModelName = "ollama:qwen-test";
        var block = ImageBlock();
        var page = Assert.IsType<NotesPage>(workspace.CurrentPage);
        page.Blocks.Clear();
        page.Blocks.Add(block);
        workspace.SelectedBlock = block;
        var first = await NotesMediaAiReview.ProposeAsync(
            ai,
            workspace,
            block,
            NotesMediaAiTarget.Caption,
            "First caption.",
            CancellationToken.None);
        ai.ProposedContent = "Replacement value";

        var second = await NotesMediaAiReview.ProposeAsync(
            ai,
            workspace,
            block,
            NotesMediaAiTarget.Caption,
            "Replacement caption.",
            CancellationToken.None);
        var pending = NotesMediaAiReview.FindPending(workspace.Document!, block.Id, NotesMediaAiTarget.Caption);

        Assert.Contains(workspace.Document!.AiChanges, value =>
            value.Id == first.Id
            && value.Status == NotesAiChangeStatus.Cancelled
            && value.ReviewedAt is not null);
        Assert.Equal(NotesAiChangeStatus.Proposed, second.Status);
        Assert.NotNull(pending);
        Assert.Equal(second.Id, pending!.Id);
        workspace.Dispose();
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _paths.Dispose();

    /// <summary>
    /// Creates workspace with the invariants required by its callers.
    /// </summary>
    private NotesWorkspaceViewModel CreateWorkspace(
        IProductionDiagnostics diagnostics,
        IOllamaClient model)
    {
        var validator = new NotesDocumentValidator();
        var repository = new VerifiedNotesRepository(
            new NotesRepository(_paths, validator, diagnostics),
            _paths,
            diagnostics);
        return new NotesWorkspaceViewModel(
            repository,
            new NotesImportExportService(validator, diagnostics),
            new RecordingNotesAiService("unused"),
            new SecureNotesAttachmentStore(new NotesAttachmentStore(_paths, diagnostics), _paths),
            model,
            diagnostics);
    }

    /// <summary>
    /// Performs the image block step owned by this component.
    /// </summary>
    private static NotesBlock ImageBlock() => new()
    {
        Kind = NotesBlockKind.Image,
        Media = new NotesMediaData
        {
            AttachmentId = Guid.NewGuid(),
            OriginalName = "diagram.png",
            StoredPath = Guid.NewGuid().ToString("N") + ".png",
            MediaType = "image/png",
            SizeBytes = 128,
            Sha256 = new string('a', 64),
            Width = 640,
            Height = 480
        }
    };

    /// <summary>
    /// Performs the video block step owned by this component.
    /// </summary>
    private static NotesBlock VideoBlock() => new()
    {
        Kind = NotesBlockKind.Video,
        Media = new NotesMediaData
        {
            AttachmentId = Guid.NewGuid(),
            OriginalName = "interview.mp4",
            StoredPath = Guid.NewGuid().ToString("N") + ".mp4",
            MediaType = "video/mp4",
            SizeBytes = 512,
            Sha256 = new string('b', 64),
            Width = 1280,
            Height = 720
        }
    };

    /// <summary>
    /// Represents recording notes ai service and keeps its related state and behavior together.
    /// </summary>
    private sealed class RecordingNotesAiService(string proposedContent) : INotesAiService
    {
        /// <summary>
        /// Gets or updates proposed content, the bindable or domain state represented by this property.
        /// </summary>
        public string ProposedContent { get; set; } = proposedContent;
        /// <summary>
        /// Gets or updates last request, the bindable or domain state represented by this property.
        /// </summary>
        public NotesAiProposalRequest? LastRequest { get; private set; }

        /// <summary>
        /// Performs propose asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<NotesAiProposalResult> ProposeAsync(
            NotesAiProposalRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new NotesAiProposalResult(
                ProposedContent,
                "Grounded in the supplied media metadata and note text.",
                [],
                "ollama",
                request.ModelName));
        }
    }

    /// <summary>
    /// Represents fake model client and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeModelClient : IOllamaClient
    {
        /// <summary>
        /// Reports whether available async applies to the current state.
        /// </summary>
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        /// <summary>
        /// Retrieves models async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        /// <summary>
        /// Performs stream chat asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public async IAsyncEnumerable<string> StreamChatAsync(
            OllamaChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
        /// <summary>
        /// Performs complete asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(string.Empty);
        /// <summary>
        /// Performs chat with tools asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(string.Empty, []));
        /// <summary>
        /// Performs pull model asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs delete model asynchronously so I/O does not block the caller's thread.
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
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-notes-media-ai-tests-" + Guid.NewGuid().ToString("N"));
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
