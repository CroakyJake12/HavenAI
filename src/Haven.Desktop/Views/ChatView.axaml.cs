using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

public sealed partial class ChatView : UserControl
{
    private readonly ConversationProductionToolbarView _productionToolbar;
    private readonly IConversationRepository? _conversations;
    private readonly IConversationProductionRepository? _production;
    private readonly IMessageAttachmentService? _attachmentService;
    private readonly IAppPaths? _paths;
    private readonly Dictionary<string, Guid> _attachmentIdsByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Guid> _pendingAttachmentIds = [];
    private ChatPageViewModel? _viewModel;
    private CancellationTokenSource? _draftDebounce;
    private CancellationTokenSource? _enterDebounce;
    private bool _loadingDraft;
    private bool _suppressAttachmentCleanup;
    private Guid _attachmentConversationId;

    public ChatView()
    {
        InitializeComponent();
        _productionToolbar = new ConversationProductionToolbarView();
        AttachProductionToolbar();

        if (App.Services is { } services)
        {
            _conversations = services.GetRequiredService<IConversationRepository>();
            _production = services.GetRequiredService<IConversationProductionRepository>();
            _attachmentService = services.GetRequiredService<IMessageAttachmentService>();
            _paths = services.GetRequiredService<IAppPaths>();
        }

        _productionToolbar.BranchChanged += OnBranchChanged;
        _productionToolbar.ModelSelected += OnProductionModelSelected;
        DataContextChanged += OnDataContextChanged;
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DetachedFromVisualTree += (_, _) => DetachViewModel();
        AttachViewModel(DataContext as ChatPageViewModel);
    }

    private void AttachProductionToolbar()
    {
        if (Content is not Grid root) return;
        var existingChildren = root.Children.ToArray();
        root.RowDefinitions.Clear();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        foreach (var child in existingChildren) Grid.SetRow(child, 1);
        Grid.SetRow(_productionToolbar, 0);
        root.Children.Add(_productionToolbar);
    }

    private void OnDataContextChanged(object? sender, EventArgs e) => AttachViewModel(DataContext as ChatPageViewModel);

