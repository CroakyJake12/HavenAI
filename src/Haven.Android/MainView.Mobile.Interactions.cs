using Haven.Core;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    public Task ApplyMobileStartupSurfaceAsync()
    {
        InstallMobileTopRailNavigation();
        return OpenGoAsync();
    }

    public async Task ApplyMobileLaunchRequestAsync(string? surface, string? prompt)
    {
        InstallMobileTopRailNavigation();

        if (!string.IsNullOrWhiteSpace(prompt))
        {
            await OpenNewChatAsync(prompt);
            return;
        }

        // Home/dashboard/launcher requests all resolve to the shared Go surface on Android.
        await OpenGoAsync();
    }

    public async Task SelectAndroidMobileConversationModeAsync(string mode)
    {
        switch (mode.Trim().ToLowerInvariant())
        {
            case "chat":
                await NavigateModeAsync(HavenMode.Chat, false);
                break;
            case "study":
                await SwitchNativeChatModeAsync(HavenMode.Study);
                break;
            case "research":
                // Mobile presents the existing Tasks mode as Research. This preserves the
                // persisted HavenMode value and its desktop workflow while matching mobile copy.
                await SwitchNativeChatModeAsync(HavenMode.Tasks);
                break;
        }
    }

    private void InstallMobileTopRailNavigation()
    {
        TopRail.HomeRequested -= OnTopRailHomeRequested;
        TopRail.HomeRequested -= OnMobileTopRailHomeRequested;
        TopRail.HomeRequested += OnMobileTopRailHomeRequested;
    }

    private async void OnMobileTopRailHomeRequested(object? sender, System.EventArgs e)
        => await OpenGoAsync();
}
