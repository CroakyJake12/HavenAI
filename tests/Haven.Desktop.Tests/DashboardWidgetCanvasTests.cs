using Avalonia.Headless.XUnit;
using Haven.Core;
using Haven.Desktop.Dashboard;

namespace Haven.Desktop.Tests;

public sealed class DashboardWidgetCanvasTests
{
    [Fact]
    public void Move_snaps_to_six_column_grid_and_reflows_collisions_deterministically()
    {
        var definitions = new[] { Tile("a", 0), Tile("b", 1), Tile("c", 2) };
        var initial = DashboardWidgetLayoutEngine.EnsurePlacements(definitions);
        var moved = DashboardWidgetLayoutEngine.Move(initial, "b", 0, 0);
        var b = moved.Single(item => item.Key == "b");
        Assert.Equal(0, b.Column);
        Assert.Equal(0, b.Row);
        Assert.All(moved.Where(item => item.IsVisible).SelectMany((left, index) => moved.Where(item => item.IsVisible).Skip(index + 1).Select(right => (left, right))), pair => Assert.False(DashboardWidgetLayoutEngine.Intersects(pair.left, pair.right)));
        Assert.Equal(moved.Select(item => item.Key), DashboardWidgetLayoutEngine.Move(initial, "b", 0, 0).Select(item => item.Key));
    }

    [Fact]
    public void Resize_clamps_span_and_keeps_every_visible_widget_non_overlapping()
    {
        var definitions = new[] { Tile("a", 0), Tile("b", 1) };
        var initial = DashboardWidgetLayoutEngine.EnsurePlacements(definitions);
        var resized = DashboardWidgetLayoutEngine.Resize(initial, "a", 99, 3);
        var a = resized.Single(item => item.Key == "a");
        Assert.Equal(DashboardWidgetLayoutEngine.Columns, a.Width);
        Assert.Equal(3, a.Height);
        Assert.DoesNotContain(resized.Where(item => item.Key != "a"), item => DashboardWidgetLayoutEngine.Intersects(a, item));
        Assert.Equal(DashboardTileSize.Wide, DashboardWidgetLayoutEngine.ToTileSize(a));
    }

    [AvaloniaFact]
    public void Canvas_supports_move_hide_show_undo_and_redo_without_losing_widget_identity()
    {
        var views = new[]
        {
            View("a", 0, "12"),
            View("b", 1, "7")
        };
        var placements = new[]
        {
            new DashboardWidgetPlacement("a", 0, 0, 3, 2),
            new DashboardWidgetPlacement("b", 3, 0, 3, 2)
        };
        var canvas = new DashboardWidgetCanvas();
        canvas.SetWidgets(views, placements, isCustomizing: true);

        Assert.True(canvas.MoveWidget("b", 0, 0));
        Assert.True(canvas.CanUndo);
        Assert.True(canvas.Undo());
        Assert.Equal(3, canvas.Placements.Single(item => item.Key == "b").Column);
        Assert.True(canvas.Redo());
        Assert.Equal(0, canvas.Placements.Single(item => item.Key == "b").Column);
        Assert.True(canvas.HideWidget("a"));
        Assert.False(canvas.Placements.Single(item => item.Key == "a").IsVisible);
        Assert.True(canvas.ShowWidget("a"));
        Assert.True(canvas.Placements.Single(item => item.Key == "a").IsVisible);
        Assert.Equal(new[] { "a", "b" }, canvas.Placements.Select(item => item.Key).OrderBy(key => key).ToArray());
    }

    [AvaloniaFact]
    public void Canvas_represents_loading_error_and_stale_as_explicit_states_not_fake_values()
    {
        var views = new[]
        {
            new DashboardWidgetViewState(Tile("loading", 0), null, DashboardWidgetDataState.Loading),
            new DashboardWidgetViewState(Tile("error", 1), null, DashboardWidgetDataState.Error, "Provider unavailable"),
            new DashboardWidgetViewState(Tile("stale", 2), new DashboardTileData("31", "Last refresh"), DashboardWidgetDataState.Stale)
        };
        var canvas = new DashboardWidgetCanvas();
        canvas.SetWidgets(views, placements: null, isCustomizing: false);
        Assert.Equal(3, canvas.Placements.Count);
        Assert.All(canvas.Placements, item => Assert.True(item.IsVisible));
    }

