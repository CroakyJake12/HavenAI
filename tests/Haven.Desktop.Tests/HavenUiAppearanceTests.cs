using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Settings;

namespace Haven.Desktop.Tests;

public sealed class HavenUiAppearanceTests
{
    [Fact]
    public void Appearance_enum_matches_the_four_slider_positions()
    {
        Assert.Equal(
            [HavenUiAppearance.SuperBright, HavenUiAppearance.Bright, HavenUiAppearance.Dark, HavenUiAppearance.SuperDark],
            Enum.GetValues<HavenUiAppearance>());
    }

    [Fact]
    public void Fresh_install_starts_in_the_mockup_defined_super_dark_appearance()
    {
        using var paths = new TemporaryPaths();

        var preferences = new UserPreferencesService(paths);

        Assert.Equal(HavenUiAppearance.SuperDark, preferences.Appearance);
        Assert.Equal("haven-ui", preferences.ThemeId);
    }

    [Fact]
    public void Default_tab_preference_round_trips_without_rewriting_an_unavailable_mode()
    {
        using var paths = new TemporaryPaths();
        var preferences = new UserPreferencesService(paths);

        preferences.SetDefaultTabAppKey("personal.mode.which.may.sync.later");

        var reloaded = new UserPreferencesService(paths);
        Assert.Equal("personal.mode.which.may.sync.later", reloaded.DefaultTabAppKey);
    }

    [AvaloniaFact]
    public void Appearance_persists_and_updates_the_canonical_semantic_resources()
    {
        using var paths = new TemporaryPaths();
        var preferences = new UserPreferencesService(paths);

        preferences.ApplyAppearance(HavenUiAppearance.SuperDark);

        Assert.Equal(HavenUiAppearance.SuperDark, preferences.Appearance);
        Assert.Equal(ThemeVariant.Dark, Avalonia.Application.Current!.RequestedThemeVariant);
        AssertBrush("HavenBackgroundPrimaryBrush");
        AssertBrush("HavenTextPrimaryBrush");
        AssertBrush("HavenTextDisabledBrush");
        AssertBrush("HavenBorderSubtleBrush");
        AssertBrush("HavenSuccessBrush");
        AssertBrush("HavenInformationBrush");
        AssertBrush("HavenAccentForegroundBrush");

        var reloaded = new UserPreferencesService(paths);
        Assert.Equal(HavenUiAppearance.SuperDark, reloaded.Appearance);
        Assert.Equal("haven-ui", reloaded.ThemeId);
    }

    [AvaloniaFact]
    public void Legacy_dark_theme_preference_migrates_to_dark_HavenUi_without_data_loss()
    {
        using var paths = new TemporaryPaths();
        File.WriteAllText(
            Path.Combine(paths.DataDirectory, "preferences.json"),
            """
            {
              "themeId": "obsidian",
              "defaultModel": "qwen3:4b",
              "temperature": 0.55
            }
            """);

        var migrated = new UserPreferencesService(paths);

        Assert.Equal(HavenUiAppearance.SuperDark, migrated.Appearance);
        Assert.Equal("haven-ui", migrated.ThemeId);
        Assert.Equal("qwen3:4b", migrated.DefaultModel);
        Assert.Equal(0.55d, migrated.GenerationOptions.Temperature, precision: 2);
    }

    [AvaloniaFact]
    public void Settings_control_exposes_exactly_four_snap_positions_and_applies_selection()
    {
        using var paths = new TemporaryPaths();
        var preferences = new UserPreferencesService(paths);
        var view = new HavenAppearanceSettingsView(preferences);
        var window = new Window { Content = view };
        try
        {
            window.Show();
            var slider = Assert.Single(view.GetVisualDescendants().OfType<Slider>());
            Assert.Equal(0, slider.Minimum);
            Assert.Equal(3, slider.Maximum);
            Assert.Equal(1, slider.TickFrequency);
            Assert.True(slider.IsSnapToTickEnabled);

            var expectedLabels = new[] { "Super Bright", "Bright", "Dark", "Super Dark" };
            var buttons = view.GetVisualDescendants()
                .OfType<Button>()
                .Where(item => item.Content is string label && expectedLabels.Contains(label, StringComparer.Ordinal))
                .ToArray();
            Assert.Equal(expectedLabels, buttons.Select(item => item.Content));
            buttons[3].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(HavenUiAppearance.SuperDark, preferences.Appearance);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertBrush(string key)
    {
        Assert.True(Avalonia.Application.Current!.Resources.TryGetValue(key, out var value));
        Assert.IsType<SolidColorBrush>(value);
    }

    private sealed class TemporaryPaths : IAppPaths, IDisposable
    {
        public TemporaryPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-ui-appearance-tests", Guid.NewGuid().ToString("N"));
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
