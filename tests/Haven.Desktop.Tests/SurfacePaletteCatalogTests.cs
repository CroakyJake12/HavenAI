using Avalonia.Styling;
using Haven.Core;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Tests;

public sealed class SurfacePaletteCatalogTests
{
    [Fact]
    public void Every_surface_has_readable_tokens_in_all_four_appearances()
    {
        foreach (var surface in Enum.GetValues<HavenSurface>())
        {
            foreach (var appearance in Enum.GetValues<HavenUiAppearance>())
            {
                var palette = SurfacePaletteCatalog.For(surface, appearance);
                Assert.NotEqual(palette.TideBase, palette.TideColour);
                Assert.NotEqual(palette.Accent, palette.AccentSoft);
                if (appearance is HavenUiAppearance.SuperBright or HavenUiAppearance.Bright)
                    Assert.True(Luminance(palette.Text) < Luminance(palette.Panel));
                else
                    Assert.True(Luminance(palette.Text) > Luminance(palette.Panel));
            }
        }
    }

    [Fact]
    public void Four_appearances_change_colours_without_changing_the_palette_contract()
    {
        var palettes = Enum.GetValues<HavenUiAppearance>()
            .Select(appearance => SurfacePaletteCatalog.For(HavenSurface.Chat, appearance))
            .ToArray();

        Assert.Equal(4, palettes.Select(item => item.TideBase).Distinct().Count());
        Assert.Equal(4, palettes.Select(item => item.Panel).Distinct().Count());
        Assert.All(palettes, item => Assert.NotEqual(default, item.Focus));
    }

    [Fact]
    public void Tasks_palette_uses_the_mockup_orange_family_for_background_and_controls()
    {
        var palette = SurfacePaletteCatalog.For(HavenSurface.Tasks, ThemeVariant.Light);

        Assert.True(palette.TideColour.R > palette.TideColour.B);
        Assert.True(palette.Accent.R > palette.Accent.G);
        Assert.True(palette.AccentStrong.R > palette.AccentStrong.B);
    }

    private static double Luminance(Avalonia.Media.Color color) =>
        (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);
}