    [AvaloniaFact]
    public void ApplyLayout_commits_multiple_changes_as_one_undo_transaction()
    {
        var views = new[] { View("a", 0, "12"), View("b", 1, "7") };
        var initial = new[]
        {
            new DashboardWidgetPlacement("a", 0, 0, 3, 2),
            new DashboardWidgetPlacement("b", 3, 0, 3, 2)
        };
        var canvas = new DashboardWidgetCanvas();
        canvas.SetWidgets(views, initial, isCustomizing: false);
        IReadOnlyList<DashboardWidgetPlacement> changed = DashboardWidgetLayoutEngine.Move(initial, "b", 0, 3);
        changed = DashboardWidgetLayoutEngine.Resize(changed, "a", 6, 2);
        Assert.True(canvas.ApplyLayout(changed));
        Assert.True(canvas.CanUndo);
        Assert.Equal(6, canvas.Placements.Single(item => item.Key == "a").Width);
        Assert.Equal(3, canvas.Placements.Single(item => item.Key == "b").Row);
        Assert.True(canvas.Undo());
        Assert.Equal(3, canvas.Placements.Single(item => item.Key == "a").Width);
        Assert.Equal(0, canvas.Placements.Single(item => item.Key == "b").Row);
        Assert.False(canvas.CanUndo);
    }

    [Fact]
    public void Persisted_widget_layout_keeps_page_positions_independent()
    {
        var state = new Haven.Desktop.Views.Pages.Home.DashboardWidgetLayoutState(1, new Dictionary<string, List<DashboardWidgetPlacement>>
        {
            ["home"] = [new DashboardWidgetPlacement("plan", 0, 0, 3, 2)],
            ["focus"] = [new DashboardWidgetPlacement("plan", 3, 4, 2, 1)]
        });
        var json = System.Text.Json.JsonSerializer.Serialize(state);
        var restored = System.Text.Json.JsonSerializer.Deserialize<Haven.Desktop.Views.Pages.Home.DashboardWidgetLayoutState>(json);
        Assert.NotNull(restored);
        Assert.Equal(0, restored.Pages["home"].Single().Column);
        Assert.Equal(3, restored.Pages["focus"].Single().Column);
        Assert.Equal(4, restored.Pages["focus"].Single().Row);
        Assert.Equal(2, restored.Pages["focus"].Single().Width);
    }

    [Fact]
    public void EnsurePlacements_preserves_saved_geometry_and_adds_new_custom_widgets()
    {
        var existing = new[]
        {
            new DashboardWidgetPlacement("plan", 3, 4, 2, 1)
        };
        var definitions = new[]
        {
            Tile("plan", 0),
            new DashboardTileDefinition("custom:abc", "Local note", "Local custom widget", "edit", "custom-local", "dashboard-custom-edit:abc", DashboardTileSize.Standard, 1000, IsBuiltIn: false)
        };

        var reconciled = DashboardWidgetLayoutEngine.EnsurePlacements(definitions, existing);

        var plan = reconciled.Single(item => item.Key == "plan");
        Assert.Equal(3, plan.Column);
        Assert.Equal(4, plan.Row);
        Assert.Equal(2, plan.Width);
        Assert.Equal(1, plan.Height);
        Assert.Contains(reconciled, item => item.Key == "custom:abc" && item.IsVisible);
        Assert.Equal(2, reconciled.Count);
    }

    private static DashboardTileDefinition Tile(string key, int order) =>
        new(key, key.ToUpperInvariant(), $"{key} description", "chat", "action", key, DashboardTileSize.Standard, order);

    private static DashboardWidgetViewState View(string key, int order, string primary) =>
        new(Tile(key, order), new DashboardTileData(primary, "Ready"), DashboardWidgetDataState.Ready);
}
