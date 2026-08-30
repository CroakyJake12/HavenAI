using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.HavenUI.Creative;
using Haven.Desktop.Views.Pages.Canvas;
using Haven.UI;

namespace Haven.Desktop.Tests;

public sealed class CanvasPointerModeRecoveryTests
{
    [AvaloniaFact]
    public void Shared_surface_lasso_selects_and_laser_lasso_remains_active_after_release()
    {
        using var scene = new CanvasHavenScene();
        var document = CanvasDocumentModel.Create("Pointer recovery");
        var board = CanvasDocumentModel.GetBoard(document);
        board.OffsetX = 0; board.OffsetY = 0; board.Zoom = 1;
        var controller = new CanvasInteractionController(board);
        var inside = controller.AddObjectAt(Haven.Core.NotesCanvasObjectKind.Shape, 40, 40, 40, 40, "inside");
        _ = controller.AddObjectAt(Haven.Core.NotesCanvasObjectKind.Shape, 300, 300, 40, 40, "outside");
        scene.SetControllerDocument(document, controller, 0, 1, false, false);

        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 900, Height = 700, Content = host };
        try
        {
            window.Show(); window.UpdateLayout();
            var router = new HavenInputRouter(scene.Root);
            var origin = new HavenPoint(scene.UnifiedSurface.Bounds.X, scene.UnifiedSurface.Bounds.Y);

            scene.UnifiedSurface.SetTool(UnifiedCanvasTool.Lasso);
            DragPolygon(router, origin, [(20,20), (120,20), (120,120), (20,120)]);
            Assert.Contains(inside.Id, controller.SelectedObjectIds);

            scene.UnifiedSurface.SetTool(UnifiedCanvasTool.LaserLasso);
            DragPolygon(router, origin, [(20,20), (120,20), (120,120), (20,120)]);
            Assert.Equal(UnifiedCanvasTool.LaserLasso, scene.UnifiedSurface.Tool);
            Assert.Contains(inside.Id, controller.SelectedObjectIds);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static void DragPolygon(HavenInputRouter router, HavenPoint origin, (double X, double Y)[] points)
    {
        var first = new HavenPoint(origin.X + points[0].X, origin.Y + points[0].Y);
        router.PointerPressed(first);
        foreach (var point in points.Skip(1))
            router.PointerMoved(new HavenPoint(origin.X + point.X, origin.Y + point.Y));
        Assert.True(router.PointerReleased(first));
    }
}
