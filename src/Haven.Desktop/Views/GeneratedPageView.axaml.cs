/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/GeneratedPageView.axaml.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns GeneratedPageView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia.Controls;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

/// <summary>
/// Represents generated page view and keeps its related state and behavior together.
/// </summary>
public sealed partial class GeneratedPageView : UserControl, IDisposable
{
    public GeneratedPageView() => InitializeComponent();

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        if (DataContext is IDisposable disposable) disposable.Dispose();
        DataContext = null;
    }
}
