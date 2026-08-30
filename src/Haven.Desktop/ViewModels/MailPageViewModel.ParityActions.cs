using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed partial class MailPageViewModel
{
    private readonly HashSet<string> _selectedMessageIds = new(StringComparer.Ordinal);
    private MailFolder? _selectedMoveTarget;
    private bool _isFolderEditorOpen;
    private bool _isFolderDeleteConfirmationOpen;
    private bool _isBulkDeleteConfirmationOpen;
    private string _folderEditorTitle = string.Empty;
    private string _folderName = string.Empty;
    private string? _folderEditingId;

    private AsyncRelayCommand? _toggleSpamCommand;
    private AsyncRelayCommand? _toggleImportantCommand;
    private AsyncRelayCommand? _bulkArchiveCommand;
    private RelayCommand? _requestBulkDeleteCommand;
    private AsyncRelayCommand? _confirmBulkDeleteCommand;
    private RelayCommand? _cancelBulkDeleteCommand;
    private AsyncRelayCommand? _bulkMarkReadCommand;
    private AsyncRelayCommand? _bulkMarkUnreadCommand;
    private AsyncRelayCommand? _bulkFlagCommand;
    private AsyncRelayCommand? _bulkSpamCommand;
    private AsyncRelayCommand? _bulkImportantCommand;
    private AsyncRelayCommand? _moveMessagesCommand;
    private RelayCommand? _openCreateFolderCommand;
    private RelayCommand? _openRenameFolderCommand;
    private RelayCommand? _closeFolderEditorCommand;
    private AsyncRelayCommand? _saveFolderCommand;
    private RelayCommand? _requestDeleteFolderCommand;
    private AsyncRelayCommand? _confirmDeleteFolderCommand;
    private RelayCommand? _cancelDeleteFolderCommand;

    public ObservableCollection<MailFolder> MoveTargets { get; } = [];

    public MailFolder? SelectedMoveTarget
    {
        get => _selectedMoveTarget;
        set => SetProperty(ref _selectedMoveTarget, value);
    }

    public bool IsFolderEditorOpen
    {
        get => _isFolderEditorOpen;
        private set => SetProperty(ref _isFolderEditorOpen, value);
    }

    public bool IsFolderDeleteConfirmationOpen
    {
        get => _isFolderDeleteConfirmationOpen;
        private set => SetProperty(ref _isFolderDeleteConfirmationOpen, value);
    }

    public bool IsBulkDeleteConfirmationOpen
    {
        get => _isBulkDeleteConfirmationOpen;
        private set => SetProperty(ref _isBulkDeleteConfirmationOpen, value);
    }

    public string FolderEditorTitle
    {
        get => _folderEditorTitle;
        private set => SetProperty(ref _folderEditorTitle, value);
    }

    public string FolderName
    {
        get => _folderName;
        set => SetProperty(ref _folderName, value);
    }

    public int BulkSelectionCount => _selectedMessageIds.Count;
    public bool HasBulkSelection => BulkSelectionCount > 1;
    public string BulkSelectionLabel => $"{BulkSelectionCount} selected";
    public string SpamActionLabel => SelectedFolder?.Kind == MailFolderKind.Spam ? "Not spam" : "Spam";
    public string ImportantActionLabel => SelectedMessage?.IsImportant == true ? "Not important" : "Important";

    public bool CanManageFolders => Supports(MailProviderCapabilities.FolderManagement);
    public bool CanManageSelectedFolder => CanManageFolders && SelectedFolder is { IsSystem: false, Kind: MailFolderKind.Custom };
    public bool CanMove => Supports(MailProviderCapabilities.Move);
    public bool CanSpam => Supports(MailProviderCapabilities.Spam);
    public bool CanImportant => Supports(MailProviderCapabilities.Important);
    public bool CanBulk => Supports(MailProviderCapabilities.Bulk);

    public AsyncRelayCommand ToggleSpamCommand => _toggleSpamCommand ??= new(ToggleSpamAsync);
    public AsyncRelayCommand ToggleImportantCommand => _toggleImportantCommand ??= new(ToggleImportantAsync);
    public AsyncRelayCommand BulkArchiveCommand => _bulkArchiveCommand ??= new(() => ExecuteBulkAsync(MailBulkActionKind.Archive));
    public RelayCommand RequestBulkDeleteCommand => _requestBulkDeleteCommand ??= new(() => IsBulkDeleteConfirmationOpen = true);
    public AsyncRelayCommand ConfirmBulkDeleteCommand => _confirmBulkDeleteCommand ??= new(ConfirmBulkDeleteAsync);
    public RelayCommand CancelBulkDeleteCommand => _cancelBulkDeleteCommand ??= new(() => IsBulkDeleteConfirmationOpen = false);
    public AsyncRelayCommand BulkMarkReadCommand => _bulkMarkReadCommand ??= new(() => ExecuteBulkAsync(MailBulkActionKind.MarkRead));
    public AsyncRelayCommand BulkMarkUnreadCommand => _bulkMarkUnreadCommand ??= new(() => ExecuteBulkAsync(MailBulkActionKind.MarkUnread));
    public AsyncRelayCommand BulkFlagCommand => _bulkFlagCommand ??= new(() => ExecuteBulkAsync(MailBulkActionKind.Flag));
    public AsyncRelayCommand BulkSpamCommand => _bulkSpamCommand ??= new(() => ExecuteBulkAsync(MailBulkActionKind.Spam));
    public AsyncRelayCommand BulkImportantCommand => _bulkImportantCommand ??= new(() => ExecuteBulkAsync(MailBulkActionKind.Important));
    public AsyncRelayCommand MoveMessagesCommand => _moveMessagesCommand ??= new(MoveMessagesAsync);
    public RelayCommand OpenCreateFolderCommand => _openCreateFolderCommand ??= new(OpenCreateFolder);
    public RelayCommand OpenRenameFolderCommand => _openRenameFolderCommand ??= new(OpenRenameFolder);
    public RelayCommand CloseFolderEditorCommand => _closeFolderEditorCommand ??= new(CloseFolderEditor);
    public AsyncRelayCommand SaveFolderCommand => _saveFolderCommand ??= new(SaveFolderAsync);
    public RelayCommand RequestDeleteFolderCommand => _requestDeleteFolderCommand ??= new(RequestDeleteFolder);
    public AsyncRelayCommand ConfirmDeleteFolderCommand => _confirmDeleteFolderCommand ??= new(ConfirmDeleteFolderAsync);
    public RelayCommand CancelDeleteFolderCommand => _cancelDeleteFolderCommand ??= new(() => IsFolderDeleteConfirmationOpen = false);

    public void SetMessageSelection(IEnumerable<MailMessageSummary> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        _selectedMessageIds.Clear();
        foreach (var message in messages)
            if (!string.IsNullOrWhiteSpace(message.Id)) _selectedMessageIds.Add(message.Id);
        RaisePropertyChanged(nameof(BulkSelectionCount));
        RaisePropertyChanged(nameof(HasBulkSelection));
        RaisePropertyChanged(nameof(BulkSelectionLabel));
    }

    public void NotifyFolderSelectionChanged()
    {
        RefreshMoveTargets();
        RaisePropertyChanged(nameof(CanManageSelectedFolder));
        RaisePropertyChanged(nameof(SpamActionLabel));
    }

    public void NotifyMessageSelectionChanged()
    {
        RaisePropertyChanged(nameof(ImportantActionLabel));
    }

    public void RefreshMoveTargets()
    {
        var selectedId = SelectedMoveTarget?.Id;
        MoveTargets.Clear();
        foreach (var folder in Folders.Where(folder => folder.Kind is not (MailFolderKind.Sent or MailFolderKind.Drafts)))
            MoveTargets.Add(folder);
        SelectedMoveTarget = MoveTargets.FirstOrDefault(folder => folder.Id == selectedId)
                             ?? MoveTargets.FirstOrDefault(folder => folder.Kind == MailFolderKind.Archive)
                             ?? MoveTargets.FirstOrDefault();
    }

    private async Task ToggleSpamAsync()
    {
        var target = SelectedFolder?.Kind != MailFolderKind.Spam;
        await MutateSelectedAsync((account, id) => _mail.SetSpamAsync(account, id, target, CancellationToken.None));
    }

    private async Task ToggleImportantAsync()
    {
        if (SelectedMessage is null) return;
        var target = !SelectedMessage.IsImportant;
        await MutateSelectedAsync((account, id) => _mail.SetImportantAsync(account, id, target, CancellationToken.None), reload: false);
        if (SelectedMessage is not null) SelectedMessage = SelectedMessage with { IsImportant = target };
        NotifyMessageSelectionChanged();
    }

    private async Task ExecuteBulkAsync(MailBulkActionKind action)
    {
        if (SelectedAccount is null || _selectedMessageIds.Count == 0) return;
        try
        {
            IsBusy = true;
            var result = await _mail.ExecuteBulkAsync(SelectedAccount.AccountId, _selectedMessageIds.ToArray(), action, CancellationToken.None);
            Status = result.Message;
            await LoadMessagesAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ApplyFailure(ex, preserveMessages: true);
        }
        finally { IsBusy = false; }
    }

    private async Task ConfirmBulkDeleteAsync()
    {
        IsBulkDeleteConfirmationOpen = false;
        await ExecuteBulkAsync(MailBulkActionKind.Delete);
    }

    private async Task MoveMessagesAsync()
    {
        if (SelectedAccount is null || SelectedMoveTarget is null) return;
        var ids = _selectedMessageIds.Count > 0
            ? _selectedMessageIds.ToArray()
            : SelectedMessage is null ? [] : [SelectedMessage.Id];
        if (ids.Length == 0) return;

        try
        {
            IsBusy = true;
            var result = await _mail.MoveMessagesToFolderAsync(SelectedAccount.AccountId, ids, SelectedMoveTarget.Id, CancellationToken.None);
            Status = result.Message;
            await LoadMessagesAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ApplyFailure(ex, preserveMessages: true);
        }
        finally { IsBusy = false; }
    }

    private void OpenCreateFolder()
    {
        if (!CanManageFolders) return;
        _folderEditingId = null;
        FolderName = string.Empty;
        FolderEditorTitle = SelectedAccount?.Provider == CalendarProviderKind.Google ? "New label" : "New folder";
        IsFolderEditorOpen = true;
    }

    private void OpenRenameFolder()
    {
        if (!CanManageSelectedFolder || SelectedFolder is null) return;
        _folderEditingId = SelectedFolder.Id;
        FolderName = SelectedFolder.DisplayName;
        FolderEditorTitle = SelectedAccount?.Provider == CalendarProviderKind.Google ? "Rename label" : "Rename folder";
        IsFolderEditorOpen = true;
    }

    private void CloseFolderEditor()
    {
        IsFolderEditorOpen = false;
        FolderName = string.Empty;
        _folderEditingId = null;
    }

    private async Task SaveFolderAsync()
    {
        if (SelectedAccount is null || string.IsNullOrWhiteSpace(FolderName))
        {
            Status = "Enter a folder or label name.";
            return;
        }

        var result = string.IsNullOrWhiteSpace(_folderEditingId)
            ? await _mail.CreateFolderAsync(SelectedAccount.AccountId, FolderName.Trim(), CancellationToken.None)
            : await _mail.RenameFolderAsync(SelectedAccount.AccountId, _folderEditingId, FolderName.Trim(), CancellationToken.None);
        Status = result.Message;
        if (!result.Succeeded)
        {
            ApplyFailure(result, preserveMessages: true);
            return;
        }
        CloseFolderEditor();
        await LoadAccountAsync();
        RefreshMoveTargets();
    }

    private void RequestDeleteFolder()
    {
        if (CanManageSelectedFolder) IsFolderDeleteConfirmationOpen = true;
    }

    private async Task ConfirmDeleteFolderAsync()
    {
        var account = SelectedAccount;
        var folder = SelectedFolder;
        IsFolderDeleteConfirmationOpen = false;
        if (account is null || folder is null || !CanManageSelectedFolder) return;

        var result = await _mail.DeleteFolderAsync(account.AccountId, folder.Id, CancellationToken.None);
        Status = result.Message;
        if (!result.Succeeded)
        {
            ApplyFailure(result, preserveMessages: true);
            return;
        }
        await LoadAccountAsync();
        RefreshMoveTargets();
    }
}
