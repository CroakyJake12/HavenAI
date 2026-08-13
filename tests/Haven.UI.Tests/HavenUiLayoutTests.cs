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

    private static Container SizedContainer(double width, double height)
    {
        var container = new Container();
        container.SetValue(HavenProperties.Width, HavenLength.Px(width));
        container.SetValue(HavenProperties.Height, HavenLength.Px(height));
        return container;
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
