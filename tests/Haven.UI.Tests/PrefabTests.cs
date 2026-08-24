using Haven.UI.Components;
using Haven.UI.Tests.Prefabs;
using Xunit;

namespace Haven.UI.Tests;

public sealed class PrefabTests
{
    private const string DynamicMarkup = "<Container><Button Name=\"AddMenu\" Content=\"Add\"/><Text Name=\"Body\" Content=\"Body\"/></Container>";

    [Theory]
    [InlineData("Prefab", "InstanceID", "PrefabID")]
    [InlineData("prefab", "InstID", "pID")]
    [InlineData("PREFAB", "iID", "ID")]
    [InlineData("pReFaB", "iid", "id")]
    public void Markup_accepts_prefab_tag_and_id_aliases_case_insensitively(string tag, string instanceAttribute, string prefabAttribute)
    {
        var catalog = new HavenPrefabCatalog();
        catalog.Register(new DynamicTestPrefab(), DynamicMarkup, "DynamicTest.hui");
        var root = new HavenMarkupParser(catalog).Parse($"<Page><{tag} {instanceAttribute}=\"Screen-A\" {prefabAttribute}=\"DynamicTest\" /></Page>");

        var prefab = Assert.Single(root.DescendantsAndSelf().OfType<Prefab>());
        Assert.Equal("DynamicTest", prefab.PrefabID);
        Assert.Equal("Screen-A", prefab.InstanceID);
        Assert.Equal(PrefabMode.Dynamic, prefab.Mode);
    }

    [Fact]
    public void Multiple_dynamic_instances_can_reuse_internal_names_and_keep_independent_state()
    {
        var catalog = new HavenPrefabCatalog();
        catalog.Register(new DynamicTestPrefab(), DynamicMarkup, "DynamicTest.hui");
        var page = new HavenMarkupParser(catalog).Parse("<Page><Prefab InstID=\"A\" ID=\"DynamicTest\"/><Prefab InstID=\"B\" ID=\"DynamicTest\"/></Page>");
        var prefabs = page.DescendantsAndSelf().OfType<Prefab>().ToArray();
        Assert.Equal(2, prefabs.Length);

        prefabs[0].SetComponentEnabled("AddMenu", false);

        Assert.Equal(HavenVisibility.Collapsed, prefabs[0].GetComponent("AddMenu").GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Visible, prefabs[1].GetComponent("AddMenu").GetValue(HavenProperties.Visibility));
        Assert.False(prefabs[0].IsComponentEnabled("AddMenu"));
        Assert.True(prefabs[1].IsComponentEnabled("AddMenu"));

        var recreated = catalog.Create("DynamicTest", "A");
        Assert.False(recreated.IsComponentEnabled("AddMenu"));
        Assert.Equal(HavenVisibility.Collapsed, recreated.GetComponent("AddMenu").GetValue(HavenProperties.Visibility));
    }

    [Fact]
    public void Static_prefab_state_is_shared_across_catalogs_instances_and_future_instances()
    {
        var firstCatalog = new HavenPrefabCatalog();
        var secondCatalog = new HavenPrefabCatalog();
        firstCatalog.Register(new StaticSharedTestPrefab(), DynamicMarkup, "StaticSharedTest.hui");
        secondCatalog.Register(new StaticSharedTestPrefab(), DynamicMarkup, "StaticSharedTest.hui");
        var first = firstCatalog.Create("StaticSharedTest", "One");
        var second = secondCatalog.Create("StaticSharedTest", "Two");

        first.SetComponentEnabled("AddMenu", false);

        Assert.False(second.IsComponentEnabled("AddMenu"));
        Assert.Equal(HavenVisibility.Collapsed, second.GetComponent("AddMenu").GetValue(HavenProperties.Visibility));
        var future = secondCatalog.Create("StaticSharedTest", "Three");
        Assert.False(future.IsComponentEnabled("AddMenu"));
        Assert.Equal(HavenVisibility.Collapsed, future.GetComponent("AddMenu").GetValue(HavenProperties.Visibility));
    }

