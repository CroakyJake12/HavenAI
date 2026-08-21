using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Write;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class WritePageTests
{
    [AvaloniaFact]
    public void Write_scene_uses_haven_inputs_and_preserves_rich_blocks()
    {
        using var scene = new WriteHavenScene();
        var document = NotesDocument.Create("Coursework");
        var page = document.Sections[0].Pages[0];
        var paragraph = NotesBlock.CreateParagraph("Draft paragraph");
        paragraph.Order = 0;
        var table = NotesBlock.TableBlock(2, 2);
        table.Order = 1;
        var canvas = NotesBlock.CanvasBlock();
        canvas.Order = 2;
        page.Blocks = [paragraph, table, canvas];

        scene.SetDocument(document, 0, 1);

        Assert.Equal("Coursework", scene.TitleInput.Text);
        Assert.Single(scene.BlockInputs);
        Assert.True(scene.BlockInputs.ContainsKey(paragraph.Id));
        Assert.Contains(
            scene.Root.DescendantsAndSelf(),
            element => element.Name == $"Write.Block.{table.Id:N}.Preserved");
        Assert.Contains(
            scene.Root.DescendantsAndSelf(),
            element => element.Name == $"Write.Block.{canvas.Id:N}.Preserved");
        Assert.DoesNotContain(scene.Root.DescendantsAndSelf(), element => element is Video or Web);
        Assert.Equal(HavenAccessibleRole.Input, scene.TitleInput.Accessibility.Role);
        Assert.Equal(HavenAccessibleRole.Input, scene.BlockInputs[paragraph.Id].Accessibility.Role);
    }

    [AvaloniaFact]
    public async Task Write_page_edits_and_saves_actual_document()
    {
        var document = NotesDocument.Create("Initial title");
        var paragraph = document.Sections[0].Pages[0].Blocks[0];
        paragraph.PlainText = "Bold italic";
        paragraph.Runs =
        [
            new NotesTextRun { Text = "Bold ", Bold = true },
            new NotesTextRun { Text = "italic", Italic = true }
        ];
        var table = NotesBlock.TableBlock(2, 2);
        table.Order = 1;
        document.Sections[0].Pages[0].Blocks.Add(table);
        var repository = new FakeNotesRepository(document);
        using var writePage = new WritePage(new HavenEventBus(), repository, new FakeNotesFormats());

        await writePage.InitializeAsync();
        var window = new Window { Width = 1100, Height = 800, Content = writePage };
        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.Same(writePage.SceneRoot, writePage.SceneHost.Root);
            Assert.Single(writePage.SceneHost.Children);
            Assert.Equal(document.Id, writePage.Document?.Id);
            Assert.Empty(writePage.Route.BlockInputs);
            Assert.Contains(writePage.Route.DocumentSurface, writePage.SceneRoot.DescendantsAndSelf());
            Assert.Equal(HavenAccessibleRole.Input, writePage.Route.DocumentSurface.Accessibility.Role);
            Assert.True(writePage.Route.DocumentSurface.Accessibility.Focusable);
            Assert.Equal("Document editor", writePage.Route.DocumentSurface.Accessibility.AccessibleName);
            Assert.False(string.IsNullOrWhiteSpace(writePage.Route.DocumentSurface.Accessibility.Description));

            writePage.Route.TitleInput.Text = "Results Day brief";
            var router = new HavenInputRouter(writePage.SceneRoot);
            router.Focus(writePage.Route.DocumentSurface);
            for (var index = 0; index < 5; index++)
                Assert.True(router.KeyDown(HavenKey.Right, new HavenInputModifiers()));
            Assert.True(router.TextInput("stronger "));

            Assert.True(writePage.IsDirty);
            Assert.Equal("Results Day brief", writePage.Document?.Title);
            Assert.Equal("Bold stronger italic", paragraph.PlainText);
            Assert.Equal("Bold stronger ", paragraph.Runs[0].Text);
            Assert.True(paragraph.Runs[0].Bold);
            Assert.Equal("italic", paragraph.Runs[1].Text);
            Assert.True(paragraph.Runs[1].Italic);
            Assert.Same(table, writePage.Document?.Sections[0].Pages[0].Blocks[1]);

            Assert.True(await writePage.SaveAsync("Focused test"));
            Assert.False(writePage.IsDirty);
            Assert.Equal(1, repository.SaveCalls);
            Assert.Equal("Results Day brief", repository.LastSaved?.Title);
            Assert.Equal("Bold stronger italic", repository.LastSaved?.Sections[0].Pages[0].Blocks[0].PlainText);
            Assert.Same(table, repository.LastSaved?.Sections[0].Pages[0].Blocks[1]);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Write_page_creates_and_persists_document_when_repository_is_empty()
    {
        var repository = new FakeNotesRepository();
        using var writePage = new WritePage(new HavenEventBus(), repository, new FakeNotesFormats());

        await writePage.InitializeAsync();

        Assert.NotNull(writePage.Document);
        Assert.Equal("Untitled document", writePage.Document!.Title);
        Assert.Equal("Untitled document", writePage.Route.TitleInput.Text);
        Assert.False(writePage.IsDirty);
        Assert.Equal(1, repository.SaveCalls);
        Assert.Single(await repository.ListAsync(CancellationToken.None));
        Assert.Equal(writePage.Document.Id, repository.LastSaved?.Id);
    }

    [AvaloniaFact]
    public async Task Write_page_keeps_dirty_document_when_save_fails()
    {
        var document = NotesDocument.Create("Failure safety");
        var repository = new FakeNotesRepository(document) { FailSaves = true };
        using var writePage = new WritePage(new HavenEventBus(), repository, new FakeNotesFormats());

        await writePage.InitializeAsync();
        writePage.Route.TitleInput.Text = "Still unsaved";

        var saved = await writePage.SaveAsync("Expected failure");

        Assert.False(saved);
        Assert.True(writePage.IsDirty);
        Assert.Equal("Still unsaved", writePage.Document?.Title);
        Assert.Contains("Couldn't save this document", writePage.Route.StatusText.Content, StringComparison.Ordinal);
        Assert.Equal(0, repository.SaveCalls);
    }

    [AvaloniaFact]
    public async Task Write_page_saves_dirty_document_when_detached()
    {
        var document = NotesDocument.Create("Detach save");
        var repository = new FakeNotesRepository(document);
        using var writePage = new WritePage(new HavenEventBus(), repository, new FakeNotesFormats());
        await writePage.InitializeAsync();
        var window = new Window { Width = 900, Height = 700, Content = writePage };
        try
        {
            window.Show();
            window.UpdateLayout();
            writePage.Route.TitleInput.Text = "Persisted title";
            Assert.True(writePage.IsDirty);
            window.Content = null;
            await Task.Yield();
            Assert.False(writePage.IsDirty);
            Assert.Equal(1, repository.SaveCalls);
            Assert.Equal("Persisted title", repository.LastSaved?.Title);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Write_page_imports_and_exports_through_document_format_service()
    {
        var original = NotesDocument.Create("Original");
        var imported = NotesDocument.Create("Imported coursework");
        imported.Sections[0].Pages[0].Blocks[0].PlainText = "Imported body";
        var repository = new FakeNotesRepository(original);
        var formats = new FakeNotesFormats { ImportedDocument = imported };
        using var writePage = new WritePage(new HavenEventBus(), repository, formats);

        await writePage.InitializeAsync();
        var sourcePath = Path.Combine(Path.GetTempPath(), "coursework.docx");
        Assert.True(await writePage.ImportFromPathAsync(sourcePath));

        Assert.Equal(sourcePath, formats.ImportPath);
        Assert.Equal(imported.Id, writePage.Document?.Id);
        Assert.Equal("Imported coursework", writePage.Route.TitleInput.Text);
        Assert.False(writePage.IsDirty);
        Assert.Same(imported, repository.LastSaved);

        var destinationPath = Path.Combine(Path.GetTempPath(), "coursework-export.docx");
        Assert.True(await writePage.ExportToPathAsync(destinationPath));

        Assert.Equal(destinationPath, formats.ExportPath);
        Assert.Same(imported, formats.ExportedDocument);
        Assert.Contains("Exported", writePage.Route.StatusText.Content, StringComparison.Ordinal);
    }

    private sealed class FakeNotesFormats : INotesImportExportService
    {
        public IReadOnlyList<string> ImportExtensions { get; } = [".docx", ".md", ".html"];
        public IReadOnlyList<string> ExportExtensions { get; } = [".docx", ".pdf", ".html", ".md"];
        public NotesDocument ImportedDocument { get; set; } = NotesDocument.Create("Imported");
        public string? ImportPath { get; private set; }
        public string? ExportPath { get; private set; }
        public NotesDocument? ExportedDocument { get; private set; }

        public Task<NotesDocument> ImportAsync(string sourcePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImportPath = sourcePath;
            return Task.FromResult(ImportedDocument);
        }

        public Task<string> ExportAsync(
            NotesDocument document,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExportedDocument = document;
            ExportPath = destinationPath;
            return Task.FromResult(destinationPath);
        }

        public Task PrintAsync(NotesDocument document, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNotesRepository(params NotesDocument[] documents) : INotesRepository
    {
        private readonly List<NotesDocument> _documents = [.. documents];

        public int SaveCalls { get; private set; }
        public NotesDocument? LastSaved { get; private set; }
        public bool FailSaves { get; set; }

        public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<NotesDocumentSummary> summaries = _documents
                .OrderByDescending(document => document.UpdatedAt)
                .Select(document => new NotesDocumentSummary(
                    document.Id,
                    document.Title,
                    document.UpdatedAt,
                    document.Version,
                    document.Sections.Count,
                    document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks).Count(),
                    NotesTextStatistics.Calculate(document).Words,
                    document.Recovery.HasUnsavedRecovery))
                .ToArray();
            return Task.FromResult(summaries);
        }

        public Task<NotesDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_documents.FirstOrDefault(document => document.Id == documentId));
        }

        public Task<NotesSaveResult> SaveAsync(
            NotesDocument document,
            string reason,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailSaves)
                throw new IOException("Synthetic save failure");

            SaveCalls++;
            LastSaved = document;
            var existing = _documents.FindIndex(candidate => candidate.Id == document.Id);
            if (existing < 0)
                _documents.Add(document);
            else
                _documents[existing] = document;

            var version = document.Version + 1;
            return Task.FromResult(new NotesSaveResult(
                document.Id,
                version,
                DateTimeOffset.UtcNow,
                "test-hash",
                "current.json",
                $"version-{version}.json"));
        }

        public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _documents.RemoveAll(document => document.Id == documentId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(
            Guid documentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<NotesVersionInfo>>([]);
        }

        public Task<NotesDocument?> LoadVersionAsync(
            Guid documentId,
            string versionId,
            CancellationToken cancellationToken) =>
            LoadAsync(documentId, cancellationToken);

        public Task<NotesDocument?> RecoverLatestAsync(
            Guid documentId,
            CancellationToken cancellationToken) =>
            LoadAsync(documentId, cancellationToken);

        public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(
            string query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<NotesSearchHit>>([]);
        }
    }
}
