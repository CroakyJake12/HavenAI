using Haven.Core;
using Haven.Desktop.Views.Pages.Tasks;
using Haven.Desktop.Views.Pages.Automations;
using Haven.Desktop.Views.Shell.NativePresentation;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    public void OpenTasksDashboard()
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

        var page = new NativeTasksSpacePage(
            _conversations,
            StartOneTimeTaskAsync,
            InvokeTaskAsync,
            OpenNativeConversationAsync);

        AddOrSelectTab(
            key,
            "Haven Tasks",
            page,
            closeable: true,
            surface: HavenSurface.Tasks);
    }

    public void OpenAutomationsDashboard()
    {
        var containerId = CurrentChat.SelectedContainer?.Id;
        var key = "haven-automations-" + (containerId?.ToString("N") ?? "global");
        var existing = OpenTabs.FirstOrDefault(item =>
            item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        var page = new NativeAutomationsPage(
            _workspaceState,
            _automations,
            containerId,
            StartOneTimeTaskAsync,
            InvokeTaskAsync,
            _versionedSettings);

        AddOrSelectTab(
            key,
            "Automations",
            page,
            closeable: true,
            surface: HavenSurface.Automations);
    }

    private async Task InvokeTaskAsync(string instruction)
    {
        if (_edition == HavenShellEdition.New)
        {
            var page = CreateNewChatPage();
            page.ConfigureTaskMode();
            await ConfigureAddMenuAsync(page);
            AddOrSelectTab(
                "task-run-" + Guid.NewGuid().ToString("N")[..8],
                "Run Task",
                page,
                true,
                HavenSurface.Tasks,
                forceNewTab: true);
            ApplyShellVisualState();
            page.Submit(instruction);
            return;
        }

        AddOrSelectTab(
            "chat-tasks",
            "Run Task",
            CurrentChat,
            false,
            HavenSurface.Tasks);
        await CurrentChat.InvokeAsync(instruction);
    }

    private async Task StartOneTimeTaskAsync()
    {
        var page = CreateNewChatPage();
        page.ConfigureTaskMode();
        await ConfigureAddMenuAsync(page);
        AddOrSelectTab(
            "task-run-" + Guid.NewGuid().ToString("N")[..8],
            "Run Task",
            page,
            true,
            HavenSurface.Tasks,
            forceNewTab: true);
        ApplyShellVisualState();
        await RefreshNativeChatSidebarAsync();
        page.FocusComposer();
    }
}
