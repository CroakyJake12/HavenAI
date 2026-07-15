using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

public sealed partial class ProjectCreatorView : UserControl
{
    public ProjectCreatorView() => InitializeComponent();

    private async void OnChooseDestinationClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProjectCreatorPageViewModel vm) return;
        var folders = await PickFolderAsync("Choose where Haven should create the project");
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) vm.SetDestination(path);
    }

    private async void OnOpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProjectCreatorPageViewModel vm) return;
        var folders = await PickFolderAsync("Open an existing local project or source folder");
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) await vm.ConnectAsync(path);
    }

    private async void OnOpenProjectFileClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProjectCreatorPageViewModel vm) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a local project",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Project and solution files") { Patterns = ["*.sln", "*.slnx", "*.csproj", "*.fsproj", "*.vbproj", "*.vcxproj"] },
                FilePickerFileTypes.All
            ]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) await vm.ConnectAsync(path);
    }

    private async Task<IReadOnlyList<IStorageFolder>> PickFolderAsync(string title)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        return storage is null
            ? []
            : await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
    }
}
