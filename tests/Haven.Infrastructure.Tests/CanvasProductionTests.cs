using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class CanvasProductionTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public void Controller_supports_object_gestures_history_connections_grouping_and_locks()
    {
        var document = CanvasDocumentModel.Create("Architecture board");
        var board = CanvasDocumentModel.GetBoard(document);
        board.OffsetX = 0;
        board.OffsetY = 0;
        board.Zoom = 1;
        var controller = new CanvasInteractionController(board);

        var first = controller.AddObject(NotesCanvasObjectKind.Text, "Alpha");
        Assert.True(controller.MoveSelected(120, 140));
        Assert.True(controller.ResizeSelected(240, 130));
        Assert.True(controller.RotateSelected(25));
        var second = controller.AddObject(NotesCanvasObjectKind.Shape, "Beta");
        Assert.True(controller.MoveSelected(520, 420));
        var connector = controller.Connect(first.Id, second.Id, "depends on");
        Assert.NotNull(connector);
        Assert.Equal(first.Id, connector!.FromObjectId);
        Assert.Equal(second.Id, connector.ToObjectId);
        Assert.NotNull(controller.Group(first.Id, second.Id));

        controller.SelectObject(first.Id);
        var beforeDragX = controller.SelectedObject!.X;
        var beforeDragY = controller.SelectedObject.Y;
        controller.Tool = CanvasTool.Select;
        controller.Begin(new CanvasPointerSample(beforeDragX + 10, beforeDragY + 10));
        Assert.True(controller.Move(new CanvasPointerSample(beforeDragX + 70, beforeDragY + 50)));
        controller.End(new CanvasPointerSample(beforeDragX + 70, beforeDragY + 50));
        Assert.True(controller.Board.Objects.Single(value => value.Id == first.Id).X > beforeDragX);
        Assert.True(controller.Undo());
        Assert.Equal(beforeDragX, controller.Board.Objects.Single(value => value.Id == first.Id).X, 3);
        Assert.True(controller.Redo());
        Assert.True(controller.Board.Objects.Single(value => value.Id == first.Id).X > beforeDragX);

        controller.SelectObject(second.Id);
        Assert.True(controller.SetSelectedLocked(true));
        var locked = controller.SelectedObject!;
        Assert.False(controller.MoveSelected(locked.X + 50, locked.Y + 50));
        Assert.False(controller.ResizeSelected(locked.Width + 50, locked.Height + 50));
        Assert.False(controller.RotateSelected(locked.Rotation + 30));
        Assert.False(controller.UpdateSelectedText("Blocked"));
        Assert.False(controller.SendSelectedToBack());
        Assert.False(controller.DeleteSelected());

        controller.SelectObject(first.Id);
        Assert.False(controller.UngroupSelected());
    }

    [Fact]
    public void Ink_preserves_pressure_tilt_supports_undo_redo_and_eraser()
    {
        var board = CanvasDocumentModel.GetBoard(CanvasDocumentModel.Create("Ink board"));
        board.OffsetX = 0;
        board.OffsetY = 0;
        board.Zoom = 1;
        var controller = new CanvasInteractionController(board) { Tool = CanvasTool.Pen, PenWidth = 5, PenColour = "#FF123456" };

        Assert.True(controller.Begin(new CanvasPointerSample(10, 20, 0.8, 12, -9, 1_000)));
        Assert.True(controller.Move(new CanvasPointerSample(25, 35, 0.6, 8, -4, 1_020)));
        controller.End(new CanvasPointerSample(40, 50, 0.4, 4, -2, 1_040));

        var stroke = Assert.Single(controller.Board.Strokes);
        Assert.Equal("pen", stroke.Tool);
        Assert.Equal("#FF123456", stroke.Colour);
        Assert.Equal(5, stroke.BaseWidth, 3);
        Assert.True(stroke.Points.Count >= 3);
        Assert.Equal(0.8, stroke.Points[0].Pressure, 3);
        Assert.Equal(12, stroke.Points[0].TiltX, 3);
        Assert.Equal(-9, stroke.Points[0].TiltY, 3);
        Assert.Equal(20, stroke.Points[1].TimestampMilliseconds);

        Assert.True(controller.Undo());
        Assert.Empty(controller.Board.Strokes);
        Assert.True(controller.Redo());
        Assert.Single(controller.Board.Strokes);

        controller.Tool = CanvasTool.Eraser;
        Assert.True(controller.Begin(new CanvasPointerSample(10, 20)));
        Assert.Empty(controller.Board.Strokes);
        Assert.True(controller.Undo());
        Assert.Single(controller.Board.Strokes);
    }

    [Fact]
    public async Task Canvas_native_format_and_notes_repository_round_trip_and_recover_last_valid_board()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var validator = new NotesDocumentValidator();
        var repository = new NotesRepository(_paths, validator, diagnostics);
        var formats = new NotesImportExportService(validator, diagnostics);
        var document = CanvasDocumentModel.Create("Persisted canvas");
        var controller = new CanvasInteractionController(CanvasDocumentModel.GetBoard(document));
        controller.AddObject(NotesCanvasObjectKind.Text, "Persist me");
        controller.Tool = CanvasTool.Highlighter;
        controller.Begin(new CanvasPointerSample(30, 40, 0.7, 5, 6, 500));
        controller.End(new CanvasPointerSample(80, 90, 0.5, 2, 3, 530));
        CanvasDocumentModel.ReplaceBoard(document, controller.Board);

        await repository.SaveAsync(document, "Canvas persistence test", CancellationToken.None);
        var loaded = await repository.LoadAsync(document.Id, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.True(CanvasDocumentModel.IsCanvasDocument(loaded));
        var loadedBoard = CanvasDocumentModel.GetBoard(loaded!);
        Assert.Single(loadedBoard.Objects);
        Assert.Single(loadedBoard.Strokes);
        Assert.Equal("Persist me", loadedBoard.Objects[0].Text);

        var nativePath = Path.Combine(_paths.DataDirectory, "persisted-canvas.haven-notes.json");
        await formats.ExportAsync(loaded, nativePath, CancellationToken.None);
        var imported = await formats.ImportAsync(nativePath, CancellationToken.None);
        Assert.True(CanvasDocumentModel.IsCanvasDocument(imported));
        Assert.Equal("Persist me", CanvasDocumentModel.GetBoard(imported).Objects.Single().Text);

        var secondController = new CanvasInteractionController(loadedBoard);
        secondController.AddObject(NotesCanvasObjectKind.Frame, "Transient second version");
        CanvasDocumentModel.ReplaceBoard(loaded, secondController.Board);
        await repository.SaveAsync(loaded, "Second Canvas version", CancellationToken.None);

        var currentPath = Path.Combine(_paths.DataDirectory, "Notes", "Documents", document.Id.ToString("D"), "current.haven-notes.json");
        await File.WriteAllTextAsync(currentPath, "{ this is not valid json");
        var recovered = await repository.LoadAsync(document.Id, CancellationToken.None);
        Assert.NotNull(recovered);
        Assert.True(recovered!.Recovery.HasUnsavedRecovery);
        Assert.True(CanvasDocumentModel.IsCanvasDocument(recovered));
        var recoveredBoard = CanvasDocumentModel.GetBoard(recovered);
        Assert.Single(recoveredBoard.Objects);
        Assert.Equal("Persist me", recoveredBoard.Objects[0].Text);
    }

    [Fact]
    public void Ghost_visibility_hides_unrevealed_layer_content_without_mutating_the_board()
    {
        var board = CanvasDocumentModel.GetBoard(CanvasDocumentModel.Create("Study board"));
        var visibleObject = new NotesCanvasObject { Kind = NotesCanvasObjectKind.Text, Text = "Question" };
        var answerObject = new NotesCanvasObject { Kind = NotesCanvasObjectKind.Text, Text = "Answer" };
        var answerStroke = new NotesInkStroke
        {
            IsGhost = true,
            Points = [new NotesInkPoint { X = 1, Y = 1 }, new NotesInkPoint { X = 2, Y = 2 }]
        };
        var layer = new NotesGhostLayer
        {
            Name = "Answer",
            IsRevealed = false,
            ObjectIds = [answerObject.Id],
            StrokeIds = [answerStroke.Id]
        };
        answerStroke.GhostLayerId = layer.Id;
        board.Objects.Add(visibleObject);
        board.Objects.Add(answerObject);
        board.Strokes.Add(answerStroke);
        board.GhostLayers.Add(layer);

        Assert.True(CanvasGhostVisibility.IsObjectVisible(board, visibleObject));
        Assert.False(CanvasGhostVisibility.IsObjectVisible(board, answerObject));
        Assert.False(CanvasGhostVisibility.IsStrokeVisible(board, answerStroke));
        Assert.Equal(2, board.Objects.Count);
        Assert.Single(board.Strokes);

        layer.IsRevealed = true;
        Assert.True(CanvasGhostVisibility.IsObjectVisible(board, answerObject));
        Assert.True(CanvasGhostVisibility.IsStrokeVisible(board, answerStroke));
        Assert.Equal(2, board.Objects.Count);
        Assert.Single(board.Strokes);
    }

    [Fact]
    public void Diagonal_line_layout_is_centered_before_rotation()
    {
        var layout = CanvasLineGeometry.Compute(10, 20, 110, 120, 4);
        Assert.Equal(Math.Sqrt(20_000), layout.Width, 6);
        Assert.Equal(4, layout.Height, 6);
        Assert.Equal(45, layout.RotationDegrees, 6);
        Assert.Equal(60 - Math.Sqrt(20_000) / 2, layout.Left, 6);
        Assert.Equal(68, layout.Top, 6);
    }

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-canvas-tests-" + Guid.NewGuid().ToString("N"));
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
