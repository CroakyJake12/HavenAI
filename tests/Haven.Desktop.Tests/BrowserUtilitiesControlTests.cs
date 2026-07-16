using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Tests;

public sealed class BrowserUtilitiesControlTests
{
    [AvaloniaFact]
    public void UtilityClusterExposesFindZoomPolicyAndSafetyFlyouts()
    {
        using var control = new BrowserUtilitiesControl();
        var window = new Window { Content = control };
        try
        {
            window.Show();
            var buttons = control.Children.OfType<Button>().ToArray();

            Assert.Equal(4, buttons.Length);
            Assert.All(buttons, button => Assert.NotNull(button.Flyout));
            Assert.Equal("⌕", buttons[0].Content);
            Assert.Equal("100%", buttons[1].Content);
            Assert.Equal("○", buttons[2].Content);
            Assert.Equal("⚑", buttons[3].Content);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void FindAndZoomFlyoutsContainInteractiveControls()
    {
        using var control = new BrowserUtilitiesControl();
        var buttons = control.Children.OfType<Button>().ToArray();
        var find = Assert.IsType<Flyout>(buttons[0].Flyout);
        var zoom = Assert.IsType<Flyout>(buttons[1].Flyout);

        var findPanel = Assert.IsType<StackPanel>(find.Content);
        var zoomPanel = Assert.IsType<StackPanel>(zoom.Content);

        Assert.Contains(findPanel.Children, child => child is TextBox);
        Assert.Contains(zoomPanel.Children, child => child is Grid grid
            && grid.Children.OfType<Slider>().Any(slider => slider.Minimum == 50 && slider.Maximum == 200));
    }
}
