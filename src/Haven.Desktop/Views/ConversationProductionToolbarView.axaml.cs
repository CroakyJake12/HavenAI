using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

public sealed partial class ConversationProductionToolbarView : UserControl
{
    private readonly ConversationProductionToolbarViewModel? _viewModel;

    public ConversationProductionToolbarView()
    {
        InitializeComponent();
        if (App.Services is null) return;
        _viewModel = ActivatorUtilities.CreateInstance<ConversationProductionToolbarViewModel>(App.Services);
        _viewModel.BranchChanged += (_, _) => BranchChanged?.Invoke(this, EventArgs.Empty);
        _viewModel.ModelSelected += model => ModelSelected?.Invoke(model);
        _viewModel.ExportRequested += OnExportRequested;
        DataContext = _viewModel;
    }

    public event EventHandler? BranchChanged;
    public event Action<ModelDescriptor>? ModelSelected;

    public Task LoadAsync(Guid conversationId, CancellationToken cancellationToken) =>
        _viewModel?.LoadAsync(conversationId, cancellationToken) ?? Task.CompletedTask;

    private void OnCloudModelChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null || sender is not ComboBox { SelectedItem: ProviderModelChoiceViewModel model }) return;
        if (_viewModel.SelectCloudModelCommand.CanExecute(model)) _viewModel.SelectCloudModelCommand.Execute(model);
    }

    private async void OnCopyShareClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || string.IsNullOrWhiteSpace(_viewModel.ShareAddress)) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null) await clipboard.SetTextAsync(_viewModel.ShareAddress);
    }

    private async void OnExportRequested(ConversationExportRequest request)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null) return;
        var fileType = request.Format switch
        {
            ConversationExportFormat.Markdown => new FilePickerFileType("Markdown") { Patterns = ["*.md"] },
            ConversationExportFormat.Json => new FilePickerFileType("JSON") { Patterns = ["*.json"] },
            _ => new FilePickerFileType("Plain text") { Patterns = ["*.txt"] }
        };
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Haven conversation",
            SuggestedFileName = request.SuggestedFileName,
            DefaultExtension = Path.GetExtension(request.SuggestedFileName).TrimStart('.'),
            FileTypeChoices = [fileType],
            ShowOverwritePrompt = true
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: false);
        await writer.WriteAsync(request.Content);
        await writer.FlushAsync();
    }
}
