using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Tests;

public sealed class BoardsRecoveryAcceptanceTests
{
    [Fact]
    public async Task LifecycleHierarchyStructuredFreeformPinsAndLiveState_SurviveFreshReload()
    {
        var repository = new CloningNotesRepository();
        var boards = new BoardsWorkspaceService(repository);
        var notebook = await boards.CreateNotebookAsync("Recovery board", TestContext.Current.CancellationToken);
        var stableId = notebook.Id;
        await boards.RenameNotebookAsync(notebook, "Recovered board", TestContext.Current.CancellationToken);
        Assert.Equal(stableId, notebook.Id);
        boards.SetPinned(notebook, true);

        var research = boards.AddSection(notebook, "Research");
        var archive = boards.AddSection(notebook, "Archive");
        boards.MoveSection(notebook, archive.Id, 0);
        var sources = boards.AddPage(notebook, research.Id, "Sources");
        var notes = boards.AddPage(notebook, research.Id, "Notes");
        boards.MovePage(notebook, notes.Id, research.Id, 0);

        var checklist = boards.AddBlock(notebook, notes.Id, NotesBlockKind.List, "Verify source");
        var checklistItem = checklist.List!.Items[0];
        Assert.True(boards.UpdateListItem(notebook, notes.Id, checklist.Id, checklistItem.Id, "Verified source", true));
        var table = boards.AddBlock(notebook, notes.Id, NotesBlockKind.Table);
        var firstCell = table.Table!.Rows[0].Cells[0];
        Assert.True(boards.UpdateTableCell(notebook, notes.Id, table.Id, firstCell.Id, "Evidence"));
        boards.MoveBlock(notebook, notes.Id, table.Id, 0);

        var card = boards.AddCanvasObject(notebook, notes.Id, NotesCanvasObjectKind.Text, "Idea", 40, 60, 280, 160);
        Assert.True(boards.MoveCanvasObject(notebook, notes.Id, card.Id, 420, 360));
        Assert.True(boards.ResizeCanvasObject(notebook, notes.Id, card.Id, 520, 260));
        Assert.True(boards.UpdateCanvasObjectText(notebook, notes.Id, card.Id, "Persisted freeform idea"));
        var component = boards.AddComponent(notebook, notes, BoardsLiveComponentKind.List);
        boards.PlaceComponent(notebook, notes, component.Id);
        boards.PlaceComponent(notebook, sources, component.Id);
        var item = component.Items[0];
        Assert.True(boards.UpdateComponentItem(notebook, component.Id, item.Id, value => value.Text = "Canonical live item"));
        Assert.True(boards.UpdateComponentSource(notebook, component.Id, source =>
        {
            source.Provider = "Test provider";
            source.ResourceId = "resource-42";
            source.DisplayName = "Shared source";
            source.Availability = BoardsLiveAvailability.Stale;
        }));
        await boards.SaveAsync(notebook, "Boards recovery acceptance", TestContext.Current.CancellationToken);

        var reopenedBoards = new BoardsWorkspaceService(repository);
        var reopened = await reopenedBoards.OpenNotebookAsync(stableId, TestContext.Current.CancellationToken);
        Assert.NotNull(reopened);
        Assert.Equal(stableId, reopened!.Id);
        Assert.Equal("Recovered board", reopened.Title);
        Assert.True(reopenedBoards.IsPinned(reopened));
        Assert.Equal(archive.Id, reopened.Sections[0].Id);
        var reopenedResearch = reopened.Sections.Single(value => value.Id == research.Id);
        Assert.Equal(notes.Id, reopenedResearch.Pages.OrderBy(value => value.Order).First().Id);
        var reopenedNotes = reopenedResearch.Pages.Single(value => value.Id == notes.Id);
        Assert.Equal(table.Id, reopenedNotes.Blocks.OrderBy(value => value.Order).First().Id);
        var reopenedChecklist = reopenedNotes.Blocks.Single(value => value.Id == checklist.Id);
        Assert.Equal("Verified source", reopenedChecklist.List!.Items[0].Text);
        Assert.True(reopenedChecklist.List.Items[0].Checked);
        Assert.Equal("Evidence", reopenedNotes.Blocks.Single(value => value.Id == table.Id).Table!.Rows[0].Cells[0].Text);
        var reopenedCard = reopenedNotes.CanvasObjects.Single(value => value.Id == card.Id);
        Assert.Equal(420, reopenedCard.X);
        Assert.Equal(360, reopenedCard.Y);
        Assert.Equal(520, reopenedCard.Width);
        Assert.Equal(260, reopenedCard.Height);
        Assert.Equal("Persisted freeform idea", reopenedCard.Text);
        var reopenedComponent = reopenedBoards.GetComponents(reopened).Single(value => value.Id == component.Id);
        Assert.Equal("Canonical live item", reopenedComponent.Items[0].Text);
        Assert.Equal("Test provider", reopenedComponent.Source.Provider);
        Assert.Equal("resource-42", reopenedComponent.Source.ResourceId);
        Assert.Equal(BoardsLiveAvailability.Stale, reopenedComponent.Source.Availability);
        Assert.Equal(2, reopenedBoards.GetPlacements(reopened).Count(value => value.ComponentId == component.Id));
    }
    [Fact]
    public async Task AttachmentReference_SurvivesReloadAndTruthfullyReportsMissingManagedFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "haven-boards-attachments-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.txt");
            await File.WriteAllTextAsync(source, "attachment payload", TestContext.Current.CancellationToken);
            var store = new TestAttachmentStore(Path.Combine(root, "managed"));
            var repository = new CloningNotesRepository();
            var boards = new BoardsWorkspaceService(repository, store);
            var notebook = await boards.CreateNotebookAsync("Attachments", TestContext.Current.CancellationToken);
            var page = notebook.Sections[0].Pages[0];
            var block = await boards.AttachAsync(notebook, page.Id, source, TestContext.Current.CancellationToken);
            await boards.SaveAsync(notebook, "Attach file", TestContext.Current.CancellationToken);

            var reopenedBoards = new BoardsWorkspaceService(repository, store);
            var reopened = await reopenedBoards.OpenNotebookAsync(notebook.Id, TestContext.Current.CancellationToken);
            var media = reopened!.Sections[0].Pages[0].Blocks.Single(value => value.Id == block.Id).Media!;
            var available = await reopenedBoards.ResolveAttachmentAsync(media, TestContext.Current.CancellationToken);
            Assert.Equal(BoardsAttachmentStatus.Available, available.Status);
            Assert.NotNull(available.ResolvedPath);

            File.Delete(available.ResolvedPath!);
            var missing = await reopenedBoards.ResolveAttachmentAsync(media, TestContext.Current.CancellationToken);
            Assert.Equal(BoardsAttachmentStatus.Missing, missing.Status);
            Assert.Null(missing.ResolvedPath);
            Assert.Contains("missing", missing.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LargeBoard_PersistsHundredsOfStructuredAndFreeformObjects()
    {
        var repository = new CloningNotesRepository();
        var boards = new BoardsWorkspaceService(repository);
        var notebook = await boards.CreateNotebookAsync("Large board", TestContext.Current.CancellationToken);
        var page = notebook.Sections[0].Pages[0];
        for (var i = 0; i < 600; i++)
            boards.AddBlock(notebook, page.Id, NotesBlockKind.Paragraph, $"Block {i}");
        for (var i = 0; i < 300; i++)
            boards.AddCanvasObject(notebook, page.Id, NotesCanvasObjectKind.Text, $"Card {i}", (i % 20) * 160, (i / 20) * 110, 150, 100);
        await boards.SaveAsync(notebook, "Large board acceptance", TestContext.Current.CancellationToken);
        var reopened = await new BoardsWorkspaceService(repository).OpenNotebookAsync(notebook.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(reopened);
        var reopenedPage = reopened!.Sections[0].Pages[0];
        Assert.True(reopenedPage.Blocks.Count >= 602);
        Assert.Equal(300, reopenedPage.CanvasObjects.Count);
        Assert.True(reopenedPage.CanvasWidth > 1800);
        Assert.True(reopenedPage.CanvasHeight > 1200);
    }
    private sealed class CloningNotesRepository : INotesRepository
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private readonly Dictionary<Guid, string> _documents = [];

        public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<NotesDocumentSummary> result = _documents.Values.Select(Deserialize).Select(document =>
                new NotesDocumentSummary(
                    document.Id, document.Title, document.UpdatedAt, document.Version, document.Sections.Count,
                    document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks).Count(),
                    NotesTextStatistics.Calculate(document).Words, document.Recovery.HasUnsavedRecovery)).ToArray();
            return Task.FromResult(result);
        }

        public Task<NotesDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_documents.TryGetValue(documentId, out var json) ? Deserialize(json) : null);
        }

        public Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            document.Version++;
            document.UpdatedAt = DateTimeOffset.UtcNow;
            _documents[document.Id] = JsonSerializer.Serialize(document, Json);
            return Task.FromResult(new NotesSaveResult(
                document.Id, document.Version, document.UpdatedAt, "clone-hash", "current.json", $"version-{document.Version}.json"));
        }

        public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _documents.Remove(documentId);
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

        private static NotesDocument Deserialize(string json)
            => JsonSerializer.Deserialize<NotesDocument>(json, Json) ?? throw new InvalidDataException("Could not clone NotesDocument.");
    }
    private sealed class TestAttachmentStore(string root) : INotesAttachmentStore
    {
        private readonly Dictionary<Guid, string> _paths = [];

        public async Task<NotesMediaData> ImportAsync(string sourcePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(root);
            var id = Guid.NewGuid();
            var destination = Path.Combine(root, id.ToString("N") + Path.GetExtension(sourcePath));
            await using (var source = File.OpenRead(sourcePath))
            await using (var target = File.Create(destination))
                await source.CopyToAsync(target, cancellationToken);
            _paths[id] = destination;
            var info = new FileInfo(destination);
            return new NotesMediaData
            {
                AttachmentId = id,
                OriginalName = Path.GetFileName(sourcePath),
                StoredPath = destination,
                MediaType = "application/octet-stream",
                SizeBytes = info.Length
            };
        }

        public Task<string> ResolvePathAsync(Guid attachmentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_paths.TryGetValue(attachmentId, out var path))
                throw new FileNotFoundException("Attachment reference was not found.");
            return Task.FromResult(path);
        }

        public Task DeleteUnreferencedAsync(IReadOnlyCollection<Guid> referencedAttachmentIds, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var pair in _paths.ToArray())
            {
                if (referencedAttachmentIds.Contains(pair.Key)) continue;
                if (File.Exists(pair.Value)) File.Delete(pair.Value);
                _paths.Remove(pair.Key);
            }
            return Task.CompletedTask;
        }
    }
}
