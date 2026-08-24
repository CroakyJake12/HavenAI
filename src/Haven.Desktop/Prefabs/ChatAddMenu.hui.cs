using Haven.UI.Components;

namespace Haven.Desktop.Prefabs;

public sealed class ChatAddMenuPrefab : HavenPrefabDefinition
{
    public override string PrefabID => "ChatAddMenu";

    public override void OnCreated(Prefab instance)
    {
        instance.GetComponent<Button>("Dismiss").Accessibility.AccessibleName = "Close Add menu";
        instance.GetComponent<Button>("Agents").Accessibility.AccessibleName = "Agents";
        instance.GetComponent<Button>("Instructions").Accessibility.AccessibleName = "Instructions";
        instance.GetComponent<Button>("Capabilities").Accessibility.AccessibleName = "Capabilities";
        instance.GetComponent<Button>("Apps").Accessibility.AccessibleName = "Apps";
        instance.GetComponent<Button>("AllowActions").Accessibility.AccessibleName = "Allow Actions";
        instance.GetComponent<Button>("VisualResponses").Accessibility.AccessibleName = "Prefer Visual Responses";
        instance.GetComponent<Button>("AttachFiles").Accessibility.AccessibleName = "Attach File(s)";
        instance.GetComponent<Input>("CatalogSearch").Accessibility.AccessibleName = "Search Add catalogue";
    }
}
