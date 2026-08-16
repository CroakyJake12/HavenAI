using Haven.UI.Components;

namespace Haven.UI.Tests.Prefabs;

public sealed class CatalogCardPrefab : HavenPrefabDefinition
{
    public override void OnCreated(Prefab instance)
    {
        instance.GetComponent<Text>("Title").Content = "Initialized by .hui.cs";
        instance.GetComponent<Button>("AddMenu").Accessibility.Description = "Wired by prefab code-behind";
    }
}
