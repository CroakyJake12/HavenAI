/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/ConversationMessageToolsViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns ConversationMessageToolsViewModel, ConversationMessageChoiceViewModel, MessageVersionItemViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents conversation message tools view model and keeps its related state and behavior together.
/// </summary>
public sealed class ConversationMessageToolsViewModel : ObservableObject
{
    /// <summary>
    /// Stores conversations locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IConversationRepository _conversations;
    /// <summary>
    /// Stores production locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IConversationProductionRepository _production;
    /// <summary>
    /// Stores versioning locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IConversationVersioningService _versioning;
    /// <summary>
    /// Stores conversation id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Guid _conversationId;
    /// <summary>
    /// Stores selected message locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ConversationMessageChoiceViewModel? _selectedMessage;
    /// <summary>
    /// Stores edit content locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _editContent = string.Empty;
    /// <summary>
    /// Stores edit mode locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private MessageEditMode _editMode = MessageEditMode.NewBranch;
    /// <summary>
    /// Stores regeneration mode locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ResponseRegenerationMode _regenerationMode = ResponseRegenerationMode.NewBranch;
    /// <summary>
    /// Stores version index locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _versionIndex = -1;
    /// <summary>
    /// Stores is bookmarked locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isBookmarked;
    /// <summary>
    /// Stores is busy locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isBusy;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = "Select a saved message to edit, regenerate, bookmark, or inspect versions.";

    public ConversationMessageToolsViewModel(
        IConversationRepository conversations,
        IConversationProductionRepository production,
        IConversationVersioningService versioning)
    {
        _conversations = conversations;
        _production = production;
        _versioning = versioning;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ApplyEditCommand = new AsyncRelayCommand(ApplyEditAsync, () => CanEdit && !string.IsNullOrWhiteSpace(EditContent) && !IsBusy);
        RegenerateCommand = new AsyncRelayCommand(RegenerateAsync, () => CanRegenerate && !IsBusy);
        ToggleBookmarkCommand = new AsyncRelayCommand(ToggleBookmarkAsync, () => SelectedMessage is not null && !IsBusy);
        PreviousVersionCommand = new AsyncRelayCommand(() => RestoreVersionAsync(-1), () => CanPreviousVersion && !IsBusy);
        NextVersionCommand = new AsyncRelayCommand(() => RestoreVersionAsync(1), () => CanNextVersion && !IsBusy);
    }

    /// <summary>
    /// Stores branch changed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler? BranchChanged;
    /// <summary>
    /// Stores regeneration requested locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event Action<string>? RegenerationRequested;

    /// <summary>
    /// Gets or updates messages, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ConversationMessageChoiceViewModel> Messages { get; } = [];
    /// <summary>
    /// Gets or updates versions, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<MessageVersionItemViewModel> Versions { get; } = [];
    /// <summary>
    /// Gets or updates edit modes, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<MessageEditMode> EditModes { get; } = Enum.GetValues<MessageEditMode>();
    /// <summary>
    /// Gets or updates regeneration modes, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<ResponseRegenerationMode> RegenerationModes { get; } = Enum.GetValues<ResponseRegenerationMode>();
    /// <summary>
    /// Gets or updates refresh command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>
    /// Gets or updates apply edit command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ApplyEditCommand { get; }
    /// <summary>
    /// Gets or updates regenerate command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RegenerateCommand { get; }
    /// <summary>
    /// Gets or updates toggle bookmark command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ToggleBookmarkCommand { get; }
    /// <summary>
    /// Gets or updates previous version command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand PreviousVersionCommand { get; }
    /// <summary>
    /// Gets or updates next version command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand NextVersionCommand { get; }

    public ConversationMessageChoiceViewModel? SelectedMessage
    {
        get => _selectedMessage;
        set
        {
            if (!SetProperty(ref _selectedMessage, value)) return;
            EditContent = value?.Content ?? string.Empty;
            _ = LoadSelectedMessageStateAsync();
            RaiseActionState();
        }
    }

