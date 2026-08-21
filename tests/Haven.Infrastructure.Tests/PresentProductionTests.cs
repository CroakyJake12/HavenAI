using System.IO.Compression;
using System.Xml.Linq;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure.Tests;

public sealed class PresentProductionTests : IDisposable
{
    private readonly PresentTestPaths _paths = new();

    [Fact]
    public void Infrastructure_registers_present_repository_and_exporter()
    {
        var services = new ServiceCollection();
        services.AddHavenInfrastructure();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<PresentRepository>(provider.GetRequiredService<IPresentRepository>());
        Assert.IsType<PresentPptxExportService>(provider.GetRequiredService<IPresentExportService>());
    }

    [Fact]
    public void Editor_transforms_multi_selection_atomically_and_undoes_the_direct_gesture()
    {
        var document = PresentDocument.Create("Direct manipulation");
        var editor = new PresentEditor(document);
        var slideId = document.Slides[0].Id;
        var first = editor.AddShape(slideId);
        var second = editor.AddText(slideId, "Caption");
        first.X = .10; first.Y = .10; first.Width = .20; first.Height = .20;
        second.X = .40; second.Y = .35; second.Width = .25; second.Height = .15;
        editor.SelectElements([first.Id, second.Id]);

        Assert.True(editor.TransformSelection(.05, .03, .04, .02, 15));
        Assert.Equal(.15, first.X, 3);
        Assert.Equal(.13, first.Y, 3);
        Assert.Equal(.24, first.Width, 3);
        Assert.Equal(.22, first.Height, 3);
        Assert.Equal(15, first.RotationDegrees, 3);
        Assert.Equal(.45, second.X, 3);
        Assert.Equal(15, second.RotationDegrees, 3);

        Assert.True(editor.Undo());
        var restoredFirst = editor.SelectedSlide.Elements.Single(element => element.Id == first.Id);
        var restoredSecond = editor.SelectedSlide.Elements.Single(element => element.Id == second.Id);
        Assert.Equal(.10, restoredFirst.X, 3);
        Assert.Equal(.40, restoredSecond.X, 3);
        Assert.Equal(0, restoredFirst.RotationDegrees, 3);
        Assert.Equal(0, restoredSecond.RotationDegrees, 3);
    }

