using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using Haven.UI.Components;
using HavenImageComponent = Haven.UI.Components.Image;
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
            Assert.Single(scene.GetVisualChildren());
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

    [AvaloniaFact]
    public void Pass_d_backend_renders_effect_geometry_and_packaged_media_commands()
    {
        const string packagedImage = "avares://Haven/Assets/haven-1024.png";
        var root = new HavenPage { Layout = HavenLayout.Grid, Columns = "1fr 1fr 1fr", Rows = "1fr" };
        root.SetValue(HavenProperties.Gap, HavenLength.Px(20));
        root.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(28)));
        root.SetValue(HavenProperties.Background, "Surface");

        var iconCard = new Container { Layout = HavenLayout.Overlay };
        iconCard.SetValue(HavenProperties.Background, "SurfaceRaised");
        iconCard.SetValue(HavenProperties.Shadow, "Card");
        iconCard.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        var icon = new Icon { Key = "search" };
        icon.SetValue(HavenProperties.Width, HavenLength.Px(64));
        icon.SetValue(HavenProperties.Height, HavenLength.Px(64));
        icon.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        icon.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        iconCard.Add(icon);

        var image = new HavenImageComponent { Source = packagedImage, Fit = HavenImageFit.Cover };
        image.SetValue(HavenProperties.Column, 1);
        image.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        image.SetValue(HavenProperties.Clip, true);

        var missingImage = new HavenImageComponent
        {
            Source = "avares://Haven/Assets/pass-d-missing-image.png",
            Fit = HavenImageFit.Contain
        };
        missingImage.SetValue(HavenProperties.Column, 2);
        missingImage.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));

        root.Add(iconCard);
        root.Add(image);
        root.Add(missingImage);

        var resolver = new HavenAvaloniaImageResolver();
        Assert.True(resolver.TryResolve(packagedImage, out var decoded));
        Assert.NotNull(decoded);
        Assert.False(resolver.TryResolve("file:///C:/untrusted.png", out _));
        Assert.False(resolver.TryResolve("https://example.invalid/image.png", out _));

        var scene = new HavenSceneControl(resolver) { Root = root };
        var window = new Window { Width = 720, Height = 300, Content = scene };
        try
        {
            window.Show();

            Assert.Equal(new HavenSize(720, 300), scene.SurfaceMetrics.Viewport);
            Assert.Equal(HavenPlatform.Windows, scene.SurfaceMetrics.Platform);
            Assert.True(scene.SurfaceMetrics.RenderScale > 0);
            Assert.Equal(720 * scene.SurfaceMetrics.RenderScale, scene.SurfaceMetrics.PixelSize.Width);
            Assert.Single(scene.GetVisualChildren());

            var commands = new HavenSceneRenderer().Render(root);
            Assert.Contains(commands, command => command is HavenShadowCommand);
            Assert.Contains(commands, command => command is HavenIconCommand { Key: "search" });
            Assert.Contains(commands, command => command is HavenImageCommand { Layout: HavenImageLayout.Cover });
            Assert.Equal(2, commands.OfType<HavenImageCommand>().Count());

            var captureDirectory = Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(captureDirectory))
            {
                Directory.CreateDirectory(captureDirectory);
                var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                var capturePath = Path.Combine(captureDirectory, "haven-scene-pass-d.png");
                frame.Save(capturePath);
                Assert.True(new FileInfo(capturePath).Length > 2_000);
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Only_explicit_native_media_enters_the_avalonia_bridge()
    {
        var root = new HavenPage { Layout = HavenLayout.Canvas };
        var button = new Haven.UI.Components.Button { Content = "Still Haven drawn" };
        button.SetValue(HavenProperties.Width, HavenLength.Px(140));
        button.SetValue(HavenProperties.Height, HavenLength.Px(48));
        root.Add(button);

        var video = new Video { Source = "capability://preview" };
        video.SetValue(HavenProperties.Left, HavenLength.Px(40));
        video.SetValue(HavenProperties.Top, HavenLength.Px(70));
        video.SetValue(HavenProperties.Width, HavenLength.Px(180));
        video.SetValue(HavenProperties.Height, HavenLength.Px(90));
        root.Add(video);

        var nativeResolver = new RecordingNativeControlResolver();
        var scene = new HavenSceneControl(new HavenAvaloniaImageResolver(), nativeResolver) { Root = root };
        var window = new Window { Width = 320, Height = 200, Content = scene };
        try
        {
            window.Show();

            Assert.Equal(2, scene.GetVisualChildren().Count());
            var native = Assert.Single(scene.GetVisualChildren().OfType<Border>());
            Assert.IsType<Border>(native);
            Assert.Equal(new Avalonia.Rect(40, 70, 180, 90), native.Bounds);
            Assert.Same(video, Assert.Single(nativeResolver.RequestedElements));
            Assert.DoesNotContain(nativeResolver.RequestedElements, element => element is Haven.UI.Components.Button);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private sealed class RecordingNativeControlResolver : IHavenAvaloniaNativeControlResolver
    {
        public List<HavenElement> RequestedElements { get; } = [];

        public bool TryCreate(HavenElement element, out Control? control)
        {
            RequestedElements.Add(element);
            control = element is Video ? new Border() : null;
            return control is not null;
        }
    }
}
