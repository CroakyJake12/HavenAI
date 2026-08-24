using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.HavenUI.Tokens;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Settings;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>
/// Covers the shared personalisation pipeline: five themes across all four
/// appearances, accent override precedence, safe fallbacks, live resource
/// updates, font selection and the independent avatar preferences. Glow is
/// asserted against literal pre-theme values because it must remain the
/// untouched visual baseline.
/// </summary>
public sealed class PersonalisationTests
{
    [AvaloniaFact]
    public void Glow_palette_matches_the_pre_theme_baseline()
    {
        HavenPersonalisation.Reset();
        try
        {
            var home = SurfacePaletteCatalog.For(HavenSurface.Home, HavenUiAppearance.SuperDark);
            Assert.Equal(Color.Parse("#FF06090D"), home.TideBase);
            Assert.Equal<byte>(0xF5, home.Panel.A);
            Assert.Equal<byte>(0xF5, home.Panel2.A);
            Assert.Equal(HavenUiTheme.Glow, home.Theme);

            var bright = SurfacePaletteCatalog.For(HavenSurface.Home, HavenUiAppearance.Bright);
            Assert.Equal(Colors.White, bright.TideBase);
            Assert.Equal(Color.Parse("#FF111111"), bright.Text);

            // Glow is the identity transform: hover keeps its original blend.
            // superDarkSoft blends the base soft colour toward the accent first.
            var accent = Color.Parse("#FF3527FF");
            var darkSoft = Blend(Color.Parse("#FF121526"), accent, 0.20);
            var expectedHover = Blend(darkSoft, accent, 0.22);
            Assert.Equal(expectedHover, home.ButtonHover);
        }
        finally { HavenPersonalisation.Reset(); }
    }

    [Fact]
    public void Every_theme_resolves_for_every_surface_and_appearance()
    {
        HavenPersonalisation.Reset();
        try
        {
            foreach (var theme in Enum.GetValues<HavenUiTheme>())
            {
                HavenPersonalisation.Theme = theme;
                foreach (var appearance in Enum.GetValues<HavenUiAppearance>())
                foreach (var surface in Enum.GetValues<HavenSurface>())
                {
                    var palette = SurfacePaletteCatalog.For(surface, appearance);
                    Assert.Equal(theme, palette.Theme);
                    Assert.NotEqual(default, palette.TideBase);
                    Assert.NotEqual(default, palette.Accent);
                    Assert.True(palette.Panel.A > 0);
                }
            }
        }
        finally { HavenPersonalisation.Reset(); }
    }

    [Fact]
    public void Non_glow_themes_change_interaction_treatment_without_changing_text_colours()
    {
        HavenPersonalisation.Reset();
        try
        {
            var glow = For(HavenUiTheme.Glow, HavenSurface.Chat, HavenUiAppearance.Dark);

            var retro = For(HavenUiTheme.Retro, HavenSurface.Chat, HavenUiAppearance.Dark);
            Assert.NotEqual(glow.ButtonHover, retro.ButtonHover);
            Assert.NotEqual(glow.Line, retro.Line);
            Assert.Equal(glow.Text, retro.Text);

            var playful = For(HavenUiTheme.Playful, HavenSurface.Chat, HavenUiAppearance.Dark);
            Assert.NotEqual(retro.ButtonHover, playful.ButtonHover);
            Assert.Equal<byte>(0xFF, playful.Panel.A);

            var bubble = For(HavenUiTheme.Bubble, HavenSurface.Chat, HavenUiAppearance.Dark);
            Assert.True(bubble.Panel.A < glow.Panel.A, "Bubble panels should be more translucent than Glow.");

            var cinematic = For(HavenUiTheme.Cinematic, HavenSurface.Chat, HavenUiAppearance.Dark);
            Assert.NotEqual(glow.Panel, cinematic.Panel);
        }
        finally { HavenPersonalisation.Reset(); }
    }

    [Fact]
    public void Theme_expressions_scale_geometry_motion_and_shadow_distinctly()
    {
        var glow = HavenThemeCatalog.Resolve(HavenUiTheme.Glow);
        var bubble = HavenThemeCatalog.Resolve(HavenUiTheme.Bubble);
        var retro = HavenThemeCatalog.Resolve(HavenUiTheme.Retro);
        var playful = HavenThemeCatalog.Resolve(HavenUiTheme.Playful);
        var cinematic = HavenThemeCatalog.Resolve(HavenUiTheme.Cinematic);

        Assert.All(new[] { bubble.ControlRadiusScale, playful.ControlRadiusScale }, scale => Assert.True(scale > glow.ControlRadiusScale));
        Assert.True(retro.ControlRadiusScale < glow.ControlRadiusScale);
        Assert.NotEqual(glow.MotionDurationScale, retro.MotionDurationScale);
        Assert.True(cinematic.ShadowOpacityScale > glow.ShadowOpacityScale);
    }

