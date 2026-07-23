using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.ProjectCreator;

public sealed partial class ProjectCreatorPage
{
    private readonly VisualStudioInstallationService _visualStudioInstallations = new();
    private bool _visualStudioEventsWired;

    private async void OnProjectCreatorAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (!_visualStudioEventsWired)
        {
            _visualStudioEventsWired = true;
            ConnectVisualStudioButton.Click += ConnectVisualStudioAsync;
        }

        await RefreshVisualStudioStateAsync().ConfigureAwait(true);
    }

    private async void ConnectVisualStudioAsync(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            VisualStudioStatusText.Text = "Folder selection is unavailable.";
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a Visual Studio installation folder",
            AllowMultiple = false
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!_visualStudioInstallations.TryConnect(path, out var error))
        {
            VisualStudioStatusText.Text = error;
            return;
        }

        await RefreshVisualStudioStateAsync().ConfigureAwait(true);
    }

    private async Task RefreshVisualStudioStateAsync()
    {
        try
        {
            var installations = await _visualStudioInstallations
                .GetAvailableInstallationsAsync(CancellationToken.None)
                .ConfigureAwait(true);

            var active = installations.FirstOrDefault(item =>
                item.IsConnected || (item.IsComplete && item.IsLaunchable));

            var isAvailable = active is not null;
            SelectPackageButton.IsVisible = isAvailable;
            ConnectVisualStudioCard.IsVisible = !isAvailable;

            if (active is not null)
            {
                var version = string.IsNullOrWhiteSpace(active.InstallationVersion)
                    ? string.Empty
                    : $" {active.InstallationVersion}";
                VisualStudioReadyText.Text = $"{active.DisplayName}{version} ready";
                VisualStudioStatusText.Text = string.Empty;
            }
            else
            {
                VisualStudioStatusText.Text =
                    "No complete launchable Visual Studio installation was detected.";
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            SelectPackageButton.IsVisible = false;
            ConnectVisualStudioCard.IsVisible = true;
            VisualStudioStatusText.Text =
                $"Visual Studio detection failed: {exception.Message}";
        }
    }
}
