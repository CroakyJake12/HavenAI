using System.Collections.Specialized;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.Projects;

namespace Haven.Desktop.Views.Shell.NativePresentation;

/// <summary>
/// Compatibility host for the shell's historical Projects route.
/// All visible Projects UI is owned by Haven.UI through one HavenSceneControl.
/// </summary>
internal sealed class NativeProjectsPage : ContentControl, IDisposable
{
    private readonly object _source;
    private readonly WorkspaceHomePageViewModel? _workspace;
    private readonly Func<IEnumerable<object>> _fallbackProjects;
    private readonly Func<Task> _openCreator;
    private readonly Func<object, Task> _openProjectFallback;
    private readonly Func<object, Task> _archiveProjectFallback;
    private readonly NativeProjectUiStateStore _stateStore;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ProjectsHavenScene _scene;
    private readonly HavenSceneControl _sceneHost;
    private readonly Dictionary<Guid, object> _sources = [];

    private INotifyPropertyChanged? _notifySource;
    private INotifyCollectionChanged? _notifyCollection;
    private bool _disposed;
    private bool _refreshing;
    private bool _refreshPending;

    public NativeProjectsPage(
        object legacySurface,
        Func<IEnumerable<object>> fallbackProjects,
        Func<Task> openCreator,
        Func<object, Task> openProjectFallback,
        Func<object, Task> archiveProjectFallback,
        NativeProjectUiStateStore? stateStore = null)
    {
        ArgumentNullException.ThrowIfNull(legacySurface);
        _source = NativePresentationReflection.Get(legacySurface, "DataContext") ?? legacySurface;
        _workspace = _source as WorkspaceHomePageViewModel;
        _fallbackProjects = fallbackProjects ?? throw new ArgumentNullException(nameof(fallbackProjects));
        _openCreator = openCreator ?? throw new ArgumentNullException(nameof(openCreator));
        _openProjectFallback = openProjectFallback ?? throw new ArgumentNullException(nameof(openProjectFallback));
        _archiveProjectFallback = archiveProjectFallback ?? throw new ArgumentNullException(nameof(archiveProjectFallback));
        _stateStore = stateStore ?? new NativeProjectUiStateStore();

        _scene = new ProjectsHavenScene();
        _sceneHost = new HavenSceneControl { Root = _scene.Root };
        Content = _sceneHost;
        Background = Brushes.Transparent;

        _scene.RefreshRequested += OnRefreshRequested;
        _scene.CreateRequested += OnCreateRequested;
        _scene.ConnectRequested += OnConnectRequested;
        _scene.OpenRequested += OnOpenRequested;
        _scene.PinRequested += OnPinRequested;
        _scene.ReadStateRequested += OnReadStateRequested;
        _scene.ArchiveRequested += OnArchiveRequested;

        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    internal ProjectsHavenScene Scene => _scene;
    internal HavenSceneControl SceneHost => _sceneHost;

    public event EventHandler<object>? ProjectOpened;
    public event EventHandler? ProjectCreatorOpened;

    private async void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AttachNotifications();
        await RefreshProjectsAsync(refreshSource: false);
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        DetachNotifications();
    }

    private void AttachNotifications()
    {
        DetachNotifications();

        _notifySource = _workspace ?? NativePresentationReflection.NotifySource(_source);
        if (_notifySource is not null)
        {
            _notifySource.PropertyChanged += OnSourcePropertyChanged;
        }

        _notifyCollection = _workspace?.Items;
        if (_notifyCollection is null)
        {
            _notifyCollection = FindFallbackProjectCollection() as INotifyCollectionChanged;
        }

        if (_notifyCollection is not null)
        {
            _notifyCollection.CollectionChanged += OnProjectCollectionChanged;
        }
    }

    private void DetachNotifications()
    {
        if (_notifySource is not null)
        {
            _notifySource.PropertyChanged -= OnSourcePropertyChanged;
            _notifySource = null;
        }

        if (_notifyCollection is not null)
        {
            _notifyCollection.CollectionChanged -= OnProjectCollectionChanged;
            _notifyCollection = null;
        }
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e) => QueueRefresh();

    private void OnProjectCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueRefresh();

