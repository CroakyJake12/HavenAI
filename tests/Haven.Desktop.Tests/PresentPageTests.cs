using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Present;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class PresentPageTests
{
    [AvaloniaFact]
    public void Present_scene_uses_haven_inputs_and_reports_preserved_rich_content()
    {
        using var scene = new PresentHavenScene();
        var document = PresentDocument.Create("Coursework deck");
        document.Slides[0].Title = "Evidence";
        document.Slides[0].GetOrCreateBodyText().Text = "Main point";
        document.Slides[0].Elements.Add(new PresentElement
        {
            Kind = PresentElementKind.GenUi, Order = 1, AlternativeText = "Interactive chart", GenUiMarkup = "<Chart />"
        });

        scene.SetDocument(document, 0, 1, 0);

        Assert.Equal("Coursework deck", scene.DeckTitleInput.Text);
        Assert.Equal("Evidence", scene.SlideTitleInput.Text);
        Assert.Equal("Main point", scene.BodyInput.Text);
        Assert.Contains("1 richer Haven element", scene.RichContentText.Content, StringComparison.Ordinal);
        Assert.Equal(HavenAccessibleRole.Input, scene.DeckTitleInput.Accessibility.Role);
        Assert.Equal(HavenAccessibleRole.Input, scene.SlideTitleInput.Accessibility.Role);
        Assert.Equal(HavenAccessibleRole.Input, scene.BodyInput.Accessibility.Role);
        Assert.DoesNotContain(scene.Root.DescendantsAndSelf(), element => element is Video or Web);
    }

    [AvaloniaFact]
    public async Task Present_page_edits_slide_actions_saves_and_exports_real_state()
    {
        var document = PresentDocument.Create("Initial deck");
        var preserved = new PresentElement { Kind = PresentElementKind.GenUi, Order = 1, AlternativeText = "Chart", GenUiMarkup = "<Chart />" };
        document.Slides[0].Elements.Add(preserved);
        var repository = new FakePresentRepository(document);
        var exporter = new FakePresentExporter();
        using var page = new PresentPage(new HavenEventBus(), repository, exporter);
        await page.InitializeAsync();
        var window = new Window { Width = 1200, Height = 850, Content = page };
        try
        {
            window.Show(); window.UpdateLayout();
            var router = new HavenInputRouter(page.SceneRoot);
            Assert.Same(page.SceneRoot, page.SceneHost.Root);
            Assert.Single(page.SceneHost.Children);

            page.Route.DeckTitleInput.Text = "Results deck";
            page.Route.SlideTitleInput.Text = "Opening result";
            page.Route.BodyInput.Text = "Key finding";
            page.Route.NotesInput.Text = "Explain the evidence";
            Assert.True(page.IsDirty);
            Assert.Equal("Key finding", page.Document!.Slides[0].GetOrCreateBodyText().Text);

            Click(router, page.Route.AddSlideButton);
            Assert.Equal(2, page.Document.Slides.Count);
            Assert.Equal("Slide 2", page.Route.SlideTitleInput.Text);
            page.Route.SlideTitleInput.Text = "Temporary second slide";
            Click(router, page.Route.DeleteSlideButton);
            Assert.Single(page.Document.Slides);
            Assert.Same(preserved, page.Document.Slides[0].Elements[1]);

            Assert.True(await page.SaveAsync("Focused test"));
            Assert.False(page.IsDirty);
            Assert.Equal(1, repository.SaveCalls);
            Assert.Equal("Results deck", repository.LastSaved?.Title);
            Assert.Equal("Opening result", repository.LastSaved?.Slides[0].Title);
            Assert.Equal("Explain the evidence", repository.LastSaved?.Slides[0].SpeakerNotes);

            var destination = Path.Combine(Path.GetTempPath(), "present-focused.pptx");
            Assert.True(await page.ExportToPathAsync(destination));
            Assert.Equal(destination, exporter.LastPath);
            Assert.Same(page.Document, exporter.LastDocument);
        }
        finally { window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Present_page_creates_first_deck_and_saves_dirty_state_on_detach()
    {
        var repository = new FakePresentRepository();
        using var page = new PresentPage(new HavenEventBus(), repository, new FakePresentExporter());
        await page.InitializeAsync();
        Assert.NotNull(page.Document);
        Assert.Equal(1, repository.SaveCalls);
        var window = new Window { Width = 1000, Height = 760, Content = page };
        try
        {
            window.Show(); window.UpdateLayout();
            page.Route.DeckTitleInput.Text = "Persist before leaving";
            page.Route.BodyInput.Text = "Saved content";
            Assert.True(page.IsDirty);
            window.Content = null; await Task.Yield();
            Assert.False(page.IsDirty);
            Assert.Equal(2, repository.SaveCalls);
            Assert.Equal("Persist before leaving", repository.LastSaved?.Title);
        }
        finally { window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Present_page_keeps_dirty_state_when_save_fails()
    {
        var document = PresentDocument.Create("Failure test");
        var repository = new FakePresentRepository(document) { FailSaves = true };
        using var page = new PresentPage(new HavenEventBus(), repository, new FakePresentExporter());
        await page.InitializeAsync();
        page.Route.DeckTitleInput.Text = "Unsaved edit";

        var saved = await page.SaveAsync("Expected failure");

        Assert.False(saved); Assert.True(page.IsDirty);
        Assert.Equal("Unsaved edit", page.Document?.Title);
        Assert.Contains("Couldn’t save", page.Route.StatusText.Content, StringComparison.Ordinal);
        Assert.Equal(0, repository.SaveCalls);
    }

    private static void Click(HavenInputRouter router, HavenElement element)
    {
        var point = new HavenPoint(
            element.Bounds.X + element.Bounds.Width / 2,
            element.Bounds.Y + element.Bounds.Height / 2);
        router.PointerPressed(point);
        Assert.True(router.PointerReleased(point));
    }

    private sealed class FakePresentRepository(params PresentDocument[] documents) : IPresentRepository
    {
        private readonly List<PresentDocument> _documents = [.. documents];
        public int SaveCalls { get; private set; }
        public PresentDocument? LastSaved { get; private set; }
        public bool FailSaves { get; set; }

        public Task<IReadOnlyList<PresentDocumentSummary>> ListAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<PresentDocumentSummary> result = _documents
                .OrderByDescending(x => x.UpdatedAt)
                .Select(x => new PresentDocumentSummary(x.Id, x.Title, x.UpdatedAt, x.Version, x.Slides.Count, x.Recovery.RecoveredFromBackup))
                .ToArray();
            return Task.FromResult(result);
        }
        public Task<PresentDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_documents.FirstOrDefault(x => x.Id == documentId));
        }
        public Task<PresentSaveResult> SaveAsync(PresentDocument document, string reason, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailSaves) throw new IOException("Synthetic save failure");
            SaveCalls++; document.Normalize(); document.Version++; LastSaved = document;
            var index = _documents.FindIndex(x => x.Id == document.Id);
            if (index < 0) _documents.Add(document); else _documents[index] = document;
            var root = Path.Combine(Path.GetTempPath(), "present-fake");
            return Task.FromResult(new PresentSaveResult(document.Id, document.Version, DateTimeOffset.UtcNow, Path.Combine(root, "current.json"), Path.Combine(root, "previous.json")));
        }
        public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); _documents.RemoveAll(x => x.Id == documentId); return Task.CompletedTask;
        }
    }

    private sealed class FakePresentExporter : IPresentExportService
    {
        public IReadOnlyList<string> ExportExtensions { get; } = [".pptx"];
        public string? LastPath { get; private set; }
        public PresentDocument? LastDocument { get; private set; }
        public Task<string> ExportAsync(PresentDocument document, string destinationPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); LastDocument = document; LastPath = destinationPath; return Task.FromResult(destinationPath);
        }
    }
}
