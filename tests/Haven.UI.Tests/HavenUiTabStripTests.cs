using Haven.UI;
using Haven.UI.Components;
using Xunit;

namespace Haven.UI.Tests;

public sealed class HavenUiTabStripTests
{
    [Fact]
    public void Text_tabs_use_selected_underline_and_context_affordance()
    {
        var strip = new TabStrip();
        strip.SetItems([
            new TabStripItem("go", "Go", IsSelected: true, HasContextMenu: true),
            new TabStripItem("new", "New tab", IsSelected: false, HasContextMenu: false)
        ]);

        Assert.Equal(2, strip.Items.Count);
        Assert.Equal(2, strip.ItemButtons.Count);
        Assert.Equal(2, strip.SelectionIndicators.Count);
        Assert.Equal("Transparent", strip.ItemButtons[0].GetValue(HavenProperties.Background));
        Assert.Equal("AccentSecondary", strip.ItemButtons[0].GetValue(HavenProperties.Foreground));
        Assert.Equal("Accent", strip.SelectionIndicators[0].GetValue(HavenProperties.Background));
        Assert.Equal("Transparent", strip.SelectionIndicators[1].GetValue(HavenProperties.Background));
        Assert.Equal("Right-click for tab options", strip.ItemButtons[0].Accessibility.Description);
        Assert.Equal("Tab", strip.ItemButtons[1].Accessibility.Description);
    }
}
