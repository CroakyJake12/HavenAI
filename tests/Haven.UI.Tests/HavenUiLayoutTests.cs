using Haven.UI;
using Haven.UI.Components;
using Xunit;

namespace Haven.UI.Tests;

public sealed class HavenUiLayoutTests
{
    [Fact]
    public void Vertical_layout_resolves_margin_and_cross_axis_alignment()
    {
        var root = SizedContainer(200, 120);
        var child = new Text("Aligned") { Name = "aligned" };
        child.SetValue(HavenProperties.Margin, HavenThickness.Parse("10px 5px"));
        child.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        root.Add(child);

        Layout(root, new NamedMeasure(("aligned", new HavenSize(50, 20))));

        Assert.Equal(new HavenRect(75, 10, 50, 20), child.Bounds);
    }

    [Fact]
    public void Vertical_fraction_allocates_the_remaining_main_axis_space()
    {
        var root = SizedContainer(100, 200);
        root.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        var fixedChild = new Text("Fixed") { Name = "fixed" };
        fixedChild.SetValue(HavenProperties.Height, HavenLength.Px(50));
        var flexibleChild = new Container { Name = "flex" };
        flexibleChild.SetValue(HavenProperties.Height, HavenLength.Fr(1));
        root.Add(fixedChild);
        root.Add(flexibleChild);

        Layout(root, new NamedMeasure());

        Assert.Equal(50, fixedChild.Bounds.Height);
        Assert.Equal(140, flexibleChild.Bounds.Height);
        Assert.Equal(60, flexibleChild.Bounds.Y);
    }

    [Fact]
    public void Horizontal_fraction_allocates_remaining_width_by_weight()
    {
        var root = SizedContainer(280, 80);
        root.Layout = HavenLayout.Horizontal;
        root.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        var fixedChild = new Container();
        fixedChild.SetValue(HavenProperties.Width, HavenLength.Px(50));
        var oneFraction = new Container();
        oneFraction.SetValue(HavenProperties.Width, HavenLength.Fr(1));
        var twoFractions = new Container();
        twoFractions.SetValue(HavenProperties.Width, HavenLength.Fr(2));
        root.Add(fixedChild);
        root.Add(oneFraction);
        root.Add(twoFractions);

        Layout(root, new NamedMeasure());

        Assert.Equal(50, fixedChild.Bounds.Width);
        Assert.Equal(70, oneFraction.Bounds.Width);
        Assert.Equal(140, twoFractions.Bounds.Width);
        Assert.Equal(140, twoFractions.Bounds.X);
    }

    [Fact]
    public void Wrap_layout_starts_a_new_line_when_the_next_child_would_overflow()
    {
        var root = SizedContainer(100, 100);
        root.Layout = HavenLayout.Wrap;
        root.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        root.Add(new Text("One"));
        root.Add(new Text("Two"));
        root.Add(new Text("Three"));

        Layout(root, new NamedMeasure());

        Assert.Equal(new HavenRect(0, 0, 40, 20), root.Children[0].Bounds);
        Assert.Equal(new HavenRect(50, 0, 40, 20), root.Children[1].Bounds);
        Assert.Equal(new HavenRect(0, 30, 40, 20), root.Children[2].Bounds);
    }

    [Fact]
    public void Leaf_padding_contributes_to_intrinsic_desired_size()
    {
        var button = new Haven.UI.Components.Button { Content = "Primary" };

        Layout(button, new NamedMeasure((string.Empty, new HavenSize(40, 20))));

        Assert.Equal(96, button.DesiredSize.Width);
        Assert.Equal(48, button.DesiredSize.Height);
    }

    [Fact]
    public void Grid_resolves_fixed_auto_fraction_tracks_and_spans()
    {
        var root = SizedContainer(400, 80);
        root.Layout = HavenLayout.Grid;
        root.Columns = "100px Auto 1fr";
        root.Rows = "Auto Auto";
        root.SetValue(HavenProperties.Gap, HavenLength.Px(10));

        var fixedChild = Cell("fixed", 0, 0);
        var autoChild = Cell("auto", 0, 1);
        var flexChild = Cell("flex", 0, 2);
        var spanningChild = Cell("span", 1, 0);
        spanningChild.SetValue(HavenProperties.ColumnSpan, 3);
        root.Add(fixedChild);
        root.Add(autoChild);
        root.Add(flexChild);
        root.Add(spanningChild);

        Layout(root, new NamedMeasure(
            ("fixed", new HavenSize(80, 20)),
            ("auto", new HavenSize(120, 30)),
            ("flex", new HavenSize(40, 20)),
            ("span", new HavenSize(300, 40))));

        Assert.Equal(new HavenRect(0, 0, 100, 30), fixedChild.Bounds);
        Assert.Equal(new HavenRect(110, 0, 120, 30), autoChild.Bounds);
        Assert.Equal(new HavenRect(240, 0, 160, 30), flexChild.Bounds);
        Assert.Equal(new HavenRect(0, 40, 400, 40), spanningChild.Bounds);
    }