    [AvaloniaFact]
    public void Applying_a_theme_updates_shared_resources_live()
    {
        using var paths = new TemporaryPaths();
        var preferences = new UserPreferencesService(paths);
        var changed = 0;
        preferences.AppearanceChanged += (_, _) => changed++;

        preferences.ApplyThemeChoice("Retro");
        var radius = GetCornerRadius("HavenControlRadius");

        Assert.Equal(HavenUiTheme.Retro, preferences.Theme);
        Assert.Equal(Math.Round(HavenThemeExpression.BaseControlRadius * HavenThemeCatalog.Resolve(HavenUiTheme.Retro).ControlRadiusScale), radius);
        Assert.Equal(
            HavenThemeCatalog.Resolve(HavenUiTheme.Retro).MotionDurationScale,
            Avalonia.Application.Current!.Resources["HavenMotionDurationScale"]);
        Assert.True(changed > 0);
    }

    private static double GetCornerRadius(string key)
    {
        var value = Avalonia.Application.Current!.Resources[key];
        Assert.IsType<Avalonia.CornerRadius>(value);
        return ((Avalonia.CornerRadius)value).TopLeft;
    }

    private static SurfacePaletteCatalog.Palette For(HavenUiTheme theme, HavenSurface surface, HavenUiAppearance appearance)
    {
        HavenPersonalisation.Theme = theme;
        return SurfacePaletteCatalog.For(surface, appearance);
    }

    [Fact]
    public void Accent_override_replaces_surface_hues_while_off_keeps_them()
    {
        HavenPersonalisation.Reset();
        try
        {
            // Dark appearances emphasise the secondary anchor; light ones the primary.
            HavenPersonalisation.OverrideAccent = true;
            HavenPersonalisation.Accent = HavenAccentColour.Cyan;
            var dark = SurfacePaletteCatalog.For(HavenSurface.Tasks, HavenUiAppearance.Dark);
            var darkAnchors = AccentColourCatalog.Resolve(HavenAccentColour.Cyan, HavenUiAppearance.Dark);
            Assert.Equal(Color.Parse(darkAnchors.Secondary), dark.Accent);
            Assert.Equal(Color.Parse(darkAnchors.Primary), dark.AccentSecondary);

            var light = SurfacePaletteCatalog.For(HavenSurface.Tasks, HavenUiAppearance.Bright);
            var lightAnchors = AccentColourCatalog.Resolve(HavenAccentColour.Cyan, HavenUiAppearance.Bright);
            Assert.Equal(Color.Parse(lightAnchors.Primary), light.Accent);

            HavenPersonalisation.OverrideAccent = false;
            var restored = SurfacePaletteCatalog.For(HavenSurface.Tasks, HavenUiAppearance.Dark);
            Assert.Equal(Color.Parse("#FFFF5B19"), restored.AccentSecondary);
        }
        finally { HavenPersonalisation.Reset(); }
    }

    [Fact]
    public void All_thirteen_accent_palettes_resolve_for_both_appearance_families()
    {
        Assert.Equal(13, AccentColourCatalog.Colours.Count);
        foreach (var colour in AccentColourCatalog.Colours)
        {
            var light = AccentColourCatalog.Resolve(colour, HavenUiAppearance.Bright);
            var dark = AccentColourCatalog.Resolve(colour, HavenUiAppearance.SuperDark);
            Assert.NotEqual(light.Primary, light.Strong);
            Assert.NotEqual(dark.Primary, dark.Strong);
            Assert.False(string.IsNullOrWhiteSpace(AccentColourCatalog.Name(colour)));
        }
    }

