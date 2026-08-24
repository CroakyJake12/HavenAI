using Haven.UI.Components;
using HavenImageComponent = Haven.UI.Components.Image;
using Xunit;

namespace Haven.UI.Tests;

public sealed class HavenUiDrawingTests
{
    [Fact]
    public void Shadow_vocabulary_resolves_named_and_explicit_effects()
    {
        Assert.True(HavenEffects.TryResolveShadow("Card", out var named));
        Assert.NotNull(named);
        Assert.Equal(34, named.Blur);
        Assert.Equal(14, named.OffsetY);
        Assert.Equal(.40, named.Opacity);
        Assert.Equal("Shadow", Assert.IsType<HavenTokenBrush>(named.Brush).Token);

        Assert.True(HavenEffects.TryResolveShadow("2px 4px 12px -1px Accent 0.65", out var explicitShadow));
        Assert.NotNull(explicitShadow);
        Assert.Equal(new HavenShadow(new HavenTokenBrush("Accent"), 12, 2, 4, -1, .65), explicitShadow);
        Assert.False(HavenEffects.TryResolveShadow("None", out var none));
        Assert.Null(none);
    }

    [Theory]
    [InlineData("0 0 nope 0 Shadow")]
    [InlineData("0 0 12 0 Shadow 1.5")]
    [InlineData("not-a-shadow")]
    public void Invalid_shadow_values_fail_at_the_framework_boundary(string value)
    {
        Assert.Throws<FormatException>(() => HavenEffects.TryResolveShadow(value, out _));
        Assert.Throws<HavenMarkupException>(() =>
            new HavenMarkupParser().Parse($"<Page Shadow='{value}' />", "shadow-test.hui"));
    }

    [Fact]
    public void Scene_renderer_preserves_effect_media_and_icon_semantics()
    {
        var root = new Container { Layout = HavenLayout.Horizontal };
        root.SetValue(HavenProperties.Width, HavenLength.Px(320));
        root.SetValue(HavenProperties.Height, HavenLength.Px(120));
        root.SetValue(HavenProperties.Background, "Surface");
        root.SetValue(HavenProperties.Shadow, "Card");

        var image = new HavenImageComponent { Source = "avares://Haven/Assets/haven-1024.png", Fit = HavenImageFit.Cover };
        var icon = new Icon { Key = "search" };
        root.Add(image);
        root.Add(icon);
        new HavenLayoutEngine().Layout(root, new HavenSize(320, 120), HavenPlatform.Windows, new FixedMeasure());

        var commands = new HavenSceneRenderer().Render(root).ToArray();
        var shadowIndex = Array.FindIndex(commands, command => command is HavenShadowCommand);
        var fillIndex = Array.FindIndex(commands, command => command is HavenFillRoundedRectCommand);
        Assert.InRange(shadowIndex, 0, commands.Length - 1);
        Assert.True(fillIndex > shadowIndex);
        Assert.Contains(commands, command => command is HavenImageCommand { Layout: HavenImageLayout.Cover });
        Assert.Contains(commands, command => command is HavenIconCommand { Key: "search" });
    }

    [Fact]
    public void Icon_geometry_and_surface_metrics_are_backend_neutral_and_visible()
    {
        var search = HavenIconCatalog.Resolve("search");
        Assert.Equal(new HavenRect(0, 0, 24, 24), search.ViewBox);
        Assert.Contains(search.Path.Figures.SelectMany(figure => figure.Segments), segment => segment is HavenArcSegment);

        var fallback = HavenIconCatalog.Resolve("not-registered");
        Assert.NotEmpty(fallback.Path.Figures);
        Assert.All(fallback.Path.Figures, figure => Assert.NotEmpty(figure.Segments));

        var metrics = new HavenRenderSurfaceMetrics(new HavenSize(640, 360), 1.5, HavenPlatform.Windows);
        Assert.Equal(new HavenSize(960, 540), metrics.PixelSize);
    }

    [Fact]
    public void Primary_button_text_uses_fixed_bright_content_token()
    {
        var button = new Button { Content = "Primary action", Variant = ButtonVariant.Primary };
        var command = Assert.Single(new HavenSceneRenderer().Render(button).OfType<HavenTextCommand>());

        Assert.True(command.Layout.CenterVertically);
        Assert.Equal("ButtonTextPrimary", Assert.IsType<HavenTokenBrush>(command.Brush).Token);
    }

    [Fact]
    public void Tertiary_button_text_is_bright_and_requests_vertical_centering()
    {
        var button = new Button { Content = "Readable action", Variant = ButtonVariant.Tertiary };
        var command = Assert.Single(new HavenSceneRenderer().Render(button).OfType<HavenTextCommand>());

        Assert.True(command.Layout.CenterVertically);
        Assert.Equal("ButtonTextSecondary", Assert.IsType<HavenTokenBrush>(command.Brush).Token);
    }

    [Theory]
    [InlineData("chevron-left")]
    [InlineData("window")]
    [InlineData("cpu")]
    [InlineData("bell")]
    [InlineData("file")]
    [InlineData("notes")]
    [InlineData("agents")]
    [InlineData("bolt")]
    [InlineData("prompt")]
    [InlineData("rocket")]
    [InlineData("browse")]
    [InlineData("tasks")]
    [InlineData("plan")]
    [InlineData("studio")]
    [InlineData("rapid")]
    [InlineData("build")]
    [InlineData("test")]
    [InlineData("experiment")]
    [InlineData("bookmark")]
    public void Go_and_add_menu_icon_keys_have_specific_geometry(string key)
    {
        var geometry = HavenIconCatalog.Resolve(key);
        var fallback = HavenIconCatalog.Resolve("not-registered");

        Assert.NotEmpty(geometry.Path.Figures);
        Assert.NotEqual(fallback.Path.Figures[0].Start, geometry.Path.Figures[0].Start);
    }

    private sealed class FixedMeasure : IHavenMeasureContext
    {
        public HavenSize MeasureLeaf(HavenElement element, HavenSize available) =>
            new(Math.Min(64, available.Width), Math.Min(64, available.Height));
    }
}