    [Fact]
    public void Grid_row_span_uses_the_complete_spanned_extent()
    {
        var root = SizedContainer(100, 100);
        root.Layout = HavenLayout.Grid;
        root.Columns = "1fr";
        root.Rows = "20px 1fr";
        var child = Cell("span", 0, 0);
        child.SetValue(HavenProperties.RowSpan, 2);
        root.Add(child);

        Layout(root, new NamedMeasure(("span", new HavenSize(40, 80))));

        Assert.Equal(new HavenRect(0, 0, 100, 100), child.Bounds);
    }

    [Fact]
    public void Canvas_positions_children_from_haven_owned_offsets()
    {
        var root = SizedContainer(100, 80);
        root.Layout = HavenLayout.Canvas;
        var child = new Text("Canvas");
        child.SetValue(HavenProperties.Left, HavenLength.Px(15));
        child.SetValue(HavenProperties.Top, HavenLength.Px(25));
        root.Add(child);

        Layout(root, new NamedMeasure());

        Assert.Equal(new HavenRect(15, 25, 40, 20), child.Bounds);
    }

    [Fact]
    public void Overlay_honours_end_and_center_alignment()
    {
        var root = SizedContainer(100, 80);
        root.Layout = HavenLayout.Overlay;
        var child = new Text("Overlay");
        child.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.End);
        child.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        root.Add(child);

        Layout(root, new NamedMeasure());

