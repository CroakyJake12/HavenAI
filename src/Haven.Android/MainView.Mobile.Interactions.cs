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

    private void InstallMobileTopRailNavigation()
    {
        TopRail.HomeRequested -= OnTopRailHomeRequested;
        TopRail.HomeRequested -= OnMobileTopRailHomeRequested;
        TopRail.HomeRequested += OnMobileTopRailHomeRequested;
    }

    private async void OnMobileTopRailHomeRequested(object? sender, System.EventArgs e)
        => await OpenGoAsync();
}
