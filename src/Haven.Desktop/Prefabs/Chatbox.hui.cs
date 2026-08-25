using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Prefabs;

public sealed class ChatboxPrefab : HavenPrefabDefinition
{
    public override string PrefabID => "Chatbox";

    public override void OnCreated(Prefab instance)
    {
        var root = instance.GetComponent<Container>("ChatboxRoot");
        var input = instance.GetComponent<Input>("Instruction");
        input.CenterVerticallyWhenCompact = true;
        input.Accessibility.AccessibleName = "Ask Haven anything";
        instance.GetComponent<Button>("AddMenu").Accessibility.AccessibleName = "Attach to chat";
        instance.GetComponent<Button>("ChatSettings").Accessibility.AccessibleName = "Manage chat";
        instance.GetComponent<Button>("Send").Accessibility.AccessibleName = "Send message";

        void RefreshShape()
        {
            var width = input.Bounds.Width > 80 ? input.Bounds.Width : 620d;
            var fontSize = Math.Max(11d, input.GetValue(HavenProperties.FontSize));
            var charsPerLine = Math.Max(12, (int)Math.Floor(width / (fontSize * .56d)));
            var lines = Math.Max(1, input.Text.Split('\n').Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length / (double)charsPerLine))));
            var clamped = Math.Min(5, lines);
            var radius = Math.Max(16d, 28d - (clamped - 1) * 3d);
            root.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(radius)));
            input.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(Math.Max(12d, radius - 6d))));
        }

        input.TextChanged += (_, _) => RefreshShape();
        RefreshShape();
    }
}
