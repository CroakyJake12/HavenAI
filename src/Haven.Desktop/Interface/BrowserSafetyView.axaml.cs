/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/BrowserSafetyView.axaml.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns BrowserSafetyView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

/// <summary>
/// Represents browser safety view and keeps its related state and behavior together.
/// </summary>
public sealed partial class BrowserSafetyView : UserControl
{
    public BrowserSafetyView()
    {
        AvaloniaXamlLoader.Load(this);
        CreateViewModel();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    /// <summary>
    /// Handles the attached to visual tree event raised by the UI or runtime.
    /// </summary>
    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) => CreateViewModel();

    /// <summary>
    /// Handles the detached from visual tree event raised by the UI or runtime.
    /// </summary>
    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is IDisposable disposable) disposable.Dispose();
        DataContext = null;
    }

    /// <summary>
    /// Creates view model with the invariants required by its callers.
    /// </summary>
    private void CreateViewModel()
    {
        if (DataContext is not null || App.Services is null) return;
        DataContext = ActivatorUtilities.CreateInstance<BrowserSafetyViewModel>(App.Services);
    }
}
