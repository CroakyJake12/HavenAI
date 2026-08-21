using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Desktop.Views.Shell.TopRail;

namespace Haven.Desktop.Tests;

public sealed class TopRailActionsLabelTests
{
    [AvaloniaFact]
    public void Visible_header_uses_Actions_label()
    {
        using var rail = new TopRail();
        var window = new Window { Width = 1440, Height = 120, Content = rail };
        try
        {
            window.Show();
            window.UpdateLayout();
            var scene = Assert.IsType<TopRailFinalScene>(rail.HavenOwnedScene);
            Assert.Equal("Actions", scene.ActionsButton.Content);
            Assert.Equal("Actions", scene.ActionsButton.Accessibility.AccessibleName);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }
}
