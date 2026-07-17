using System.Diagnostics;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class NotesStressTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task LargeMixedDocumentValidatesSavesLoadsAndSearchesWithinProductionBounds()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var validator = new NotesDocumentValidator();
        var repository = new NotesRepository(_paths, validator, diagnostics);
        var document = BuildLargeDocument();
        var stopwatch = Stopwatch.StartNew();

        var validation = validator.Validate(document);
        Assert.True(validation.IsValid, string.Join(" | ", validation.Issues.Where(issue => issue.IsError).Take(12).Select(issue => issue.Path + ": " + issue.Message)));

        var saved = await repository.SaveAsync(document, "Large-document stress fixture", CancellationToken.None);
        var loaded = await repository.LoadAsync(document.Id, CancellationToken.None);
        var hits = await repository.SearchAsync("stress-target-9876", CancellationToken.None);
        stopwatch.Stop();

        Assert.NotNull(loaded);
        Assert.Equal(10_000, loaded!.Sections.SelectMany(section => section.Pages).Sum(page => page.Blocks.Count));
        Assert.Equal(25_000, loaded.Sections[0].Pages[0].Blocks.Single(block => block.Canvas is not null).Canvas!.Strokes[0].Points.Count);
        Assert.Single(hits);
        Assert.Equal(document.Id, hits[0].DocumentId);
        Assert.True(new FileInfo(saved.CurrentPath).Length > 1_000_000);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(60), $"Large Notes round trip took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task CancelledLargeSearchStopsWithoutChangingPersistedDocument()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var validator = new NotesDocumentValidator();
        var repository = new NotesRepository(_paths, validator, diagnostics);
        var document = BuildLargeDocument();
        await repository.SaveAsync(document, "Cancellation fixture", CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.SearchAsync("paragraph", cancellation.Token));

        var loaded = await repository.LoadAsync(document.Id, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(document.Version, loaded!.Version);
        Assert.False(loaded.Recovery.HasUnsavedRecovery);
    }

    public void Dispose() => _paths.Dispose();

    private static NotesDocument BuildLargeDocument()
    {
        var document = NotesDocument.Create("Large mixed Notes stress document");
        document.Sections.Clear();
        var globalIndex = 0;
        for (var sectionIndex = 0; sectionIndex < 10; sectionIndex++)
        {
            var section = new NotesSection
            {
                Title = "Section " + (sectionIndex + 1)
            };
            for (var pageIndex = 0; pageIndex < 10; pageIndex++)
            {
                var page = new NotesPage
                {
                    Title = "Page " + (pageIndex + 1),
                    Order = pageIndex,
                    CanvasWidth = 2400,
                    CanvasHeight = 1800
                };
                for (var blockIndex = 0; blockIndex < 100; blockIndex++)
                {
                    var marker = globalIndex == 9876 ? " stress-target-9876" : string.Empty;
                    var block = NotesBlock.Paragraph($"Paragraph {globalIndex} contains deterministic stress content for validation and search.{marker}");
                    block.Order = blockIndex;
                    page.Blocks.Add(block);
                    globalIndex++;
                }
                section.Pages.Add(page);
            }
            document.Sections.Add(section);
        }

        var canvasBlock = NotesBlock.CanvasBlock();
        canvasBlock.Order = 0;
        canvasBlock.Canvas!.Width = 20_000;
        canvasBlock.Canvas.Height = 20_000;
        canvasBlock.Canvas.Strokes.Add(new NotesInkStroke
        {
            Tool = "pen",
            Points = Enumerable.Range(0, 25_000)
                .Select(index => new NotesInkPoint
                {
                    X = index % 500,
                    Y = index / 500,
                    Pressure = 0.25 + index % 75 / 100d,
                    TiltX = index % 45,
                    TiltY = -(index % 45),
                    TimestampMilliseconds = index
                })
                .ToList()
        });
        var firstPage = document.Sections[0].Pages[0];
        for (var index = 0; index < firstPage.Blocks.Count; index++)
            firstPage.Blocks[index].Order = index + 1;
        firstPage.Blocks.Insert(0, canvasBlock);
        return document;
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-notes-stress-tests-" + Guid.NewGuid().ToString("N"));
            DatabasePath = Path.Combine(DataDirectory, "haven.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(LogsDirectory);
        }

        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }

        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
