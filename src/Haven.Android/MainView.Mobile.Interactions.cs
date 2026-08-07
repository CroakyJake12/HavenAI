namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    public Task ApplyMobileStartupSurfaceAsync()
        => OpenGoAsync();

    public async Task ApplyMobileLaunchRequestAsync(string? surface, string? prompt)
    {
        if (!string.IsNullOrWhiteSpace(prompt))
        {
            await OpenNewChatAsync(prompt);
            return;
        }

        // Home/dashboard/launcher requests all resolve to the shared Go surface on Android.
        await OpenGoAsync();
    }
}
