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
        using var scene = new ImagineScene(); var session = new ImagineProjectSession(ImagineProjectSession.CreateProject("Results Day")); var asset = new ImagineMediaAsset(Guid.NewGuid(), ImagineMediaKind.Image, "source.png", "C:/source.png", "C:/managed.png", 512, "hash", DateTimeOffset.UtcNow); session.AddImportedAsset(asset); session.ReplaceSemanticComponents(asset.Id, [new ImagineSemanticComponent(Guid.NewGuid(), asset.Id, null, "subject", "Subject", "object", new ImagineRegion(.1, .1, .8, .8), 0, null, .9, "vision-bounds", "vision")], "vision"); scene.SetSession(session); scene.Sync(session.Project, [session.Project]);
        Assert.Equal("Chatbox", scene.Assistant.PrefabID); Assert.Equal("Ask Haven to edit the selected object", scene.AssistantInput.Placeholder); Assert.Single(scene.RecentProjects.Items); Assert.Single(scene.Assets.Items); Assert.Single(scene.Components.Items); Assert.Equal(HavenAccessibleRole.Image, scene.Canvas.Accessibility.Role);
    }
    [Fact] public void Imagine_scene_compacts_without_replacing_canvas_or_dynamic_runtime() { using var scene = new ImagineScene(); var canvas = scene.Canvas; var assets = scene.Assets; scene.SetViewportWidth(700); Assert.Equal("1fr", scene.Body.Columns); Assert.Same(canvas, scene.Canvas); Assert.Same(assets, scene.Assets); Assert.Equal(0, scene.Canvas.GetValue(HavenProperties.Column)); }
    [Fact] public void Vision_scene_is_a_distinct_haven_native_visual_understanding_surface() { var scene = new VisionScene(); Assert.Equal("Vision", scene.Title.Content); Assert.Equal("Ask about the image", scene.Question.Placeholder); Assert.Equal(HavenAccessibleRole.Image, scene.Preview.Accessibility.Role); Assert.Equal("Edit in Imagine", scene.OpenImagine.Content); }
}
