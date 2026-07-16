using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

public sealed partial class GenerativeUiThemeSelectorView : UserControl, IDisposable
{
    private readonly GenerativeUiThemeStudioViewModel? _viewModel;
    private bool _disposed;

    public GenerativeUiThemeSelectorView()
    {
        InitializeComponent();
        if (App.Services is null) return;
        _viewModel = ActivatorUtilities.CreateInstance<GenerativeUiThemeStudioViewModel>(App.Services);
        DataContext = _viewModel;
        _viewModel.ExportRequested += OnExportRequested;
        _viewModel.ImportRequested += OnImportRequested;
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

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

    private async void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        AttachedToVisualTree -= OnAttachedToVisualTree;
        if (_viewModel is not null) await _viewModel.InitializeAsync(CancellationToken.None);
    }

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
