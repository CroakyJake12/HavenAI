using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Imagine;
using Haven.UI;

namespace Haven.Desktop.Tests;

public sealed class ImagineCreativeSceneTests
{
    [Fact]
    public void Imagine_scene_is_haven_native_and_projects_repeated_state_through_dynamic_ui()
    {
        using var scene = new ImagineWorkspaceScene();
        var session = new ImagineProjectSession(ImagineProjectSession.CreateProject("Results Day"));
        var asset = new ImagineMediaAsset(Guid.NewGuid(), ImagineMediaKind.Image, "source.png", "C:/source.png", "C:/managed.png", 512, "hash", DateTimeOffset.UtcNow);
        session.AddImportedAsset(asset);
        session.ReplaceSemanticComponents(asset.Id, [new ImagineSemanticComponent(Guid.NewGuid(), asset.Id, null, "subject", "Subject", "object", new ImagineRegion(.1, .1, .8, .8), 0, null, .9, "vision-bounds", "vision")], "vision");
        scene.SetSession(session);
        scene.Sync(session.Project, [session.Project]);

        Assert.Equal("Chatbox", scene.Assistant.PrefabID);
        Assert.Equal("Ask Haven to edit the selected object", scene.AssistantInput.Placeholder);
        Assert.Single(scene.RecentProjects.Items);
        Assert.Single(scene.Assets.Items);
        Assert.Single(scene.Components.Items);
        Assert.Equal(HavenAccessibleRole.Image, scene.Canvas.Accessibility.Role);
        Assert.Equal("Imagine editable image canvas", scene.Canvas.Accessibility.AccessibleName);
    }

    [Fact]
    public void Imagine_scene_compacts_without_replacing_canvas_or_dynamic_runtime()
    {
        using var scene = new ImagineWorkspaceScene();
        var canvas = scene.Canvas;
        var assets = scene.Assets;
        scene.SetViewportWidth(700);
        Assert.Equal("1fr", scene.Body.Columns);
        Assert.Same(canvas, scene.Canvas);
        Assert.Same(assets, scene.Assets);
        Assert.Equal(0, scene.Canvas.GetValue(HavenProperties.Column));
    }

    [Fact]
    public void Rotated_object_hit_testing_uses_object_space_not_axis_aligned_bounds()
    {
        var transform = new ImagineTransform(100, 100, 200, 80, 45);
        Assert.True(ImagineCanvasGeometry.Contains(transform, new HavenPoint(200, 140)));
        Assert.False(ImagineCanvasGeometry.Contains(transform, new HavenPoint(90, 90)));
    }

    [Fact]
    public void Corner_resize_preserves_the_opposite_rotated_corner()
    {
        var original = new ImagineTransform(100, 100, 200, 100, 30);
        var fixedCorner = ImagineCanvasGeometry.CornerPoint(original, ImagineResizeHandle.NorthWest);
        var target = ImagineCanvasGeometry.CornerPoint(new ImagineTransform(70, 80, 260, 140, 30), ImagineResizeHandle.SouthEast);
        var resized = ImagineCanvasGeometry.ResizeFromCorner(original, ImagineResizeHandle.SouthEast, target);
        var afterFixed = ImagineCanvasGeometry.CornerPoint(resized, ImagineResizeHandle.NorthWest);

        Assert.Equal(fixedCorner.X, afterFixed.X, 6);
        Assert.Equal(fixedCorner.Y, afterFixed.Y, 6);
        Assert.True(resized.Width >= 12);
        Assert.True(resized.Height >= 12);
    }

    [Fact]
    public void Moving_object_snaps_to_canvas_center_and_reports_a_guide()
    {
        var session = new ImagineProjectSession(ImagineProjectSession.CreateProject("Snap", 1000, 800));
        var id = session.AddRectangle(453, 100, 100, 100);
        var item = session.Project.Objects.Single(x => x.Id == id);
        var snapped = ImagineCanvasGeometry.SnapMove(session.Project, id, item.Transform, 1);

        Assert.Equal(450, snapped.Transform.X, 6);
        Assert.Equal(500, snapped.GuideX);
    }

    [Fact]
    public void Selection_geometry_exposes_all_four_resize_handles()
    {
        var handles = ImagineCanvasGeometry.CornerHandles(new HavenRect(10, 20, 300, 200));
        Assert.Equal(4, handles.Length);
    }

    [Fact]
    public void Vision_scene_is_a_distinct_haven_native_visual_understanding_surface()
    {
        var scene = new VisionScene();
        Assert.Equal("Vision", scene.Title.Content);
        Assert.Equal("Ask about the image", scene.Question.Placeholder);
        Assert.Equal(HavenAccessibleRole.Image, scene.Preview.Accessibility.Role);
        Assert.Equal("Vision interactive image preview", scene.Preview.Accessibility.AccessibleName);
        Assert.Equal("Pan", scene.Pan.Content);
        Assert.Equal("Select region", scene.SelectRegion.Content);
        Assert.Equal("Ask region", scene.AskRegion.Content);
        Assert.Equal("Edit in Imagine", scene.OpenImagine.Content);
    }

    [Fact]
    public void Vision_preview_exposes_real_view_modes_and_fit_zoom_state()
    {
        var preview = new VisionPreviewElement();
        Assert.Equal(VisionInteractionMode.Pan, preview.Mode);
        preview.SetMode(VisionInteractionMode.SelectRegion);
        Assert.Equal(VisionInteractionMode.SelectRegion, preview.Mode);
        preview.ZoomBy(2);
        Assert.Equal(200, preview.ZoomPercent);
        preview.Fit();
        Assert.Equal(100, preview.ZoomPercent);
    }
}
