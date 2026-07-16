using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

public sealed partial class ReliabilityStatusView : UserControl
{
    private readonly ReliabilityStatusViewModel? _viewModel;

    public ReliabilityStatusView()
    {
        InitializeComponent();
        if (App.Services is null) return;
        _viewModel = ActivatorUtilities.CreateInstance<ReliabilityStatusViewModel>(App.Services);
        DataContext = _viewModel;
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private async void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        AttachedToVisualTree -= OnAttachedToVisualTree;
        if (_viewModel is not null) await _viewModel.InitializeAsync(CancellationToken.None);
    }

    private async void OnCreateBundleClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose where to save the Haven support bundle",
            AllowMultiple = false
        });
        var folder = folders.FirstOrDefault();
        if (folder is null) return;
        try
        {
            await _viewModel.CreateBundleAsync(folder.Path.LocalPath, CancellationToken.None);
        }
        catch
        {
            // The view model exposes the redacted error in its status field.
        }
    }
}
