using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

public sealed partial class ContainerSettingsView : UserControl
{
    public ContainerSettingsView() => InitializeComponent();

    private async void OnBrowseFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ContainerSettingsPageViewModel vm) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the Haven workspace folder",
            AllowMultiple = false
        });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) vm.SetRootPath(path);
    }
}
