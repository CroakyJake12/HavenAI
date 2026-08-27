using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Prefabs;

public sealed class ChatboxPrefab : HavenPrefabDefinition
{
    public override string PrefabID => "Chatbox";

    public override void OnCreated(Prefab instance)
    {
        instance.GetComponent<Button>("AddMenu").Accessibility.AccessibleName = "Add to chat";
        var instruction = instance.GetComponent<Input>("Instruction");
        instruction.Accessibility.AccessibleName = "Ask Haven anything";
        instruction.SetValue(HavenProperties.FontWeight, 500);
        instance.GetComponent<Button>("Send").Accessibility.AccessibleName = "Send message";
    }
}