    [Fact]
    public async Task Repository_round_trip_preserves_slides_notes_and_haven_elements()
    {
        var repository = new PresentRepository(_paths);
        var document = PresentDocument.Create("Results presentation");
        var first = document.Slides[0];
        first.Title = "Opening";
        first.GetOrCreateBodyText().Text = "Key result";
        first.SpeakerNotes = "Explain the context.";
        first.Elements.Add(new PresentElement
        {
            Kind = PresentElementKind.GenUi, Order = 1,
            AlternativeText = "Interactive result card",
            GenUiMarkup = "<Card Name=\"Result\" />",
            X = 0.55, Y = 0.30, Width = 0.35, Height = 0.45
        });
        var second = PresentSlide.Create(1);
        second.Title = "Next steps";
        second.GetOrCreateBodyText().Text = "Follow-up actions";
        document.Slides.Add(second);

        var saved = await repository.SaveAsync(document, "Initial deck", CancellationToken.None);
        var loaded = await repository.LoadAsync(document.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(1, saved.Version);
        Assert.Equal(2, loaded!.Slides.Count);
        Assert.Equal("Opening", loaded.Slides[0].Title);
        Assert.Equal("Key result", loaded.Slides[0].GetOrCreateBodyText().Text);
        Assert.Equal("Explain the context.", loaded.Slides[0].SpeakerNotes);
        var genUi = Assert.Single(loaded.Slides[0].Elements, x => x.Kind == PresentElementKind.GenUi);
        Assert.Equal("<Card Name=\"Result\" />", genUi.GenUiMarkup);
        Assert.Equal(0.55, genUi.X, 3);
        Assert.Equal("Next steps", loaded.Slides[1].Title);
        Assert.True(File.Exists(saved.CurrentPath));
    }

    [Fact]
    public async Task Repository_recovers_previous_valid_deck_and_preserves_backup_when_recommitted()
    {
        var repository = new PresentRepository(_paths);
        var document = PresentDocument.Create("Version one");
        document.Slides[0].GetOrCreateBodyText().Text = "First body";
        _ = await repository.SaveAsync(document, "First", CancellationToken.None);
        document.Title = "Version two";
        document.Slides[0].GetOrCreateBodyText().Text = "Second body";
        var second = await repository.SaveAsync(document, "Second", CancellationToken.None);
        var backupBefore = await File.ReadAllTextAsync(second.BackupPath, CancellationToken.None);

        await File.WriteAllTextAsync(second.CurrentPath, "{ unreadable json", CancellationToken.None);
        var recovered = await repository.LoadAsync(document.Id, CancellationToken.None);

        Assert.NotNull(recovered);
        Assert.True(recovered!.Recovery.RecoveredFromBackup);
        Assert.Equal("Version one", recovered.Title);
        Assert.Equal("First body", recovered.Slides[0].GetOrCreateBodyText().Text);

        recovered.Title = "Recovered edit";
        var saved = await repository.SaveAsync(recovered, "Recovery confirmed", CancellationToken.None);
        Assert.Equal(backupBefore, await File.ReadAllTextAsync(saved.BackupPath, CancellationToken.None));
        Assert.Contains(Directory.EnumerateFiles(Path.GetDirectoryName(saved.CurrentPath)!, "unreadable-current-*.json"), File.Exists);
        var reopened = await repository.LoadAsync(document.Id, CancellationToken.None);
        Assert.NotNull(reopened);
        Assert.Equal("Recovered edit", reopened!.Title);
        Assert.False(reopened.Recovery.RecoveredFromBackup);
    }

    [Fact]
    public async Task Pptx_export_writes_real_parts_and_truthful_haven_fallbacks()
    {
        var document = PresentDocument.Create("Export deck");
        var first = document.Slides[0];
        first.Title = "Opening & evidence";
        first.GetOrCreateBodyText().Text = "First line\nSecond line";
        first.Elements.Add(new PresentElement
        {
            Kind = PresentElementKind.GenUi, Order = 1,
            AlternativeText = "Interactive chart", GenUiMarkup = "<Chart />"
        });
        first.Elements.Add(new PresentElement
        {
            Kind = PresentElementKind.Image, Order = 2,
            AlternativeText = "Results photograph", AssetId = "asset-1"
        });
        var second = PresentSlide.Create(1);
        second.Title = "Conclusion";
        second.GetOrCreateBodyText().Text = "Finish clearly";
        document.Slides.Add(second);
        var destination = Path.Combine(_paths.DataDirectory, "deck.pptx");

        var exported = await new PresentPptxExportService()
            .ExportAsync(document, destination, CancellationToken.None);

        Assert.Equal(destination, exported, ignoreCase: true);
        await using var file = File.OpenRead(exported);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var part in new[] { "[Content_Types].xml", "ppt/presentation.xml",
            "ppt/slides/slide1.xml", "ppt/slides/slide2.xml",
            "ppt/slideMasters/slideMaster1.xml", "ppt/slideLayouts/slideLayout1.xml",
            "ppt/theme/theme1.xml" })
            Assert.NotNull(archive.GetEntry(part));

        var presentation = ReadXml(archive, "ppt/presentation.xml");
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var slideIds = presentation.Descendants(p + "sldId").ToArray();
        Assert.Equal(2, slideIds.Length);
        Assert.Equal("rId2", slideIds[0].Attribute(r + "id")?.Value);
        Assert.Equal("rId3", slideIds[1].Attribute(r + "id")?.Value);

        var slide = ReadXml(archive, "ppt/slides/slide1.xml");
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var text = string.Join(" | ", slide.Descendants(a + "t").Select(x => x.Value));
        Assert.Contains("Opening & evidence", text, StringComparison.Ordinal);
        Assert.Contains("First line", text, StringComparison.Ordinal);
        Assert.Contains("Second line", text, StringComparison.Ordinal);
        Assert.Contains("[Interactive Haven content — open in Haven to use] Interactive chart", text, StringComparison.Ordinal);
        Assert.Contains("[Image preserved in Haven] Results photograph", text, StringComparison.Ordinal);

        foreach (var entry in archive.Entries.Where(x => x.FullName.EndsWith(".xml", StringComparison.Ordinal)
            || x.FullName.EndsWith(".rels", StringComparison.Ordinal)))
        {
            using var stream = entry.Open();
            _ = XDocument.Load(stream);
        }
    }

    private static XDocument ReadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidDataException($"Missing {path}");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    public void Dispose() => _paths.Dispose();

    private sealed class PresentTestPaths : IAppPaths, IDisposable
    {
        public PresentTestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-present-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
        }

        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