        Assert.Equal(new HavenRect(60, 30, 40, 20), child.Bounds);
    }

    [Fact]
    public void Flow_is_default_overlay_does_not_reflow_and_genuine_children_still_do()
    {
        var root = new Container();
        var first = new Text("First") { Name = "first" };
        var floating = new Text("Floating") { Name = "floating" };
        floating.SetValue(HavenProperties.LayoutParticipation, HavenLayoutParticipation.Overlay);
        root.Add(first);
        root.Add(floating);
        var measure = new NamedMeasure(
            ("first", new HavenSize(80, 30)),
            ("second", new HavenSize(60, 20)),
            ("floating", new HavenSize(250, 180)));

        Layout(root, measure);

        Assert.Equal(HavenLayoutParticipation.Flow, first.GetValue(HavenProperties.LayoutParticipation));
        Assert.Equal(HavenLayoutParticipation.Overlay, floating.GetValue(HavenProperties.LayoutParticipation));
        Assert.Equal(new HavenSize(80, 30), root.DesiredSize);
        Assert.Equal(new HavenSize(250, 180), floating.DesiredSize);

        root.Add(new Text("Second") { Name = "second" });
        Layout(root, measure);

        Assert.Equal(new HavenSize(80, 50), root.DesiredSize);
    }

    [Fact]
    public void Popup_menu_is_detached_anchored_hit_testable_focusable_and_restores_layout_on_close()
    {
        var root = new Container { Layout = HavenLayout.Overlay };
        var anchor = new Button { Content = "Open menu" };
        anchor.SetValue(HavenProperties.Width, HavenLength.Px(80));
        anchor.SetValue(HavenProperties.Height, HavenLength.Px(40));
        anchor.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Start);
        anchor.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Start);
        root.Add(anchor);
        Layout(root, new NamedMeasure());
        var desiredBeforeOpen = root.DesiredSize;

        var invoked = false;
        var popup = new PopupMenu(anchor, root, [new PopupMenuItem("Open", () => invoked = true)], 120, "Test menu");
        root.Add(popup);
        Layout(root, new NamedMeasure());

        Assert.Equal(HavenLayoutParticipation.Overlay, popup.GetValue(HavenProperties.LayoutParticipation));
        Assert.Equal(desiredBeforeOpen, root.DesiredSize);
        Assert.True(popup.Card.Bounds.Y >= anchor.Bounds.Bottom);
        var menuItem = Assert.IsType<Button>(Assert.Single(popup.Card.Children));
        var point = new HavenPoint(menuItem.Bounds.X + menuItem.Bounds.Width / 2, menuItem.Bounds.Y + menuItem.Bounds.Height / 2);
        var router = new HavenInputRouter(root);
        Assert.Same(menuItem, router.HitTest(point));
        router.PointerPressed(point);
        Assert.Same(menuItem, router.Focused);
        Assert.True(router.PointerReleased(point));
        Assert.True(invoked);
        Assert.DoesNotContain(popup, root.Children);

        Layout(root, new NamedMeasure());
        Assert.Equal(desiredBeforeOpen, root.DesiredSize);
    }

    [Fact]
    public void Popup_menu_keeps_disabled_actions_visible_but_noninteractive()
    {
        var root = SizedContainer(320, 220);
        root.Layout = HavenLayout.Overlay;
        var anchor = new Button { Content = "Open menu" };
        root.Add(anchor);
        Layout(root, new NamedMeasure());

        var invoked = false;
        var popup = new PopupMenu(anchor, root, [new PopupMenuItem("Close", () => invoked = true, Enabled: false)]);
        root.Add(popup);
        Layout(root, new NamedMeasure());

        var item = Assert.IsType<Button>(Assert.Single(popup.Card.Children));
        Assert.False(item.GetValue(HavenProperties.Enabled));
        Assert.True(item.State.HasFlag(HavenElementState.Disabled));
        Assert.NotSame(item, new HavenInputRouter(root).HitTest(new HavenPoint(item.Bounds.X + 4, item.Bounds.Y + 4)));
        Assert.False(invoked);
        Assert.Contains(popup, root.Children);
    }

    [Fact]
    public void Signed_zindex_controls_render_and_hit_order()
    {
        var root = SizedContainer(160, 80);
        root.Layout = HavenLayout.Overlay;
        var high = new Button { Content = "High" };
        var low = new Button { Content = "Low" };
        var normal = new Button { Content = "Default" };
        high.SetValue(HavenProperties.ZIndex, 10);
        low.SetValue(HavenProperties.ZIndex, -10);
        root.Add(high);
        root.Add(normal);
        root.Add(low);

        Layout(root, new NamedMeasure());

        Assert.Equal(0, normal.GetValue(HavenProperties.ZIndex));
        var labels = new HavenSceneRenderer().Render(root)
            .OfType<HavenTextCommand>()
            .Select(command => command.Layout.Text)
            .Where(text => text is "Low" or "Default" or "High")
            .ToArray();
        Assert.Equal(new[] { "Low", "Default", "High" }, labels);
        var point = new HavenPoint(high.Bounds.X + high.Bounds.Width / 2, high.Bounds.Y + high.Bounds.Height / 2);
        Assert.Same(high, new HavenInputRouter(root).HitTest(point));
    }

    [Fact]
    public void Stacked_and_nested_overlays_do_not_corrupt_flow_layout()
    {
        var root = new Container();
        var flow = new Text("Flow") { Name = "flow" };
        var lowerLayer = new Container { Layout = HavenLayout.Overlay };
        lowerLayer.SetValue(HavenProperties.LayoutParticipation, HavenLayoutParticipation.Overlay);
        lowerLayer.SetValue(HavenProperties.ZIndex, 10);
        var lowerButton = new Button { Content = "Lower" };
        lowerLayer.Add(lowerButton);
        var upperButton = new Button { Content = "Upper" };
        upperButton.SetValue(HavenProperties.LayoutParticipation, HavenLayoutParticipation.Overlay);
        upperButton.SetValue(HavenProperties.ZIndex, 20);
        root.Add(flow);
        root.Add(lowerLayer);
        root.Add(upperButton);

        Layout(root, new NamedMeasure(("flow", new HavenSize(100, 30))));

        Assert.Equal(new HavenSize(100, 30), root.DesiredSize);
        var upperPoint = new HavenPoint(upperButton.Bounds.X + upperButton.Bounds.Width / 2, upperButton.Bounds.Y + upperButton.Bounds.Height / 2);
        Assert.Same(upperButton, new HavenInputRouter(root).HitTest(upperPoint));

        Assert.True(root.Remove(upperButton));
        Layout(root, new NamedMeasure(("flow", new HavenSize(100, 30))));
        Assert.Equal(new HavenSize(100, 30), root.DesiredSize);
        var lowerPoint = new HavenPoint(lowerButton.Bounds.X + lowerButton.Bounds.Width / 2, lowerButton.Bounds.Y + lowerButton.Bounds.Height / 2);
        Assert.Same(lowerButton, new HavenInputRouter(root).HitTest(lowerPoint));
    }

    [Fact]
    public void Detached_overlay_stays_in_viewport_while_flow_content_scrolls()
    {
        var root = SizedContainer(100, 100);
        root.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        var firstFlowRow = new Text("Flow 0");
        firstFlowRow.SetValue(HavenProperties.Height, HavenLength.Px(60));
        root.Add(firstFlowRow);
        for (var index = 1; index < 3; index++)
        {
            var row = new Text($"Flow {index}");
            row.SetValue(HavenProperties.Height, HavenLength.Px(60));
            root.Add(row);
        }
        var overlay = new Button { Content = "Overlay" };
        overlay.SetValue(HavenProperties.LayoutParticipation, HavenLayoutParticipation.Overlay);
        overlay.SetValue(HavenProperties.Height, HavenLength.Px(20));
        overlay.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Start);
        root.Add(overlay);

        Layout(root, new NamedMeasure());
        root.ScrollY = 50;
        Layout(root, new NamedMeasure());

        Assert.Equal(new HavenSize(100, 180), root.ExtentSize);
        Assert.Equal(-50, firstFlowRow.Bounds.Y);
        Assert.Equal(0, overlay.Bounds.Y);
    }

    [Fact]
    public void Markup_parses_layout_participation_and_signed_zindex()
    {
        var root = new HavenMarkupParser().Parse("<Page><Container LayoutParticipation='Overlay' ZIndex='-3' /></Page>");
        var child = Assert.IsType<Container>(Assert.Single(root.Children));

        Assert.Equal(HavenLayoutParticipation.Overlay, child.GetValue(HavenProperties.LayoutParticipation));
        Assert.Equal(-3, child.GetValue(HavenProperties.ZIndex));
    }

    [Fact]
    public void Aspect_ratio_reflows_the_auto_dimension_after_size_constraints()
    {
        var child = new Text("Ratio");
        child.SetValue(HavenProperties.Width, HavenLength.Percent(90));
        child.SetValue(HavenProperties.MaxWidth, HavenLength.Px(120));
        child.SetValue(HavenProperties.AspectRatio, 2d);

        Layout(child, new NamedMeasure());

        Assert.Equal(new HavenSize(120, 60), child.DesiredSize);
    }

    [Fact]
    public void Scroll_container_tracks_extent_clamps_offsets_and_translates_content()
    {
        var root = SizedContainer(100, 100);
        root.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        for (var index = 0; index < 3; index++)
        {
            var child = new Text($"Row {index}") { Name = $"row-{index}" };
            child.SetValue(HavenProperties.Height, HavenLength.Px(60));
            root.Add(child);
        }

        Layout(root, new NamedMeasure());
        Assert.Equal(new HavenSize(100, 180), root.ExtentSize);
        Assert.Equal(new HavenSize(100, 100), root.ViewportSize);
        Assert.Equal(80, root.MaxScrollY);

        root.ScrollY = 500;
        Layout(root, new NamedMeasure());
        Assert.Equal(80, root.ScrollY);
        Assert.Equal(-80, root.Children[0].Bounds.Y);
        Assert.Equal(40, root.Children[2].Bounds.Y);
    }

    [Fact]
    public void Input_router_scrolls_the_nearest_scroll_container()
    {
        var root = SizedContainer(100, 100);
        root.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        for (var index = 0; index < 3; index++)
        {
            var child = new Text($"Row {index}");
            child.SetValue(HavenProperties.Height, HavenLength.Px(60));
            root.Add(child);
        }
        Layout(root, new NamedMeasure());

        var router = new HavenInputRouter(root);
        Assert.True(router.Scroll(new HavenPoint(10, 10), 0, 50));
        Assert.Equal(50, root.ScrollY);
    }

    [Fact]
    public void Clipped_overflow_emits_backend_neutral_clip_commands_and_blocks_hits()
    {
        var root = SizedContainer(100, 100);
        root.Layout = HavenLayout.Canvas;
        root.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);
        var outside = new Button { Content = "Outside" };
        outside.SetValue(HavenProperties.Left, HavenLength.Px(150));
        outside.SetValue(HavenProperties.Width, HavenLength.Px(30));
        outside.SetValue(HavenProperties.Height, HavenLength.Px(30));
        root.Add(outside);
        Layout(root, new NamedMeasure());

        var commands = new HavenSceneRenderer().Render(root);
        Assert.Contains(commands, command => command is HavenPushClipCommand);
        Assert.Contains(commands, command => command is HavenPopClipCommand);
        Assert.Null(new HavenInputRouter(root).HitTest(new HavenPoint(155, 10)));
    }

    [Fact]
    public void Required_screen_ranges_re_evaluate_with_the_viewport()
    {
        var root = Assert.IsType<Page>(new HavenMarkupParser().Parse(
            "<Page><Container RequiredScreenWidth='200px,600px' RequiredScreenHeight='300px,900px' /></Page>"));
        var child = Assert.IsType<Container>(root.Children[0]);
        var engine = new HavenLayoutEngine();
        var measure = new NamedMeasure();

        engine.Layout(root, new HavenSize(500, 500), HavenPlatform.Windows, measure);
        Assert.True(child.IsIncluded);
        engine.Layout(root, new HavenSize(700, 500), HavenPlatform.Windows, measure);
        Assert.False(child.IsIncluded);
    }

    [Fact]
    public void Excluded_parent_marks_the_entire_subtree_not_included()
    {
        var root = new Page();
        var parent = new Container();
        parent.Conditions.Add(new HavenScreenRangeCondition(
            HavenScreenAxis.Width,
            maximum: HavenLength.Px(600)));
        var input = new Input { Placeholder = "Nested input" };
        parent.Add(input);
        root.Add(parent);
        var engine = new HavenLayoutEngine();
        var measure = new NamedMeasure();

        engine.Layout(root, new HavenSize(500, 500), HavenPlatform.Windows, measure);
        Assert.True(parent.IsIncluded);
        Assert.True(input.IsIncluded);

        engine.Layout(root, new HavenSize(700, 500), HavenPlatform.Windows, measure);
        Assert.False(parent.IsIncluded);
        Assert.False(input.IsIncluded);
    }

    private static Container SizedContainer(double width, double height)
    {
        var container = new Container();
        container.SetValue(HavenProperties.Width, HavenLength.Px(width));
        container.SetValue(HavenProperties.Height, HavenLength.Px(height));
        return container;
    }

    [Fact]
    public void Responsive_grid_preserves_auto_rows_before_shrinking_fraction_rows()
    {
        var root = SizedContainer(100, 100);
        root.Layout = HavenLayout.Grid;
        root.Rows = "Auto 1fr Auto";
        root.SetValue(HavenProperties.Responsive, true);
        var header = Cell("header", 0, 0);
        var content = Cell("content", 1, 0);
        var footer = Cell("footer", 2, 0);
        root.Add(header); root.Add(content); root.Add(footer);
        Layout(root, new NamedMeasure(("header", new HavenSize(100, 30)), ("content", new HavenSize(100, 100)), ("footer", new HavenSize(100, 20))));
        Assert.Equal(30, header.Bounds.Height);
        Assert.Equal(50, content.Bounds.Height);
        Assert.Equal(20, footer.Bounds.Height);
        Assert.Equal(30, content.Bounds.Y);
        Assert.Equal(80, footer.Bounds.Y);
    }
    private static Text Cell(string name, int row, int column)
    {
        var child = new Text(name) { Name = name };
        child.SetValue(HavenProperties.Row, row);
        child.SetValue(HavenProperties.Column, column);
        return child;
    }

    private static void Layout(HavenElement root, IHavenMeasureContext measure) =>
        new HavenLayoutEngine().Layout(root, new HavenSize(400, 300), HavenPlatform.Windows, measure);

    private sealed class NamedMeasure(params (string Name, HavenSize Size)[] values) : IHavenMeasureContext
    {
        private readonly IReadOnlyDictionary<string, HavenSize> _values = values.ToDictionary(item => item.Name, item => item.Size, StringComparer.Ordinal);

        public HavenSize MeasureLeaf(HavenElement element, HavenSize available)
        {
            var desired = element.Name is { } name && _values.TryGetValue(name, out var value)
                ? value
                : new HavenSize(40, 20);
            return new HavenSize(Math.Min(desired.Width, available.Width), Math.Min(desired.Height, available.Height));
        }
    }
}
