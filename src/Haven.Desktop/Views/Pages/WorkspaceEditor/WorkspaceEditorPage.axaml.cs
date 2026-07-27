using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views.Pages.WorkspaceEditor;

public sealed partial class WorkspaceEditorPage : UserControl, INotifyPropertyChanged, IDisposable
{
    private readonly ContainerDefinition _container;
    private readonly Guid? _conversationId;
    private readonly WorkspaceFileItemViewModel _file;
    private readonly IWorkspaceToolService _tools;
    private readonly IWorkspaceStateRepository _history;
    private readonly IConversationRepository _conversations;
    private readonly Func<Task> _branch;
    private readonly Action _interrupt;
    private readonly Stack<WorkspaceVersion> _undo = new();
    private readonly Stack<WorkspaceVersion> _redo = new();
    private FileSystemWatcher? _watcher;
    private string _content = string.Empty;
    private string _savedContent = string.Empty;
    private string _status = "Loading file…";
    private bool _requiresBranchAfterRollback;
    private string? _rollforwardContent;
    private WorkspaceVersionItemViewModel? _selectedVersion;
    private string _commentPrompt = string.Empty;
    private string _selectedSnippet = string.Empty;
    private bool _showDiff;
    private string _diffText = string.Empty;

    public new event PropertyChangedEventHandler? PropertyChanged;

    public WorkspaceEditorPage(
        ContainerDefinition container,
        Guid? conversationId,
        WorkspaceFileItemViewModel file,
        IWorkspaceToolService tools,
        IWorkspaceStateRepository history,
        IConversationRepository conversations,
        Func<Task> branch,
        Action interrupt)
    {
        _container = container;
        _conversationId = conversationId;
        _file = file;
        _tools = tools;
        _history = history;
        _conversations = conversations;
        _branch = branch;
        _interrupt = interrupt;

        InitializeComponent();

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => IsDirty && !RequiresBranchAfterRollback);
        UndoCommand = new AsyncRelayCommand(UndoAsync, () => _undo.Count > 0 && !RequiresBranchAfterRollback);
        RedoCommand = new AsyncRelayCommand(RedoAsync, () => _redo.Count > 0 && !RequiresBranchAfterRollback);
        RollbackCommand = new AsyncRelayCommand(RollbackAsync, () => SelectedVersion is not null);
        RollforwardCommand = new AsyncRelayCommand(RollforwardAsync, () => !string.IsNullOrEmpty(_rollforwardContent));
        BranchAfterRollbackCommand = new AsyncRelayCommand(BranchAfterRollbackAsync, () => RequiresBranchAfterRollback);
        AddCommentCommand = new AsyncRelayCommand(AddCommentAsync, () => !string.IsNullOrWhiteSpace(CommentPrompt));
        InterruptCommand = new RelayCommand(() => { _interrupt(); Status = "Asked Haven to stop after the current safe boundary."; });
        ReloadCommand = new AsyncRelayCommand(LoadAsync);
        ToggleDiffCommand = new RelayCommand(ToggleDiff);