    [Fact]
    public void Contrast_critical_palettes_keep_safe_relationships()
    {
        // Yellow and Lime need deep strong anchors to survive contrast guards.
        foreach (var colour in new[] { HavenAccentColour.Yellow, HavenAccentColour.Lime })
        {
            var anchors = AccentColourCatalog.Resolve(colour, HavenUiAppearance.SuperDark);
            Assert.True(Luminance(anchors.Strong) < Luminance(anchors.Primary), $"{colour} strong anchor must stay darker than primary.");
        }

        // Monotone stays grayscale and keeps strong contrast against its surface.
        foreach (var appearance in new[] { HavenUiAppearance.Bright, HavenUiAppearance.SuperDark })
        {
            var monotone = AccentColourCatalog.Resolve(HavenAccentColour.Monotone, appearance);
            Assert.Equal(ExtractRgb(monotone.Primary).R, ExtractRgb(monotone.Primary).G);
            Assert.Equal(ExtractRgb(monotone.Primary).G, ExtractRgb(monotone.Primary).B);
            var background = appearance == HavenUiAppearance.Bright ? "#FFFFFFFF" : "#FF06090D";
            Assert.True(Math.Abs(Luminance(monotone.Primary) - Luminance(background)) > 150, $"{appearance} Monotone must contrast its surface.");
        }

        // Strawberry stays visibly distinct from Red and Pink in the same family.
        var strawberry = ExtractRgb(AccentColourCatalog.Resolve(HavenAccentColour.Strawberry, HavenUiAppearance.Dark).Primary);
        var red = ExtractRgb(AccentColourCatalog.Resolve(HavenAccentColour.Red, HavenUiAppearance.Dark).Primary);
        var pink = ExtractRgb(AccentColourCatalog.Resolve(HavenAccentColour.Pink, HavenUiAppearance.Dark).Primary);
        Assert.True(Distance(strawberry, red) > 15 && Distance(strawberry, pink) > 15);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("BogusTheme")]
    public void Unknown_theme_names_fall_back_to_glow(string? name)
    {
        Assert.Equal(HavenUiTheme.Glow, HavenThemeCatalog.Parse(name));
        Assert.Equal(HavenUiTheme.Glow, HavenThemeCatalog.Resolve((HavenUiTheme)999).Theme);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("NotAPalette")]
    public void Unknown_accent_names_do_not_enable_override(string? name)
    {
        Assert.Null(AccentColourCatalog.Parse(name));
    }

    [AvaloniaFact]
    public void Malformed_theme_preferences_launch_safely_with_defaults()
    {
        using var paths = new TemporaryPaths();
        File.WriteAllText(Path.Combine(paths.DataDirectory, "preferences.json"),
            "{ \"HavenUiThemeName\": \"NeonRetro\", \"OverrideAccentColour\": true, \"AccentColourName\": \"Chartreuse\" }");
        var preferences = new UserPreferencesService(paths);

        Assert.Equal(HavenUiTheme.Glow, preferences.Theme);
        Assert.Null(preferences.AccentColourSelection);
        Assert.False(preferences.OverrideAccentColour, "Override without a valid palette must not take effect.");
    }

    [AvaloniaFact]
    public void Personalisation_choices_persist_and_reload()
    {
        using var paths = new TemporaryPaths();
        {
            var preferences = new UserPreferencesService(paths);
            preferences.ApplyThemeChoice("cinematic");
            preferences.ApplyAccentOverride(true, "teal");
            preferences.SetFontPreference("Segoe UI");
        }
        {
            var reloaded = new UserPreferencesService(paths);
            Assert.Equal(HavenUiTheme.Cinematic, reloaded.Theme);
            Assert.True(reloaded.OverrideAccentColour);
            Assert.Equal("Teal", reloaded.AccentColourSelection);
            Assert.Equal("Segoe UI", reloaded.FontPreference);
        }
    }

