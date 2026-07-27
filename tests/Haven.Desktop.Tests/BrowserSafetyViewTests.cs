/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/BrowserSafetyViewTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns BrowserSafetyViewTests. Read the type and member comments below as a map of each responsibility.
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
/// Represents browser safety view tests and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserSafetyViewTests
{
    /// <summary>
    /// Performs the safety surface exposes permission administration alongside existing queues step owned by this component.
    /// </summary>
    [AvaloniaFact]
    public void SafetySurfaceExposesPermissionAdministrationAlongsideExistingQueues()
    {
        var view = new BrowserSafetyView();
        var window = new Window { Content = view };
        try
        {
            window.Show();
            var tabs = FindDescendants<TabItem>(view).ToArray();
            Assert.Equal(new[] { "Permissions", "Approvals", "Audit", "Downloads" }, tabs.Select(tab => tab.Header?.ToString()).ToArray());
            Assert.Contains(FindDescendants<Button>(view), button => button.Content?.ToString() == "Save decision");
            Assert.Contains(FindDescendants<Button>(view), button => button.Content?.ToString() == "Reset origin to Ask");
            Assert.True(FindDescendants<ComboBox>(view).Count() >= 2);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Performs the detach and reattach keeps permission surface reusable step owned by this component.
    /// </summary>
    [AvaloniaFact]
    public void DetachAndReattachKeepsPermissionSurfaceReusable()
    {
        var view = new BrowserSafetyView();
        var first = new Window { Content = view };
        first.Show();
        first.Content = null;
        first.Close();

        var second = new Window { Content = view };
        try
        {
            second.Show();
            Assert.Contains(FindDescendants<TabItem>(view), tab => tab.Header?.ToString() == "Permissions");
            Assert.Contains(FindDescendants<Button>(view), button => button.Content?.ToString() == "Save decision");
        }
        finally
        {
            second.Close();
        }
    }

    private static IEnumerable<T> FindDescendants<T>(Control root) where T : Control
    {
        foreach (var child in root.GetVisualChildren().OfType<Control>())
        {
            if (child is T match) yield return match;
            foreach (var nested in FindDescendants<T>(child)) yield return nested;
        }
    }
}
