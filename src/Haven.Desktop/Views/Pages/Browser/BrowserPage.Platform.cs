using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Haven.Desktop.Views.Pages.Browser;

public sealed partial class BrowserPage
{
    private async void OnImportExtensionRequested(object? sender, bool convertChrome)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = convertChrome ? "Select a Chrome manifest.json" : "Select a Haven extension manifest",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("JSON manifest") { Patterns = ["*.json"] }]
            });
            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path)) await ImportExtensionAsync(path, convertChrome);
        }
        catch (Exception exception) { ReportBrowserError(exception); }
    }
}
