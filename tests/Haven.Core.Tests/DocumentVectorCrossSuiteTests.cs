using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class DocumentVectorCrossSuiteTests
{
    [Fact]
    public void One_native_shape_inserts_into_write_present_canvas_and_data_without_geometry_loss()
    {
        var source = DocumentVectorShapes.CreateEditableStarter("Shared badge");
        var sourceGeometry = Geometry(source);

        var writeDocument = NotesDocument.Create("Write");
        var writeEditor = new WriteDocumentEditor(writeDocument);
        var writeBlock = writeEditor.InsertCustomShape(source);

        var presentDocument = PresentDocument.Create("Present");
        var presentEditor = new PresentEditor(presentDocument);
        var presentElement = presentEditor.AddCustomShape(presentDocument.Slides[0].Id, source);

        var canvasDocument = CanvasDocumentModel.Create("Canvas");
        var canvasBoard = CanvasDocumentModel.GetBoard(canvasDocument);
        var canvasEditor = new CanvasInteractionController(canvasBoard);
        var canvasObject = canvasEditor.AddCustomShape(source);

        var dataWorkbook = DataWorkbook.Create("Data");
        var dataEditor = new DataSheetDrawingEditor(dataWorkbook.Sheets[0]);
        var dataDrawing = dataEditor.AddCustomShape(source);

        var inserted = new[]
        {
            Assert.IsType<DocumentVectorShape>(writeBlock.VectorShape),
            Assert.IsType<DocumentVectorShape>(presentElement.VectorShape),
            Assert.IsType<DocumentVectorShape>(canvasObject.VectorShape),
            Assert.IsType<DocumentVectorShape>(dataDrawing.VectorShape)
        };

        Assert.All(inserted, shape => Assert.Equal(sourceGeometry, Geometry(shape)));
        Assert.Equal(4, inserted.Select(shape => shape.Id).Distinct().Count());
        Assert.DoesNotContain(inserted, shape => shape.Id == source.Id);
        Assert.All(inserted, shape => Assert.True(DocumentVectorShapeValidator.Validate(shape).IsValid));
    }

    [Fact]
    public void Editing_one_suite_copy_does_not_mutate_the_source_or_other_apps()
    {
        var source = DocumentVectorShapes.CreateEditableStarter();
        var sourceNode = source.Paths[0].Subpaths[0].Nodes[1];

        var writeEditor = new WriteDocumentEditor(NotesDocument.Create());
        var writeBlock = writeEditor.InsertCustomShape(source);
        var presentDocument = PresentDocument.Create();
        var presentElement = new PresentEditor(presentDocument).AddCustomShape(presentDocument.Slides[0].Id, source);
        var originalPresentX = presentElement.VectorShape!.Paths[0].Subpaths[0].Nodes[1].X;

        Assert.True(writeEditor.UpdateSelectedCustomShape(editor => editor.MoveNode(writeBlock.VectorShape!.Paths[0].Subpaths[0].Nodes[1].Id, 77, 19)));

        Assert.Equal(77, writeBlock.VectorShape!.Paths[0].Subpaths[0].Nodes[1].X);
        Assert.Equal(sourceNode.X, source.Paths[0].Subpaths[0].Nodes[1].X);
        Assert.Equal(originalPresentX, presentElement.VectorShape.Paths[0].Subpaths[0].Nodes[1].X);
        Assert.True(writeEditor.CanUndo);
        Assert.True(writeEditor.Undo());
        Assert.Equal(sourceNode.X, writeEditor.SelectedBlock!.VectorShape!.Paths[0].Subpaths[0].Nodes[1].X);
    }

    [Fact]
    public void Native_vector_shapes_survive_json_reopen_in_all_four_document_models()
    {
        var source = DocumentVectorShapes.CreateEditableStarter("Reopen me");

        var write = NotesDocument.Create();
        _ = new WriteDocumentEditor(write).InsertCustomShape(source);

        var present = PresentDocument.Create();
        _ = new PresentEditor(present).AddCustomShape(present.Slides[0].Id, source);

        var canvas = CanvasDocumentModel.Create();
        _ = new CanvasInteractionController(CanvasDocumentModel.GetBoard(canvas)).AddCustomShape(source);

        var data = DataWorkbook.Create();
        _ = new DataSheetDrawingEditor(data.Sheets[0]).AddCustomShape(source);

        var writeReopened = RoundTrip(write);
        var presentReopened = RoundTrip(present);
        var canvasReopened = RoundTrip(canvas);
        var dataReopened = RoundTrip(data);

        var shapes = new[]
        {
            writeReopened.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks).Single(block => block.Kind == NotesBlockKind.Shape).VectorShape!,
            presentReopened.Slides.SelectMany(slide => slide.Elements).Single(element => element.VectorShape is not null).VectorShape!,
            CanvasDocumentModel.GetBoard(canvasReopened).Objects.Single(value => value.VectorShape is not null).VectorShape!,
            dataReopened.Sheets[0].Drawings.Single().VectorShape!
        };

        Assert.All(shapes, shape =>
        {
            shape.Normalize();
            Assert.Equal("Reopen me", shape.Name);
            Assert.True(DocumentVectorShapeValidator.Validate(shape).IsValid);
            Assert.Contains(shape.Paths.SelectMany(path => path.Subpaths).SelectMany(subpath => subpath.Nodes), node => node.IncomingSegment == DocumentVectorSegmentKind.Cubic);
        });
    }

    [Fact]
    public void Vector_transform_round_trips_and_bezier_control_edits_are_undoable()
    {
        var shape = DocumentVectorShapes.CreateEditableStarter();
        shape.Transform = new DocumentVectorTransform
        {
            TranslateX = 11, TranslateY = -7, ScaleX = 1.35, ScaleY = .8,
            RotationDegrees = 31, OriginX = .4, OriginY = .6
        };
        shape.Normalize();
        var source = new DocumentVectorPoint(23.5, 64.25);
        var transformed = DocumentVectorShapes.TransformPoint(shape, source);
        var restored = DocumentVectorShapes.InverseTransformPoint(shape, transformed);
        Assert.Equal(source.X, restored.X, 8);
        Assert.Equal(source.Y, restored.Y, 8);

        var node = shape.Paths[0].Subpaths[0].Nodes[1];
        var original = Assert.IsType<DocumentVectorPoint>(node.Control1);
        var editor = new DocumentVectorShapeEditor(shape);
        editor.MoveControlPoint(node.Id, 1, 44, 12);
        Assert.Equal(44, editor.Shape.Paths[0].Subpaths[0].Nodes[1].Control1!.X);
        Assert.True(editor.Undo());
        Assert.Equal(original.X, editor.Shape.Paths[0].Subpaths[0].Nodes[1].Control1!.X, 8);
        Assert.Equal(original.Y, editor.Shape.Paths[0].Subpaths[0].Nodes[1].Control1!.Y, 8);
    }

    private static string Geometry(DocumentVectorShape shape) => string.Join("|", shape.Paths.SelectMany(path => path.Subpaths).SelectMany(subpath => subpath.Nodes).Select(node => $"{node.X:0.###},{node.Y:0.###}:{node.IncomingSegment}:{node.Control1?.X:0.###},{node.Control1?.Y:0.###}:{node.Control2?.X:0.###},{node.Control2?.Y:0.###}"));

    private static T RoundTrip<T>(T value) where T : class =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value)) ?? throw new InvalidDataException("Round trip returned null.");
}
