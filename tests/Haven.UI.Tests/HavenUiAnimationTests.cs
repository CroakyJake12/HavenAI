using Haven.UI;
using Haven.UI.Components;
using Xunit;

namespace Haven.UI.Tests;

public sealed class HavenUiAnimationTests
{
    [Fact]
    public void Keyframe_engine_interpolates_numeric_and_length_values_then_reveals_state()
    {
        var button = new Button();
        button.SetValue(HavenProperties.Scale, 1.2d, HavenValueSource.State);
        var definition = new HavenAnimationDefinition("Move", TimeSpan.FromMilliseconds(100), "Linear", [new HavenAnimationKeyframe(0, new Dictionary<string, string> { ["Scale"] = "1", ["TranslationX"] = "0px" }), new HavenAnimationKeyframe(100, new Dictionary<string, string> { ["Scale"] = "2", ["TranslationX"] = "20px" })], 1);
        var engine = new HavenAnimationEngine();
        var start = DateTimeOffset.UtcNow;
        engine.Start(button, definition, start);
        Assert.True(engine.Tick(start.AddMilliseconds(50)));
        Assert.Equal(1.5d, button.GetValue(HavenProperties.Scale), 3);
        Assert.Equal(HavenLength.Px(10), button.GetValue(HavenProperties.TranslationX));
        Assert.False(engine.Tick(start.AddMilliseconds(100)));
        Assert.Equal(1.2d, button.GetValue(HavenProperties.Scale));
    }

    [Fact]
    public void Transform_properties_emit_framework_independent_push_and_pop_commands()
    {
        var root = new Container();
        var button = new Button { Content = "Bounce" };
        button.SetValue(HavenProperties.Scale, 1.1d);
        root.Add(button);
        new HavenLayoutEngine().Layout(root, new HavenSize(300, 100), HavenPlatform.Windows, new FixedMeasure());
        var commands = new HavenSceneRenderer().Render(root);
        Assert.Contains(commands, command => command is HavenPushTransformCommand);
        Assert.Contains(commands, command => command is HavenPopTransformCommand);
    }

    private sealed class FixedMeasure : IHavenMeasureContext
    {
        public HavenSize MeasureLeaf(HavenElement element, HavenSize available) => new(120, 48);
    }
}
