using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class DocumentVectorShapeTests
{
    [Fact]
    public void Native_shape_serializes_and_reopens_line_quadratic_and_cubic_geometry()
    {
        var shape = CreateShape();

        var json = JsonSerializer.Serialize(shape);
        var reopened = JsonSerializer.Deserialize<DocumentVectorShape>(json);

        Assert.NotNull(reopened);
        reopened!.Normalize();
        Assert.True(DocumentVectorShapeValidator.Validate(reopened).IsValid);
        var nodes = Assert.Single(Assert.Single(reopened.Paths).Subpaths).Nodes;
        Assert.Equal(DocumentVectorSegmentKind.Line, nodes[0].IncomingSegment);
        Assert.Equal(DocumentVectorSegmentKind.Quadratic, nodes[1].IncomingSegment);
        Assert.NotNull(nodes[1].Control1);
        Assert.Equal(DocumentVectorSegmentKind.Cubic, nodes[2].IncomingSegment);
        Assert.NotNull(nodes[2].Control1);
        Assert.NotNull(nodes[2].Control2);
        Assert.True(Assert.Single(reopened.Paths).Subpaths[0].Closed);
    }

    [Fact]
    public void Shape_editor_applies_atomic_native_edits_and_undo_redo()
    {
        var shape = CreateShape();
        var nodeId = shape.Paths[0].Subpaths[0].Nodes[1].Id;
        var editor = new DocumentVectorShapeEditor(shape);

        editor.MoveNode(nodeId, 64, 18, DocumentOperationOrigin.Ai);
        Assert.Equal(64, FindNode(editor.Shape, nodeId).X);
        Assert.Equal(DocumentOperationOrigin.Ai, editor.LastOperation?.Origin);
        Assert.True(editor.CanUndo);

        Assert.True(editor.Undo());
        Assert.Equal(50, FindNode(editor.Shape, nodeId).X);
        Assert.True(editor.CanRedo);
        Assert.True(editor.Redo());
        Assert.Equal(64, FindNode(editor.Shape, nodeId).X);
    }

    [Fact]
    public void Rejected_edit_does_not_leak_partial_state()
    {
        var shape = CreateShape();
        var nodeId = shape.Paths[0].Subpaths[0].Nodes[1].Id;
        var editor = new DocumentVectorShapeEditor(shape);

        Assert.Throws<ArgumentOutOfRangeException>(() => editor.MoveNode(nodeId, double.NaN, 5));

        Assert.Equal(50, FindNode(editor.Shape, nodeId).X);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void Ai_shape_is_sanitized_into_same_native_representation_and_invalid_geometry_is_rejected()
    {
        var candidate = CreateShape();
        candidate.SourceKind = DocumentShapeSourceKind.Manual;
        candidate.Transform.ScaleX = double.NaN;
        candidate.Paths[0].Subpaths[0].Nodes[1].Control1 = null;

        var prepared = DocumentVectorShapes.PrepareAiShape(candidate);

        Assert.Equal(DocumentShapeSourceKind.Ai, prepared.SourceKind);
        Assert.Equal(1, prepared.Transform.ScaleX);
        Assert.NotNull(prepared.Paths[0].Subpaths[0].Nodes[1].Control1);
        Assert.True(DocumentVectorShapeValidator.Validate(prepared).IsValid);

        var invalid = new DocumentVectorShape { Paths = [] };
        Assert.Throws<InvalidDataException>(() => DocumentVectorShapes.PrepareAiShape(invalid));
    }

    [Fact]
    public void Transform_maps_geometry_without_rasterizing_or_mutating_local_nodes()
    {
        var shape = CreateShape();
        var original = shape.Paths[0].Subpaths[0].Nodes[0].Point;
        shape.Transform = new DocumentVectorTransform { ScaleX = 2, ScaleY = 0.5, RotationDegrees = 90, TranslateX = 7, TranslateY = -3, OriginX = 0, OriginY = 0 };
        shape.Transform.Normalize();

        var mapped = DocumentVectorShapes.TransformPoint(shape, original);

        Assert.Equal(original, shape.Paths[0].Subpaths[0].Nodes[0].Point);
        Assert.True(double.IsFinite(mapped.X));
        Assert.True(double.IsFinite(mapped.Y));
        Assert.NotEqual(original, mapped);
    }

    [Fact]
    public void Insertion_clone_preserves_geometry_but_uses_independent_ids()
    {
        var shape = CreateShape();
        var galleryId = Guid.NewGuid();

        var inserted = DocumentVectorShapes.CloneForInsertion(shape, galleryId);

        Assert.NotEqual(shape.Id, inserted.Id);
        Assert.Equal(galleryId, inserted.GallerySourceId);
        Assert.Equal(shape.Paths.Count, inserted.Paths.Count);
        Assert.NotEqual(shape.Paths[0].Id, inserted.Paths[0].Id);
        Assert.Equal(shape.Paths[0].Subpaths[0].Nodes.Select(n => (n.X, n.Y)), inserted.Paths[0].Subpaths[0].Nodes.Select(n => (n.X, n.Y)));
    }

    private static DocumentVectorShape CreateShape()
    {
        var shape = new DocumentVectorShape
        {
            Name = "Editable test shape",
            AccessibilityDescription = "A native editable test shape",
            Paths =
            [
                new DocumentVectorPath
                {
                    Fill = new DocumentVectorFill { Kind = DocumentVectorFillKind.Solid, Color = "#FF336699" },
                    Stroke = new DocumentVectorStroke { Enabled = true, Color = "#FF112233", Width = 2, Join = DocumentVectorLineJoin.Round, Cap = DocumentVectorLineCap.Round },
                    Subpaths =
                    [
                        new DocumentVectorSubpath
                        {
                            Closed = true,
                            Nodes =
                            [
                                new DocumentVectorNode { X = 10, Y = 10 },
                                new DocumentVectorNode { X = 50, Y = 20, IncomingSegment = DocumentVectorSegmentKind.Quadratic, Control1 = new DocumentVectorPoint(30, 0) },
                                new DocumentVectorNode { X = 90, Y = 80, IncomingSegment = DocumentVectorSegmentKind.Cubic, Control1 = new DocumentVectorPoint(60, 30), Control2 = new DocumentVectorPoint(80, 60) }
                            ]
                        }
                    ]
                }
            ],
            ConnectorPoints = [new DocumentVectorConnectorPoint { Name = "Right", X = 100, Y = 50, DirectionDegrees = 0 }]
        };
        shape.Normalize();
        return shape;
    }

    private static DocumentVectorNode FindNode(DocumentVectorShape shape, Guid id) =>
        shape.Paths.SelectMany(path => path.Subpaths).SelectMany(path => path.Nodes).Single(node => node.Id == id);
}
