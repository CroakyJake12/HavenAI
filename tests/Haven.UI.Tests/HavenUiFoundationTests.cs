using Haven.UI;
using Haven.UI.Components;
using Xunit;

namespace Haven.UI.Tests;

public sealed class HavenUiFoundationTests
{
    [Fact]
    public void Canonical_component_sources_are_obvious()
    {
        HavenElement[] items = [new Button(), new Text(), new Container(), new Toggle(), new Slider()];
        Assert.All(items, item =>
        {
            Assert.StartsWith("Components/", item.Metadata.CanonicalSource, StringComparison.Ordinal);
            Assert.Contains(item.Metadata.ComponentName, item.Metadata.CanonicalSource, StringComparison.OrdinalIgnoreCase);
            Assert.NotEmpty(item.Metadata.Notes);
        });
    }

    [Fact]
    public void Property_precedence_is_deterministic()
    {
        var button = new Button();
        button.SetValue(HavenProperties.Opacity, .9, HavenValueSource.SystemClass);
        button.SetValue(HavenProperties.Opacity, .8, HavenValueSource.UserClass);
        button.SetValue(HavenProperties.Opacity, .7);
        button.SetValue(HavenProperties.Opacity, .6, HavenValueSource.State);
        button.SetValue(HavenProperties.Opacity, .5, HavenValueSource.Animation);
        Assert.Equal(.5, button.GetValue(HavenProperties.Opacity));
        button.ClearValue(HavenProperties.Opacity, HavenValueSource.Animation);
        Assert.Equal(.6, button.GetValue(HavenProperties.Opacity));
    }

    [Theory]
    [InlineData("400px", HavenLengthUnit.Pixel, 400)]
    [InlineData("50%", HavenLengthUnit.Percent, 50)]
    [InlineData("25vw", HavenLengthUnit.ViewportWidth, 25)]
    [InlineData("20vh", HavenLengthUnit.ViewportHeight, 20)]
    [InlineData("1fr", HavenLengthUnit.Fraction, 1)]
    public void Haven_lengths_parse(string source, HavenLengthUnit unit, double amount)
    {
        var length = HavenLength.Parse(source);
        Assert.Equal(unit, length.Unit);
        Assert.Equal(amount, length.Value);
    }

    [Fact]
    public void Selectors_are_deterministic()
    {
        var root = new Container();
        var one = new Button { Name = "Save", Group = "Steve,Navigation", Class = "PrimaryAction" };
        var two = new Button { Group = "Steve" };
        root.Add(one);
        root.Add(two);
        root.ValidateUniqueNames();
        Assert.Same(one, Assert.Single(HavenSelector.Parse("Name.Save").Select(root)));
        Assert.Equal(2, HavenSelector.Parse("Group.Steve").Select(root).Count);
        Assert.Same(one, Assert.Single(HavenSelector.Parse("Class.PrimaryAction").Select(root)));
        Assert.Equal(2, HavenSelector.Parse("Type.Button").Select(root).Count);
    }

    [Fact]
    public void Conditions_remove_nodes_from_layout()
    {
        var root = new Container();
        var child = new Text("desktop");
        child.Conditions.Add(new HavenPlatformCondition("Windows"));
        child.Conditions.Add(new HavenScreenRangeCondition(HavenScreenAxis.Width, HavenLength.Px(600)));
        root.Add(child);
        new HavenLayoutEngine().Layout(root, new HavenSize(500, 300), HavenPlatform.Windows, new FixedMeasure());
        Assert.False(child.IsIncluded);
        Assert.Equal(HavenSize.Zero, child.DesiredSize);
    }

    [Fact]
    public void Container_owns_vertical_layout()
    {
        var root = new Container();
        root.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        root.Add(new Text("one"));
        root.Add(new Text("two"));
        new HavenLayoutEngine().Layout(root, new HavenSize(300, 200), HavenPlatform.Windows, new FixedMeasure());
        Assert.Equal(50, root.DesiredSize.Height);
        Assert.Equal(30, root.Children[1].Bounds.Y);
    }

    [Fact]
    public void Button_state_uses_canonical_defaults()
    {
        var button = new Button { Variant = ButtonVariant.Tertiary };
        Assert.Equal("AccentMuted", button.GetValue(HavenProperties.Background));
        button.SetState(HavenElementState.Hover, true);
        Assert.Equal("AccentTertiaryHover", button.GetValue(HavenProperties.Background));
        Assert.Equal(ButtonDefaults.HoverTransition, button.GetValue(HavenProperties.Transition));
        button.SetState(HavenElementState.Pressed, true);
        Assert.Equal(.94, button.GetValue(HavenProperties.Scale));
        Assert.Equal(ButtonDefaults.PressedTransition, button.GetValue(HavenProperties.Transition));
    }

    [Fact]
    public void Central_resources_are_embedded()
    {
        Assert.Contains("Class Button", HavenResourceCatalog.SystemClasses, StringComparison.Ordinal);
        Assert.Contains("Transition ButtonHover", HavenResourceCatalog.SystemAnimations, StringComparison.Ordinal);
        Assert.Contains("User-defined Haven.UI classes", HavenResourceCatalog.UserClasses, StringComparison.Ordinal);
        Assert.Contains("User-defined Haven.UI transitions", HavenResourceCatalog.UserAnimations, StringComparison.Ordinal);
    }

    private sealed class FixedMeasure : IHavenMeasureContext
    {
        public HavenSize MeasureLeaf(HavenElement element, HavenSize available) => new(Math.Min(100, available.Width), 20);
    }
}