    public string EditContent
    {
        get => _editContent;
        set
        {
            if (!SetProperty(ref _editContent, value)) return;
            ApplyEditCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Gets or updates edit mode, the bindable or domain state represented by this property.
    /// </summary>
    public MessageEditMode EditMode { get => _editMode; set => SetProperty(ref _editMode, value); }
    /// <summary>
    /// Gets or updates regeneration mode, the bindable or domain state represented by this property.
    /// </summary>
    public ResponseRegenerationMode RegenerationMode { get => _regenerationMode; set => SetProperty(ref _regenerationMode, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            RaiseActionState();
        }
    }
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Reports whether edit applies to the current state.
    /// </summary>
    public bool CanEdit => SelectedMessage?.Role == MessageRole.User;
    /// <summary>
    /// Reports whether regenerate applies to the current state.
    /// </summary>
    public bool CanRegenerate => SelectedMessage?.Role == MessageRole.Assistant;
    public bool IsBookmarked
    {
        get => _isBookmarked;
        private set
        {
            if (!SetProperty(ref _isBookmarked, value)) return;
            RaisePropertyChanged(nameof(BookmarkLabel));
        }
    }
    /// <summary>
    /// Gets or updates bookmark label, the bindable or domain state represented by this property.
    /// </summary>
    public string BookmarkLabel => IsBookmarked ? "Remove bookmark" : "Bookmark";
    public int VersionIndex
    {
        get => _versionIndex;
        private set
        {
            if (!SetProperty(ref _versionIndex, value)) return;
            RaisePropertyChanged(nameof(VersionLabel));
            RaisePropertyChanged(nameof(CanPreviousVersion));
            RaisePropertyChanged(nameof(CanNextVersion));
            PreviousVersionCommand.RaiseCanExecuteChanged();
            NextVersionCommand.RaiseCanExecuteChanged();
        }
    }
    /// <summary>
    /// Gets or updates version label, the bindable or domain state represented by this property.
    /// </summary>
    public string VersionLabel => Versions.Count == 0 || VersionIndex < 0 ? "No saved versions" : $"Version {VersionIndex + 1} of {Versions.Count}";
    /// <summary>
    /// Reports whether previous version applies to the current state.
    /// </summary>
    public bool CanPreviousVersion => VersionIndex > 0;
    /// <summary>
    /// Reports whether next version applies to the current state.
    /// </summary>
    public bool CanNextVersion => VersionIndex >= 0 && VersionIndex < Versions.Count - 1;

    /// <summary>
    /// Performs load asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task LoadAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        _conversationId = conversationId;
        await RefreshAsync(cancellationToken);
    }

    /// <summary>
    /// Opens the tools on a specific rendered message. Keeping selection by stable
    /// message id avoids relying on the old global message dropdown's current value.
    /// </summary>
    public void SelectMessage(Guid messageId)
    {
        SelectedMessage = Messages.FirstOrDefault(message => message.Id == messageId);
    }

    /// <summary>
    /// Performs refresh asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    /// <summary>
    /// Performs refresh asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_conversationId == Guid.Empty) return;
        try
        {
            IsBusy = true;
            var selectedId = SelectedMessage?.Id;
            var messages = await _conversations.GetMessagesAsync(_conversationId, cancellationToken);
            Messages.Clear();
            foreach (var message in messages.Where(item => item.Role is MessageRole.User or MessageRole.Assistant))
                Messages.Add(new ConversationMessageChoiceViewModel(message));
            SelectedMessage = selectedId is null
                ? Messages.LastOrDefault()
                : Messages.FirstOrDefault(item => item.Id == selectedId) ?? Messages.LastOrDefault();
            Status = Messages.Count == 0
                ? "Send a message before using message versions."
                : $"{Messages.Count} saved user/assistant message{(Messages.Count == 1 ? string.Empty : "s")}.";
        }
        catch (Exception ex)
        {
            Status = "Message tools could not refresh: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Performs load selected message state asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task LoadSelectedMessageStateAsync()
    {
        Versions.Clear();
        VersionIndex = -1;
        IsBookmarked = false;
        if (SelectedMessage is null || _conversationId == Guid.Empty) return;
        try
        {
            var branch = await _production.GetCurrentBranchAsync(_conversationId, CancellationToken.None);
            if (branch is not null)
            {
                var versions = await _production.GetVersionsAsync(SelectedMessage.Id, branch.Id, CancellationToken.None);
                if (versions.Count == 0 && await _production.GetCurrentVersionAsync(SelectedMessage.Id, branch.Id, CancellationToken.None) is { } inherited)
                    versions = [inherited];
                foreach (var version in versions) Versions.Add(new MessageVersionItemViewModel(version));
                VersionIndex = Versions.Select((item, index) => (item, index)).FirstOrDefault(pair => pair.item.IsCurrent).index;
                if (Versions.Count > 0 && VersionIndex < 0) VersionIndex = Versions.Count - 1;
            }
            IsBookmarked = (await _production.GetBookmarksAsync(_conversationId, CancellationToken.None))
                .Any(item => item.MessageId == SelectedMessage.Id);
            RaisePropertyChanged(nameof(VersionLabel));
            RaiseActionState();
        }
        catch (Exception ex)
        {
            Status = "Could not load message versions: " + ex.Message;
        }
    }

    /// <summary>
    /// Performs apply edit asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ApplyEditAsync()
    {
        if (SelectedMessage is null || !CanEdit) return;
        try
        {
            IsBusy = true;
            await _versioning.EditUserMessageAsync(
                _conversationId,
                SelectedMessage.Id,
                EditContent,
                EditMode,
                CancellationToken.None);
            Status = EditMode == MessageEditMode.NewBranch
                ? "Edited the user message in a new branch. Later turns were removed from that branch."
                : "Overwrote this branch and saved a recovery version.";
            BranchChanged?.Invoke(this, EventArgs.Empty);
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Status = "Edit failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Performs regenerate asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RegenerateAsync()
    {
        if (SelectedMessage is null || !CanRegenerate) return;
        try
        {
            IsBusy = true;
            var messages = await _conversations.GetMessagesAsync(_conversationId, CancellationToken.None);
            var selectedIndex = messages.Select((item, index) => (item, index))
                .First(pair => pair.item.Id == SelectedMessage.Id).index;
            var latestAssistant = messages.LastOrDefault(item => item.Role == MessageRole.Assistant);
            var isLatest = latestAssistant?.Id == SelectedMessage.Id;
            var mode = isLatest ? RegenerationMode : ResponseRegenerationMode.NewBranch;
            var precedingUser = messages.Take(selectedIndex).LastOrDefault(item => item.Role == MessageRole.User)
                                ?? throw new InvalidOperationException("This response has no preceding user message.");
            await _versioning.PrepareRegenerationAsync(
                _conversationId,
                SelectedMessage.Id,
                isLatest,
                mode,
                CancellationToken.None);
            Status = isLatest && mode == ResponseRegenerationMode.Here
                ? "Prepared this branch for a replacement response."
                : "Created a new branch for the regenerated response.";
            RegenerationRequested?.Invoke(precedingUser.Content);
        }
        catch (Exception ex)
        {
            Status = "Regeneration failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Performs toggle bookmark asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ToggleBookmarkAsync()
    {
        if (SelectedMessage is null) return;
        try
        {
            IsBusy = true;
            var existing = (await _production.GetBookmarksAsync(_conversationId, CancellationToken.None))
                .FirstOrDefault(item => item.MessageId == SelectedMessage.Id);
            if (existing is null)
            {
                await _production.UpsertBookmarkAsync(new MessageBookmark(
                    Guid.NewGuid(),
                    _conversationId,
                    SelectedMessage.Id,
                    SelectedMessage.RoleLabel,
                    BuildBookmarkNote(SelectedMessage.Content),
                    DateTimeOffset.UtcNow), CancellationToken.None);
                IsBookmarked = true;
                Status = "Message bookmarked.";
            }
            else
            {
                await _production.DeleteBookmarkAsync(existing.Id, CancellationToken.None);
                IsBookmarked = false;
                Status = "Bookmark removed.";
            }
        }
        catch (Exception ex)
        {
            Status = "Bookmark update failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Performs restore version asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RestoreVersionAsync(int offset)
    {
        if (SelectedMessage is null || Versions.Count == 0) return;
        var targetIndex = VersionIndex + offset;
        if (targetIndex < 0 || targetIndex >= Versions.Count) return;
        try
        {
            IsBusy = true;
            var branch = await _production.GetCurrentBranchAsync(_conversationId, CancellationToken.None)
                         ?? await _production.EnsureRootBranchAsync(_conversationId, CancellationToken.None);
            var target = Versions[targetIndex].Definition;
            await _production.AddVersionAsync(
                SelectedMessage.Id,
                branch.Id,
                MessageVersionKind.RecoverySnapshot,
                target.Content,
                target.MetadataJson,
                true,
                CancellationToken.None);
            await _production.ReplaceMessageContentAsync(SelectedMessage.Id, target.Content, target.MetadataJson, CancellationToken.None);
            Status = $"Restored version {targetIndex + 1}. The previously current content remains in version history.";
            BranchChanged?.Invoke(this, EventArgs.Empty);
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Status = "Version restore failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Performs the raise action state step owned by this component.
    /// </summary>
    private void RaiseActionState()
    {
        RaisePropertyChanged(nameof(CanEdit));
        RaisePropertyChanged(nameof(CanRegenerate));
        ApplyEditCommand.RaiseCanExecuteChanged();
        RegenerateCommand.RaiseCanExecuteChanged();
        ToggleBookmarkCommand.RaiseCanExecuteChanged();
        PreviousVersionCommand.RaiseCanExecuteChanged();
        NextVersionCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Builds bookmark note from the currently available inputs.
    /// </summary>
    private static string BuildBookmarkNote(string content)
    {
        var text = content.ReplaceLineEndings(" ").Trim();
        return text.Length <= 160 ? text : text[..160] + "…";
    }
}

/// <summary>
/// Represents conversation message choice view model and keeps its related state and behavior together.
/// </summary>
public sealed record ConversationMessageChoiceViewModel(ChatMessage Message)
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id => Message.Id;
    /// <summary>
    /// Gets or updates role, the bindable or domain state represented by this property.
    /// </summary>
    public MessageRole Role => Message.Role;
    /// <summary>
    /// Gets or updates content, the bindable or domain state represented by this property.
    /// </summary>
    public string Content => Message.Content;
    /// <summary>
    /// Gets or updates role label, the bindable or domain state represented by this property.
    /// </summary>
    public string RoleLabel => Role == MessageRole.User ? "You" : Message.AgentName ?? "Haven";
    public string DisplayName
    {
        get
        {
            var singleLine = Content.ReplaceLineEndings(" ").Trim();
            if (singleLine.Length > 90) singleLine = singleLine[..90] + "…";
            return $"{RoleLabel}: {singleLine}";
        }
    }
}

/// <summary>
/// Represents message version item view model and keeps its related state and behavior together.
/// </summary>
public sealed record MessageVersionItemViewModel(MessageVersion Definition)
{
    /// <summary>
    /// Reports whether current applies to the current state.
    /// </summary>
    public bool IsCurrent => Definition.IsCurrent;
    /// <summary>
    /// Gets or updates label, the bindable or domain state represented by this property.
    /// </summary>
    public string Label => $"v{Definition.VersionNumber} · {Definition.Kind} · {Definition.CreatedAt.LocalDateTime:g}";
    public string Preview
    {
        get
        {
            var text = Definition.Content.ReplaceLineEndings(" ").Trim();
            return text.Length <= 120 ? text : text[..120] + "…";
        }
    }
}
