using Avalonia;
using Avalonia.Controls;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Overlay;

namespace Haven.Desktop.Tests;

public sealed class OverlayWorkspaceWindowTests
{
    [Fact]
    public void Visual_root_is_shell_only_even_when_execution_backends_are_present()
    {
        var shell = new HavenSceneControl();
        var chatBackend = new Border();
        var goBackend = new Border();

        var root = OverlayWorkspaceWindow.CreateVisualRoot(shell, chatBackend, goBackend);

        Assert.Same(shell, root);
        Assert.NotSame(chatBackend, root);
        Assert.NotSame(goBackend, root);
    }

    [Fact]
    public void Restored_position_stays_on_negative_coordinate_secondary_monitor()
    {
        var secondaryWorkingArea = new PixelRect(-1920, 0, 1920, 1080);
        var desired = new PixelPoint(-1500, 120);

        var clamped = OverlayWorkspaceWindow.ClampPositionToWorkingArea(
            desired,
            secondaryWorkingArea,
            1,
            480,
            420);

        Assert.Equal(desired, clamped);
    }

    [Fact]
    public void Offscreen_restored_position_is_clamped_inside_monitor_working_area()
    {
        var workingArea = new PixelRect(0, 0, 1920, 1080);

        var clamped = OverlayWorkspaceWindow.ClampPositionToWorkingArea(
            new PixelPoint(5000, 3000),
            workingArea,
            1.5,
            480,
            420);

        Assert.Equal(new PixelPoint(1200, 450), clamped);
    }
}
