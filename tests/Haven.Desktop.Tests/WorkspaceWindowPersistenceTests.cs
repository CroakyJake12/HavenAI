using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

public sealed class WorkspaceWindowPersistenceTests
{
    [AvaloniaFact]
    public void ApplyWindowBounds_restores_valid_geometry_and_clamps_to_window_minimums()
    {
        var window = new Window { MinWidth = 420, MinHeight = 320, Width = 640, Height = 480 };

        WorkspaceWindowService.ApplyWindowBounds(window, """{"X":120,"Y":80,"Width":300,"Height":200}""");

        Assert.Equal(420, window.Width);
        Assert.Equal(320, window.Height);
        Assert.Equal(120, window.Position.X);
        Assert.Equal(80, window.Position.Y);
    }

    [AvaloniaFact]
    public void ApplyWindowBounds_clamps_oversized_geometry_to_common_restore_size()
    {
        var window = new Window { MinWidth = 420, MinHeight = 320, Width = 640, Height = 480 };

        WorkspaceWindowService.ApplyWindowBounds(window, """{"X":120,"Y":80,"Width":6000,"Height":3000}""");

        Assert.Equal(1600, window.Width);
        Assert.Equal(1000, window.Height);
    }

    [AvaloniaFact]
    public void ApplyWindowBounds_ignores_corrupt_or_invalid_geometry()
    {
        var window = new Window { MinWidth = 420, MinHeight = 320, Width = 640, Height = 480 };

        WorkspaceWindowService.ApplyWindowBounds(window, "not-json");
        WorkspaceWindowService.ApplyWindowBounds(window, """{"X":0,"Y":0,"Width":-10,"Height":0}""");

        Assert.Equal(640, window.Width);
        Assert.Equal(480, window.Height);
    }
}
