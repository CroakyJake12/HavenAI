using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using Haven.Core;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Immutable snapshot of one screen in a workspace tab's navigation history.
/// </summary>
public sealed record WorkspaceTabState(
    string Key,
    string AppKey,
    string Title,
    object Page,
    bool IsCloseable,
    HavenSurface Surface);

/// <summary>
/// Represents command palette item view model and keeps its related state and behavior together.
/// </summary>
public sealed record CommandPaletteItemViewModel(string Name, string Description, string Shortcut, RelayCommand RunCommand);

/// <summary>
/// Represents workspace tab view model and keeps its related state and behavior together.
/// </summary>
public sealed class WorkspaceTabViewModel : ObservableObject, IDisposable
{
    private string _title;
    private bool _isSelected;
    private bool _isHovered;
    private string _key;
    private bool _isCloseable;
    private Guid? _groupId;
    private string _groupName = string.Empty;
    private bool _isGroupCollapsed;
    private bool _isMarkedForGrouping;
    private string _appKey;
    private bool _isPinned;
    private bool _isProtected;
    private readonly Stack<WorkspaceTabState> _backHistory = new();
    private readonly Stack<WorkspaceTabState> _forwardHistory = new();
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;

    public WorkspaceTabViewModel(string key, string title, object page, bool isCloseable, HavenSurface surface, Guid? sessionId = null, string? appKey = null)
    {
        SessionId = sessionId ?? Guid.NewGuid();
        _key = key;
        _appKey = string.IsNullOrWhiteSpace(appKey) ? InferAppKey(key) : appKey;
        _title = title;
        Page = page;
        _isCloseable = isCloseable;
        Surface = surface;
    }

    public Guid SessionId { get; private set; }
    public string Key { get => _key; private set => SetProperty(ref _key, value); }
    public string AppKey { get => _appKey; private set => SetProperty(ref _appKey, value); }
    public string Title { get => _title; set => SetProperty(ref _title, value); }
    public object Page { get; private set; }
    public bool IsCloseable { get => _isCloseable; private set => SetProperty(ref _isCloseable, value); }
    public HavenSurface Surface { get; private set; }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    public bool IsHovered { get => _isHovered; set => SetProperty(ref _isHovered, value); }
    public Guid? GroupId { get => _groupId; set => SetProperty(ref _groupId, value); }
    public string GroupName { get => _groupName; set => SetProperty(ref _groupName, value); }
    public bool IsGroupCollapsed { get => _isGroupCollapsed; set => SetProperty(ref _isGroupCollapsed, value); }
    public bool IsMarkedForGrouping { get => _isMarkedForGrouping; set => SetProperty(ref _isMarkedForGrouping, value); }
    public bool IsPinned { get => _isPinned; set => SetProperty(ref _isPinned, value); }
    public bool IsProtected { get => _isProtected; set => SetProperty(ref _isProtected, value); }
    public bool CanGoBack => _backHistory.Count > 0;
    public bool CanGoForward => _forwardHistory.Count > 0;
    public CancellationToken LifetimeToken => _lifetime.Token;

    public void NavigateTo(string key, string title, object page, bool isCloseable, HavenSurface surface)
    {
        if (ReferenceEquals(Page, page) && Key.Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            Title = title;
            IsCloseable = isCloseable;
            SetSurface(surface);
            return;
        }

        _backHistory.Push(CaptureState());
        _forwardHistory.Clear();
        ApplyState(new WorkspaceTabState(key, InferAppKey(key), title, page, isCloseable, surface));
        RaiseHistoryChanged();
    }

    public bool TryGoBack()
    {
        if (_backHistory.Count == 0) return false;
        _forwardHistory.Push(CaptureState());
        ApplyState(_backHistory.Pop());
        RaiseHistoryChanged();
        return true;
    }

    public bool TryGoForward()
    {
        if (_forwardHistory.Count == 0) return false;
        _backHistory.Push(CaptureState());
        ApplyState(_forwardHistory.Pop());
        RaiseHistoryChanged();
        return true;
    }

    private WorkspaceTabState CaptureState() => new(Key, AppKey, Title, Page, IsCloseable, Surface);

    private void ApplyState(WorkspaceTabState state)
    {
        Key = state.Key;
        AppKey = state.AppKey;
        Title = state.Title;
        Page = state.Page;
        IsCloseable = state.IsCloseable;
        Surface = state.Surface;
        RaisePropertyChanged(nameof(Page));
        RaisePropertyChanged(nameof(Surface));
    }

