/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/GenerativeUiThemeSelectorView.axaml.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns GenerativeUiThemeSelectorView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

/// <summary>
/// Represents generative ui theme selector view and keeps its related state and behavior together.
/// </summary>
public sealed partial class GenerativeUiThemeSelectorView : UserControl, IDisposable
{
    /// <summary>
    /// Stores view model locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly GenerativeUiThemeStudioViewModel? _viewModel;
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _disposed;

    public GenerativeUiThemeSelectorView()
    {
        InitializeComponent();
        var appearanceHint = this.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(text => text.Text?.StartsWith("System follows Windows", StringComparison.Ordinal) == true);
        if (appearanceHint is not null)
            appearanceHint.Text = "Choose the theme's explicit Light or Dark variant for the whole Haven window.";

        if (App.Services is null) return;
        _viewModel = new GenerativeUiThemeStudioViewModel(
            App.Services.GetRequiredService<IGenerativeThemeStore>(),
            App.Services.GetRequiredService<IGenerativeUiRuntime>(),
            App.Services.GetRequiredService<IGenerativeThemeAiService>(),
            App.Services.GetRequiredService<IGenerativeThemeValidator>(),
            App.Services.GetRequiredService<IProviderModelClient>());
        DataContext = _viewModel;
        _viewModel.ExportRequested += OnExportRequested;
        _viewModel.ImportRequested += OnImportRequested;
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AttachedToVisualTree -= OnAttachedToVisualTree;
        if (_viewModel is not null)
        {
            _viewModel.ExportRequested -= OnExportRequested;
            _viewModel.ImportRequested -= OnImportRequested;
        }
        DataContext = null;
    }

    /// <summary>
    /// Handles the attached to visual tree event raised by the UI or runtime.
    /// </summary>
    private async void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        AttachedToVisualTree -= OnAttachedToVisualTree;
        if (_viewModel is not null) await _viewModel.InitializeAsync(CancellationToken.None);
    }

    /// <summary>
    /// Handles the export requested event raised by the UI or runtime.
    /// </summary>
    private async void OnExportRequested(object? sender, ThemeExportRequestedEventArgs e)
    {
        if (_viewModel is null || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose where to share the Haven theme",
            AllowMultiple = false
        });
        var folder = folders.FirstOrDefault();
        if (folder is null) return;
        try { await _viewModel.ExportAsync(e.ThemeId, folder.Path.LocalPath, CancellationToken.None); }
        catch { }
    }

    /// <summary>
    /// Handles the import requested event raised by the UI or runtime.
    /// </summary>
    private async void OnImportRequested(object? sender, EventArgs e)
    {
        if (_viewModel is null || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import a Haven Generative UI theme",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Haven theme") { Patterns = ["*.haven-theme.json"] },
                FilePickerFileTypes.All
            ]
        });
        var file = files.FirstOrDefault();
        if (file is null) return;
        try { await _viewModel.ImportAsync(file.Path.LocalPath, CancellationToken.None); }
        catch { }
    }
}
