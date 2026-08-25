using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Tests;

public sealed class BoardsWorkspaceTests
{
    [Fact]
    public async Task CreateNotebook_MarksPersistsAndFilters()
    {
        var ordinary = NotesDocument.Create("Ordinary");
        var repository = new FakeNotesRepository(ordinary);
        var boards = new BoardsWorkspaceService(repository);

        var created = await boards.CreateNotebookAsync("Study board", CancellationToken.None);
        var listed = await boards.ListNotebooksAsync(CancellationToken.None);

        Assert.Equal("boards", created.Metadata[BoardsWorkspaceService.ProductKey]);
        Assert.Single(listed);
        Assert.Equal(created.Id, listed[0].Id);
        Assert.NotNull(await boards.OpenNotebookAsync(created.Id, CancellationToken.None));
        Assert.Null(await boards.OpenNotebookAsync(ordinary.Id, CancellationToken.None));
    }

    [Fact]
    public void DeepLink_RoundTripsNotebookSectionAndPage()
    {
        var expected = new BoardsDeepLink(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var text = expected.ToString();

        Assert.True(BoardsDeepLink.TryParse(text, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task LiveComponent_UpdateRefreshesEveryPlacementAndSurvivesSave()
    {
        var repository = new FakeNotesRepository();
        var boards = new BoardsWorkspaceService(repository);
        var notebook = await boards.CreateNotebookAsync("Shared", CancellationToken.None);
        var section = notebook.Sections[0];
        var secondPage = NotesPage.CreateDefault();
        secondPage.Order = 1;
        secondPage.Title = "Second";
        section.Pages.Add(secondPage);

        var component = boards.AddComponent(notebook, section.Pages[0], BoardsLiveComponentKind.TaskList);
        boards.PlaceComponent(notebook, section.Pages[0], component.Id);
        boards.PlaceComponent(notebook, secondPage, component.Id);
        var item = component.Items[0];

        Assert.True(boards.UpdateComponentItem(notebook, component.Id, item.Id, value =>
        {
            value.Text = "Changed everywhere";
            value.Checked = true;
        }));
        await boards.SaveAsync(notebook, "test", CancellationToken.None);

        foreach (var page in section.Pages)
        {
            var placed = page.Blocks.Single(block =>
                block.Metadata.TryGetValue(BoardsWorkspaceService.ComponentIdKey, out var raw) &&
                raw == component.Id.ToString("D"));
            Assert.Equal("Changed everywhere", placed.List!.Items[0].Text);
            Assert.True(placed.List.Items[0].Checked);
        }

        var reopened = await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        Assert.NotNull(reopened);
        Assert.Equal(component.Id, boards.GetComponents(reopened!)[0].Id);
    }

    [Fact]
    public async Task TypedOperations_CreateHierarchyContentAndActivityTarget()
    {
        var repository = new FakeNotesRepository();
        var boards = new BoardsWorkspaceService(repository);
        var executor = new BoardsOperationExecutor(boards);
        var notebook = await boards.CreateNotebookAsync("Agent board", CancellationToken.None);

        var sectionResult = await executor.ExecuteAsync(
            notebook.Id,
            new BoardsOperation(BoardsOperationKind.AddSection, Text: "Research"),
            CancellationToken.None);
        var pageResult = await executor.ExecuteAsync(
            notebook.Id,
            new BoardsOperation(BoardsOperationKind.AddPage, SectionId: sectionResult.SectionId, Text: "Sources"),
            CancellationToken.None);
        var blockResult = await executor.ExecuteAsync(
            notebook.Id,
            new BoardsOperation(
                BoardsOperationKind.AddBlock,
                SectionId: sectionResult.SectionId,
                PageId: pageResult.PageId,
                Text: "Evidence",
                BlockKind: NotesBlockKind.Heading),
            CancellationToken.None);

        Assert.StartsWith("haven://boards/", blockResult.ActivityTarget, StringComparison.Ordinal);
        Assert.NotNull(blockResult.BlockId);
        var reopened = await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        var section = reopened!.Sections.Single(value => value.Id == sectionResult.SectionId);
        var page = section.Pages.Single(value => value.Id == pageResult.PageId);
        Assert.Contains(page.Blocks, value => value.Id == blockResult.BlockId && value.Kind == NotesBlockKind.Heading);
    }

    [Fact]
    public async Task CancelledToken_IsHonoured()
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => boards.CreateNotebookAsync("Cancelled", cts.Token));
    }

    private sealed class FakeNotesRepository(params NotesDocument[] documents) : INotesRepository
    {
        private readonly List<NotesDocument> _documents = [.. documents];

        public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<NotesDocumentSummary> result = _documents.Select(document =>
                new NotesDocumentSummary(
                    document.Id,
                    document.Title,
                    document.UpdatedAt,
                    document.Version,
                    document.Sections.Count,
                    document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks).Count(),
                    NotesTextStatistics.Calculate(document).Words,
                    document.Recovery.HasUnsavedRecovery)).ToArray();
            return Task.FromResult(result);
        }

        public Task<NotesDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_documents.FirstOrDefault(document => document.Id == documentId));
        }

        public Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = _documents.FindIndex(value => value.Id == document.Id);
            if (index < 0) _documents.Add(document); else _documents[index] = document;
            document.Version++;
            document.UpdatedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(new NotesSaveResult(
                document.Id, document.Version, document.UpdatedAt, "test-hash", "current.json", $"version-{document.Version}.json"));
        }

        public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _documents.RemoveAll(value => value.Id == documentId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid documentId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<NotesVersionInfo>>([]);

        public Task<NotesDocument?> LoadVersionAsync(Guid documentId, string versionId, CancellationToken cancellationToken)
            => LoadAsync(documentId, cancellationToken);

        public Task<NotesDocument?> RecoverLatestAsync(Guid documentId, CancellationToken cancellationToken)
            => LoadAsync(documentId, cancellationToken);

        public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<NotesSearchHit>>([]);
    }
}
