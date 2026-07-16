using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Haven.Desktop.Views;

namespace Haven.Desktop.Tests;

public sealed class GenerativeUiAdvancedHandoffViewTests
{
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
