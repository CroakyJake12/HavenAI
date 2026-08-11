/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/WorkspaceSurfacesViewModels.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns workspace, project, editor, archive, and activity presentation models. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents workspace home page view model and keeps its related state and behavior together.
/// </summary>
public sealed class WorkspaceHomePageViewModel : ObservableObject
{
    private readonly HavenMode _mode;
    private readonly IContainerRepository _containers;
    private readonly IConversationRepository _conversations;
    private readonly IAutomationRepository _automations;
    private readonly IWorkspaceStateRepository _workspaceState;
    private readonly IProjectIntelligenceService _intelligence;
    private readonly Func<ContainerDefinition, Task> _open;
    private readonly Func<Task>? _create;
    private string _status = "Loading…";

    public WorkspaceHomePageViewModel(HavenMode mode, IContainerRepository containers, IConversationRepository conversations,
        IAutomationRepository automations, IWorkspaceStateRepository workspaceState, IProjectIntelligenceService intelligence,
        Func<ContainerDefinition, Task> open, Func<Task>? create = null)
    {
        _mode = mode;
        _containers = containers;
        _conversations = conversations;
        _automations = automations;
        _workspaceState = workspaceState;
        _intelligence = intelligence;
        _open = open;
        _create = create;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        CreateBlankCommand = new AsyncRelayCommand(CreateBlankAsync);
        OpenCommand = new AsyncRelayCommand<WorkspaceHomeCardViewModel>(item => item is null ? Task.CompletedTask : _open(item.Definition));
        ArchiveCommand = new AsyncRelayCommand<WorkspaceHomeCardViewModel>(ArchiveAsync);
        _ = RefreshAsync();
    }

    public string Title => _mode == HavenMode.Studio ? "Studio Home" : "Tasks";
    public string Subtitle => _mode == HavenMode.Studio
        ? "Projects, live state, active Scheduled Actions, and the next useful step."
        : "Task Groups, reusable tasks, Scheduled Actions, and recent work.";
    public string CollectionHeading => _mode == HavenMode.Studio ? "Projects" : "Task Groups";
    public string CreateLabel => _mode == HavenMode.Studio ? "New project" : "New Task Group";
    public bool IsStudio => _mode == HavenMode.Studio;
    public bool IsTasks => _mode == HavenMode.Tasks;
    public bool HasAutomations => ActiveAutomations.Count > 0;
    public bool HasItems => Items.Count > 0;
    public bool HasReusableTasks => ReusableTasks.Count > 0;
    public ObservableCollection<WorkspaceHomeCardViewModel> Items { get; } = [];
    public ObservableCollection<AutomationSummaryViewModel> ActiveAutomations { get; } = [];
    public ObservableCollection<ReusableTaskSummaryViewModel> ReusableTasks { get; } = [];
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand CreateBlankCommand { get; }
    public AsyncRelayCommand<WorkspaceHomeCardViewModel> OpenCommand { get; }
    public AsyncRelayCommand<WorkspaceHomeCardViewModel> ArchiveCommand { get; }

