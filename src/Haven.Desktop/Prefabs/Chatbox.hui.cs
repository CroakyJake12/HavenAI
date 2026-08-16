using Haven.UI.Components;

namespace Haven.Desktop.Prefabs;

public sealed class ChatboxPrefab : HavenPrefabDefinition
{
    public override string PrefabID => "Chatbox";

    public override void OnCreated(Prefab instance)
    {
        instance.GetComponent<Button>("AddMenu").Accessibility.AccessibleName = "Add to chat";
        instance.GetComponent<Input>("Instruction").Accessibility.AccessibleName = "Ask Haven anything";
        instance.GetComponent<Button>("Send").Accessibility.AccessibleName = "Send message";
    }
}
