/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/ReliabilityStatusView.axaml.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns ReliabilityStatusView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

/// <summary>
/// Represents reliability status view and keeps its related state and behavior together.
/// </summary>
public sealed partial class ReliabilityStatusView : UserControl
{
    /// <summary>
    /// Stores view model locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ReliabilityStatusViewModel? _viewModel;

    public ReliabilityStatusView()
    {
        InitializeComponent();
        if (App.Services is null) return;
        _viewModel = ActivatorUtilities.CreateInstance<ReliabilityStatusViewModel>(App.Services);
        DataContext = _viewModel;
        AttachedToVisualTree += OnAttachedToVisualTree;
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
    /// Handles the create bundle clicked event raised by the UI or runtime.
    /// </summary>
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
