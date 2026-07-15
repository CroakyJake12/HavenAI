using Avalonia.Controls;
using Avalonia.Interactivity;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

public sealed partial class WorkspaceEditorView : UserControl
{
    public WorkspaceEditorView() => InitializeComponent();

    private void OnEditorSelectionChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && DataContext is WorkspaceEditorPageViewModel vm)
            vm.SetSelection(box.SelectedText ?? string.Empty);
    }
}
