/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/BrowserUtilitiesControlTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns BrowserUtilitiesControlTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Tests;

/// <summary>
/// Represents browser utilities control tests and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserUtilitiesControlTests
{
    /// <summary>
    /// Performs the utility cluster exposes find zoom policy tools and safety flyouts step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the find zoom policy and tools flyouts contain real interactive controls step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the detach and reattach keeps utility control reusable step owned by this component.
    /// </summary>
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
