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

    [Fact]
    public void Tabs_expose_semantics_and_keyboard_navigation()
    {
        var root = new Page();
        var strip = new TabStrip();
        strip.SetItems([
            new TabStripItem("one", "One"),
            new TabStripItem("two", "Two", IsSelected: true),
            new TabStripItem("three", "Three")
        ]);
        root.Add(strip);
        var router = new HavenInputRouter(root);
        var invoked = new List<string>();
        strip.ItemInvoked += (_, key) => invoked.Add(key);

        Assert.Equal(HavenAccessibleRole.Tab, strip.Accessibility.Role);
        Assert.Equal(HavenAccessibleRole.TabItem, strip.ItemButtons[0].Accessibility.Role);
        Assert.True(strip.ItemButtons[1].Accessibility.Selected);
        router.Focus(strip.ItemButtons[1]);

        Assert.True(router.KeyDown(HavenKey.Right));
        Assert.Same(strip.ItemButtons[2], router.Focused);
        Assert.Equal(new[] { "three" }, invoked);
        Assert.True(router.KeyUp(HavenKey.Right));
        Assert.Single(invoked);

        Assert.True(router.KeyDown(HavenKey.Home));
        Assert.Same(strip.ItemButtons[0], router.Focused);
        Assert.Equal(new[] { "three", "one" }, invoked);

        Assert.True(router.KeyDown(HavenKey.Left));
        Assert.Same(strip.ItemButtons[2], router.Focused);
        Assert.Equal(new[] { "three", "one", "three" }, invoked);

        Assert.True(router.KeyDown(HavenKey.End));
        Assert.Equal(3, invoked.Count);
    }

    [Fact]
    public void Item_refresh_preserves_scroll_and_measured_overflow_state()
    {
        var strip = new TabStrip();
        var items = Enumerable.Range(0, 6)
            .Select(i => new TabStripItem($"tab-{i}", $"Tab {i}"))
            .ToArray();
        strip.SetItems(items);
        var scroller = Assert.IsType<Container>(strip.Children[1]);
        var left = Assert.IsType<Button>(strip.Children[0]);
        var right = Assert.IsType<Button>(strip.Children[2]);

        static void SetMetrics(Container container, HavenSize viewport, HavenSize extent)
        {
            var method = typeof(Container).GetMethod("UpdateScrollMetrics", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(container, new object?[] { viewport, extent });
        }

        SetMetrics(scroller, new HavenSize(120, 54), new HavenSize(600, 54));
        scroller.ScrollX = 160;
        strip.SetItems(items);

        Assert.Equal(160d, strip.ScrollOffset);
        Assert.Equal(HavenVisibility.Visible, left.GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Visible, right.GetValue(HavenProperties.Visibility));

        SetMetrics(scroller, new HavenSize(700, 54), new HavenSize(600, 54));
        strip.SetItems(items);

        Assert.Equal(0d, strip.ScrollOffset);
        Assert.Equal(HavenVisibility.Collapsed, left.GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Collapsed, right.GetValue(HavenProperties.Visibility));
    }
}
