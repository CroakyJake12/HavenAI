using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.HavenUI.Components;
using Haven.Desktop.HavenUI.Tokens;
using Haven.Desktop.HavenUI.Registry;

namespace Haven.Desktop.Tests;

public sealed class HavenUiComponentSystemTests
{
    [Fact]
    public void Every_page_palette_exposes_three_non_flat_gradient_tiers()
    {
        foreach (var surface in Enum.GetValues<HavenSurface>())
        foreach (var appearance in Enum.GetValues<HavenUiAppearance>())
        {
            var accents = SurfacePaletteCatalog.For(surface, appearance).AccentPalette;
            AssertGradient(accents.Primary);
            AssertGradient(accents.Secondary);
            AssertGradient(accents.Tertiary);
            Assert.NotEqual(accents.Primary.Middle, accents.Secondary.Middle);
            Assert.NotEqual(accents.Primary.Middle, accents.Tertiary.Middle);
        }
    }

    [AvaloniaFact]
    public void Accent_switch_mutates_live_gradient_resources_without_rebuilding_controls()
    {
        var button = new HavenPrimaryButton { Content = "Run" };
        var window = new Window { Width = 260, Height = 120, Content = button };
        window.Show();

        HavenUiResourceApplier.Apply(SurfacePaletteCatalog.For(HavenSurface.Tasks, HavenUiAppearance.SuperDark));
        var first = Assert.IsType<LinearGradientBrush>(Avalonia.Application.Current!.Resources["HavenAccentPrimaryBrush"]);
        var tasksColour = first.GradientStops[1].Color;

        HavenUiResourceApplier.Apply(SurfacePaletteCatalog.For(HavenSurface.Studio, HavenUiAppearance.SuperDark));
        var second = Assert.IsType<LinearGradientBrush>(Avalonia.Application.Current.Resources["HavenAccentPrimaryBrush"]);

        Assert.Same(first, second);
        Assert.NotEqual(tasksColour, second.GradientStops[1].Color);
        Assert.Same(button, window.Content);
        window.Close();
    }

    [AvaloniaFact]
    public void Tidal_background_first_frame_uses_requested_dark_palette_without_white_flash()
    {
        var window = new Window { Width = 640, Height = 360 };
        using var tide = new TidalBackground(window, HavenUiAppearance.SuperDark);
        var expected = SurfacePaletteCatalog.For(HavenSurface.Home, HavenUiAppearance.SuperDark);
        var brush = Assert.IsType<LinearGradientBrush>(window.Background);

        Assert.Equal(expected.TideBase, brush.GradientStops[0].Color);
        Assert.Equal(expected.TideBase, brush.GradientStops[1].Color);
        Assert.Equal(expected.TideColour, brush.GradientStops[2].Color);
        Assert.Equal(expected.TideColour, brush.GradientStops[3].Color);
        Assert.DoesNotContain(brush.GradientStops, stop => stop.Color == Colors.White);
        Assert.True(brush.GradientStops[1].Offset < 0.35);
        Assert.True(brush.GradientStops[2].Offset > 0.75);
    }

