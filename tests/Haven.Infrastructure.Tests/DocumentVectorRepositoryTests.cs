using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class DocumentVectorRepositoryTests : IDisposable
{
    private readonly VectorTestPaths _paths = new();

    [Fact]
    public async Task Write_shape_survives_real_notes_repository_save_and_reopen()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var validator = new NotesDocumentValidator();
        var document = NotesDocument.Create("Vector Write");
        var inserted = new WriteDocumentEditor(document).InsertCustomShape(DocumentVectorShapes.CreateEditableStarter("Write badge"));
        var nodeId = inserted.VectorShape!.Paths[0].Subpaths[0].Nodes[1].Id;
        var writeEditor = new WriteDocumentEditor(document);
        writeEditor.SelectBlock(inserted.Id);
        Assert.True(writeEditor.UpdateSelectedCustomShape(editor => editor.MoveNode(nodeId, 73, 21)));

        var first = new NotesRepository(_paths, validator, diagnostics);
        await first.SaveAsync(document, "Persist Write vector", CancellationToken.None);
        var reopened = await new NotesRepository(_paths, validator, diagnostics).LoadAsync(document.Id, CancellationToken.None);

        Assert.NotNull(reopened);
        var shape = reopened!.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks).Single(block => block.Kind == NotesBlockKind.Shape).VectorShape;
        Assert.NotNull(shape);
        Assert.Equal("Write badge", shape!.Name);
        Assert.Equal(73, shape.Paths[0].Subpaths[0].Nodes[1].X);
        Assert.True(DocumentVectorShapeValidator.Validate(shape).IsValid);
    }

    [Fact]
    public async Task Canvas_shape_survives_real_notes_repository_save_and_reopen()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var validator = new NotesDocumentValidator();
        var document = CanvasDocumentModel.Create("Vector Canvas");
        var controller = new CanvasInteractionController(CanvasDocumentModel.GetBoard(document));
        var created = controller.AddCustomShape(DocumentVectorShapes.CreateEditableStarter("Canvas badge"));
        Assert.NotNull(created.VectorShape);
        CanvasDocumentModel.ReplaceBoard(document, controller.Board);

        await new NotesRepository(_paths, validator, diagnostics).SaveAsync(document, "Persist Canvas vector", CancellationToken.None);
        var reopened = await new NotesRepository(_paths, validator, diagnostics).LoadAsync(document.Id, CancellationToken.None);

        Assert.NotNull(reopened);
        var shape = CanvasDocumentModel.GetBoard(reopened!).Objects.Single(value => value.VectorShape is not null).VectorShape;
        Assert.NotNull(shape);
        Assert.Equal("Canvas badge", shape!.Name);
        Assert.True(DocumentVectorShapeValidator.Validate(shape).IsValid);
    }

    [Fact]
    public async Task Present_shape_survives_real_present_repository_save_and_reopen()
    {
        var document = PresentDocument.Create("Vector Present");
        _ = new PresentEditor(document).AddCustomShape(document.Slides[0].Id, DocumentVectorShapes.CreateEditableStarter("Present badge"));

        await new PresentRepository(_paths).SaveAsync(document, "Persist Present vector", CancellationToken.None);
        var reopened = await new PresentRepository(_paths).LoadAsync(document.Id, CancellationToken.None);

        Assert.NotNull(reopened);
        var shape = reopened!.Slides.SelectMany(slide => slide.Elements).Single(element => element.VectorShape is not null).VectorShape;
        Assert.NotNull(shape);
        Assert.Equal("Present badge", shape!.Name);
        Assert.True(DocumentVectorShapeValidator.Validate(shape).IsValid);
    }

    [Fact]
    public async Task Data_shape_survives_real_workbook_repository_save_and_reopen()
    {
        var workbook = DataWorkbook.Create("Vector Data");
        _ = new DataSheetDrawingEditor(workbook.Sheets[0]).AddCustomShape(DocumentVectorShapes.CreateEditableStarter("Data badge"));

        await new DataWorkbookRepository(_paths).SaveAsync(workbook, "Persist Data vector", CancellationToken.None);
        var reopened = await new DataWorkbookRepository(_paths).LoadAsync(workbook.Id, CancellationToken.None);

        Assert.NotNull(reopened);
        var drawing = Assert.Single(reopened!.Sheets[0].Drawings);
        Assert.NotNull(drawing.VectorShape);
        Assert.Equal("Data badge", drawing.VectorShape!.Name);
        Assert.True(DocumentVectorShapeValidator.Validate(drawing.VectorShape).IsValid);
    }

    public void Dispose() => _paths.Dispose();

    private sealed class VectorTestPaths : IAppPaths, IDisposable
    {
        public VectorTestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-document-vector-repository-tests", Guid.NewGuid().ToString("N"));
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
            try { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
