using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.HavenUI.Creative;
using Haven.Desktop.Views.Pages.Canvas;
using Haven.UI;

namespace Haven.Desktop.Tests;

public sealed class CanvasRecoveryTests
{
    [AvaloniaFact]
    public void Canvas_scene_keeps_board_dominant_and_direct_shape_creation_works_at_narrow_width()
    {
        using var scene = new CanvasHavenScene();
        var document = CanvasDocumentModel.Create();
        var controller = new CanvasInteractionController(CanvasDocumentModel.GetBoard(document));
        scene.SetControllerDocument(document, controller, 0, 1, false, false);

        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 900, Height = 700, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();
            var router = new HavenInputRouter(scene.Root);

            Assert.True(scene.BoardSurface.Bounds.Width > scene.Inspector.Bounds.Width);
            Assert.True(scene.UnifiedSurface.Bounds.Width > 420);
            Assert.Equal(HavenAccessibleRole.Group, scene.UnifiedSurface.Accessibility.Role);
            Assert.True(scene.UnifiedSurface.Accessibility.Focusable);
            Assert.Equal("Editable canvas", scene.UnifiedSurface.Accessibility.AccessibleName);
            Assert.False(string.IsNullOrWhiteSpace(scene.UnifiedSurface.Accessibility.Description));
            Assert.Contains(scene.Root.DescendantsAndSelf(), element => element.Name == "Canvas.Release.ToolDock" && element.Bounds.Width > 0);
            Assert.Contains(scene.Root.DescendantsAndSelf(), element => element.Name == "Canvas.Release.Tool.Add");

            scene.UnifiedSurface.SetTool(UnifiedCanvasTool.Rectangle);
            Assert.Equal(UnifiedCanvasTool.Rectangle, scene.UnifiedSurface.Tool);

            var createPoint = new HavenPoint(
                scene.UnifiedSurface.Bounds.X + scene.UnifiedSurface.Bounds.Width * .45,
                scene.UnifiedSurface.Bounds.Y + scene.UnifiedSurface.Bounds.Height * .45);
            Assert.Same(scene.UnifiedSurface, router.HitTest(createPoint));
            router.PointerPressed(createPoint);
            Assert.True(router.PointerReleased(createPoint));

            var shape = Assert.Single(controller.Board.Objects, value => value.Kind == NotesCanvasObjectKind.Shape);
            Assert.Contains("rectangle", shape.StyleJson, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(shape.Id, controller.SelectedObjectId);
            Assert.Equal(UnifiedCanvasTool.Select, scene.UnifiedSurface.Tool);

            var beforeX = shape.X;
            router.Focus(scene.UnifiedSurface);
            Assert.True(router.KeyDown(HavenKey.Right, new HavenInputModifiers()));
            Assert.True(shape.X > beforeX);
            Assert.True(controller.Undo());
            var restored = Assert.Single(controller.Board.Objects, value => value.Id == shape.Id);
            Assert.Equal(beforeX, restored.X, 8);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static void Click(HavenInputRouter router, HavenElement element)
    {
        var point = new HavenPoint(
            element.Bounds.X + element.Bounds.Width / 2,
            element.Bounds.Y + element.Bounds.Height / 2);
        Assert.Same(element, router.HitTest(point));
        router.PointerPressed(point);
        Assert.True(router.PointerReleased(point));
    }
}
