using Haven.UI.Components;
using Xunit;

namespace Haven.UI.Tests;

public sealed class DynamicUiIncrementalTests
{
    [Fact]
    public void Nonstructural_variable_updates_preserve_component_identity_and_name_collisions_roll_back()
    {
        var templates = new HavenDynamicUITemplateCatalog();
        templates.Register("<DynamicUI Name=\"Row\"><Container><Text Name=\"{{FIRSTNAME}}\">{{VALUE}}</Text><Text Name=\"Second\">Two</Text></Container></DynamicUI>");
        var page = new HavenMarkupParser().Parse("<Page><DynamicUIRuntime Name=\"List\"/></Page>");
        var item = new DynamicUI(page, templates).CreateItem("Row", "List", "A", new Dictionary<string, object?>
        {
            ["FIRSTNAME"] = "First",
            ["VALUE"] = "One"
        });
        var first = item.GetComponent<Text>("First");
        var second = item.GetComponent<Text>("Second");

        item.SetVariable("VALUE", "Changed");

        Assert.Same(first, item.GetComponent<Text>("First"));
        Assert.Same(second, item.GetComponent<Text>("Second"));
        Assert.Equal("Changed", first.Content);

        Assert.Throws<InvalidOperationException>(() => item.SetVariable("FIRSTNAME", "Second"));
        Assert.Same(first, item.GetComponent<Text>("First"));
        Assert.Same(second, item.GetComponent<Text>("Second"));
        Assert.Equal("First", item.Values["FIRSTNAME"]);
    }

    [Fact]
    public void Structural_prefab_identity_binding_rebuilds_item_when_the_identity_changes()
    {
        var prefabs = new HavenPrefabCatalog();
        prefabs.Register(new StructuralPrefabDefinition(), "<Container><Text Name=\"Body\">Body</Text></Container>", "StructuralPrefab.hui");
        var templates = new HavenDynamicUITemplateCatalog();
        templates.Register("<DynamicUI Name=\"WithPrefab\"><Prefab InstID=\"{{PID}}\" ID=\"StructuralPrefab\"/></DynamicUI>");
        var page = new HavenMarkupParser(prefabs).Parse("<Page><DynamicUIRuntime Name=\"List\"/></Page>");
        var item = new DynamicUI(page, templates, prefabs).CreateItem("WithPrefab", "List", "A", new Dictionary<string, object?> { ["PID"] = "First" });
        var firstPrefab = Assert.Single(item.Children.OfType<Prefab>());

        item.SetVariable("PID", "Second");

        var secondPrefab = Assert.Single(item.Children.OfType<Prefab>());
        Assert.NotSame(firstPrefab, secondPrefab);
        Assert.Equal("Second", secondPrefab.InstanceID);
    }

    [Fact]
    public void Invalid_runtime_property_mutation_rolls_back_to_the_previous_item_tree()
    {
        var templates = new HavenDynamicUITemplateCatalog();
        templates.Register("<DynamicUI Name=\"Row\"><Container><Text Name=\"First\">One</Text><Text Name=\"Second\">Two</Text></Container></DynamicUI>");
        var page = new HavenMarkupParser().Parse("<Page><DynamicUIRuntime Name=\"List\"/></Page>");
        var item = new DynamicUI(page, templates).CreateItem("Row", "List", "A");

        Assert.Throws<InvalidOperationException>(() => item.SetProperty("First", "Name", "Second"));

        Assert.Equal("One", item.GetComponent<Text>("First").Content);
        Assert.Equal("Two", item.GetComponent<Text>("Second").Content);
        item.ValidateUniqueNames();
    }

    private sealed class StructuralPrefabDefinition : HavenPrefabDefinition
    {
        public override string PrefabID => "StructuralPrefab";
    }
}
