using Haven.UI.Components;
using Xunit;

namespace Haven.UI.Tests;

public sealed class DynamicUiRenderTests
{
    [Fact]
    public void Runtime_created_items_participate_in_normal_layout_and_rendering_and_update_in_place()
    {
        var templates = new HavenDynamicUITemplateCatalog();
        templates.Register("<DynamicUI Name=\"Message\"><Text Name=\"Body\">{{TEXT}}</Text></DynamicUI>");
        var page = new HavenMarkupParser().Parse("<Page><DynamicUIRuntime Name=\"Messages\"/></Page>");
        var item = new DynamicUI(page, templates).CreateItem("Message", "Messages", "M1", new Dictionary<string, object?> { ["TEXT"] = "Hello" });
        var body = item.GetComponent<Text>("Body");
        var layout = new HavenLayoutEngine();
        var renderer = new HavenSceneRenderer();

        layout.Layout(page, new HavenSize(640, 360), HavenPlatform.Windows, new FixedMeasure());
        Assert.Contains(renderer.Render(page).OfType<HavenTextCommand>(), command => command.Layout.Text == "Hello");
        Assert.True(body.Bounds.Width > 0);

        item.SetVariable("TEXT", "Updated");
        layout.Layout(page, new HavenSize(640, 360), HavenPlatform.Windows, new FixedMeasure());

        Assert.Same(body, item.GetComponent<Text>("Body"));
        Assert.DoesNotContain(renderer.Render(page).OfType<HavenTextCommand>(), command => command.Layout.Text == "Hello");
        Assert.Contains(renderer.Render(page).OfType<HavenTextCommand>(), command => command.Layout.Text == "Updated");
    }

    private sealed class FixedMeasure : IHavenMeasureContext
    {
        public HavenSize MeasureLeaf(HavenElement element, HavenSize available) =>
            new(Math.Min(120, available.Width), Math.Min(24, available.Height));
    }
}
