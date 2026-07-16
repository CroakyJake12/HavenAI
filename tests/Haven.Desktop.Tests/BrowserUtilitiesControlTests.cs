using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Tests;

public sealed class BrowserUtilitiesControlTests
{
    [AvaloniaFact]
    public void UtilityClusterExposesFindZoomPolicyToolsAndSafetyFlyouts()
    {
        using var control = new BrowserUtilitiesControl();
        var window = new Window { Content = control };
        try
        {
            window.Show();
            var buttons = control.Children.OfType<Button>().ToArray();

            Assert.Equal(5, buttons.Length);
            Assert.All(buttons, button => Assert.NotNull(button.Flyout));
            Assert.Equal("⌕", buttons[0].Content);
            Assert.Equal("100%", buttons[1].Content);
            Assert.Equal("○", buttons[2].Content);
            Assert.Equal("⋯", buttons[3].Content);
            Assert.Equal("⚑", buttons[4].Content);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void FindZoomPolicyAndToolsFlyoutsContainRealInteractiveControls()
    {
        using var control = new BrowserUtilitiesControl();
        var buttons = control.Children.OfType<Button>().ToArray();
        var find = Assert.IsType<Flyout>(buttons[0].Flyout);
        var zoom = Assert.IsType<Flyout>(buttons[1].Flyout);
        var policy = Assert.IsType<Flyout>(buttons[2].Flyout);
        var tools = Assert.IsType<Flyout>(buttons[3].Flyout);

        var findPanel = Assert.IsType<StackPanel>(find.Content);
        var zoomPanel = Assert.IsType<StackPanel>(zoom.Content);
        var policyPanel = Assert.IsType<StackPanel>(policy.Content);
        var toolsPanel = Assert.IsType<StackPanel>(tools.Content);

        Assert.Contains(findPanel.Children, child => child is TextBox);
        Assert.Contains(zoomPanel.Children, child => child is Grid grid
            && grid.Children.OfType<Slider>().Any(slider => slider.Minimum == 50 && slider.Maximum == 200));
        Assert.Contains(policyPanel.Children.OfType<TextBlock>(), text => text.Text == "LATEST BROWSER STATUS");
        Assert.Contains(toolsPanel.Children.OfType<Button>(), button => button.Content?.ToString() == "Print current page");
        Assert.Contains(toolsPanel.Children.OfType<Button>(), button => button.Content?.ToString() == "Open developer tools");
    }

    [AvaloniaFact]
    public void DetachAndReattachKeepsUtilityControlReusable()
    {
        using var control = new BrowserUtilitiesControl();
        var firstWindow = new Window { Content = control };
        firstWindow.Show();
        firstWindow.Content = null;
        firstWindow.Close();

        var secondWindow = new Window { Content = control };
        try
        {
            secondWindow.Show();
            Assert.Equal(5, control.Children.OfType<Button>().Count());
            Assert.All(control.Children.OfType<Button>(), button => Assert.NotNull(button.Flyout));
        }
        finally
        {
            secondWindow.Close();
        }
    }
}
