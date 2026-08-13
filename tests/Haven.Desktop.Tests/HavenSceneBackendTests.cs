using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using Haven.UI.Components;
using HavenPage = Haven.UI.Components.Page;

namespace Haven.Desktop.Tests;

public sealed class HavenSceneBackendTests
{
    [AvaloniaFact]
    public void One_backend_surface_hosts_and_lays_out_a_complete_haven_scene()
    {
        var root = new HavenPage { Layout = HavenLayout.Grid, Columns = "1fr 2fr", Rows = "Auto" };
        root.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        root.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(20)));
        root.SetValue(HavenProperties.Background, "Surface");
        root.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);
        var primary = new Haven.UI.Components.Button { Content = "Primary" };
        var secondary = new Haven.UI.Components.Button { Content = "Secondary", Variant = ButtonVariant.Secondary };
        secondary.SetValue(HavenProperties.Column, 1);
        root.Add(primary);
        root.Add(secondary);

        var scene = new HavenSceneControl { Root = root };
        var window = new Window { Width = 320, Height = 200, Content = scene };
        try
        {
            window.Show();

            Assert.Equal(320, root.Bounds.Width);
            Assert.Equal(200, root.Bounds.Height);
            Assert.True(primary.Bounds.Width >= primary.DesiredSize.Width);
            Assert.True(secondary.Bounds.Width >= secondary.DesiredSize.Width);
            Assert.True(secondary.Bounds.Width > primary.Bounds.Width);
            Assert.Empty(scene.GetVisualChildren());
            var commands = new HavenSceneRenderer().Render(root);
            Assert.Equal(3, commands.OfType<HavenFillRoundedRectCommand>().Count());
            Assert.Equal(2, commands.OfType<HavenTextCommand>().Count());

            var captureDirectory = Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(captureDirectory))
            {
                Directory.CreateDirectory(captureDirectory);
                var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                var capturePath = Path.Combine(captureDirectory, "haven-scene-pass-c.png");
                frame.Save(capturePath);
                Assert.True(new FileInfo(capturePath).Length > 1_000);
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Dynamically_added_scene_nodes_join_backend_invalidation_and_layout()
    {
        var root = new HavenPage();
        var scene = new HavenSceneControl { Root = root };
        var window = new Window { Width = 240, Height = 120, Content = scene };
        try
        {
            window.Show();
            var added = new Text("Added after attach");
            root.Add(added);
            scene.Measure(new Avalonia.Size(240, 120));
            scene.Arrange(new Avalonia.Rect(0, 0, 240, 120));

            Assert.True(added.Bounds.Width > 0);
            Assert.True(added.Bounds.Height > 0);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }
}