    private void AttachViewModel(ChatPageViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel)) return;
        DetachViewModel();
        _viewModel = viewModel;
        if (_viewModel is null) return;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.ConversationChanged += OnConversationChanged;
        _viewModel.Attachments.CollectionChanged += OnAttachmentCollectionChanged;
        _ = LoadProductionStateAsync(_viewModel, CancellationToken.None);
    }

    private void DetachViewModel()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.ConversationChanged -= OnConversationChanged;
            _viewModel.Attachments.CollectionChanged -= OnAttachmentCollectionChanged;
        }
        _viewModel = null;
        _draftDebounce?.Cancel();
        _draftDebounce?.Dispose();
        _draftDebounce = null;
        _enterDebounce?.Cancel();
        _enterDebounce?.Dispose();
        _enterDebounce = null;
    }

    private async void OnConversationChanged(object? sender, EventArgs e)
    {
        if (_viewModel is null) return;
        try
        {
            if (_attachmentConversationId != Guid.Empty && _attachmentConversationId != _viewModel.ConversationId)
            {
                _attachmentIdsByPath.Clear();
                _pendingAttachmentIds.Clear();
            }
            await AssociatePendingAttachmentsAsync(_viewModel, CancellationToken.None);
            await LoadProductionStateAsync(_viewModel, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine("Conversation production refresh failed: " + ex.Message);
        }
    }

    private async Task LoadProductionStateAsync(ChatPageViewModel viewModel, CancellationToken cancellationToken)
    {
        await _productionToolbar.LoadAsync(viewModel.ConversationId, cancellationToken);
        if (_production is null || viewModel.IsTemporary) return;
        try
        {
            var branch = await _production.GetCurrentBranchAsync(viewModel.ConversationId, cancellationToken);
            var draft = await _production.GetDraftAsync(viewModel.ConversationId, branch?.Id, cancellationToken);
            if (draft is null || !string.IsNullOrWhiteSpace(viewModel.Composer)) return;
            _loadingDraft = true;
            try { viewModel.Composer = draft.Content; }
            finally { _loadingDraft = false; }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            System.Diagnostics.Debug.WriteLine("Draft recovery failed: " + ex.Message);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatPageViewModel.Composer) && !_loadingDraft)
            ScheduleDraftSave();
    }

    private void ScheduleDraftSave()
    {
        _draftDebounce?.Cancel();
        _draftDebounce?.Dispose();
        _draftDebounce = new CancellationTokenSource();
        _ = SaveDraftAfterDelayAsync(_draftDebounce.Token);
    }

    private async Task SaveDraftAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(650, cancellationToken);
            if (_viewModel is null || _production is null || _viewModel.IsTemporary) return;
            await EnsureConversationSavedAsync(_viewModel, cancellationToken);
            var branch = await _production.GetCurrentBranchAsync(_viewModel.ConversationId, cancellationToken)
                         ?? await _production.EnsureRootBranchAsync(_viewModel.ConversationId, cancellationToken);
            if (string.IsNullOrWhiteSpace(_viewModel.Composer) && _pendingAttachmentIds.Count == 0)
            {
                await _production.DeleteDraftAsync(_viewModel.ConversationId, branch.Id, cancellationToken);
                return;
            }
            await _production.SaveDraftAsync(new ConversationDraft(
                _viewModel.ConversationId,
                branch.Id,
                _viewModel.Composer,
                JsonSerializer.Serialize(_pendingAttachmentIds),
                DateTimeOffset.UtcNow), cancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            System.Diagnostics.Debug.WriteLine("Draft save failed: " + ex.Message);
        }
    }

    private async Task EnsureConversationSavedAsync(ChatPageViewModel viewModel, CancellationToken cancellationToken)
    {
        if (_conversations is null || viewModel.IsTemporary) return;
        if (await _conversations.GetAsync(viewModel.ConversationId, cancellationToken) is not null) return;
        var now = DateTimeOffset.UtcNow;
        var kind = viewModel.Mode switch
        {
            HavenMode.Chat => ConversationKind.Chat,
            HavenMode.Teach when viewModel.SelectedLesson is null => ConversationKind.QuickChat,
            HavenMode.Teach => ConversationKind.LessonChat,
            HavenMode.Do => ConversationKind.Task,
            HavenMode.Studio => ConversationKind.StudioChat,
            _ => ConversationKind.Chat
        };
        await _conversations.UpsertConversationAsync(new Conversation(
            viewModel.ConversationId,
            viewModel.Mode,
            kind,
            viewModel.ConversationTitle,
            viewModel.SelectedContainer?.Id,
            viewModel.SelectedLesson?.Id,
            false,
            false,
            now,
            now), cancellationToken);
    }

    private async void OnBranchChanged(object? sender, EventArgs e)
    {
        if (_viewModel is null) return;
        await _viewModel.LoadConversationAsync(_viewModel.ConversationId, CancellationToken.None);
    }

    private void OnProductionModelSelected(ModelDescriptor model)
    {
        if (_viewModel is null) return;
        var existing = _viewModel.Models.FirstOrDefault(item => item.Name.Equals(model.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            _viewModel.Models.Add(model);
            existing = model;
        }
        _viewModel.SelectedModel = existing;
    }

    private async void OnAttachClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Attach files",
            AllowMultiple = true,
            FileTypeFilter = [FilePickerFileTypes.All]
        });
        await ImportAttachmentsAsync(files.Select(item => item.TryGetLocalPath()).OfType<string>());
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        e.DragEffects = files?.Any(item => item is IStorageFile && !string.IsNullOrWhiteSpace(item.TryGetLocalPath())) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var paths = e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().Select(item => item.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
        e.DragEffects = paths.Length == 0 ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
        if (paths.Length > 0) await ImportAttachmentsAsync(paths);
    }

    private async Task ImportAttachmentsAsync(IEnumerable<string> sourcePaths)
    {
        if (_viewModel is null) return;
        var paths = sourcePaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0) return;

        if (_attachmentService is null || _production is null || _paths is null || _viewModel.IsTemporary)
        {
            _viewModel.AddAttachments(paths);
            return;
        }

        await EnsureConversationSavedAsync(_viewModel, CancellationToken.None);
        var branch = await _production.GetCurrentBranchAsync(_viewModel.ConversationId, CancellationToken.None)
                     ?? await _production.EnsureRootBranchAsync(_viewModel.ConversationId, CancellationToken.None);
        _attachmentConversationId = _viewModel.ConversationId;
        foreach (var path in paths)
        {
            var attachment = await _attachmentService.ImportAsync(
                _viewModel.ConversationId,
                null,
                branch.Id,
                path,
                null,
                CancellationToken.None);
            _pendingAttachmentIds.Add(attachment.Id);
            await AddAttachmentRepresentationsAsync(_viewModel, attachment, CancellationToken.None);
        }
        ScheduleDraftSave();
    }

    private async Task AddAttachmentRepresentationsAsync(ChatPageViewModel viewModel, MessageAttachment attachment, CancellationToken cancellationToken)
    {
        if (_paths is null) return;
        var conversationDirectory = Path.Combine(_paths.AttachmentsDirectory, attachment.ConversationId.ToString("N"));
        var storedPath = Path.Combine(conversationDirectory, attachment.StoredName);
        if (attachment.Kind == MessageAttachmentKind.Image && File.Exists(storedPath))
            AddMappedAttachment(viewModel, storedPath, attachment.Id);

        if (attachment.Kind == MessageAttachmentKind.Video)
        {
            foreach (var relativePath in ReadSampledFrames(attachment.MetadataJson))
            {
                var framePath = Path.GetFullPath(Path.Combine(conversationDirectory, relativePath));
                if (IsInside(framePath, conversationDirectory) && File.Exists(framePath))
                    AddMappedAttachment(viewModel, framePath, attachment.Id);
            }
        }

        if (attachment.Kind != MessageAttachmentKind.Image)
        {
            var contextPath = Path.Combine(conversationDirectory, attachment.Id.ToString("N") + ".haven-context.txt");
            var notice = ReadProcessingNotice(attachment.MetadataJson);
            var builder = new StringBuilder()
                .AppendLine("Haven persistent attachment context")
                .Append("Original file: ").AppendLine(attachment.OriginalName)
                .Append("Media type: ").AppendLine(attachment.MediaType)
                .Append("Processing state: ").AppendLine(attachment.ProcessingState.ToString())
                .Append("Analysis method: ").AppendLine(attachment.AnalysisMethod.ToString())
                .Append("Notice: ").AppendLine(notice);
            if (!string.IsNullOrWhiteSpace(attachment.ExtractedText))
                builder.AppendLine().AppendLine("Extracted content:").AppendLine(attachment.ExtractedText);
            await File.WriteAllTextAsync(contextPath, builder.ToString(), new UTF8Encoding(false), cancellationToken);
            AddMappedAttachment(viewModel, contextPath, attachment.Id);
        }
    }

    private void AddMappedAttachment(ChatPageViewModel viewModel, string path, Guid attachmentId)
    {
        if (viewModel.Attachments.Any(item => item.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
        _attachmentIdsByPath[path] = attachmentId;
        viewModel.AddAttachment(path);
    }

    private async void OnAttachmentCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppressAttachmentCleanup || _viewModel is null || _viewModel.IsSending || _attachmentService is null || e.OldItems is null) return;
        try
        {
            var removedIds = e.OldItems.OfType<AttachmentItemViewModel>()
                .Select(item => _attachmentIdsByPath.TryGetValue(item.Path, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToArray();
            if (removedIds.Length == 0) return;

            _suppressAttachmentCleanup = true;
            foreach (var id in removedIds)
            {
                foreach (var remaining in _viewModel.Attachments.Where(item => _attachmentIdsByPath.TryGetValue(item.Path, out var mapped) && mapped == id).ToArray())
                    _viewModel.Attachments.Remove(remaining);
                foreach (var path in _attachmentIdsByPath.Where(pair => pair.Value == id).Select(pair => pair.Key).ToArray())
                    _attachmentIdsByPath.Remove(path);
                _pendingAttachmentIds.Remove(id);
                await _attachmentService.DeleteAsync(id, CancellationToken.None);
            }
            ScheduleDraftSave();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine("Attachment cleanup failed: " + ex.Message);
        }
        finally
        {
            _suppressAttachmentCleanup = false;
        }
    }

    private async Task AssociatePendingAttachmentsAsync(ChatPageViewModel viewModel, CancellationToken cancellationToken)
    {
        if (_production is null || _conversations is null || _pendingAttachmentIds.Count == 0 || viewModel.IsTemporary || viewModel.Attachments.Count > 0) return;
        var messages = await _conversations.GetMessagesAsync(viewModel.ConversationId, cancellationToken);
        var userMessage = messages.LastOrDefault(item => item.Role == MessageRole.User);
        if (userMessage is null) return;
        var attachments = await _production.GetAttachmentsAsync(viewModel.ConversationId, null, cancellationToken);
        foreach (var attachment in attachments.Where(item => _pendingAttachmentIds.Contains(item.Id)))
            await _production.UpsertAttachmentAsync(attachment with { MessageId = userMessage.Id, UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken);
        _pendingAttachmentIds.Clear();
        _attachmentIdsByPath.Clear();
        var branch = await _production.GetCurrentBranchAsync(viewModel.ConversationId, cancellationToken);
        await _production.DeleteDraftAsync(viewModel.ConversationId, branch?.Id, cancellationToken);
    }

    private async void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null || sender is not TextBox textBox) return;
        if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            await PasteIntoComposerAsync(textBox);
            return;
        }
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        if (e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Control)) return;

        e.Handled = true;
        _enterDebounce?.Cancel();
        _enterDebounce?.Dispose();
        _enterDebounce = new CancellationTokenSource();
        var snapshot = _viewModel.Composer;
        try
        {
            await Task.Delay(80, _enterDebounce.Token);
            if (_viewModel.Composer != snapshot || string.IsNullOrWhiteSpace(snapshot)) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_viewModel.SendCommand.CanExecute(null)) _viewModel.SendCommand.Execute(null);
            });
        }
        catch (OperationCanceledException) { }
    }

    private async Task PasteIntoComposerAsync(TextBox textBox)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null || _viewModel is null) return;
        var files = await clipboard.TryGetFilesAsync();
        var paths = files?.OfType<IStorageFile>().Select(item => item.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
        if (paths.Length > 0)
        {
            await ImportAttachmentsAsync(paths);
            return;
        }

        using var bitmap = await clipboard.TryGetBitmapAsync();
        if (bitmap is not null && _paths is not null)
        {
            var pasteDirectory = Path.Combine(_paths.DataDirectory, "clipboard-imports");
            Directory.CreateDirectory(pasteDirectory);
            var path = Path.Combine(pasteDirectory, "clipboard-" + Guid.NewGuid().ToString("N") + ".png");
            bitmap.Save(path);
            var keepTemporaryCopy = _viewModel.IsTemporary;
            try { await ImportAttachmentsAsync([path]); }
            finally
            {
                if (!keepTemporaryCopy)
                {
                    try { File.Delete(path); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
            return;
        }

        var text = await clipboard.TryGetTextAsync();
        if (text is null) return;
        var current = _viewModel.Composer;
        var start = Math.Clamp(Math.Min(textBox.SelectionStart, textBox.SelectionEnd), 0, current.Length);
        var end = Math.Clamp(Math.Max(textBox.SelectionStart, textBox.SelectionEnd), start, current.Length);
        _viewModel.Composer = current[..start] + text + current[end..];
        textBox.CaretIndex = start + text.Length;
    }

    private async void OnCopyMessageClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: MessageBubbleViewModel message }) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null) await clipboard.SetTextAsync(message.Content);
    }

    private static IReadOnlyList<string> ReadSampledFrames(string metadataJson)
    {
        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (!document.RootElement.TryGetProperty("sampledFrames", out var frames) || frames.ValueKind != JsonValueKind.Array) return [];
            return frames.EnumerateArray().Select(item => item.GetString()).OfType<string>().ToArray();
        }
        catch (JsonException) { return []; }
    }

    private static string ReadProcessingNotice(string metadataJson)
    {
        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (document.RootElement.TryGetProperty("processingNotice", out var notice) && notice.GetString() is { Length: > 0 } text) return text;
        }
        catch (JsonException) { }
        return "No additional processing notice was recorded.";
    }

    private static bool IsInside(string path, string root)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }
}
