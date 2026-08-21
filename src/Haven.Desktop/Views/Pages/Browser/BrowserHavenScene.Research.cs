using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using Container = Haven.UI.Components.Container;

namespace Haven.Desktop.Views.Pages.Browser;

internal sealed partial class BrowserHavenScene
{
    public HavenButton ResearchButton { get; private set; } = null!;

    private void BuildResearchTool(Container tools)
    {
        ResearchButton = Tool("Browser.Tools.Research", "Research");
        ResearchButton.Invoked += (_, _) => _page.ToggleResearchCommand.Execute(null);
        tools.Add(ResearchButton);
    }
}
