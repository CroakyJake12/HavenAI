using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

public sealed partial class ChatView : UserControl
{
    public ChatView() => InitializeComponent();

    private async void OnAttachClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ChatPageViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Attach a file to Haven",
            AllowMultiple = true,
            FileTypeFilter = [FilePickerFileTypes.All]
        });
        vm.AddAttachments(files.Select(file => file.TryGetLocalPath()).OfType<string>());
    }

    private async void OnCopyMessageClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: MessageBubbleViewModel message }) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null) await clipboard.SetTextAsync(message.Content);
    }

    private void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ChatPageViewModel vm) return;
        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control) && vm.SendCommand.CanExecute(null))
        {
            vm.SendCommand.Execute(null);
            e.Handled = true;
        }
    }
}
