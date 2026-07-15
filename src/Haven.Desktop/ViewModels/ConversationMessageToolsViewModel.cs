using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed class ConversationMessageToolsViewModel : ObservableObject
{
    private readonly IConversationRepository _conversations;
    private readonly IConversationProductionRepository _production;
    private readonly IConversationVersioningService _versioning;
    private Guid _conversationId;
    private ConversationMessageChoiceViewModel? _selectedMessage;
    private string _editContent = string.Empty;
    private MessageEditMode _editMode = MessageEditMode.NewBranch;
    private ResponseRegenerationMode _regenerationMode = ResponseRegenerationMode.NewBranch;
    private int _versionIndex = -1;
    private bool _isBookmarked;
    private bool _isBusy;
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

    public event EventHandler? BranchChanged;
    public event Action<string>? RegenerationRequested;

    public ObservableCollection<ConversationMessageChoiceViewModel> Messages { get; } = [];
    public ObservableCollection<MessageVersionItemViewModel> Versions { get; } = [];
    public IReadOnlyList<MessageEditMode> EditModes { get; } = Enum.GetValues<MessageEditMode>();
    public IReadOnlyList<ResponseRegenerationMode> RegenerationModes { get; } = Enum.GetValues<ResponseRegenerationMode>();
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ApplyEditCommand { get; }
    public AsyncRelayCommand RegenerateCommand { get; }
    public AsyncRelayCommand ToggleBookmarkCommand { get; }
    public AsyncRelayCommand PreviousVersionCommand { get; }
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

    public MessageEditMode EditMode { get => _editMode; set => SetProperty(ref _editMode, value); }
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
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool CanEdit => SelectedMessage?.Role == MessageRole.User;
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
    public string VersionLabel => Versions.Count == 0 || VersionIndex < 0 ? "No saved versions" : $"Version {VersionIndex + 1} of {Versions.Count}";
    public bool CanPreviousVersion => VersionIndex > 0;
    public bool CanNextVersion => VersionIndex >= 0 && VersionIndex < Versions.Count - 1;

    public async Task LoadAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        _conversationId = conversationId;
        await RefreshAsync(cancellationToken);
    }

    private Task RefreshAsync() => RefreshAsync(CancellationToken.None);

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

    private static string BuildBookmarkNote(string content)
    {
        var text = content.ReplaceLineEndings(" ").Trim();
        return text.Length <= 160 ? text : text[..160] + "…";
    }
}

public sealed record ConversationMessageChoiceViewModel(ChatMessage Message)
{
    public Guid Id => Message.Id;
    public MessageRole Role => Message.Role;
    public string Content => Message.Content;
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

public sealed record MessageVersionItemViewModel(MessageVersion Definition)
{
    public bool IsCurrent => Definition.IsCurrent;
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
