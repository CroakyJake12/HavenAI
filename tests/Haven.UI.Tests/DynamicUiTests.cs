using Haven.UI.Components;
using Xunit;

namespace Haven.UI.Tests;

public sealed class DynamicUiTests
{
    private const string ButtonTemplate = """
        <DynamicUI Name="DynamicButton">
          <Container Name="Row" Layout="Horizontal">
            <Button Name="Action" Type="{{BUTTONTYPE}}" Enabled="{{ISENABLED}}" Opacity="{{OPACITY}}">{{BTNTXT}}</Button>
            <Text Name="Greeting">Hello {{USERNAME}}, {{COUNT}} items.</Text>
          </Container>
        </DynamicUI>
        """;

    [Fact]
    public void Declaration_is_catalog_only_and_runtime_host_is_empty_authored_scene_element()
    {
        var page = new HavenMarkupParser().Parse("<Page><DynamicUIRuntime Name=\"List\" /></Page>");
        Assert.IsType<DynamicUIRuntime>(Assert.Single(page.Children));
        var declarationError = Assert.Throws<HavenMarkupException>(() => new HavenMarkupParser().Parse("<DynamicUI Name=\"Nope\"><Text>Hi</Text></DynamicUI>"));
        Assert.Contains("template declaration", declarationError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<HavenMarkupException>(() => new HavenMarkupParser().Parse("<Page><DynamicUIRuntime Name=\"List\"><Text>Authored</Text></DynamicUIRuntime></Page>"));
    }

    [Fact]
    public void CreateItem_interpolates_text_and_typed_properties_for_independent_instances()
    {
        var (dynamicUi, host) = CreateRuntime(ButtonTemplate);
        var first = dynamicUi.CreateItem("DynamicButton", "List", "A", Values("Alpha", "Primary", true, .8, "Ada", 2));
        var second = dynamicUi.CreateItem("DynamicButton", "List", "B", Values("Beta", "Danger", false, .5, "Ben", 7));

        Assert.Equal(2, host.Items.Count);
        Assert.Equal("Alpha", first.GetComponent<Button>("Action").Content);
        Assert.Equal(ButtonVariant.Primary, first.GetComponent<Button>("Action").Variant);
        Assert.True(first.GetComponent("Action").GetValue(HavenProperties.Enabled));
        Assert.Equal(.8d, first.GetComponent("Action").GetValue(HavenProperties.Opacity), 3);
        Assert.Equal("Hello Ada, 2 items.", first.GetComponent<Text>("Greeting").Content);
        Assert.Equal("Beta", second.GetComponent<Button>("Action").Content);
        Assert.Equal(ButtonVariant.Danger, second.GetComponent<Button>("Action").Variant);
        Assert.False(second.GetComponent("Action").GetValue(HavenProperties.Enabled));
        Assert.Equal("Hello Ben, 7 items.", second.GetComponent<Text>("Greeting").Content);
    }

    [Fact]
    public void Auto_ids_are_unique_and_duplicate_explicit_ids_are_rejected()
    {
        var (dynamicUi, host) = CreateRuntime("<DynamicUI Name=\"Row\"><Text Name=\"Value\">{{VALUE}}</Text></DynamicUI>");
        var first = dynamicUi.CreateItem("Row", "List", values: new Dictionary<string, object?> { ["VALUE"] = "One" });
        var second = dynamicUi.CreateItem("Row", "List", values: new Dictionary<string, object?> { ["VALUE"] = "Two" });

        Assert.False(string.IsNullOrWhiteSpace(first.InstanceID));
        Assert.NotEqual(first.InstanceID, second.InstanceID);
        Assert.Same(first, dynamicUi.GetItem("List", first.InstanceID));
        Assert.Throws<InvalidOperationException>(() => dynamicUi.CreateItem("Row", "List", first.InstanceID, new Dictionary<string, object?> { ["VALUE"] = "Again" }));
        Assert.Equal(2, host.Items.Count);
    }

    [Fact]
    public void Variable_and_property_mutation_are_instance_scoped_and_clear_restores_template_value()
    {
        var (dynamicUi, _) = CreateRuntime(ButtonTemplate);
        var first = dynamicUi.CreateItem("DynamicButton", "List", "A", Values("Alpha", "Primary", true, .8, "Ada", 2));
        var second = dynamicUi.CreateItem("DynamicButton", "List", "B", Values("Beta", "Secondary", true, .6, "Ben", 3));

        first.SetVariable("BTNTXT", "Changed");
        first.SetVariables(new Dictionary<string, object?> { ["USERNAME"] = "Ava", ["COUNT"] = 9, ["OPACITY"] = .7 });
        first.SetProperty("Action", "Opacity", .25);

        Assert.Equal("Changed", first.GetComponent<Button>("Action").Content);
        Assert.Equal("Hello Ava, 9 items.", first.GetComponent<Text>("Greeting").Content);
        Assert.Equal(.25d, first.GetComponent("Action").GetValue(HavenProperties.Opacity), 3);
        Assert.Equal("Beta", second.GetComponent<Button>("Action").Content);
        Assert.Equal(.6d, second.GetComponent("Action").GetValue(HavenProperties.Opacity), 3);

        first.SetVariable("OPACITY", .65);
        Assert.Equal(.25d, first.GetComponent("Action").GetValue(HavenProperties.Opacity), 3);
        Assert.True(first.ClearProperty("Action", "Opacity"));
        Assert.Equal(.65d, first.GetComponent("Action").GetValue(HavenProperties.Opacity), 3);
        Assert.False(first.ClearProperty("Action", "Opacity"));
        Assert.Throws<KeyNotFoundException>(() => first.SetVariable("MISSING", "x"));
    }

    [Fact]
    public void Property_override_can_change_name_and_clear_by_current_name()
    {
        var (dynamicUi, _) = CreateRuntime("<DynamicUI Name=\"Named\"><Text Name=\"Original\">Body</Text></DynamicUI>");
        var item = dynamicUi.CreateItem("Named", "List", "A");

        item.SetProperty("Original", "Name", "Renamed");
        Assert.Equal("Body", item.GetComponent<Text>("Renamed").Content);
        Assert.True(item.ClearProperty("Renamed", "Name"));
        Assert.Equal("Body", item.GetComponent<Text>("Original").Content);
    }

    [Fact]
    public void Delete_clear_lookup_and_move_manage_lifecycle_and_order()
    {
        var (dynamicUi, host) = CreateRuntime("<DynamicUI Name=\"Row\"><Text Name=\"Value\">{{VALUE}}</Text></DynamicUI>");
        var a = dynamicUi.CreateItem("Row", "List", "A", new Dictionary<string, object?> { ["VALUE"] = "A" });
        _ = dynamicUi.CreateItem("Row", "List", "B", new Dictionary<string, object?> { ["VALUE"] = "B" });
        var c = dynamicUi.CreateItem("Row", "List", "C", new Dictionary<string, object?> { ["VALUE"] = "C" }, index: 1);

        Assert.Equal(new[] { "A", "C", "B" }, host.Items.Select(item => item.InstanceID));
        dynamicUi.MoveItem("List", "B", 0);
        Assert.Equal(new[] { "B", "A", "C" }, host.Items.Select(item => item.InstanceID));
        Assert.True(dynamicUi.TryGetItem("List", "A", out var found));
        Assert.Same(a, found);
        Assert.True(c.Delete());
        Assert.True(c.IsDeleted);
        Assert.Empty(c.Children);
        Assert.False(dynamicUi.TryGetItem("List", "C", out _));
        Assert.Throws<InvalidOperationException>(() => c.SetVariable("VALUE", "stale"));
        Assert.True(dynamicUi.DeleteItem("List", "B"));
        Assert.False(dynamicUi.DeleteItem("List", "B"));

        dynamicUi.Clear("List");
        Assert.Empty(host.Items);
        Assert.True(a.IsDeleted);
    }

    [Fact]
    public void Each_item_is_a_name_scope_and_internal_click_actions_stay_in_their_instance()
    {
        var template = "<DynamicUI Name=\"Row\"><Container><Text Name=\"Title\" Opacity=\"1\">{{VALUE}}</Text><Button Name=\"Trigger\" OnClick=\"Name.Title -> Opacity=0.25\"/></Container></DynamicUI>";
        var (dynamicUi, host) = CreateRuntime(template);
        var first = dynamicUi.CreateItem("Row", "List", "A", new Dictionary<string, object?> { ["VALUE"] = "One" });
        var second = dynamicUi.CreateItem("Row", "List", "B", new Dictionary<string, object?> { ["VALUE"] = "Two" });

        new HavenActionExecutor().ExecuteClick(host, first.GetComponent("Trigger"));

        Assert.Equal(.25d, first.GetComponent("Title").GetValue(HavenProperties.Opacity), 3);
        Assert.Equal(1d, second.GetComponent("Title").GetValue(HavenProperties.Opacity), 3);
        Assert.True(first.CreatesNameScope);
        host.ValidateUniqueNames();
    }

    [Fact]
    public void DynamicUI_and_Prefab_compose_in_both_directions()
    {
        var prefabs = new HavenPrefabCatalog();
        prefabs.Register(new InlinePrefabDefinition(), "<Container><Text Name=\"PrefabTitle\">Inside</Text></Container>", "InlinePrefab.hui");
        prefabs.Register(new RuntimeHostPrefabDefinition(), "<Container><DynamicUIRuntime Name=\"NestedList\"/></Container>", "RuntimeHostPrefab.hui");
        var templates = new HavenDynamicUITemplateCatalog();
        templates.Register("<DynamicUI Name=\"WithPrefab\"><Prefab InstID=\"{{PID}}\" ID=\"InlinePrefab\"/></DynamicUI>", "WithPrefab.hui");
        templates.Register("<DynamicUI Name=\"NestedRow\"><Text Name=\"NestedText\">{{TEXT}}</Text></DynamicUI>", "NestedRow.hui");
        var page = new HavenMarkupParser(prefabs).Parse("<Page><DynamicUIRuntime Name=\"List\"/><Prefab InstID=\"Host-A\" ID=\"RuntimeHostPrefab\"/></Page>");
        var item = new DynamicUI(page, templates, prefabs).CreateItem("WithPrefab", "List", "A", new Dictionary<string, object?> { ["PID"] = "Nested-A" });

        var nestedPrefab = Assert.Single(item.DescendantsAndSelf().OfType<Prefab>());
        Assert.Equal("Nested-A", nestedPrefab.InstanceID);
        var hostPrefab = page.DescendantsAndSelf().OfType<Prefab>().Single(prefab => prefab.PrefabID == "RuntimeHostPrefab");
        var nestedItem = new DynamicUI(hostPrefab, templates, prefabs).CreateItem("NestedRow", "NestedList", "N1", new Dictionary<string, object?> { ["TEXT"] = "Nested" });
        Assert.Equal("Nested", nestedItem.GetComponent<Text>("NestedText").Content);
    }

    [Fact]
    public void Runtime_data_is_not_persisted_when_scope_is_reconstructed()
    {
        var templates = new HavenDynamicUITemplateCatalog();
        templates.Register("<DynamicUI Name=\"Row\"><Text Name=\"Value\">{{VALUE}}</Text></DynamicUI>");
        var firstPage = new HavenMarkupParser().Parse("<Page><DynamicUIRuntime Name=\"List\"/></Page>");
        var first = new DynamicUI(firstPage, templates).CreateItem("Row", "List", "Same-ID", new Dictionary<string, object?> { ["VALUE"] = "Initial" });
        first.SetVariable("VALUE", "Changed");
        Assert.Equal("Changed", first.GetComponent<Text>("Value").Content);

        var secondPage = new HavenMarkupParser().Parse("<Page><DynamicUIRuntime Name=\"List\"/></Page>");
        var second = new DynamicUI(secondPage, templates).CreateItem("Row", "List", "Same-ID", new Dictionary<string, object?> { ["VALUE"] = "Initial" });
        Assert.Equal("Initial", second.GetComponent<Text>("Value").Content);
    }

    [Fact]
    public void Missing_or_malformed_variables_fail_at_template_boundary()
    {
        var templates = new HavenDynamicUITemplateCatalog();
        templates.Register("<DynamicUI Name=\"Row\"><Text>{{VALUE}}</Text></DynamicUI>");
        var page = new HavenMarkupParser().Parse("<Page><DynamicUIRuntime Name=\"List\"/></Page>");
        var dynamicUi = new DynamicUI(page, templates);

        Assert.Throws<KeyNotFoundException>(() => dynamicUi.CreateItem("Row", "List", "A"));
        Assert.Throws<FormatException>(() => templates.Register("<DynamicUI Name=\"Broken\"><Text>{{oops</Text></DynamicUI>", "Broken.hui"));
    }

    [Fact]
    public void Assembly_catalog_discovers_DynamicUI_hui_resources()
    {
        var catalog = HavenDynamicUITemplateCatalog.FromAssembly(typeof(DynamicUiTests).Assembly);
        Assert.Contains("AssemblyButton", catalog.TemplateNames);
        var page = new HavenMarkupParser().Parse("<Page><DynamicUIRuntime Name=\"List\"/></Page>");
        var item = new DynamicUI(page, catalog).CreateItem("AssemblyButton", "List", "Assembly-A", new Dictionary<string, object?>
        {
            ["BUTTONTYPE"] = "Tertiary",
            ["ISENABLED"] = true,
            ["BTNTXT"] = "Hello"
        });

        Assert.Equal("Hello", item.GetComponent<Button>("Action").Content);
        Assert.Equal(ButtonVariant.Tertiary, item.GetComponent<Button>("Action").Variant);
        Assert.True(item.GetComponent("Action").GetValue(HavenProperties.Enabled));
    }

    private static (DynamicUI DynamicUi, DynamicUIRuntime Host) CreateRuntime(string template)
    {
        var catalog = new HavenDynamicUITemplateCatalog();
        catalog.Register(template, "TestDynamicUI.hui");
        var page = new HavenMarkupParser().Parse("<Page><DynamicUIRuntime Name=\"List\"/></Page>");
        var runtime = new DynamicUI(page, catalog);
        return (runtime, runtime.GetRuntime("List"));
    }

    private static Dictionary<string, object?> Values(string text, string type, bool enabled, double opacity, string username, int count) => new()
    {
        ["BTNTXT"] = text,
        ["BUTTONTYPE"] = type,
        ["ISENABLED"] = enabled,
        ["OPACITY"] = opacity,
        ["USERNAME"] = username,
        ["COUNT"] = count
    };

    private sealed class InlinePrefabDefinition : HavenPrefabDefinition
    {
        public override string PrefabID => "InlinePrefab";
    }

    private sealed class RuntimeHostPrefabDefinition : HavenPrefabDefinition
    {
        public override string PrefabID => "RuntimeHostPrefab";
    }
}