    [AvaloniaFact]
    public void Typed_button_family_inherits_mockup_geometry_montserrat_and_canonical_roles()
    {
        HavenButtonBase[] buttons =
        [
            new HavenPrimaryButton(),
            new HavenSecondaryButton(),
            new HavenTertiaryButton(),
            new HavenNegativeButton(),
            new HavenTextButton()
        ];
        var window = new Window { Content = new StackPanel { Children = { buttons[0], buttons[1], buttons[2], buttons[3], buttons[4] } } };
        window.Show();

        Assert.All(buttons.Take(4), button =>
        {
            Assert.Equal(48, button.MinHeight);
            Assert.Equal(new CornerRadius(24), button.CornerRadius);
            Assert.Equal(FontWeight.ExtraBold, button.FontWeight);
            Assert.Contains("Montserrat", button.FontFamily?.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Contains("havenPrimary", buttons[0].Classes);
        Assert.Contains("havenSecondary", buttons[1].Classes);
        Assert.Contains("havenTertiary", buttons[2].Classes);
        Assert.Contains("havenNegative", buttons[3].Classes);
        Assert.Contains("havenText", buttons[4].Classes);
        window.Close();
    }

    [AvaloniaFact]
    public void Model_picker_is_the_documented_reasoning_palette_exception()
    {
        var picker = new HavenModelPickerButton { EffortPercentage = 20, Content = "Model" };
        var window = new Window { Content = picker };
        window.Show();

        Assert.Contains("reasoningLow", picker.Classes);
        picker.EffortPercentage = 80;
        Assert.Contains("reasoningHigh", picker.Classes);
        Assert.DoesNotContain("reasoningLow", picker.Classes);
        window.Close();
    }

    [AvaloniaFact]
    public void Scoped_app_accent_changes_nested_components_without_flattening_global_page_accents()
    {
        HavenUiResourceApplier.Apply(SurfacePaletteCatalog.For(HavenSurface.Go, HavenUiAppearance.SuperDark));
        var global = Assert.IsType<LinearGradientBrush>(Avalonia.Application.Current!.Resources["HavenAccentPrimaryBrush"]);
        var globalMiddle = global.GradientStops[1].Color;
        var scope = new HavenAccentScope
        {
            AccentSurface = HavenSurface.Tasks,
            Content = new HavenSuggestionButton { Content = "Review today's tasks" }
        };
        var window = new Window { Width = 420, Height = 140, Content = scope };
        window.Show();

        var local = Assert.IsType<LinearGradientBrush>(scope.Resources["HavenAccentPrimaryBrush"]);
        Assert.NotEqual(globalMiddle, local.GradientStops[1].Color);
        Assert.True(local.GradientStops[1].Color.R > local.GradientStops[1].Color.B);
        Assert.Equal(globalMiddle, global.GradientStops[1].Color);
        scope.AccentSurface = HavenSurface.Studio;
        Assert.Same(local, scope.Resources["HavenAccentPrimaryBrush"]);
        Assert.True(local.GradientStops[1].Color.B > local.GradientStops[1].Color.R);
        window.Close();
    }

    [Fact]
    public void Canonical_family_covers_mockup_and_product_wide_common_controls()
    {
        object[] controls =
        [
            new HavenTextInput(), new HavenSearchInput(), new HavenMultilineInput(),
            new HavenSlider(), new HavenProgressBar(), new HavenSwitch(),
            new HavenComboBox(), new HavenSelect(), new HavenNumericInput(), new HavenCheckBox(), new HavenRadioButton(),
            new HavenCard(), new HavenDropdownCard(), new HavenPopupCard(), new HavenMobileSheet(),
            new HavenDragHandle(), new HavenSelectionIndicator(),
            new HavenComposerShell(), new HavenToolbar(), new HavenNavigationRail(),
            new Haven.Desktop.HavenUI.Components.HavenNotification(), new HavenBadge(), new HavenStatusChip(),
            new HavenLoadingState(), new HavenErrorState(), new HavenContextMenu()
        ];

        Assert.Equal(26, controls.Select(control => control.GetType()).Distinct().Count());
    }

    [AvaloniaFact]
    public void Slider_switch_and_progress_use_haven_owned_mockup_templates()
    {
        var slider = new HavenSlider { Minimum = 0, Maximum = 100, Value = 72, Width = 320 };
        var progress = new HavenProgressBar { Minimum = 0, Maximum = 100, Value = 64, Width = 320 };
        var toggle = new HavenSwitch { IsChecked = true };
        var window = new Window { Width = 420, Height = 220, Content = new StackPanel { Children = { slider, progress, toggle } } };
        window.Show();
        slider.ApplyTemplate();
        progress.ApplyTemplate();
        toggle.ApplyTemplate();

        var thumb = Assert.Single(slider.GetVisualDescendants().OfType<Thumb>());
        var sliderTrack = Assert.Single(slider.GetVisualDescendants().OfType<Track>());
        var visibleTrack = Assert.Single(slider.GetVisualDescendants().OfType<HavenSliderTrack>());
        Assert.Equal(34, thumb.Width);
        Assert.Equal(34, thumb.Height);
        Assert.True(sliderTrack.Bounds.Width >= 300);
        Assert.True(visibleTrack.Bounds.Width >= 280);
        Assert.Equal(18, visibleTrack.Bounds.Height);
        Assert.Equal(72, visibleTrack.Value);
        Assert.IsType<LinearGradientBrush>(visibleTrack.ActiveBrush);
        var track = toggle.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "HavenSwitchTrack");
        var knob = toggle.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "HavenSwitchThumb");
        Assert.Equal(58, track.Width);
        Assert.Equal(22, knob.Width);
        Assert.Equal(42, progress.Height);
        window.Close();
    }

    [AvaloniaFact]
    public void Chat_user_message_template_renders_message_text_instead_of_control_type_names()
    {
        const string content = "Can you generate an interactive study surface?";
        var templates = Haven.UI.Components.HavenDynamicUITemplateCatalog.FromAssembly(typeof(Haven.Desktop.Views.Pages.Chat.ChatHavenScene).Assembly);
        var sceneRoot = new Haven.UI.Components.Container { Name = "ChatRoot" };
        sceneRoot.Add(new Haven.UI.Components.DynamicUIRuntime { Name = "Messages" });
        var runtime = new Haven.UI.Components.DynamicUI(sceneRoot, templates);

        var item = runtime.CreateItem(
            "ChatUserMessage",
            "Messages",
            Guid.NewGuid().ToString("N"),
            new Dictionary<string, object?>
            {
                ["CONTENT"] = content,
                ["AVATAR"] = "avatar://user",
                ["AVATARVISIBILITY"] = "Collapsed"
            });

        var body = item.GetComponent<Haven.UI.Components.Markdown>("Body");
        Assert.NotNull(body);
        Assert.Equal(content, body.Content);
        var roleLabel = item.GetComponent<Haven.UI.Components.Text>("Role");
        Assert.Equal("You", roleLabel.Content);
        Assert.DoesNotContain(
            item.DescendantsAndSelf(),
            element => element.Name?.Contains("Avalonia.Controls.", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Generative_ui_registry_only_creates_trusted_canonical_control_types()
    {
        Assert.True(HavenUiComponentRegistry.All.Count >= 28);
        Assert.Equal(HavenPrimaryButtonType(), HavenUiComponentRegistry.Resolve("HavenPrimaryButton").ControlType);
        Assert.All(HavenUiComponentRegistry.All, descriptor =>
        {
            var control = HavenUiComponentRegistry.Create(descriptor.ComponentType);
            Assert.Equal(descriptor.ControlType, control.GetType());
            Assert.StartsWith("Haven", descriptor.ControlType.Name, StringComparison.Ordinal);
        });
    }

    private static Type HavenPrimaryButtonType() => typeof(HavenPrimaryButton);

    private static void AssertGradient(HavenAccentGradient gradient)
    {
        Assert.NotEqual(gradient.Start, gradient.Middle);
        Assert.NotEqual(gradient.Middle, gradient.End);
        Assert.NotEqual(gradient.Start, gradient.End);
    }
}
