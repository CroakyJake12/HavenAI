/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/WorkspaceEditorView.axaml.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns WorkspaceEditorView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia.Controls;
using Avalonia.Interactivity;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

/// <summary>
/// Represents workspace editor view and keeps its related state and behavior together.
/// </summary>
public sealed partial class WorkspaceEditorView : UserControl
{
    public WorkspaceEditorView() => InitializeComponent();

    /// <summary>
    /// Handles the editor selection changed event raised by the UI or runtime.
    /// </summary>
    private void OnEditorSelectionChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && DataContext is WorkspaceEditorPageViewModel vm)
            vm.SetSelection(box.SelectedText ?? string.Empty);
    }
}
