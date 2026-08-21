using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Overlay;

namespace Haven.Desktop.Tests;

public sealed class HavenSceneControlMeasureTests
{
    [AvaloniaFact]
    public void Auto_row_measure_preserves_intrinsic_Haven_scene_height()
    {
        using var scene = new OverlayShellHavenScene();
        var control = new HavenSceneControl { Root = scene.Root };
        var host = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Width = 900,
            Height = 640
        };
        host.Children.Add(control);
        host.Children.Add(new Border { [Grid.RowProperty] = 1 });

        host.Measure(new Size(900, 640));
        host.Arrange(new Rect(0, 0, 900, 640));

        Assert.True(control.DesiredSize.Height > 1, $"Expected intrinsic Overlay shell height, got {control.DesiredSize.Height}.");
        Assert.True(control.Bounds.Height > 1, $"Expected non-collapsed Overlay shell row, got {control.Bounds.Height}.");
    }
}