/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/ContainerSettingsView.axaml.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns ContainerSettingsView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Haven.Desktop.Views.Pages.ContainerSettings;

namespace Haven.Desktop.Views;

/// <summary>
/// Represents container settings view and keeps its related state and behavior together.
/// </summary>
public sealed partial class ContainerSettingsView : UserControl
{
    public ContainerSettingsView() => InitializeComponent();

    /// <summary>
    /// Returns the named action buttons so adapter pages can wire event-bus proxies without visual-tree searches.
    /// </summary>
    public IReadOnlyDictionary<string, Button> GetActionButtons() => new Dictionary<string, Button>
    {
        ["Save"] = SaveButton,
        ["Archive"] = ArchiveButton,
        ["Delete"] = DeleteButton,
        ["Discard"] = DiscardButton,
        ["CancelDelete"] = CancelDeleteButton,
        ["ConfirmDelete"] = ConfirmDeleteButton
    };

    /// <summary>
    /// Handles the browse folder clicked event raised by the UI or runtime.
    /// </summary>
    private async void OnBrowseFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ContainerSettingsPage page) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the Haven workspace folder",
            AllowMultiple = false
        });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) page.SetRootPath(path);
    }
}