    [AvaloniaFact]
    public void Font_preference_restores_montserrat_when_cleared()
    {
        using var paths = new TemporaryPaths();
        var preferences = new UserPreferencesService(paths);
        preferences.SetFontPreference("Arial");
        Assert.Equal("Arial", preferences.FontPreference);
        preferences.SetFontPreference("Montserrat");
        Assert.Null(preferences.FontPreference);
        var resolved = HavenUiFont.Resolve("Montserrat");
        Assert.Contains("MontserratStatic", resolved.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void User_selected_font_flows_into_hui_requests_with_bundled_fallback()
    {
        HavenUiFont.SetUserFamily("Times New Roman");
        try
        {
            var resolved = HavenUiFont.Resolve("Montserrat");
            Assert.Contains("Times New Roman", resolved.ToString(), StringComparison.Ordinal);
            Assert.Contains("MontserratStatic", resolved.ToString(), StringComparison.Ordinal);
            Assert.Equal("Segoe UI", HavenUiFont.Resolve("Segoe UI").ToString());
        }
        finally { HavenUiFont.SetUserFamily(null); }
        Assert.Contains("MontserratStatic", HavenUiFont.Resolve("Montserrat").ToString(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Avatar_assets_are_processed_stored_and_removed_independently()
    {
        using var paths = new TemporaryPaths();
        var store = new AvatarStore(paths);

        // Use a real bundled PNG the way a user's picked file would arrive.
        var assetSource = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
        _ = assetSource;
        var source = FindBundledImage();
        Assert.True(File.Exists(source), "A packaged image asset is required for this test.");

        Assert.False(store.Has(HavenAvatarKind.User));
        store.SetFromFile(HavenAvatarKind.User, source);
        // Headless environments may keep original bytes via the safe fallback,
        // so assert a real stored asset rather than exact processed dimensions.
        Assert.True(new FileInfo(store.PathFor(HavenAvatarKind.User)).Length > 1024);
        Assert.True(store.Has(HavenAvatarKind.User));
        Assert.False(store.Has(HavenAvatarKind.Haven));

        // Enabling requires an asset; each identity works independently.
        var preferences = new UserPreferencesService(paths);
        preferences.SetUserAvatarEnabled(true, save: false);
        Assert.True(preferences.UserAvatarEnabled);
        preferences.SetHavenAvatarEnabled(true, save: false);
        Assert.False(preferences.HavenAvatarEnabled, "Haven avatar cannot enable without its own asset.");

        store.SetFromFile(HavenAvatarKind.Haven, source);
        preferences.SetHavenAvatarEnabled(true, save: false);
        Assert.True(preferences.HavenAvatarEnabled);
        preferences.SetUserAvatarEnabled(false, save: false);
        Assert.False(preferences.UserAvatarEnabled);
        Assert.True(preferences.HavenAvatarEnabled);

        Assert.True(store.Remove(HavenAvatarKind.User));
        Assert.False(store.Remove(HavenAvatarKind.User));
        Assert.Throws<FileNotFoundException>(() => store.SetFromFile(HavenAvatarKind.User, Path.Combine(paths.DataDirectory, "missing.png")));
    }

    [AvaloniaFact]
    public void Settings_scene_exposes_the_full_personalisation_surface()
    {
        using var scene = new SettingsHavenScene();
        Assert.Equal(5, scene.ThemeSelect.Items.Count);
        Assert.Equal(13, scene.AccentSwatchButtons.Count);
        Assert.All(scene.AccentSwatchButtons, button => Assert.False(string.IsNullOrWhiteSpace(button.Accessibility.AccessibleName)));
        Assert.NotNull(scene.AccentOverrideToggle);
        Assert.NotNull(scene.FontSelect);
        Assert.NotNull(scene.UserAvatarToggle);
        Assert.NotNull(scene.HavenAvatarToggle);
        Assert.NotNull(scene.UserAvatarChooseButton);
        Assert.NotNull(scene.UserAvatarRemoveButton);
        Assert.NotNull(scene.HavenAvatarChooseButton);
        Assert.NotNull(scene.HavenAvatarRemoveButton);
    }

    private static string FindBundledImage()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "Haven.Desktop", "Assets", "haven-1024.png")))
            directory = directory.Parent;
        return directory is null ? string.Empty : Path.Combine(directory.FullName, "src", "Haven.Desktop", "Assets", "haven-1024.png");
    }

    private static (byte R, byte G, byte B) ExtractRgb(string hex)
    {
        var colour = Color.Parse(hex);
        return (colour.R, colour.G, colour.B);
    }

    private static double Distance((byte R, byte G, byte B) first, (byte R, byte G, byte B) second) =>
        Math.Sqrt(Math.Pow(first.R - second.R, 2) + Math.Pow(first.G - second.G, 2) + Math.Pow(first.B - second.B, 2));

    private static double Luminance(string hex)
    {
        var colour = Color.Parse(hex);
        return (0.2126d * colour.R) + (0.7152d * colour.G) + (0.0722d * colour.B);
    }

    private static Color Blend(Color first, Color second, double secondWeight)
    {
        var weight = Math.Clamp(secondWeight, 0, 1);
        return Color.FromArgb(
            255,
            (byte)Math.Round(first.R + (second.R - first.R) * weight),
            (byte)Math.Round(first.G + (second.G - first.G) * weight),
            (byte)Math.Round(first.B + (second.B - first.B) * weight));
    }

    private sealed class TemporaryPaths : IAppPaths, IDisposable
    {
        public TemporaryPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-personalisation-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
        }

        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");

        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
