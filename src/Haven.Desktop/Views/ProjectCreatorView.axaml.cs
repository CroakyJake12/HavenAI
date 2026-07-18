/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/ProjectCreatorView.axaml.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns ProjectCreatorView. Read the type and member comments below as a map of each responsibility.
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
/// Represents project creator view and keeps its related state and behavior together.
/// </summary>
public sealed partial class ProjectCreatorView : UserControl
{
    public ProjectCreatorView() => InitializeComponent();

    /// <summary>
    /// Handles the choose destination clicked event raised by the UI or runtime.
    /// </summary>
    private async void OnChooseDestinationClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProjectCreatorPageViewModel vm) return;
        var folders = await PickFolderAsync("Choose where Haven should create the project");
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) vm.SetDestination(path);
    }

    /// <summary>
    /// Handles the open folder clicked event raised by the UI or runtime.
    /// </summary>
    private async void OnOpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProjectCreatorPageViewModel vm) return;
        var folders = await PickFolderAsync("Open an existing local project or source folder");
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) await vm.ConnectAsync(path);
    }

    /// <summary>
    /// Handles the open project file clicked event raised by the UI or runtime.
    /// </summary>
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

    /// <summary>
    /// Performs pick folder asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<IReadOnlyList<IStorageFolder>> PickFolderAsync(string title)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        return storage is null
            ? []
            : await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
    }
}