    private void RaiseHistoryChanged()
    {
        RaisePropertyChanged(nameof(CanGoBack));
        RaisePropertyChanged(nameof(CanGoForward));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _lifetime.Cancel();
        var pages = _backHistory.Select(state => state.Page)
            .Concat(_forwardHistory.Select(state => state.Page))
            .Append(Page)
            .Distinct(ReferenceEqualityComparer.Instance);
        foreach (var page in pages)
            if (page is IDisposable disposable) disposable.Dispose();
        _backHistory.Clear();
        _forwardHistory.Clear();
        RaiseHistoryChanged();
        _lifetime.Dispose();
    }

    public void ReplacePage(object page)
    {
        if (ReferenceEquals(Page, page)) return;
        if (Page is IDisposable disposable) disposable.Dispose();
        Page = page;
        RaisePropertyChanged(nameof(Page));
    }

    public void SetSurface(HavenSurface surface)
    {
        if (Surface == surface) return;
        Surface = surface;
        RaisePropertyChanged(nameof(Surface));
    }

    internal void RestoreIdentity(Guid sessionId, string appKey)
    {
        SessionId = sessionId;
        AppKey = appKey;
        RaisePropertyChanged(nameof(SessionId));
    }

    private static string InferAppKey(string key)
    {
        var separator = key.IndexOf('-');
        return separator <= 0 ? key : key[..separator];
    }
}

/// <summary>
/// Represents recent conversation view model and keeps its related state and behavior together.
/// </summary>
public sealed class RecentConversationViewModel : ObservableObject
{
    private Conversation _definition;
    private bool _isRenaming;
    private bool _isDeleteConfirming;
    private string _draftTitle;
    private bool _isActive;

    public RecentConversationViewModel(Conversation definition, Func<RecentConversationViewModel?, Task> open,
        Func<RecentConversationViewModel?, Task> rename, Func<RecentConversationViewModel?, Task> togglePin,
        Func<RecentConversationViewModel?, Task> branch, Func<RecentConversationViewModel?, Task> archive,
        Func<RecentConversationViewModel?, Task> delete)
    {
        _definition = definition;
        _draftTitle = definition.Title;
        OpenCommand = new AsyncRelayCommand(() => open(this));
        BeginRenameCommand = new RelayCommand(() => IsRenaming = true);
        SaveRenameCommand = new AsyncRelayCommand(() => rename(this), () => !string.IsNullOrWhiteSpace(DraftTitle));
        CancelRenameCommand = new RelayCommand(() => { DraftTitle = Definition.Title; IsRenaming = false; });
        TogglePinCommand = new AsyncRelayCommand(() => togglePin(this));
        BranchCommand = new AsyncRelayCommand(() => branch(this));
        ArchiveCommand = new AsyncRelayCommand(() => archive(this));
        DeleteCommand = new RelayCommand(() => IsDeleteConfirming = true);
        ConfirmDeleteCommand = new AsyncRelayCommand(async () => { await delete(this); IsDeleteConfirming = false; });
        CancelDeleteCommand = new RelayCommand(() => IsDeleteConfirming = false);
    }

    public Conversation Definition => _definition;
    public string Title => Definition.Title;
    public string Meta => Definition.UpdatedAt.LocalDateTime.ToString("g");
    public bool IsPinned => Definition.IsPinned;
    public string PinLabel => IsPinned ? "Unpin" : "Pin";
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }
    public bool IsRenaming { get => _isRenaming; set { if (SetProperty(ref _isRenaming, value)) RaisePropertyChanged(nameof(IsNormal)); } }
    public bool IsDeleteConfirming { get => _isDeleteConfirming; set { if (SetProperty(ref _isDeleteConfirming, value)) RaisePropertyChanged(nameof(IsNormal)); } }
    public bool IsNormal => !IsRenaming && !IsDeleteConfirming;
    public string DraftTitle { get => _draftTitle; set { if (SetProperty(ref _draftTitle, value)) SaveRenameCommand.RaiseCanExecuteChanged(); } }
    public AsyncRelayCommand OpenCommand { get; }
    public RelayCommand BeginRenameCommand { get; }
    public AsyncRelayCommand SaveRenameCommand { get; }
    public RelayCommand CancelRenameCommand { get; }
    public AsyncRelayCommand TogglePinCommand { get; }
    public AsyncRelayCommand BranchCommand { get; }
    public AsyncRelayCommand ArchiveCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public AsyncRelayCommand ConfirmDeleteCommand { get; }
    public RelayCommand CancelDeleteCommand { get; }

    public void FinishRename(Conversation updated)
    {
        _definition = updated;
        DraftTitle = updated.Title;
        IsRenaming = false;
        RaisePropertyChanged(nameof(Definition));
        RaisePropertyChanged(nameof(Title));
        RaisePropertyChanged(nameof(Meta));
    }
}
