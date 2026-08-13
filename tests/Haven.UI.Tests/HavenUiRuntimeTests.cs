using Haven.UI;
using Haven.UI.Components;
using Xunit;

namespace Haven.UI.Tests;

public sealed class HavenUiRuntimeTests
{
    [Fact]
    public void Markup_builds_scene_and_reports_source_location_for_invalid_property()
    {
        var parser = new HavenMarkupParser();
        var root = Assert.IsType<Page>(parser.Parse("<Page><Container Layout=\"Horizontal\"><Button Name=\"Save\" Content=\"Save\" /></Container></Page>", "sample.hui"));
        Assert.Equal("Save", Assert.IsType<Button>(root.Children[0].Children[0]).Content);
        var error = Assert.Throws<HavenMarkupException>(() => parser.Parse("<Page><Button Banana=\"1\" /></Page>", "bad.hui"));
        Assert.Contains("bad.hui", error.Message, StringComparison.Ordinal);
        Assert.True(error.Line > 0);
    }

    [Fact] public void Page_markup_rejects_local_reusable_class_declarations() { var error = Assert.Throws<HavenMarkupException>(() => new HavenMarkupParser().Parse("<Page><Class Name=\"Oops\" /></Page>")); Assert.Contains("central resource files", error.Message, StringComparison.Ordinal); }

    [Fact]
    public void Resource_lookup_prefers_user_animation_and_class_cascade_is_ordered()
    {
        var resources = new HavenResourceSet("Class Strong { Opacity = 0.8; }", "Class Strong { Opacity = 0.7; } Class Final { Opacity = 0.6; }", "Animation Pulse { Duration = 100ms; 0% { Opacity = 0; } 100% { Opacity = 1; } }", "Animation Pulse { Duration = 200ms; 0% { Opacity = 1; } 100% { Opacity = 0; } }");
        var button = new Button { Class = "Strong,Final" }; resources.ApplyClasses(button);
        Assert.Equal(0.6, button.GetValue(HavenProperties.Opacity)); Assert.Equal(TimeSpan.FromMilliseconds(200), resources.ResolveAnimation("Pulse").Duration);
    }

    [Fact]
    public void Input_router_owns_hover_press_focus_and_safe_click_actions()
    {
        var root = new Container(); var button = new Button { Name = "Action", Content = "Action" }; var target = new Toggle { Name = "Target" }; root.Add(button); root.Add(target);
        button.ClickActions.Add(HavenAction.Parse("Name.Target -> Checked=True"));
        new HavenLayoutEngine().Layout(root, new HavenSize(300, 160), HavenPlatform.Windows, new FixedMeasure());
        var router = new HavenInputRouter(root); var point = new HavenPoint(button.Bounds.X + 2, button.Bounds.Y + 2);
        router.PointerMoved(point); Assert.True(button.State.HasFlag(HavenElementState.Hover)); router.PointerPressed(point); Assert.True(button.State.HasFlag(HavenElementState.Pressed)); Assert.True(router.PointerReleased(point)); Assert.True(target.IsChecked); Assert.True(button.State.HasFlag(HavenElementState.Focused));
    }

    [Fact]
    public void Scene_renderer_emits_haven_commands_not_avalonia_controls()
    {
        var root = new Container(); var button = new Button { Content = "Save" }; root.Add(button); new HavenLayoutEngine().Layout(root, new HavenSize(300, 100), HavenPlatform.Windows, new FixedMeasure());
        var commands = new HavenSceneRenderer().Render(root); Assert.Contains(commands, command => command is HavenFillRoundedRectCommand); Assert.Contains(commands, command => command is HavenTextCommand text && text.Layout.Text == "Save");
    }

    [Fact] public void Unsafe_actions_are_rejected() { var root = new Container(); var button = new Button { Name = "Action" }; root.Add(button); var action = HavenAction.Parse("Name.Action -> Content=Injected"); Assert.Throws<InvalidOperationException>(() => new HavenActionExecutor().Execute(root, action)); }

    private sealed class FixedMeasure : IHavenMeasureContext { public HavenSize MeasureLeaf(HavenElement element, HavenSize available) => element is Button ? new HavenSize(120, 48) : new HavenSize(100, 30); }
}