    private void QueueRefresh()
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () => _ = RefreshProjectsAsync(refreshSource: false),
            DispatcherPriority.Background);
    }

    private void OnRefreshRequested(object? sender, EventArgs e) => _ = RefreshProjectsAsync(refreshSource: true);

    private void OnCreateRequested(object? sender, EventArgs e) => _ = OpenCreatorAsync();

    private void OnConnectRequested(object? sender, EventArgs e) => _ = ConnectExistingAsync();

    private void OnOpenRequested(object? sender, ProjectActionEventArgs e) => _ = OpenProjectAsync(e.ProjectId);

    private void OnPinRequested(object? sender, ProjectToggleActionEventArgs e) =>
        _ = SetPinnedAsync(e.ProjectId, e.Value);

    private void OnReadStateRequested(object? sender, ProjectToggleActionEventArgs e) =>
        _ = SetReadStateAsync(e.ProjectId, e.Value);

    private void OnArchiveRequested(object? sender, ProjectActionEventArgs e) => _ = ArchiveProjectAsync(e.ProjectId);

    private async Task OpenCreatorAsync()
    {
        if (_disposed)
        {
            return;
        }

        _scene.SetStatus("Opening the project creator…");

        try
        {
            if (_workspace is not null)
            {
                await _workspace.CreateBlankCommand.ExecuteAsync();
            }
            else
            {
                var handled = await NativePresentationReflection.ExecuteCommandAsync(
                    _source,
                    null,
                    "NewProjectCommand",
                    "CreateProjectCommand",
                    "OpenProjectCreatorCommand",
                    "SwitchToCreateCommand");

                if (!handled)
                {
                    var invocation = await NativePresentationReflection.InvokeAsync(
                        _source,
                        ["OpenProjectCreatorAsync", "OpenProjectCreator", "CreateProjectAsync", "SwitchToCreate"],
                        Array.Empty<object?>());
                    handled = invocation.Invoked;
                }

                if (!handled)
                {
                    await _openCreator();
                }
            }

            _scene.SetStatus(string.Empty);
            ProjectCreatorOpened?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _scene.SetStatus($"The project creator could not be opened: {ex.Message}", isError: true);
        }
    }

    private async Task ConnectExistingAsync()
    {
        if (_disposed)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            _scene.SetStatus("Folder selection is unavailable on this device.", isError: true);
            return;
        }

        try
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Connect an existing project folder",
                    AllowMultiple = false
                });

            var folder = folders.FirstOrDefault();
            if (folder is null)
            {
                return;
            }

            using (folder)
            {
                var path = folder.TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(path))
                {
                    _scene.SetStatus("The selected folder does not expose a local path and cannot be connected.", isError: true);
                    return;
                }

                _scene.SetStatus("Connecting the selected project folder…");

                if (_workspace is not null)
                {
                    await _workspace.AddPathAsync(path);
                }
                else
                {
                    var handled = await NativePresentationReflection.ExecuteCommandAsync(
                        _source,
                        path,
                        "ConnectExistingFolderCommand",
                        "ConnectFolderCommand",
                        "AddExistingProjectCommand",
                        "ImportProjectCommand");

                    if (!handled)
                    {
                        var invocation = await NativePresentationReflection.InvokeAsync(
                            _source,
                            ["AddPathAsync", "ConnectExistingAsync", "ConnectFolderAsync", "AddExistingProjectAsync", "ImportProjectAsync"],
                            path);
                        handled = invocation.Invoked;
                    }

                    if (!handled)
                    {
                        await _openCreator();
                        _scene.SetStatus("The project creator was opened. Choose the existing-folder option to finish connecting it.");
                        ProjectCreatorOpened?.Invoke(this, EventArgs.Empty);
                        return;
                    }
                }
            }

            AttachNotifications();
            await RefreshProjectsAsync(refreshSource: false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _scene.SetStatus($"The folder could not be connected: {ex.Message}", isError: true);
        }
    }

    private async Task OpenProjectAsync(Guid projectId)
    {
        if (!_sources.TryGetValue(projectId, out var source))
        {
            return;
        }

        try
        {
            _scene.SetStatus("Opening project…");

            if (_workspace is not null && source is WorkspaceHomeCardViewModel item)
            {
                await _workspace.OpenCommand.ExecuteAsync(item);
            }
            else
            {
                var handled = await NativePresentationReflection.ExecuteCommandAsync(
                    source,
                    null,
                    "OpenCommand",
                    "OpenProjectCommand",
                    "SelectCommand");

                if (!handled)
                {
                    handled = await NativePresentationReflection.ExecuteCommandAsync(
                        _source,
                        source,
                        "OpenProjectCommand",
                        "SelectProjectCommand",
                        "OpenCommand",
                        "SelectCommand");
                }

                if (!handled)
                {
                    await _openProjectFallback(source);
                }
            }

            await _stateStore.MarkReadAsync(projectId, DateTimeOffset.UtcNow, CancellationToken.None);
            ProjectOpened?.Invoke(this, source);
            await RefreshProjectsAsync(refreshSource: false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _scene.SetStatus($"The project could not be opened: {ex.Message}", isError: true);
        }
    }

    private async Task ArchiveProjectAsync(Guid projectId)
    {
        if (!_sources.TryGetValue(projectId, out var source))
        {
            return;
        }

        try
        {
            _scene.SetStatus("Archiving project…");

            if (_workspace is not null && source is WorkspaceHomeCardViewModel item)
            {
                await _workspace.ArchiveCommand.ExecuteAsync(item);
            }
            else
            {
                var handled = await NativePresentationReflection.ExecuteCommandAsync(
                    source,
                    null,
                    "ArchiveCommand",
                    "ArchiveProjectCommand");

                if (!handled)
                {
                    handled = await NativePresentationReflection.ExecuteCommandAsync(
                        _source,
                        source,
                        "ArchiveProjectCommand",
                        "ArchiveCommand");
                }

                if (!handled)
                {
                    await _archiveProjectFallback(source);
                }
            }

            await RefreshProjectsAsync(refreshSource: false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _scene.SetStatus($"The project could not be archived: {ex.Message}", isError: true);
        }
    }

    private async Task SetPinnedAsync(Guid projectId, bool isPinned)
    {
        try
        {
            await _stateStore.SetPinnedAsync(projectId, isPinned, _lifetime.Token);
            await RefreshProjectsAsync(refreshSource: false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _scene.SetStatus($"The pinned state could not be saved: {ex.Message}", isError: true);
        }
    }

    private async Task SetReadStateAsync(Guid projectId, bool markUnread)
    {
        try
        {
            if (markUnread)
            {
                await _stateStore.MarkUnreadAsync(projectId, _lifetime.Token);
            }
            else
            {
                await _stateStore.MarkReadAsync(projectId, DateTimeOffset.UtcNow, _lifetime.Token);
            }

            await RefreshProjectsAsync(refreshSource: false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _scene.SetStatus($"The read state could not be saved: {ex.Message}", isError: true);
        }
    }

    private async Task RefreshProjectsAsync(bool refreshSource)
    {
        if (_disposed)
        {
            return;
        }

        if (_refreshing)
        {
            _refreshPending = true;
            return;
        }

        _refreshing = true;

        try
        {
            do
            {
                _refreshPending = false;

                if (refreshSource)
                {
                    _scene.SetStatus("Refreshing projects…");
                    await RefreshSourceAsync();
                    refreshSource = false;
                    AttachNotifications();
                }

                var items = await ReadItemsAsync(_lifetime.Token);
                _scene.SetItems(items);
                _scene.SetStatus(_workspace?.Status ?? (items.Count == 0
                    ? "No projects yet."
                    : $"{items.Count} projects available locally."));
            }
            while (_refreshPending && !_disposed);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _scene.SetStatus($"Projects could not be refreshed: {ex.Message}", isError: true);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task RefreshSourceAsync()
    {
        if (_workspace is not null)
        {
            await _workspace.RefreshCommand.ExecuteAsync();
            return;
        }

        var handled = await NativePresentationReflection.ExecuteCommandAsync(
            _source,
            null,
            "RefreshProjectsCommand",
            "RefreshCommand",
            "ReloadCommand",
            "LoadProjectsCommand");

        if (handled)
        {
            return;
        }

        await NativePresentationReflection.InvokeAsync(
            _source,
            ["RefreshAsync", "ReloadAsync", "LoadProjectsAsync", "LoadAsync"],
            Array.Empty<object?>());
    }

    private async Task<IReadOnlyList<ProjectsHavenItem>> ReadItemsAsync(CancellationToken cancellationToken)
    {
        var uiStates = await _stateStore.GetAllAsync(cancellationToken);
        var items = new List<ProjectsHavenItem>();
        _sources.Clear();

        if (_workspace is not null)
        {
            foreach (var card in _workspace.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var definition = card.Definition;
                uiStates.TryGetValue(definition.Id, out var uiState);
                uiState ??= ProjectUiState.Empty;
                _sources[definition.Id] = card;
                items.Add(new ProjectsHavenItem(
                    definition.Id,
                    card.Name,
                    card.Path,
                    card.LastTask,
                    card.Branch,
                    card.WorkState,
                    card.BuildState,
                    card.RecommendedAction,
                    definition.UpdatedAt,
                    uiState.IsPinned,
                    uiState.IsUnread(definition.UpdatedAt)));
            }

            return items;
        }

        var fallback = FindFallbackProjectCollection().ToArray();
        if (fallback.Length == 0)
        {
            fallback = _fallbackProjects().Where(item => item is not null).ToArray();
        }

        foreach (var project in fallback)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (NativePresentationReflection.Boolean(project, false, "IsArchived", "Archived"))
            {
                continue;
            }

            var name = NativePresentationReflection.Text(
                project,
                "Untitled project",
                "Name",
                "Title",
                "ProjectName");
            var path = NativePresentationReflection.Text(
                project,
                "No folder connected",
                "RootPath",
                "Path",
                "FolderPath",
                "WorkspacePath");
            var id = NativePresentationReflection.Identifier(
                         project,
                         "Id",
                         "ProjectId",
                         "WorkspaceId",
                         "ContainerId")
                     ?? CreateStableIdentifier(project, name, path);
            var updatedAt = NativePresentationReflection.Timestamp(
                                project,
                                "UpdatedAt",
                                "LastActivityAt",
                                "LastUpdatedAt",
                                "ModifiedAt",
                                "CreatedAt")
                            ?? DateTimeOffset.MinValue;

            uiStates.TryGetValue(id, out var uiState);
            uiState ??= ProjectUiState.Empty;

            var sourcePinned = NativePresentationReflection.Boolean(project, false, "IsPinned", "Pinned");
            var sourceUnread = NativePresentationReflection.Boolean(project, false, "IsUnread", "Unread", "HasUnreadActivity");
            _sources[id] = project;

            items.Add(new ProjectsHavenItem(
                id,
                name,
                path,
                NativePresentationReflection.Text(
                    project,
                    "No meaningful task recorded yet",
                    "LastMeaningfulTask",
                    "RecentTask",
                    "LastTask",
                    "Summary",
                    "Description"),
                NativePresentationReflection.Text(
                    project,
                    "No Git branch",
                    "Branch",
                    "BranchName",
                    "GitBranch"),
                NativePresentationReflection.Text(
                    project,
                    "Not inspected",
                    "WorkState",
                    "WorkingTreeState",
                    "State",
                    "Status"),
                NativePresentationReflection.Text(
                    project,
                    "Build not run",
                    "BuildState",
                    "LastBuildResult",
                    "BuildResult"),
                NativePresentationReflection.Text(
                    project,
                    "Open the project to inspect its next useful action.",
                    "RecommendedAction",
                    "NextAction",
                    "AdaptiveHelp"),
                updatedAt,
                sourcePinned || uiState.IsPinned,
                sourceUnread || uiState.IsUnread(updatedAt)));
        }

        return items;
    }

    private IEnumerable<object> FindFallbackProjectCollection() =>
        NativePresentationReflection.ReadCollection(
            _source,
            "Projects",
            "ProjectItems",
            "ProjectCards",
            "Workspaces",
            "Containers",
            "Items");

    private static Guid CreateStableIdentifier(object source, string name, string rootPath)
    {
        var input = $"{source.GetType().FullName}|{rootPath}|{name}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(hash.AsSpan(0, 16));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        AttachedToVisualTree -= OnAttached;
        DetachedFromVisualTree -= OnDetached;

        _scene.RefreshRequested -= OnRefreshRequested;
        _scene.CreateRequested -= OnCreateRequested;
        _scene.ConnectRequested -= OnConnectRequested;
        _scene.OpenRequested -= OnOpenRequested;
        _scene.PinRequested -= OnPinRequested;
        _scene.ReadStateRequested -= OnReadStateRequested;
        _scene.ArchiveRequested -= OnArchiveRequested;

        _lifetime.Cancel();
        DetachNotifications();
        _sceneHost.Root = null;
        _scene.Dispose();
        _lifetime.Dispose();
    }
}
