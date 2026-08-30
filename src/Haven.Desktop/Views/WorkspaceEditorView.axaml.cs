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
using Haven.Desktop.Views.Pages.WorkspaceEditor;

namespace Haven.Desktop.Views;

/// <summary>
/// Represents workspace editor view and keeps its related state and behavior together.
/// </summary>
public sealed partial class WorkspaceEditorView : UserControl
{
    private WorkspaceEditorPage? _page;

    public WorkspaceEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachPage();
        AttachedToVisualTree += (_, _) => AttachPage();
        DetachedFromVisualTree += (_, _) => DetachPage();
    }

    private void AttachPage()
    {
        if (ReferenceEquals(_page, DataContext)) { ApplyRequestedNavigation(); return; }
        DetachPage();
        _page = DataContext as WorkspaceEditorPage;
        if (_page is null) return;
        _page.NavigationRequested += OnNavigationRequested;
        ApplyRequestedNavigation();
        _ = _page.RefreshAdvancedLanguageFeaturesAsync();
    }

    private void DetachPage()
    {
        if (_page is not null) _page.NavigationRequested -= OnNavigationRequested;
        _page = null;
    }

    private void OnNavigationRequested(object? sender, EventArgs e) => ApplyRequestedNavigation();

    private void ApplyRequestedNavigation()
    {
        var range = _page?.TakeRequestedNavigation();
        if (range is null) return;
        try
        {
            var text = EditorTextBox.Text ?? string.Empty;
            var start = WorkspaceEditorPage.OffsetAt(text, range.Start);
            var end = WorkspaceEditorPage.OffsetAt(text, range.End);
            EditorTextBox.SelectionStart = start;
            EditorTextBox.SelectionEnd = end;
            EditorTextBox.CaretIndex = end;
            EditorTextBox.Focus();
        }
        catch (InvalidOperationException) { }
    }

    /// <summary>
    /// Handles the editor selection changed event raised by the UI or runtime.
    /// </summary>
    private void OnEditorSelectionChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && DataContext is WorkspaceEditorPage page)
            page.SetEditorSelection(box.SelectedText ?? string.Empty, box.CaretIndex, box.SelectionStart, box.SelectionEnd);
    }
}
