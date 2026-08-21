using Haven.Desktop.Views;
using Haven.Desktop.Views.Pages.Imagine;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class Worker4AccessibilityHonestyTests
{
    [Fact]
    public void Worker4_interactive_controls_have_text_or_accessible_names()
    {
        using var project = new ProjectHavenScene();
        using var imagine = new ImagineWorkspaceScene();
        var vision = new VisionScene();

        AssertNamedControls(project.Root);
        AssertNamedControls(imagine.Root);
        AssertNamedControls(vision.Root);
    }

    [Fact]
    public void Imagine_export_and_playback_copy_describes_only_real_capabilities()
    {
        using var scene = new ImagineWorkspaceScene();
        var export = scene.Root.DescendantsAndSelf().OfType<Button>().Single(button => button.Name == "Export");

        scene.SetMode(Haven.Core.ImagineMediaKind.Image);
        Assert.Equal("Export image", export.Content);

        scene.SetMode(Haven.Core.ImagineMediaKind.Audio);
        Assert.Equal("Export project", export.Content);
        Assert.Contains("Full mixed-timeline playback is not yet available", scene.ModeHint.Content, StringComparison.OrdinalIgnoreCase);

        scene.SetMode(Haven.Core.ImagineMediaKind.Video);
        Assert.Equal("Export project", export.Content);
        Assert.Contains("Continuous native video playback is not yet available", scene.ModeHint.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(scene.Root.DescendantsAndSelf().OfType<Button>(), button => button.Name == "TimelineExportVideoClip" && button.Content == "Export selected clip");
    }

    [Fact]
    public void Project_release_scene_does_not_advertise_unwired_debug_lsp_or_problems_controls()
    {
        using var scene = new ProjectHavenScene();
        var labels = scene.Root.DescendantsAndSelf().OfType<Button>()
            .Select(button => button.Content?.ToString() ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(labels, value => value.Contains("Debug", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(labels, value => value.Contains("LSP", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(labels, value => value.Contains("Problems", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Vision_narrow_layout_preserves_preview_and_question_panel()
    {
        var scene = new VisionScene();
        scene.SetViewportWidth(700);

        Assert.Equal("1fr", scene.Body.Columns);
        Assert.Equal("Auto Auto", scene.Body.Rows);
        Assert.Equal(HavenVisibility.Visible, scene.Preview.GetValue(HavenProperties.Visibility));
        Assert.Equal(1, scene.Body.Children[1].GetValue(HavenProperties.Row));
    }

    private static void AssertNamedControls(HavenElement root)
    {
        foreach (var button in root.DescendantsAndSelf().OfType<Button>())
        {
            var content = button.Content?.ToString();
            var accessible = button.Accessibility.AccessibleName;
            Assert.True(!string.IsNullOrWhiteSpace(content) || !string.IsNullOrWhiteSpace(accessible), $"Button '{button.Name}' has no readable label or accessible name.");
        }

        foreach (var input in root.DescendantsAndSelf().OfType<Input>())
        {
            Assert.True(!string.IsNullOrWhiteSpace(input.Placeholder) || !string.IsNullOrWhiteSpace(input.Accessibility.AccessibleName), $"Input '{input.Name}' has no placeholder or accessible name.");
        }
    }
}
