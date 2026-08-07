/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/ChatView.axaml.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns ChatView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

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
using Haven.Desktop.Views.Pages.Chat;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

/// <summary>
/// Represents chat view and keeps its related state and behavior together.
/// </summary>
public sealed partial class ChatView : UserControl
{
    /// <summary>
    /// Stores production toolbar locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ConversationProductionToolbarView _productionToolbar;
    /// <summary>Hosts edit, bookmark, regeneration, and branch actions beside a message.</summary>
    private readonly ConversationMessageToolsView _messageTools;
    /// <summary>Reusable floating surface opened by each message's three-dot button.</summary>
    private readonly Flyout _messageToolsFlyout;
    /// <summary>
    /// Stores conversations locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IConversationRepository? _conversations;
    /// <summary>
    /// Stores production locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IConversationProductionRepository? _production;
    /// <summary>
    /// Stores attachment service locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IMessageAttachmentService? _attachmentService;
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IAppPaths? _paths;
    /// <summary>
    /// Stores attachment ids by path locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Dictionary<string, Guid> _attachmentIdsByPath = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Stores pending attachment ids locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly HashSet<Guid> _pendingAttachmentIds = [];
    /// <summary>
    /// Stores chat page locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ChatPage? _chat;
    /// <summary>
    /// Stores draft debounce locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CancellationTokenSource? _draftDebounce;
    /// <summary>
    /// Stores enter debounce locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CancellationTokenSource? _enterDebounce;
    /// <summary>
    /// Stores loading draft locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _loadingDraft;
    /// <summary>
    /// Stores suppress attachment cleanup locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _suppressAttachmentCleanup;
    /// <summary>
    /// Stores attachment conversation id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Guid _attachmentConversationId;

    public ChatView()
    {
        InitializeComponent();
        _productionToolbar = new ConversationProductionToolbarView();
        _messageTools = new ConversationMessageToolsView { Width = 650 };
        _messageTools.BranchChanged += OnBranchChanged;
        _messageTools.RegenerationRequested += OnMessageRegenerationRequested;
        _messageToolsFlyout = new Flyout { Content = _messageTools };

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
        AttachViewModel(DataContext as ChatPage);
    }

    /// <summary>
    /// Performs the attach production toolbar step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Handles the data context changed event raised by the UI or runtime.
    /// </summary>
    private void OnDataContextChanged(object? sender, EventArgs e) => AttachViewModel(DataContext as ChatPage);

    /// <summary>
    /// Performs the attach view model step owned by this component.
    /// </summary>
    private void AttachViewModel(ChatPage? chat)
    {
        if (ReferenceEquals(_chat, chat)) return;
        DetachViewModel();
        _chat = chat;
        if (_chat is null) return;
        _chat.PropertyChanged += OnViewModelPropertyChanged;
        _chat.ConversationChanged += OnConversationChanged;
        _chat.Attachments.CollectionChanged += OnAttachmentCollectionChanged;
        _ = LoadProductionStateAsync(_chat, CancellationToken.None);
    }

    /// <summary>
    /// Performs the detach view model step owned by this component.
    /// </summary>
    private void DetachViewModel()
    {
        if (_chat is not null)
        {
            _chat.PropertyChanged -= OnViewModelPropertyChanged;
            _chat.ConversationChanged -= OnConversationChanged;
            _chat.Attachments.CollectionChanged -= OnAttachmentCollectionChanged;
        }
        _chat = null;
        _draftDebounce?.Cancel();
        _draftDebounce?.Dispose();
        _draftDebounce = null;
        _enterDebounce?.Cancel();
        _enterDebounce?.Dispose();
        _enterDebounce = null;
    }

