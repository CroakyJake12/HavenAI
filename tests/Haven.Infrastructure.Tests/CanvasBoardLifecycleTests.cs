using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class CanvasBoardLifecycleTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task Multi_board_round_trip_preserves_identity_titles_and_content()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var validator = new NotesDocumentValidator();
        var repository = new NotesRepository(_paths, validator, diagnostics);
        var document = CanvasDocumentModel.Create("Recovery canvas");
        var id = document.Id;
        var first = new CanvasInteractionController(CanvasDocumentModel.GetBoard(document, 0));
        first.AddObject(NotesCanvasObjectKind.Text, "Board one data");
        CanvasDocumentModel.ReplaceBoard(document, first.Board, 0);
        var secondIndex = CanvasDocumentModel.AddBoard(document, "Ideas");
        var second = new CanvasInteractionController(CanvasDocumentModel.GetBoard(document, secondIndex));
        second.AddObject(NotesCanvasObjectKind.Shape, "Board two data");
        CanvasDocumentModel.ReplaceBoard(document, second.Board, secondIndex);
        Assert.True(CanvasDocumentModel.RenameBoard(document, secondIndex, "Recovered ideas"));
        Assert.Equal(id, document.Id);
        await repository.SaveAsync(document, "Canvas recovery test", CancellationToken.None);
        var reopened = await repository.LoadAsync(id, CancellationToken.None);
        Assert.NotNull(reopened);
        Assert.Equal(id, reopened!.Id);
        Assert.Equal(new[] { "Board", "Recovered ideas" }, CanvasDocumentModel.GetBoardTitles(reopened));
        Assert.Equal("Board one data", Assert.Single(CanvasDocumentModel.GetBoard(reopened, 0).Objects).Text);
        Assert.Equal("Board two data", Assert.Single(CanvasDocumentModel.GetBoard(reopened, 1).Objects).Text);
    }

    [Fact]
    public void Recovered_interactions_keep_history_lasso_and_chunk_eraser()
    {
        var board = CanvasDocumentModel.GetBoard(CanvasDocumentModel.Create("Interaction recovery"));
        board.OffsetX = 0; board.OffsetY = 0; board.Zoom = 1;
        var controller = new CanvasInteractionController(board) { Tool = CanvasTool.Pen, PenEffect = "Uniform", PenOpacity = .65 };
        Assert.True(controller.Begin(new CanvasPointerSample(0, 20)));
        for (var x = 10; x <= 100; x += 10) controller.Move(new CanvasPointerSample(x, 20));
        Assert.True(controller.ReleaseInteraction());
        Assert.All(Assert.Single(controller.Board.Strokes).Points, point => Assert.Equal(.5, point.Pressure, 3));
        controller.Tool = CanvasTool.Eraser; controller.EraserMode = CanvasEraserMode.Chunk;
        Assert.True(controller.Begin(new CanvasPointerSample(50, 20)));
        controller.End(new CanvasPointerSample(50, 20));
        Assert.Equal(2, controller.Board.Strokes.Count);
        Assert.True(controller.Undo());
        Assert.Single(controller.Board.Strokes);
        var inside = controller.AddObjectAt(NotesCanvasObjectKind.Shape, 10, 10, 20, 20, "inside");
        _ = controller.AddObjectAt(NotesCanvasObjectKind.Shape, 200, 200, 20, 20, "outside");
        var selected = controller.SelectViewportPolygon([new(0,0), new(60,0), new(60,60), new(0,60)]);
        Assert.Contains(inside.Id, selected);
    }

    public void Dispose() => _paths.Dispose();
    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths() { DataDirectory = Path.Combine(Path.GetTempPath(), "haven-canvas-recovery-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(DataDirectory); Directory.CreateDirectory(LogsDirectory); }
        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "missing.json");
        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch { } }
    }
}
