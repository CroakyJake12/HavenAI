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

    [Fact]
    public void Transition_morphs_layout_and_visual_properties_then_reveals_target_state()
    {
        var element = new Container();
        element.SetValue(HavenProperties.Width, HavenLength.Px(100));
        element.SetValue(HavenProperties.Padding, HavenThickness.Parse("4px 8px"));
        element.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(8)));
        element.SetValue(HavenProperties.Background, "Surface");
        var definition = new HavenTransitionDefinition("Morph", TimeSpan.FromMilliseconds(200), "Linear", ["Width", "Padding", "Radius", "Background"], 1);
        var engine = new HavenAnimationEngine();
        var from = engine.Capture(element, definition.Properties, includeAnimationValues: false);

        element.SetValue(HavenProperties.Width, HavenLength.Px(200));
        element.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px 20px"));
        element.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(20)));
        element.SetValue(HavenProperties.Background, "Accent");
        var to = engine.Capture(element, definition.Properties, includeAnimationValues: false);
        var start = DateTimeOffset.UtcNow;

        Assert.True(engine.StartTransition(element, definition, from, to, start));
        Assert.True(engine.Tick(start.AddMilliseconds(100)));
        Assert.Equal(HavenLength.Px(150), element.GetValue(HavenProperties.Width));
        Assert.Equal(HavenThickness.Parse("8px 14px"), element.GetValue(HavenProperties.Padding));
        Assert.Equal(HavenCornerRadius.Uniform(HavenLength.Px(14)), element.GetValue(HavenProperties.Radius));

        new HavenLayoutEngine().Layout(element, new HavenSize(400, 200), HavenPlatform.Windows, new FixedMeasure());
        var fills = new HavenSceneRenderer().Render(element).OfType<HavenFillRoundedRectCommand>().ToArray();
        Assert.Contains(fills, command => command.Brush == new HavenTokenBrush("Surface") && Math.Abs(command.Opacity - .5) < .001);
        Assert.Contains(fills, command => command.Brush == new HavenTokenBrush("Accent") && Math.Abs(command.Opacity - .5) < .001);

        Assert.False(engine.Tick(start.AddMilliseconds(200)));
        Assert.Equal(HavenLength.Px(200), element.GetValue(HavenProperties.Width));
        Assert.Equal("Accent", element.GetValue(HavenProperties.Background));
    }

    [Fact]
    public void Lifecycle_reports_start_interruption_and_completion()
    {
        var element = new Container();
        var engine = new HavenAnimationEngine();
        var events = new List<(string Name, HavenAnimationLifecycleState State)>();
        engine.LifecycleChanged += (_, args) => events.Add((args.Name, args.State));
        var first = new HavenAnimationDefinition("First", TimeSpan.FromMilliseconds(100), "Linear", [new HavenAnimationKeyframe(0, new Dictionary<string, string> { ["Opacity"] = "0" }), new HavenAnimationKeyframe(100, new Dictionary<string, string> { ["Opacity"] = "1" })], 1);
        var second = first with { Name = "Second" };
        var start = DateTimeOffset.UtcNow;

        engine.Start(element, first, start);
        engine.Start(element, second, start.AddMilliseconds(25));
        Assert.False(engine.Tick(start.AddMilliseconds(125)));

        Assert.Equal([
            ("First", HavenAnimationLifecycleState.Started),
            ("First", HavenAnimationLifecycleState.Cancelled),
            ("Second", HavenAnimationLifecycleState.Started),
            ("Second", HavenAnimationLifecycleState.Completed)
        ], events);
    }

    [Fact]
    public void Reduced_motion_completes_without_scheduling_frames()
    {
        var element = new Container();
        element.SetValue(HavenProperties.Opacity, .4);
        var definition = new HavenTransitionDefinition("Opacity", TimeSpan.FromSeconds(2), "EaseOut", ["Opacity"], 1);
        var engine = new HavenAnimationEngine { MotionPolicy = new HavenMotionPolicy(ReducedMotion: true) };
        var from = engine.Capture(element, definition.Properties, includeAnimationValues: false);
        element.SetValue(HavenProperties.Opacity, 1d);
        var to = engine.Capture(element, definition.Properties, includeAnimationValues: false);

        Assert.False(engine.StartTransition(element, definition, from, to, DateTimeOffset.UtcNow));
        Assert.False(engine.HasActiveAnimations);
        Assert.Equal(1d, element.GetValue(HavenProperties.Opacity));
    }

    [Fact]
    public void Toggle_thumb_uses_boolean_transition_progress_without_reverting_semantic_state()
    {
        var toggle = new Toggle();
        new HavenLayoutEngine().Layout(toggle, new HavenSize(58, 30), HavenPlatform.Windows, new FixedMeasure());
        var definition = new HavenTransitionDefinition("Toggle", TimeSpan.FromMilliseconds(100), "Linear", ["Toggle.Checked"], 1);
        var engine = new HavenAnimationEngine();
        var from = engine.Capture(toggle, definition.Properties, includeAnimationValues: false);
        toggle.IsChecked = true;
        var to = engine.Capture(toggle, definition.Properties, includeAnimationValues: false);
        var start = DateTimeOffset.UtcNow;

        Assert.True(engine.StartTransition(toggle, definition, from, to, start));
        engine.Tick(start.AddMilliseconds(50));
        Assert.True(toggle.IsChecked);
        var thumb = Assert.Single(new HavenSceneRenderer().Render(toggle).OfType<HavenEllipseCommand>());
        Assert.Equal(17, thumb.Rect.X, 3);
    }

    [Fact]
    public void Resource_files_separate_transitions_from_keyframes_and_user_resources_override_by_kind()
    {
        var resources = new HavenResourceSet(
            string.Empty,
            string.Empty,
            "Transition Hover { Duration=100ms; Properties=Opacity; } Animation Pulse { Duration=100ms; 0% { Opacity=0; } 100% { Opacity=1; } }",
            "Transition Hover { Duration=250ms; Easing=EaseInOut; Properties=Opacity,Scale; } Animation Pulse { Duration=300ms; 0% { Opacity=1; } 100% { Opacity=0; } }");

        Assert.Equal(TimeSpan.FromMilliseconds(250), resources.ResolveTransition("Hover").Duration);
        Assert.Equal(["Opacity", "Scale"], resources.ResolveTransition("Hover").Properties);
        Assert.Equal(TimeSpan.FromMilliseconds(300), resources.ResolveAnimation("Pulse").Duration);
    }

    [Theory]
    [InlineData("Wobble", "Transition Wobble { Duration=100ms; Properties=Opacity; Easing=Banana; }")]
    [InlineData("Wobble", "Animation Wobble { Duration=100ms; 110% { Opacity=1; } }")]
    public void Malformed_motion_resources_report_useful_errors(string name, string source)
    {
        var error = Assert.Throws<FormatException>(() => new HavenResourceSet(string.Empty, string.Empty, source, string.Empty));
        Assert.Contains(name, error.Message, StringComparison.Ordinal);
        Assert.Contains("SystemAnimations.hui", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cubic_bezier_easing_is_validated_and_monotonic_for_ui_motion()
    {
        HavenEasing.Validate("CubicBezier(0.2, 0.8, 0.2, 1)");
        var samples = Enumerable.Range(0, 11).Select(index => HavenEasing.Evaluate(index / 10d, "CubicBezier(0.2, 0.8, 0.2, 1)")).ToArray();
        Assert.Equal(0, samples[0]);
        Assert.Equal(1, samples[^1]);
        Assert.True(samples.Zip(samples.Skip(1)).All(pair => pair.First <= pair.Second));
    }

    private sealed class FixedMeasure : IHavenMeasureContext
    {
        public HavenSize MeasureLeaf(HavenElement element, HavenSize available) => new(120, 48);
    }
}
