using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

public sealed partial class ChatGroupView : UserControl
{
    public ChatGroupView() => InitializeComponent();

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