    /// <summary>
    /// Handles the conversation changed event raised by the UI or runtime.
    /// </summary>
    private async void OnConversationChanged(object? sender, EventArgs e)
    {
        if (_chat is null) return;
        try
        {
            if (_attachmentConversationId != Guid.Empty && _attachmentConversationId != _chat.ConversationId)
            {
                _attachmentIdsByPath.Clear();
                _pendingAttachmentIds.Clear();
            }
            await AssociatePendingAttachmentsAsync(_chat, CancellationToken.None);
            await LoadProductionStateAsync(_chat, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine("Conversation production refresh failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Performs load production state asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task LoadProductionStateAsync(ChatPage chat, CancellationToken cancellationToken)
    {
        await _productionToolbar.LoadAsync(chat.ConversationId, cancellationToken);
        await _messageTools.LoadAsync(chat.ConversationId, cancellationToken);
        if (_production is null || chat.IsTemporary) return;
        try
        {
            var branch = await _production.GetCurrentBranchAsync(chat.ConversationId, cancellationToken);
            var draft = await _production.GetDraftAsync(chat.ConversationId, branch?.Id, cancellationToken);
            if (draft is null || !string.IsNullOrWhiteSpace(chat.Composer)) return;
            _loadingDraft = true;
            try { chat.Composer = draft.Content; }
            finally { _loadingDraft = false; }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            System.Diagnostics.Debug.WriteLine("Draft recovery failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Handles the view model property changed event raised by the UI or runtime.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatPage.Composer) && !_loadingDraft)
            ScheduleDraftSave();
    }

    /// <summary>
    /// Performs the schedule draft save step owned by this component.
    /// </summary>
    private void ScheduleDraftSave()
    {
        _draftDebounce?.Cancel();
        _draftDebounce?.Dispose();
        _draftDebounce = new CancellationTokenSource();
        _ = SaveDraftAfterDelayAsync(_draftDebounce.Token);
    }

    /// <summary>
    /// Performs save draft after delay asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task SaveDraftAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(650, cancellationToken);
            if (_chat is null || _production is null || _chat.IsTemporary) return;
            await EnsureConversationSavedAsync(_chat, cancellationToken);
            var branch = await _production.GetCurrentBranchAsync(_chat.ConversationId, cancellationToken)
                         ?? await _production.EnsureRootBranchAsync(_chat.ConversationId, cancellationToken);
            if (string.IsNullOrWhiteSpace(_chat.Composer) && _pendingAttachmentIds.Count == 0)
            {
                await _production.DeleteDraftAsync(_chat.ConversationId, branch.Id, cancellationToken);
                return;
            }
            await _production.SaveDraftAsync(new ConversationDraft(
                _chat.ConversationId,
                branch.Id,
                _chat.Composer,
                JsonSerializer.Serialize(_pendingAttachmentIds),
                DateTimeOffset.UtcNow), cancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            System.Diagnostics.Debug.WriteLine("Draft save failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Performs ensure conversation saved asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task EnsureConversationSavedAsync(ChatPage chat, CancellationToken cancellationToken)
    {
        if (_conversations is null || chat.IsTemporary) return;
        if (await _conversations.GetAsync(chat.ConversationId, cancellationToken) is not null) return;
        var now = DateTimeOffset.UtcNow;
        var kind = chat.Mode switch
        {
            HavenMode.Chat => ConversationKind.Chat,
            HavenMode.Study when chat.SelectedLesson is null => ConversationKind.QuickChat,
            HavenMode.Study => ConversationKind.LessonChat,
            HavenMode.Tasks => ConversationKind.Task,
            HavenMode.Studio => ConversationKind.StudioChat,
            _ => ConversationKind.Chat
        };
        await _conversations.UpsertConversationAsync(new Conversation(
            chat.ConversationId,
            chat.Mode,
            kind,
            chat.ConversationTitle,
            chat.SelectedContainer?.Id,
            chat.SelectedLesson?.Id,
            false,
            false,
            now,
            now), cancellationToken);
    }

    /// <summary>
    /// Handles the branch changed event raised by the UI or runtime.
    /// </summary>
    private async void OnBranchChanged(object? sender, EventArgs e)
    {
        if (_chat is null) return;
        await _chat.LoadConversationAsync(_chat.ConversationId, CancellationToken.None);
    }

    /// <summary>
    /// Handles the production model selected event raised by the UI or runtime.
    /// </summary>
    private void OnProductionModelSelected(ModelDescriptor model)
    {
        if (_chat is null) return;
        var existing = _chat.Models.FirstOrDefault(item => item.Name.Equals(model.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            _chat.Models.Add(model);
            existing = model;
        }
        _chat.SelectedModel = existing;
    }

    /// <summary>
    /// Handles the attach clicked event raised by the UI or runtime.
    /// </summary>
    private async void OnAttachClicked(object? sender, RoutedEventArgs e)
    {
        if (_chat is null) return;
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

    /// <summary>
    /// Handles the drag over event raised by the UI or runtime.
    /// </summary>
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        e.DragEffects = files?.Any(item => item is IStorageFile && !string.IsNullOrWhiteSpace(item.TryGetLocalPath())) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// Handles the drop event raised by the UI or runtime.
    /// </summary>
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var paths = e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().Select(item => item.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
        e.DragEffects = paths.Length == 0 ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
        if (paths.Length > 0) await ImportAttachmentsAsync(paths);
    }

    /// <summary>
    /// Performs import attachments asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ImportAttachmentsAsync(IEnumerable<string> sourcePaths)
    {
        if (_chat is null) return;
        var paths = sourcePaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0) return;

        if (_attachmentService is null || _production is null || _paths is null || _chat.IsTemporary)
        {
            _chat.AddAttachments(paths);
            return;
        }

        await EnsureConversationSavedAsync(_chat, CancellationToken.None);
        var branch = await _production.GetCurrentBranchAsync(_chat.ConversationId, CancellationToken.None)
                     ?? await _production.EnsureRootBranchAsync(_chat.ConversationId, CancellationToken.None);
        _attachmentConversationId = _chat.ConversationId;
        foreach (var path in paths)
        {
            var attachment = await _attachmentService.ImportAsync(
                _chat.ConversationId,
                null,
                branch.Id,
                path,
                null,
                CancellationToken.None);
            _pendingAttachmentIds.Add(attachment.Id);
            await AddAttachmentRepresentationsAsync(_chat, attachment, CancellationToken.None);
        }
        ScheduleDraftSave();
    }

    /// <summary>
    /// Performs add attachment representations asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task AddAttachmentRepresentationsAsync(ChatPage chat, MessageAttachment attachment, CancellationToken cancellationToken)
    {
        if (_paths is null) return;
        var conversationDirectory = Path.Combine(_paths.AttachmentsDirectory, attachment.ConversationId.ToString("N"));
        var storedPath = Path.Combine(conversationDirectory, attachment.StoredName);
        if (attachment.Kind == MessageAttachmentKind.Image && File.Exists(storedPath))
            AddMappedAttachment(chat, storedPath, attachment.Id);

        if (attachment.Kind == MessageAttachmentKind.Video)
        {
            foreach (var relativePath in ReadSampledFrames(attachment.MetadataJson))
            {
                var framePath = Path.GetFullPath(Path.Combine(conversationDirectory, relativePath));
                if (IsInside(framePath, conversationDirectory) && File.Exists(framePath))
                    AddMappedAttachment(chat, framePath, attachment.Id);
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
            AddMappedAttachment(chat, contextPath, attachment.Id);
        }
    }

    /// <summary>
    /// Performs the add mapped attachment step owned by this component.
    /// </summary>
    private void AddMappedAttachment(ChatPage chat, string path, Guid attachmentId)
    {
        if (chat.Attachments.Any(item => item.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
        _attachmentIdsByPath[path] = attachmentId;
        chat.AddAttachment(path);
    }

    /// <summary>
    /// Handles the attachment collection changed event raised by the UI or runtime.
    /// </summary>
    private async void OnAttachmentCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppressAttachmentCleanup || _chat is null || _chat.IsSending || _attachmentService is null || e.OldItems is null) return;
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
                foreach (var remaining in _chat.Attachments.Where(item => _attachmentIdsByPath.TryGetValue(item.Path, out var mapped) && mapped == id).ToArray())
                    _chat.Attachments.Remove(remaining);
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

    /// <summary>
    /// Performs associate pending attachments asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task AssociatePendingAttachmentsAsync(ChatPage chat, CancellationToken cancellationToken)
    {
        if (_production is null || _conversations is null || _pendingAttachmentIds.Count == 0 || chat.IsTemporary || chat.Attachments.Count > 0) return;
        var messages = await _conversations.GetMessagesAsync(chat.ConversationId, cancellationToken);
        var userMessage = messages.LastOrDefault(item => item.Role == MessageRole.User);
        if (userMessage is null) return;
        var attachments = await _production.GetAttachmentsAsync(chat.ConversationId, null, cancellationToken);
        foreach (var attachment in attachments.Where(item => _pendingAttachmentIds.Contains(item.Id)))
            await _production.UpsertAttachmentAsync(attachment with { MessageId = userMessage.Id, UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken);
        _pendingAttachmentIds.Clear();
        _attachmentIdsByPath.Clear();
        var branch = await _production.GetCurrentBranchAsync(chat.ConversationId, cancellationToken);
        await _production.DeleteDraftAsync(chat.ConversationId, branch?.Id, cancellationToken);
    }

    /// <summary>
    /// Handles the composer key down event raised by the UI or runtime.
    /// </summary>
    private async void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (_chat is null || sender is not TextBox textBox) return;
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
        var snapshot = _chat.Composer;
        try
        {
            await Task.Delay(80, _enterDebounce.Token);
            if (_chat.Composer != snapshot || string.IsNullOrWhiteSpace(snapshot)) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_chat.SendCommand.CanExecute(null)) _chat.SendCommand.Execute(null);
            });
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Performs paste into composer asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task PasteIntoComposerAsync(TextBox textBox)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null || _chat is null) return;
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
            var keepTemporaryCopy = _chat.IsTemporary;
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
        var current = _chat.Composer;
        var start = Math.Clamp(Math.Min(textBox.SelectionStart, textBox.SelectionEnd), 0, current.Length);
        var end = Math.Clamp(Math.Max(textBox.SelectionStart, textBox.SelectionEnd), start, current.Length);
        _chat.Composer = current[..start] + text + current[end..];
        textBox.CaretIndex = start + text.Length;
    }

    /// <summary>
    /// Handles the copy message clicked event raised by the UI or runtime.
    /// </summary>
    private async void OnCopyMessageClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: MessageBubbleViewModel message }) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null) await clipboard.SetTextAsync(message.Content);
    }

    /// <summary>Opens the same complete action surface from a message's right-click menu.</summary>
    private void OnMessageActionsMenuClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: MessageBubbleViewModel message } item) return;
        _messageTools.SelectMessage(message.Id);
        _messageToolsFlyout.ShowAt(item);
    }

    /// <summary>Opens message-specific actions next to the message instead of in a global toolbar.</summary>
    private void OnMessageActionsClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MessageBubbleViewModel message } button) return;
        _messageTools.SelectMessage(message.Id);
        _messageToolsFlyout.ShowAt(button);
    }

    /// <summary>Resumes chat after the message panel prepares a response regeneration branch.</summary>
    private async void OnMessageRegenerationRequested(string prompt)
    {
        if (_chat is null) return;
        try
        {
            await _chat.LoadConversationAsync(_chat.ConversationId, CancellationToken.None);
            _chat.Composer = prompt;
            if (_chat.SendCommand.CanExecute(null)) _chat.SendCommand.Execute(null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            System.Diagnostics.Debug.WriteLine("Regeneration could not resume chat: " + ex.Message);
        }
    }

    /// <summary>
    /// Performs the read sampled frames step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the read processing notice step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Reports whether inside applies to the current state.
    /// </summary>
    private static bool IsInside(string path, string root)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }
}
