/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/ChatGroupView.axaml.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns ChatGroupView. Read the type and member comments below as a map of each responsibility.
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
/// Represents chat group view and keeps its related state and behavior together.
/// </summary>
public sealed partial class ChatGroupView : UserControl
{
    public ChatGroupView() => InitializeComponent();

    /// <summary>
    /// Performs the add references_on click step owned by this component.
    /// </summary>
    private async void AddReferences_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ChatGroupPageViewModel viewModel || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add Chat Group references",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Haven references")
                {
                    Patterns = ["*.txt", "*.md", "*.json", "*.csv", "*.tsv", "*.pdf", "*.docx", "*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif", "*.bmp", "*.cs", "*.axaml", "*.js", "*.ts", "*.py", "*.html", "*.css", "*.sql", "*.yaml", "*.yml"]
                }
            ]
        });
        await viewModel.AddReferencesAsync(files.Select(file => file.TryGetLocalPath()).OfType<string>(), CancellationToken.None);
    }
}
