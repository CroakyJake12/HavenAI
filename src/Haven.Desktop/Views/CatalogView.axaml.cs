using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

public sealed partial class CatalogView : UserControl
{
    public CatalogView() => InitializeComponent();

    private async void OnUploadPluginClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CatalogPageViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a declarative Haven plugin manifest",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("JSON manifest") { Patterns = ["*.json"] }]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) await vm.ImportPluginAsync(path);
    }
}