    [Fact]
    public void Reenabling_component_removes_prefab_override_and_restores_authored_visibility()
    {
        var catalog = new HavenPrefabCatalog();
        catalog.Register(new RestoreVisibilityTestPrefab(), "<Container><Text Name=\"Optional\" Visibility=\"Hidden\" Content=\"Optional\"/></Container>", "RestoreVisibilityTest.hui");
        var prefab = catalog.Create("RestoreVisibilityTest", "A");
        var optional = prefab.GetComponent("Optional");
        Assert.Equal(HavenVisibility.Hidden, optional.GetValue(HavenProperties.Visibility));

        prefab.SetComponentEnabled("Optional", false);
        Assert.Equal(HavenVisibility.Collapsed, optional.GetValue(HavenProperties.Visibility));
        prefab.SetComponentEnabled("Optional", true);

        Assert.Equal(HavenVisibility.Hidden, optional.GetValue(HavenProperties.Visibility));
    }

    [Fact]
    public void Click_actions_inside_prefabs_are_scoped_to_the_originating_instance()
    {
        var catalog = new HavenPrefabCatalog();
        catalog.Register(new ActionScopeTestPrefab(), "<Container><Text Name=\"Target\" Opacity=\"1\"/><Button Name=\"Trigger\" OnClick=\"Name.Target -> Opacity=0.25\"/></Container>", "ActionScopeTest.hui");
        var page = new HavenMarkupParser(catalog).Parse("<Page><Prefab InstID=\"A\" ID=\"ActionScopeTest\"/><Prefab InstID=\"B\" ID=\"ActionScopeTest\"/></Page>");
        var prefabs = page.DescendantsAndSelf().OfType<Prefab>().ToArray();
        var trigger = prefabs[0].GetComponent("Trigger");

        new HavenActionExecutor().ExecuteClick(page, trigger);

        Assert.Equal(.25d, prefabs[0].GetComponent("Target").GetValue(HavenProperties.Opacity), 3);
        Assert.Equal(1d, prefabs[1].GetComponent("Target").GetValue(HavenProperties.Opacity), 3);
    }

    [Fact]
    public void Component_toggle_rejects_unknown_component_names()
    {
        var catalog = new HavenPrefabCatalog();
        catalog.Register(new DynamicTestPrefab(), DynamicMarkup, "DynamicTest.hui");
        var prefab = catalog.Create("DynamicTest", "A");

        Assert.Throws<KeyNotFoundException>(() => prefab.SetComponentEnabled("Missing", false));
    }

    [Fact]
    public void Assembly_catalog_discovers_real_paired_hui_and_hui_cs_prefab_files_and_runs_code_behind()
    {
        var catalog = HavenPrefabCatalog.FromAssembly(typeof(CatalogCardPrefab).Assembly);

        Assert.Contains("CatalogCard", catalog.PrefabIDs);
        var prefab = catalog.Create("CatalogCard", "Discovery-A");
        Assert.Equal("Initialized by .hui.cs", prefab.GetComponent<Text>("Title").Content);
        Assert.Equal("Wired by prefab code-behind", prefab.GetComponent<Button>("AddMenu").Accessibility.Description);
    }

    private sealed class DynamicTestPrefab : HavenPrefabDefinition
    {
        public override string PrefabID => "DynamicTest";
    }

    private sealed class StaticSharedTestPrefab : HavenPrefabDefinition
    {
        public override string PrefabID => "StaticSharedTest";
        public override PrefabMode Mode => PrefabMode.Static;
    }

    private sealed class RestoreVisibilityTestPrefab : HavenPrefabDefinition
    {
        public override string PrefabID => "RestoreVisibilityTest";
    }

    private sealed class ActionScopeTestPrefab : HavenPrefabDefinition
    {
        public override string PrefabID => "ActionScopeTest";
    }
}
