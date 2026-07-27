using Haven.Core;
using Haven.Desktop.Views.Pages.Macros;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private void OpenTasksDashboard()
    {
        var containerId = CurrentChat.SelectedContainer?.Id;
        var key = "haven-tasks-" + (containerId?.ToString("N") ?? "global");
        var existing = OpenTabs.FirstOrDefault(item =>
            item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        var page = new MacrosPage(
            _bus,
            _workspaceState,
            containerId,
            InvokeMacroAsync);

        AddOrSelectTab(
            key,
            "Haven Tasks",
            page,
            closeable: true,
            surface: HavenSurface.Do);
    }
}
