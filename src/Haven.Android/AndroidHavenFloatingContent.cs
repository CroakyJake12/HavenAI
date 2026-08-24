using Haven.Application;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;

namespace Haven.Android;

/// <summary>Adapts a shared Haven.UI scene to the existing Android floating-activity backend.</summary>
public sealed class AndroidHavenFloatingContent : IFloatingActivityContent
{
    public AndroidHavenFloatingContent(HavenElement root, string automationName)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (string.IsNullOrWhiteSpace(automationName)) throw new ArgumentException("An automation name is required.", nameof(automationName));
        Root = root;
        AutomationName = automationName.Trim();
        Scene = new HavenSceneControl
        {
            Platform = HavenPlatform.Android,
            Root = root
        };
    }

    public HavenElement Root { get; }
    public HavenSceneControl Scene { get; }
    public object Content => Scene;
    public string AutomationName { get; }
}
