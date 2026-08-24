using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using Haven.UI.Components;
using HavenPage = Haven.UI.Components.Page;

namespace Haven.Desktop.Tests;

public sealed class HavenAnimationBackendTests
{
    [AvaloniaFact]
    public void Button_hover_morphs_in_and_out_on_the_single_haven_surface()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero));
        var root = new HavenPage();
        root.SetValue(HavenProperties.Background, "Surface");
        root.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(28)));
        var button = new Haven.UI.Components.Button { Content = "Animated hover" };
        root.Add(button);
        var scene = new HavenSceneControl(
            new HavenAvaloniaImageResolver(),
            new HavenAvaloniaNativeControlResolver(),
            () => false,
            clock)
        { Root = root };
        var window = new Window { Width = 420, Height = 180, Content = scene };
        try
        {
            window.Show();
            button.SetState(HavenElementState.Hover, true);
            Assert.True(scene.HasActiveAnimations);
            Assert.Equal(1d, button.GetValue(HavenProperties.Scale), 4);

            clock.Advance(TimeSpan.FromMilliseconds(90));
            Assert.True(scene.AdvanceAnimationFrame());
            Assert.InRange(button.GetValue(HavenProperties.Scale), 1.001d, 1.0179d);
            var commands = new HavenSceneRenderer().Render(root);
            var buttonFills = commands.OfType<HavenFillRoundedRectCommand>().Where(command => command.Bounds == button.Bounds).ToArray();
            Assert.Contains(buttonFills, command => command.Brush == new HavenTokenBrush("Accent") && command.Opacity is > 0 and < 1);
            Assert.Contains(buttonFills, command => command.Brush == new HavenTokenBrush("AccentHover") && command.Opacity is > 0 and < 1);
            Assert.Contains(commands, command => command is HavenGlowCommand { Glow.Opacity: > 0 and < 1 });

            var captureDirectory = Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(captureDirectory))
            {
                Directory.CreateDirectory(captureDirectory);
                var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                var path = Path.Combine(captureDirectory, "haven-scene-pass-e-hover-mid.png");
                frame.Save(path);
                Assert.True(new FileInfo(path).Length > 2_000);
            }

            clock.Advance(TimeSpan.FromMilliseconds(90));
            Assert.False(scene.AdvanceAnimationFrame());
            Assert.Equal(1.018d, button.GetValue(HavenProperties.Scale), 4);

            button.SetState(HavenElementState.Hover, false);
            Assert.True(scene.HasActiveAnimations);
            clock.Advance(TimeSpan.FromMilliseconds(90));
            Assert.True(scene.AdvanceAnimationFrame());
            Assert.InRange(button.GetValue(HavenProperties.Scale), 1.0001d, 1.0179d);
            clock.Advance(TimeSpan.FromMilliseconds(90));
            Assert.False(scene.AdvanceAnimationFrame());
            Assert.Equal(1d, button.GetValue(HavenProperties.Scale), 4);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Reduced_motion_applies_hover_target_without_starting_the_frame_timer()
    {
        var root = new HavenPage();
        var button = new Haven.UI.Components.Button { Content = "Reduced motion" };
        root.Add(button);
        var scene = new HavenSceneControl(
            new HavenAvaloniaImageResolver(),
            new HavenAvaloniaNativeControlResolver(),
            () => true,
            new ManualTimeProvider(DateTimeOffset.UtcNow))
        { Root = root };
        var window = new Window { Width = 320, Height = 140, Content = scene };
        try
        {
            window.Show();
            button.SetState(HavenElementState.Hover, true);

            Assert.False(scene.HasActiveAnimations);
            Assert.Equal(1.018d, button.GetValue(HavenProperties.Scale), 4);
            var commands = new HavenSceneRenderer().Render(root);
            Assert.Contains(commands, command => command is HavenFillRoundedRectCommand { Brush: HavenTokenBrush { Token: "AccentHover" } });
            Assert.Contains(commands, command => command is HavenGlowCommand);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Slider_value_change_is_interpolated_by_the_backend_coordinator()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero));
        var root = new HavenPage();
        var slider = new Haven.UI.Components.Slider { Minimum = 0, Maximum = 100, Value = 0 };
        root.Add(slider);
        var scene = new HavenSceneControl(
            new HavenAvaloniaImageResolver(),
            new HavenAvaloniaNativeControlResolver(),
            () => false,
            clock)
        { Root = root };
        var window = new Window { Width = 420, Height = 160, Content = scene };
        try
        {
            window.Show();
            slider.Value = 100;

            Assert.True(scene.HasActiveAnimations);
            Assert.Equal(0d, slider.Value, 4);
            clock.Advance(TimeSpan.FromMilliseconds(60));
            Assert.True(scene.AdvanceAnimationFrame());
            Assert.InRange(slider.Value, 74.9d, 75.1d);
            clock.Advance(TimeSpan.FromMilliseconds(60));
            Assert.False(scene.AdvanceAnimationFrame());
            Assert.Equal(100d, slider.Value, 4);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Removing_actively_animated_child_does_not_reenter_motion_capture()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 17, 17, 0, 0, TimeSpan.Zero));
        var root = new HavenPage();
        var button = new Haven.UI.Components.Button { Content = "Animated removal" };
        root.Add(button);
        var scene = new HavenSceneControl(
            new HavenAvaloniaImageResolver(),
            new HavenAvaloniaNativeControlResolver(),
            () => false,
            clock)
        { Root = root };
        var window = new Window { Width = 320, Height = 140, Content = scene };
        try
        {
            window.Show();
            button.SetState(HavenElementState.Hover, true);
            Assert.True(scene.HasActiveAnimations);

            Assert.True(root.Remove(button));

            Assert.False(scene.HasActiveAnimations);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
