using Haven.UI.Components;
using Xunit;

namespace Haven.UI.Tests;

public sealed class PrefabAdditionalTests
{
    [Fact]
    public void Nested_prefabs_parse_inside_the_originating_catalog_without_name_collisions()
    {
        var catalog = new HavenPrefabCatalog();
        catalog.Register(new InnerPrefab(), "<Container><Text Name=\"SharedName\" Content=\"Inner\"/></Container>", "Inner.hui");
        catalog.Register(new OuterPrefab(), "<Container><Text Name=\"SharedName\" Content=\"Outer\"/><Prefab InstID=\"Nested\" ID=\"Inner\"/></Container>", "Outer.hui");

        var outer = catalog.Create("Outer", "Screen-A");
        var nested = Assert.Single(outer.DescendantsAndSelf().OfType<Prefab>(), prefab => !ReferenceEquals(prefab, outer));

        Assert.Equal("Inner", nested.PrefabID);
        Assert.Equal("Inner", nested.GetComponent<Text>("SharedName").Content);
        outer.ValidateUniqueNames();
    }

    [Fact]
    public void Disabled_prefab_components_are_omitted_from_rendering_and_reappear_when_enabled()
    {
        var catalog = new HavenPrefabCatalog();
        catalog.Register(new RenderPrefab(), "<Container><Text Name=\"Optional\" Content=\"Optional prefab content\"/></Container>", "Render.hui");
        var prefab = catalog.Create("Render", "A");
        var renderer = new HavenSceneRenderer();

        Assert.Contains(renderer.Render(prefab).OfType<HavenTextCommand>(), command => command.Layout.Text == "Optional prefab content");
        prefab.SetComponentEnabled("Optional", false);
        Assert.DoesNotContain(renderer.Render(prefab).OfType<HavenTextCommand>(), command => command.Layout.Text == "Optional prefab content");
        prefab.SetComponentEnabled("Optional", true);
        Assert.Contains(renderer.Render(prefab).OfType<HavenTextCommand>(), command => command.Layout.Text == "Optional prefab content");
    }

    [Fact]
    public void Dynamic_instance_ids_are_distinct_values_not_case_folded()
    {
        var catalog = new HavenPrefabCatalog();
        catalog.Register(new DynamicCasePrefab(), "<Container><Text Name=\"Feature\" Content=\"Feature\"/></Container>", "DynamicCase.hui");
        var upper = catalog.Create("DynamicCase", "Screen-A");
        var lower = catalog.Create("DynamicCase", "screen-a");

        upper.SetComponentEnabled("Feature", false);

        Assert.False(upper.IsComponentEnabled("Feature"));
        Assert.True(lower.IsComponentEnabled("Feature"));
    }

    [Fact]
    public void Existing_non_prefab_component_tag_casing_remains_unchanged()
    {
        Assert.Throws<HavenMarkupException>(() => new HavenMarkupParser().Parse("<page />"));
        Assert.IsType<Page>(new HavenMarkupParser().Parse("<Page />"));
    }

    private sealed class InnerPrefab : HavenPrefabDefinition { public override string PrefabID => "Inner"; }
    private sealed class OuterPrefab : HavenPrefabDefinition { public override string PrefabID => "Outer"; }
    private sealed class RenderPrefab : HavenPrefabDefinition { public override string PrefabID => "Render"; }
    private sealed class DynamicCasePrefab : HavenPrefabDefinition { public override string PrefabID => "DynamicCase"; }
}
