/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/CatalogView.axaml.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns CatalogView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

/// <summary>
/// Represents catalog view and keeps its related state and behavior together.
/// </summary>
public sealed partial class CatalogView : UserControl
{
    public CatalogView() => InitializeComponent();

    /// <summary>
    /// Handles the upload plugin clicked event raised by the UI or runtime.
    /// </summary>
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
