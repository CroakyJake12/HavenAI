using Avalonia.Styling;
using Haven.Core;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Tests;

public sealed class SurfacePaletteCatalogTests
{
    [Fact]
    public void Every_surface_has_readable_light_and_dark_palette_tokens()
    {
        foreach (var surface in Enum.GetValues<HavenSurface>())
        {
            var light = SurfacePaletteCatalog.For(surface, ThemeVariant.Light);
            var dark = SurfacePaletteCatalog.For(surface, ThemeVariant.Dark);

            Assert.NotEqual(light.TideBase, light.TideColour);
            Assert.NotEqual(dark.TideBase, dark.TideColour);
            Assert.NotEqual(light.Accent, light.AccentSoft);
            Assert.NotEqual(dark.Accent, dark.AccentSoft);
            Assert.True(Luminance(light.Text) < Luminance(light.Panel));
            Assert.True(Luminance(dark.Text) > Luminance(dark.Panel));
        }
    }

    [Fact]
    public void Tasks_palette_uses_its_green_family_for_background_and_controls()
    {
        var palette = SurfacePaletteCatalog.For(HavenSurface.Tasks, ThemeVariant.Light);

        Assert.True(palette.TideColour.G > palette.TideColour.R);
        Assert.True(palette.Accent.G > palette.Accent.R);
        Assert.True(palette.AccentSoft.G > palette.AccentSoft.R);
    }

    private static double Luminance(Avalonia.Media.Color color) =>
        (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);
}