        DataContext = this;
        _ = LoadAsync();
    }

    public ContainerDefinition Container => _container;
    public string Title => _file.Name;
    public string RelativePath => _file.RelativePath;
    public string ProjectName => _container.Name;
    public new string Content { get => _content; set { if (!SetProperty(ref _content, value)) return; RaiseDirtyProperties(); } }
    public bool IsDirty => !string.Equals(Content, _savedContent, StringComparison.Ordinal);
    public string DirtyLabel => IsDirty ? "Unsaved changes" : "Saved";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool RequiresBranchAfterRollback { get => _requiresBranchAfterRollback; private set { if (!SetProperty(ref _requiresBranchAfterRollback, value)) return; RaisePropertyChanged(nameof(CanEdit)); BranchAfterRollbackCommand.RaiseCanExecuteChanged(); SaveCommand.RaiseCanExecuteChanged(); } }
    public bool CanEdit => !RequiresBranchAfterRollback;
    public bool CanRollforward => !string.IsNullOrEmpty(_rollforwardContent);
    public WorkspaceVersionItemViewModel? SelectedVersion { get => _selectedVersion; set { if (SetProperty(ref _selectedVersion, value)) RollbackCommand.RaiseCanExecuteChanged(); } }
    public string CommentPrompt { get => _commentPrompt; set { if (SetProperty(ref _commentPrompt, value)) AddCommentCommand.RaiseCanExecuteChanged(); } }
    public string SelectedSnippet { get => _selectedSnippet; private set => SetProperty(ref _selectedSnippet, value); }
    public ObservableCollection<WorkspaceVersionItemViewModel> Versions { get; } = [];
    public ObservableCollection<EditorCommentViewModel> Comments { get; } = [];
    public ObservableCollection<string> Changelog { get; } = [];
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand UndoCommand { get; }
    public AsyncRelayCommand RedoCommand { get; }
    public AsyncRelayCommand RollbackCommand { get; }
    public AsyncRelayCommand RollforwardCommand { get; }
    public AsyncRelayCommand BranchAfterRollbackCommand { get; }
    public AsyncRelayCommand AddCommentCommand { get; }
    public RelayCommand InterruptCommand { get; }
    public AsyncRelayCommand ReloadCommand { get; }
    public RelayCommand ToggleDiffCommand { get; }
    public bool ShowDiff { get => _showDiff; set => SetProperty(ref _showDiff, value); }
    public string DiffText { get => _diffText; set => SetProperty(ref _diffText, value); }

    public void SetSelection(string text) => SelectedSnippet = text;

    private async Task LoadAsync()
    {
        try
        {
            Content = await _tools.ReadTextAsync(_file.Root, _file.RelativePath, CancellationToken.None);
            _savedContent = Content;
            _undo.Clear();
            _redo.Clear();
            await RefreshVersionsAsync();
            StartWatcher();
            RaiseDirtyProperties();
            Status = "File opened in Haven. External and AI edits are monitored live.";
        }
        catch (Exception ex) { Status = $"Could not open file: {ex.Message}"; }
    }

    private async Task SaveAsync()
    {
        if (RequiresBranchAfterRollback) { Status = "Branch this chat before continuing edits after a rollback."; return; }
        var before = _savedContent;
        var after = Content;
        var (added, removed) = CountLineChanges(before, after);
        await _tools.WriteTextAtomicAsync(_file.Root, _file.RelativePath, after, CancellationToken.None);
        var version = new WorkspaceVersion(Guid.NewGuid(), _conversationId, _container.Id, _file.Root, _file.RelativePath,
            WorkspaceVersionKind.Edit, before, after, $"Edited {_file.RelativePath}", added, removed, DateTimeOffset.UtcNow);
        await _history.AddVersionAsync(version, CancellationToken.None);
        _undo.Push(version);
        _redo.Clear();
        _savedContent = after;
        Changelog.Insert(0, $"{DateTimeOffset.Now:t} · {_file.RelativePath} · +{added}/-{removed} lines");
        await RefreshVersionsAsync();
        RaiseDirtyProperties();
        Status = $"Saved atomically · +{added}/-{removed} lines. A Smart Undo version was recorded.";
    }

    private async Task UndoAsync()
    {
        if (_undo.Count == 0) return;
        var version = _undo.Pop();
        await WriteHistoryStateAsync(version.BeforeContent, WorkspaceVersionKind.Undo, "Undid " + version.Summary);
        _redo.Push(version);
        RaiseHistoryCommands();
    }

    private async Task RedoAsync()
    {
        if (_redo.Count == 0) return;
        var version = _redo.Pop();
        await WriteHistoryStateAsync(version.AfterContent, WorkspaceVersionKind.Redo, "Redid " + version.Summary);
        _undo.Push(version);
        RaiseHistoryCommands();
    }

    private async Task RollbackAsync()
    {
        if (SelectedVersion is null) return;
        _rollforwardContent = _savedContent;
        await WriteHistoryStateAsync(SelectedVersion.Definition.BeforeContent, WorkspaceVersionKind.Rollback,
            $"Rolled back to before {SelectedVersion.Definition.CreatedAt.LocalDateTime:g}");
        RequiresBranchAfterRollback = true;
        RaisePropertyChanged(nameof(CanRollforward));
        RollforwardCommand.RaiseCanExecuteChanged();
        Status = "Rollback complete. Roll forward to undo it, or branch the chat before making further edits.";
    }

    private async Task RollforwardAsync()
    {
        if (_rollforwardContent is null) return;
        var target = _rollforwardContent;
        _rollforwardContent = null;
        await WriteHistoryStateAsync(target, WorkspaceVersionKind.Rollforward, "Rolled forward after rollback");
        RequiresBranchAfterRollback = false;
        RaisePropertyChanged(nameof(CanRollforward));
        RollforwardCommand.RaiseCanExecuteChanged();
    }

    private async Task BranchAfterRollbackAsync()
    {
        await _branch();
        RequiresBranchAfterRollback = false;
        Status = "Branched the chat. New edits can continue without overwriting the original history.";
    }

    private async Task WriteHistoryStateAsync(string target, WorkspaceVersionKind kind, string summary)
    {
        var before = _savedContent;
        var (added, removed) = CountLineChanges(before, target);
        await _tools.WriteTextAtomicAsync(_file.Root, _file.RelativePath, target, CancellationToken.None);
        await _history.AddVersionAsync(new WorkspaceVersion(Guid.NewGuid(), _conversationId, _container.Id, _file.Root, _file.RelativePath,
            kind, before, target, summary, added, removed, DateTimeOffset.UtcNow), CancellationToken.None);
        _savedContent = target;
        Content = target;
        Changelog.Insert(0, $"{DateTimeOffset.Now:t} · {summary} · +{added}/-{removed}");
        await RefreshVersionsAsync();
        RaiseDirtyProperties();
    }

    private async Task AddCommentAsync()
    {
        var comment = new EditorCommentViewModel(Guid.NewGuid(), string.IsNullOrWhiteSpace(SelectedSnippet) ? "Whole file" : Truncate(SelectedSnippet, 180), CommentPrompt.Trim(), DateTimeOffset.Now);
        Comments.Add(comment);
        if (_conversationId is not null && await _conversations.GetAsync(_conversationId.Value, CancellationToken.None) is not null)
            await _conversations.AddContextEntryAsync(new ConversationContextEntry(Guid.NewGuid(), _conversationId.Value, ContextEntryKind.Registered,
                $"Prompt comment on {_file.RelativePath}", $"Selection: {comment.Selection}\nComment: {comment.Prompt}", string.Empty, DateTimeOffset.UtcNow), CancellationToken.None);
        CommentPrompt = string.Empty;
        Status = "Prompt comment attached to the selected text and registered in chat context.";
    }

    private async Task RefreshVersionsAsync()
    {
        Versions.Clear();
        foreach (var version in await _history.GetVersionsAsync(_container.Id, _file.RelativePath, 100, CancellationToken.None)) Versions.Add(new(version));
        SelectedVersion = Versions.FirstOrDefault();
    }

    private void StartWatcher()
    {
        _watcher?.Dispose();
        _watcher = new FileSystemWatcher(Path.GetDirectoryName(_file.FullPath)!, Path.GetFileName(_file.FullPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnFileChanged;
        _watcher.Renamed += OnFileChanged;
    }

    private async void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        await Task.Delay(120);
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                var disk = await _tools.ReadTextAsync(_file.Root, _file.RelativePath, CancellationToken.None);
                if (string.Equals(disk, _savedContent, StringComparison.Ordinal)) return;
                if (IsDirty) { Status = "Haven or another editor changed this file while you have unsaved text. Save elsewhere or reload after reviewing."; return; }
                _savedContent = disk;
                Content = disk;
                Changelog.Insert(0, $"{DateTimeOffset.Now:t} · Live external/AI edit observed");
                Status = "Live edit received from Haven or an external editor.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        });
    }

    private void RaiseDirtyProperties()
    {
        RaisePropertyChanged(nameof(IsDirty));
        RaisePropertyChanged(nameof(DirtyLabel));
        SaveCommand.RaiseCanExecuteChanged();
    }

    private void RaiseHistoryCommands()
    {
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
    }

    private static (int Added, int Removed) CountLineChanges(string before, string after)
    {
        var oldLines = before.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var newLines = after.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length && oldLines[prefix] == newLines[prefix]) prefix++;
        var suffix = 0;
        while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix &&
               oldLines[oldLines.Length - 1 - suffix] == newLines[newLines.Length - 1 - suffix]) suffix++;
        return (Math.Max(0, newLines.Length - prefix - suffix), Math.Max(0, oldLines.Length - prefix - suffix));
    }

    private static string Truncate(string value, int limit) => value.Length <= limit ? value : value[..limit] + "…";

    private void ToggleDiff()
    {
        ShowDiff = !ShowDiff;
        if (!ShowDiff) { DiffText = string.Empty; return; }
        var before = _savedContent;
        var after = Content;
        if (string.IsNullOrEmpty(before) && string.IsNullOrEmpty(after)) { DiffText = "(no changes to compare)"; return; }
        var oldLines = (before ?? "").Replace("\r\n", "\n").Split('\n');
        var newLines = (after ?? "").Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        var maxLen = Math.Max(oldLines.Length, newLines.Length);
        for (var i = 0; i < maxLen; i++)
        {
            var oldLine = i < oldLines.Length ? oldLines[i] : null;
            var newLine = i < newLines.Length ? newLines[i] : null;
            if (oldLine == newLine)
                sb.AppendLine($"  {(i + 1),4}  {oldLine}");
            else
            {
                if (oldLine is not null) sb.AppendLine($"- {(i + 1),4}  {oldLine}");
                if (newLine is not null) sb.AppendLine($"+ {(i + 1),4}  {newLine}");
            }
        }
        DiffText = sb.ToString();
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose() => _watcher?.Dispose();
}
