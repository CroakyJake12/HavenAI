using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Haven.Desktop.Views;

namespace Haven.Desktop.Tests;

public sealed class BrowserSafetyViewTests
{
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
