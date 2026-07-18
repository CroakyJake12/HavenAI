/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/ConversationProductionToolbarView.axaml.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns ConversationProductionToolbarView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

/// <summary>
/// Represents conversation production toolbar view and keeps its related state and behavior together.
/// </summary>
public sealed partial class ConversationProductionToolbarView : UserControl
{
    /// <summary>
    /// Stores view model locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ConversationProductionToolbarViewModel? _viewModel;
    /// <summary>
    /// Stores usage view locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ConversationUsageView _usageView;
    /// <summary>
    /// Stores message tools locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ConversationMessageToolsView _messageTools;

    public ConversationProductionToolbarView()
    {
        InitializeComponent();
        _usageView = new ConversationUsageView();
        _messageTools = new ConversationMessageToolsView();
        _messageTools.BranchChanged += (_, _) => BranchChanged?.Invoke(this, EventArgs.Empty);
        _messageTools.RegenerationRequested += OnRegenerationRequested;
        ExpandedContent.Children.Add(_usageView);
        ExpandedContent.Children.Add(_messageTools);

        if (App.Services is null) return;
        _viewModel = ActivatorUtilities.CreateInstance<ConversationProductionToolbarViewModel>(App.Services);
        _viewModel.BranchChanged += (_, _) => BranchChanged?.Invoke(this, EventArgs.Empty);
        _viewModel.ModelSelected += model => ModelSelected?.Invoke(model);
        _viewModel.ExportRequested += OnExportRequested;
        DataContext = _viewModel;
    }

    /// <summary>
    /// Stores branch changed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler? BranchChanged;
    /// <summary>
    /// Stores model selected locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event Action<ModelDescriptor>? ModelSelected;

    /// <summary>
    /// Performs load async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task LoadAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        if (_viewModel is not null) await _viewModel.LoadAsync(conversationId, cancellationToken);
        await _usageView.LoadAsync(conversationId, cancellationToken);
        await _messageTools.LoadAsync(conversationId, cancellationToken);
    }

    /// <summary>
    /// Handles the regeneration requested event raised by the UI or runtime.
    /// </summary>
    private async void OnRegenerationRequested(string prompt)
    {
        if (this.FindAncestorOfType<ChatView>()?.DataContext is not ChatPageViewModel chat) return;
        try
        {
            await chat.LoadConversationAsync(chat.ConversationId, CancellationToken.None);
            chat.Composer = prompt;
            if (chat.SendCommand.CanExecute(null)) chat.SendCommand.Execute(null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            System.Diagnostics.Debug.WriteLine("Regeneration could not resume chat: " + ex.Message);
        }
    }

    /// <summary>
    /// Handles the cloud model changed event raised by the UI or runtime.
    /// </summary>
    private void OnCloudModelChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null || sender is not ComboBox { SelectedItem: ProviderModelChoiceViewModel model }) return;
        if (_viewModel.SelectCloudModelCommand.CanExecute(model)) _viewModel.SelectCloudModelCommand.Execute(model);
    }

    /// <summary>
    /// Handles the copy share clicked event raised by the UI or runtime.
    /// </summary>
    private async void OnCopyShareClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || string.IsNullOrWhiteSpace(_viewModel.ShareAddress)) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null) await clipboard.SetTextAsync(_viewModel.ShareAddress);
    }

    /// <summary>
    /// Handles the export requested event raised by the UI or runtime.
    /// </summary>
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
