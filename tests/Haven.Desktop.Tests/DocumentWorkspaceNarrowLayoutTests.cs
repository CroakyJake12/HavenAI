using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Present;
using Haven.Desktop.Views.Pages.Write;

namespace Haven.Desktop.Tests;

public sealed class DocumentWorkspaceNarrowLayoutTests
{
    [AvaloniaFact]
    public void Write_keeps_the_document_surface_dominant_at_900_pixels()
    {
        using var scene = new WordWriteHavenScene();
        scene.SetDocument(NotesDocument.Create("Narrow document"), 0, 1);
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 900, Height = 700, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.True(scene.DocumentSurface.Bounds.Width > 560);
            Assert.True(scene.DocumentSurface.Bounds.Right <= scene.Root.Bounds.Right + .01);
            Assert.True(scene.RibbonContent.Bounds.Right <= scene.Root.Bounds.Right + .01);
        }
        finally { window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public void Present_keeps_navigation_canvas_and_inspector_inside_900_pixels()
    {
        using var scene = new PresentHavenScene();
        scene.SetDocument(PresentDocument.Create("Narrow deck"), 0, 1, 0);
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 900, Height = 700, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.True(scene.SlidePane.Bounds.Width >= 150);
            Assert.True(scene.SlideCanvas.Bounds.Width > 340);
            Assert.True(scene.InspectorPane.Bounds.Width >= 200);
            Assert.True(scene.InspectorPane.Bounds.Right <= scene.Root.Bounds.Right + .01);
        }
        finally { window.Content = null; window.Close(); }
    }
}
