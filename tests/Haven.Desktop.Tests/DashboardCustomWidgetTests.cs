using Haven.Desktop.Views.Pages.Home;

namespace Haven.Desktop.Tests;

public sealed class DashboardCustomWidgetTests
{
    [Fact]
    public void Custom_widget_maps_to_fixed_local_non_executable_view()
    {
        var id = Guid.NewGuid().ToString("N");
        var widget = new DashboardCustomWidgetDefinition(id, "Revision", "3 topics", "Before Friday");
        var view = NewDashboardPage.ToCustomWidgetView(widget);
        Assert.Equal($"custom:{id}", view.Definition.Key);
        Assert.Equal("custom-local", view.Definition.ProviderKey);
        Assert.False(view.Definition.IsBuiltIn);
        Assert.Equal("3 topics", view.Data?.Primary);
        Assert.Equal("Before Friday", view.Data?.Secondary);
        Assert.True(NewDashboardPage.TryGetCustomWidgetId(view.Definition.ActionKey, out var parsed));
        Assert.Equal(id, parsed);
    }

    [Fact]
    public void Custom_widget_action_parser_rejects_external_or_malformed_actions()
    {
        Assert.False(NewDashboardPage.TryGetCustomWidgetId("https://example.com", out _));
        Assert.False(NewDashboardPage.TryGetCustomWidgetId("dashboard-custom-edit:not-a-guid", out _));
        Assert.False(NewDashboardPage.TryGetCustomWidgetId("launch:calculator", out _));
    }

    [Fact]
    public void Custom_widgets_round_trip_independently_per_dashboard_page()
    {
        var home = new DashboardCustomWidgetDefinition(Guid.NewGuid().ToString("N"), "Home note", "A", "one");
        var focus = new DashboardCustomWidgetDefinition(Guid.NewGuid().ToString("N"), "Focus metric", "42", "two");
        var state = new DashboardCustomWidgetState(1, new Dictionary<string, List<DashboardCustomWidgetDefinition>>
        {
            ["home"] = [home],
            ["focus"] = [focus]
        });
        var json = System.Text.Json.JsonSerializer.Serialize(state);
        var restored = System.Text.Json.JsonSerializer.Deserialize<DashboardCustomWidgetState>(json);
        Assert.NotNull(restored);
        Assert.Equal(home.Id, restored.Pages["home"].Single().Id);
        Assert.Equal(focus.Id, restored.Pages["focus"].Single().Id);
        Assert.Equal("42", restored.Pages["focus"].Single().Value);
    }
}