    public async Task AddPathAsync(string path)
    {
        try
        {
            var canonical = Path.GetFullPath(path);
            var existing = await _containers.GetByModeAsync(_mode, CancellationToken.None);
            var candidates = _mode == HavenMode.Studio ? await _intelligence.ScanAsync(canonical, CancellationToken.None) : [];
            if (candidates.Count == 0)
                candidates = [new ProjectDiscoveryItem(Path.GetFileName(canonical.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : "Workspace", canonical, canonical, "Folder", "Workspace")];
            var now = DateTimeOffset.UtcNow;
            foreach (var candidate in candidates.Where(candidate => existing.All(item => !string.Equals(item.RootPath, candidate.RootPath, StringComparison.OrdinalIgnoreCase))))
            {
                await _containers.UpsertAsync(new ContainerDefinition(Guid.NewGuid(), _mode, candidate.Name, candidate.RootPath,
                    string.Empty, string.Empty, now, now), CancellationToken.None);
            }
            await RefreshAsync();
            Status = candidates.Count == 1 ? $"Added {candidates[0].Name}." : $"Discovered and added {candidates.Count} projects.";
        }
        catch (Exception ex) { Status = $"Could not add that folder: {ex.Message}"; }
    }

    private async Task RefreshAsync()
    {
        Items.Clear();
        var recent = await _conversations.GetRecentAsync(_mode, 300, CancellationToken.None);
        foreach (var item in await _containers.GetByModeAsync(_mode, CancellationToken.None))
        {
            var conversation = recent.FirstOrDefault(chat => chat.ContainerId == item.Id);
            ProjectStateSnapshot? state = null;
            if (!string.IsNullOrWhiteSpace(item.RootPath) && Directory.Exists(item.RootPath))
            {
                try { state = await _intelligence.GetStateAsync(item.RootPath, CancellationToken.None); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
            }
            Items.Add(new WorkspaceHomeCardViewModel(item, conversation, state));
        }

        ActiveAutomations.Clear();
        foreach (var automation in (await _automations.GetAllAsync(CancellationToken.None)).Where(item => item.IsEnabled && item.Mode == _mode).Take(8))
            ActiveAutomations.Add(new AutomationSummaryViewModel(automation.Name, automation.Instruction, automation.NextRunAt?.LocalDateTime.ToString("g") ?? "Waiting for trigger"));

        ReusableTasks.Clear();
        if (_mode == HavenMode.Tasks)
            foreach (var task in (await _workspaceState.GetReusableTasksAsync(null, CancellationToken.None)).Take(12))
                ReusableTasks.Add(new ReusableTaskSummaryViewModel(task.Name, task.Description));
        RaisePropertyChanged(nameof(HasAutomations));
        RaisePropertyChanged(nameof(HasItems));
        RaisePropertyChanged(nameof(HasReusableTasks));
        Status = Items.Count == 0 ? $"No {CollectionHeading.ToLowerInvariant()} yet." : $"{Items.Count} {CollectionHeading.ToLowerInvariant()} available locally.";
    }

    private async Task CreateBlankAsync()
    {
        if (_mode == HavenMode.Studio && _create is not null)
        {
            await _create();
            return;
        }
        var now = DateTimeOffset.UtcNow;
        var name = _mode == HavenMode.Studio ? "Untitled Project" : "Untitled Task Group";
        var item = new ContainerDefinition(Guid.NewGuid(), _mode, name, null, string.Empty, string.Empty, now, now);
        await _containers.UpsertAsync(item, CancellationToken.None);
        await RefreshAsync();
        await _open(item);
    }

    private async Task ArchiveAsync(WorkspaceHomeCardViewModel? item)
    {
        if (item is null) return;
        await _containers.UpsertAsync(item.Definition with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        await RefreshAsync();
        Status = $"Archived {item.Name}.";
    }
}

/// <summary>
/// Represents workspace home card view model and keeps its related state and behavior together.
/// </summary>
public sealed class WorkspaceHomeCardViewModel(ContainerDefinition definition, Conversation? recent, ProjectStateSnapshot? state)
{
    public ContainerDefinition Definition => definition;
    public string Name => definition.Name;
    public string Path => definition.RootPath ?? "No folder selected";
    public string LastTask => recent?.Title ?? "No meaningful task recorded yet";
    public string Branch => state?.Branch ?? "No Git state";
    public string WorkState => state is null ? "Folder not connected" : state.HasUncommittedWork ? "Uncommitted work" : "Working tree clean";
    public string BuildState => state?.LastBuildResult ?? "Build not run";
    public string RecommendedAction => state?.RecommendedAction ?? "Connect a project folder in settings";
    public string Accent => definition.Mode == HavenMode.Studio ? "STUDIO" : "TASKS";
}

/// <summary>
/// Represents automation summary view model and keeps its related state and behavior together.
/// </summary>
public sealed record AutomationSummaryViewModel(string Name, string Instruction, string NextRun);
/// <summary>
/// Represents a reusable task summary.
/// </summary>
public sealed record ReusableTaskSummaryViewModel(string Name, string Description);

/// <summary>
/// Represents project feature card view model and keeps its related state and behavior together.
/// </summary>
public sealed record ProjectFeatureCardViewModel(string Name, string Description, string Prompt);

/// <summary>
/// Represents decision item view model and keeps its related state and behavior together.
/// </summary>
public sealed class DecisionItemViewModel(DecisionRecord definition)
{
    public DecisionRecord Definition => definition;
    public string Title => definition.Title;
    public string Decision => definition.Decision;
    public string Reasoning => definition.Reasoning;
    public string Evidence => definition.Evidence;
    public string Consequences => definition.Consequences;
    public string Updated => definition.UpdatedAt.LocalDateTime.ToString("g");
}

/// <summary>
/// Represents workspace file item view model and keeps its related state and behavior together.
/// </summary>
public sealed class WorkspaceFileItemViewModel(string root, string relativePath)
{
    public string Root => root;
    public string RelativePath => relativePath.Replace(Path.DirectorySeparatorChar, '/');
    public string Name => Path.GetFileName(relativePath);
    public string Folder => Path.GetDirectoryName(relativePath)?.Replace(Path.DirectorySeparatorChar, '/') ?? string.Empty;
    public string FullPath => Path.GetFullPath(Path.Combine(root, relativePath));
    public override string ToString() => RelativePath;
}

/// <summary>
/// Represents workspace version item view model and keeps its related state and behavior together.
/// </summary>
public sealed class WorkspaceVersionItemViewModel(WorkspaceVersion definition)
{
    public WorkspaceVersion Definition => definition;
    public string Label => $"{definition.CreatedAt.LocalDateTime:g} · {definition.Kind}";
    public string Summary => definition.Summary;
    public string Changes => $"+{definition.LinesAdded}/-{definition.LinesRemoved}";
    public override string ToString() => Label;
}

/// <summary>
/// Represents editor comment view model and keeps its related state and behavior together.
/// </summary>
public sealed record EditorCommentViewModel(Guid Id, string Selection, string Prompt, DateTimeOffset CreatedAt)
{
    public string Time => CreatedAt.ToString("t");
}

/// <summary>
/// Represents archive page view model and keeps its related state and behavior together.
/// </summary>
public sealed class ArchivePageViewModel : ObservableObject
{
    private readonly HavenMode _mode;
    private readonly IConversationRepository _conversations;
    private readonly IContainerRepository _containers;
    private string _status = "Loading archive…";

    public ArchivePageViewModel(HavenMode mode, IConversationRepository conversations, IContainerRepository containers)
    {
        _mode = mode;
        _conversations = conversations;
        _containers = containers;
        RestoreCommand = new AsyncRelayCommand<ArchiveItemViewModel>(RestoreAsync);
        DeleteForeverCommand = new AsyncRelayCommand<ArchiveItemViewModel>(DeleteForeverAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        _ = RefreshAsync();
    }

    public ObservableCollection<ArchiveItemViewModel> Items { get; } = [];
    public string Title => $"{_mode} Archive";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public AsyncRelayCommand<ArchiveItemViewModel> RestoreCommand { get; }
    public AsyncRelayCommand<ArchiveItemViewModel> DeleteForeverCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    private async Task RefreshAsync()
    {
        Items.Clear();
        foreach (var item in await _conversations.GetArchivedAsync(_mode, 500, CancellationToken.None)) Items.Add(ArchiveItemViewModel.ForConversation(item));
        foreach (var item in await _containers.GetArchivedByModeAsync(_mode, 500, CancellationToken.None)) Items.Add(ArchiveItemViewModel.ForContainer(item));
        Status = Items.Count == 0 ? "Archive is empty." : $"{Items.Count} archived item{(Items.Count == 1 ? string.Empty : "s")}.";
    }

    private async Task RestoreAsync(ArchiveItemViewModel? item)
    {
        if (item?.Conversation is not null) await _conversations.UpsertConversationAsync(item.Conversation with { IsArchived = false, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        if (item?.Container is not null) await _containers.UpsertAsync(item.Container with { IsArchived = false, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        await RefreshAsync();
    }

    private async Task DeleteForeverAsync(ArchiveItemViewModel? item)
    {
        if (item?.Conversation is not null) await _conversations.DeleteConversationAsync(item.Conversation.Id, CancellationToken.None);
        if (item?.Container is not null) await _containers.DeleteAsync(item.Container.Id, CancellationToken.None);
        await RefreshAsync();
        Status = "Archived item permanently deleted.";
    }
}

/// <summary>
/// Represents archive item view model and keeps its related state and behavior together.
/// </summary>
public sealed class ArchiveItemViewModel : ObservableObject
{
    private bool _isDeleteConfirming;
    private ArchiveItemViewModel(string title, string kind, DateTimeOffset updated, Conversation? conversation, ContainerDefinition? container)
    {
        Title = title;
        Kind = kind;
        Updated = updated.LocalDateTime.ToString("g");
        Conversation = conversation;
        Container = container;
        BeginDeleteCommand = new RelayCommand(() => IsDeleteConfirming = true);
        CancelDeleteCommand = new RelayCommand(() => IsDeleteConfirming = false);
    }
    public string Title { get; }
    public string Kind { get; }
    public string Updated { get; }
    public Conversation? Conversation { get; }
    public ContainerDefinition? Container { get; }
    public bool IsDeleteConfirming { get => _isDeleteConfirming; set { if (SetProperty(ref _isDeleteConfirming, value)) RaisePropertyChanged(nameof(IsNormal)); } }
    public bool IsNormal => !IsDeleteConfirming;
    public RelayCommand BeginDeleteCommand { get; }
    public RelayCommand CancelDeleteCommand { get; }
    public static ArchiveItemViewModel ForConversation(Conversation item) => new(item.Title, "Conversation", item.UpdatedAt, item, null);
    public static ArchiveItemViewModel ForContainer(ContainerDefinition item) => new(item.Name, item.Mode == HavenMode.Studio ? "Project" : "Group", item.UpdatedAt, null, item);
}
