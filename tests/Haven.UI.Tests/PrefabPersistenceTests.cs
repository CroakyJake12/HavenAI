using Haven.UI.Components;
using Xunit;

namespace Haven.UI.Tests;

public sealed class PrefabPersistenceTests
{
    [Fact]
    public void Dynamic_state_survives_catalog_recreation_by_prefab_and_instance_id()
    {
        const string markup = "<Container><Text Name=\"AddMenu\" Content=\"Feature\"/></Container>";
        var firstCatalog = new HavenPrefabCatalog();
        var secondCatalog = new HavenPrefabCatalog();
        firstCatalog.Register(new PersistentDynamicPrefab(), markup, "PersistentDynamic.hui");
        secondCatalog.Register(new PersistentDynamicPrefab(), markup, "PersistentDynamic.hui");

        var first = firstCatalog.Create("PersistentDynamic", "Chat-A");
        first.SetComponentEnabled("AddMenu", false);

        var recreated = secondCatalog.Create("PersistentDynamic", "Chat-A");
        var independent = secondCatalog.Create("PersistentDynamic", "Chat-B");
        Assert.False(recreated.IsComponentEnabled("AddMenu"));
        Assert.Equal(HavenVisibility.Collapsed, recreated.GetComponent("AddMenu").GetValue(HavenProperties.Visibility));
        Assert.True(independent.IsComponentEnabled("AddMenu"));
    }

    private sealed class PersistentDynamicPrefab : HavenPrefabDefinition
    {
        public override string PrefabID => "PersistentDynamic";
    }
}
