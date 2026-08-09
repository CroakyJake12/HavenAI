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
using Haven.Desktop.HavenUI.Components;
using Haven.Desktop.Views;
using Haven.Desktop.Views.Pages.Settings;

namespace Haven.Desktop.Tests;

/// <summary>
/// Represents generative ui advanced handoff view tests and keeps its related state and behavior together.
/// </summary>
public sealed class GenerativeUiAdvancedHandoffViewTests
{
    /// <summary>
    /// Ensures the superseded theme Studio/handoff is not reachable from normal
    /// Settings after the canonical HavenUI appearance migration.
    /// </summary>
    [AvaloniaFact]
    public void SettingsDoesNotLoadSupersededThemeStudioOrAdvancedHandoff()
    {
        var settings = new SettingsView();
        var window = new Window { Content = settings };
        try
        {
            window.Show();
            var children = settings.GetVisualDescendants().ToArray();
            Assert.Single(children.OfType<HavenAppearanceSettingsView>());
            Assert.Empty(children.OfType<GenerativeUiThemeSelectorView>());
            Assert.Empty(children.OfType<GenerativeUiAdvancedPageHandoffView>());
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
            Assert.Single(handoff.GetVisualDescendants().OfType<HavenButton>());
        }
        finally
        {
            secondWindow.Close();
            handoff.Dispose();
        }
    }
}
