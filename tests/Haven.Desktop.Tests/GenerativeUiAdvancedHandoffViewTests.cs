/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/GenerativeUiAdvancedHandoffViewTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns GenerativeUiAdvancedHandoffViewTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Haven.Desktop.Views;

namespace Haven.Desktop.Tests;

/// <summary>
/// Represents generative ui advanced handoff view tests and keeps its related state and behavior together.
/// </summary>
public sealed class GenerativeUiAdvancedHandoffViewTests
{
    /// <summary>
    /// Performs the settings loads exactly one reviewed advanced handoff after theme studio step owned by this component.
    /// </summary>
    [AvaloniaFact]
    public void SettingsLoadsExactlyOneReviewedAdvancedHandoffAfterThemeStudio()
    {
        var settings = new SettingsView();
        var window = new Window { Content = settings };
        try
        {
            window.Show();
            var children = settings.GetVisualDescendants().ToArray();
            var selector = Assert.Single(children.OfType<GenerativeUiThemeSelectorView>());
            var handoff = Assert.Single(children.OfType<GenerativeUiAdvancedPageHandoffView>());

            var selectorIndex = Array.IndexOf(children, selector);
            var handoffIndex = Array.IndexOf(children, handoff);
            Assert.True(selectorIndex >= 0);
            Assert.True(handoffIndex > selectorIndex);

            var labels = handoff.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(item => item.Text ?? string.Empty)
                .ToArray();
            Assert.Contains("Build with Haven Studio", labels);
            Assert.Contains(labels, value => value.Contains("Nothing is created or installed automatically", StringComparison.Ordinal));
        }
        finally
        {
            window.Close();
            settings.Dispose();
        }
    }

    /// <summary>
    /// Performs the handoff view can detach and reattach without being disposed step owned by this component.
    /// </summary>
    [AvaloniaFact]
    public void HandoffViewCanDetachAndReattachWithoutBeingDisposed()
    {
        var handoff = new GenerativeUiAdvancedPageHandoffView();
        var firstWindow = new Window { Content = handoff };
        firstWindow.Show();
        firstWindow.Content = null;
        firstWindow.Close();

        var secondWindow = new Window { Content = handoff };
        try
        {
            secondWindow.Show();
            Assert.True(handoff.IsVisible);
            Assert.Single(handoff.GetVisualDescendants().OfType<Button>());
        }
        finally
        {
            secondWindow.Close();
            handoff.Dispose();
        }
    }
}
