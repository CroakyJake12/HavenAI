/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/WorkspaceSurfacesViewModels.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns WorkspaceHomePageViewModel, WorkspaceHomeCardViewModel, AutomationSummaryViewModel, MacroSummaryViewModel, StudioCreationKind, StudioProjectPageViewModel, ProjectFeatureCardViewModel, DecisionItemViewModel, WorkspaceFileItemViewModel, WorkspaceEditorPageViewModel, WorkspaceVersionItemViewModel, EditorCommentViewModel, MacrosPageViewModel, MacroItemViewModel, ArchivePageViewModel, ArchiveItemViewModel. Read the type and member comments below as a map of each responsibility.
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
    /// <summary>
    /// Stores mode locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly HavenMode _mode;
    /// <summary>
    /// Stores containers locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IContainerRepository _containers;
    /// <summary>
    /// Stores conversations locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IConversationRepository _conversations;
    /// <summary>
    /// Stores automations locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IAutomationRepository _automations;
    /// <summary>
    /// Stores workspace state locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IWorkspaceStateRepository _workspaceState;
    /// <summary>
    /// Stores intelligence locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IProjectIntelligenceService _intelligence;
    /// <summary>
    /// Stores open locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Func<ContainerDefinition, Task> _open;
    /// <summary>
    /// Stores create locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Func<Task>? _create;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title => _mode == HavenMode.Studio ? "Studio Home" : "Do Home";
    /// <summary>
    /// Gets or updates subtitle, the bindable or domain state represented by this property.
    /// </summary>
    public string Subtitle => _mode == HavenMode.Studio
        ? "Projects, live state, active Scheduled Actions, and the next useful step."
        : "Task Groups, click-to-run macros, Scheduled Actions, and recent work.";
    /// <summary>
    /// Gets or updates collection heading, the bindable or domain state represented by this property.
    /// </summary>
    public string CollectionHeading => _mode == HavenMode.Studio ? "Projects" : "Task Groups";
    /// <summary>
    /// Creates label with the invariants required by its callers.
    /// </summary>
    public string CreateLabel => _mode == HavenMode.Studio ? "New project" : "New Task Group";
    /// <summary>
    /// Reports whether is studio is true for the current state.
    /// </summary>
    public bool IsStudio => _mode == HavenMode.Studio;
    /// <summary>
    /// Reports whether is do is true for the current state.
    /// </summary>
    public bool IsDo => _mode == HavenMode.Do;
    /// <summary>
    /// Reports whether has automations is true for the current state.
    /// </summary>
    public bool HasAutomations => ActiveAutomations.Count > 0;
    /// <summary>
    /// Reports whether has items is true for the current state.
    /// </summary>
    public bool HasItems => Items.Count > 0;
    /// <summary>
    /// Reports whether has macros is true for the current state.
    /// </summary>
    public bool HasMacros => Macros.Count > 0;
    /// <summary>
    /// Gets or updates items, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<WorkspaceHomeCardViewModel> Items { get; } = [];
    /// <summary>
    /// Gets or updates active automations, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<AutomationSummaryViewModel> ActiveAutomations { get; } = [];
    /// <summary>
    /// Gets or updates macros, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<MacroSummaryViewModel> Macros { get; } = [];
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Gets or updates refresh command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>
    /// Creates blank command with the invariants required by its callers.
    /// </summary>
    public AsyncRelayCommand CreateBlankCommand { get; }
    /// <summary>
    /// Gets or updates open command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<WorkspaceHomeCardViewModel> OpenCommand { get; }
    /// <summary>
    /// Gets or updates archive command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<WorkspaceHomeCardViewModel> ArchiveCommand { get; }

    /// <summary>
    /// Performs add path async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs refresh async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

        Macros.Clear();
        if (_mode == HavenMode.Do)
            foreach (var macro in (await _workspaceState.GetMacrosAsync(null, CancellationToken.None)).Take(12))
                Macros.Add(new MacroSummaryViewModel(macro.Name, macro.Description));
        RaisePropertyChanged(nameof(HasAutomations));
        RaisePropertyChanged(nameof(HasItems));
        RaisePropertyChanged(nameof(HasMacros));
        Status = Items.Count == 0 ? $"No {CollectionHeading.ToLowerInvariant()} yet." : $"{Items.Count} {CollectionHeading.ToLowerInvariant()} available locally.";
    }

    /// <summary>
    /// Creates blank async with the invariants required by its callers.
    /// </summary>
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

    /// <summary>
    /// Performs archive async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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
    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public ContainerDefinition Definition => definition;
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => definition.Name;
    /// <summary>
    /// Gets or updates path, the bindable or domain state represented by this property.
    /// </summary>
    public string Path => definition.RootPath ?? "No folder selected";
    /// <summary>
    /// Gets or updates last task, the bindable or domain state represented by this property.
    /// </summary>
    public string LastTask => recent?.Title ?? "No meaningful task recorded yet";
    /// <summary>
    /// Gets or updates branch, the bindable or domain state represented by this property.
    /// </summary>
    public string Branch => state?.Branch ?? "No Git state";
    /// <summary>
    /// Gets or updates work state, the bindable or domain state represented by this property.
    /// </summary>
    public string WorkState => state is null ? "Folder not connected" : state.HasUncommittedWork ? "Uncommitted work" : "Working tree clean";
    /// <summary>
    /// Builds state from the currently available inputs.
    /// </summary>
    public string BuildState => state?.LastBuildResult ?? "Build not run";
    /// <summary>
    /// Gets or updates recommended action, the bindable or domain state represented by this property.
    /// </summary>
    public string RecommendedAction => state?.RecommendedAction ?? "Connect a project folder in settings";
    /// <summary>
    /// Gets or updates accent, the bindable or domain state represented by this property.
    /// </summary>
    public string Accent => definition.Mode == HavenMode.Studio ? "STUDIO" : "DO";
}

/// <summary>
/// Represents automation summary view model and keeps its related state and behavior together.
/// </summary>
public sealed record AutomationSummaryViewModel(string Name, string Instruction, string NextRun);
/// <summary>
/// Represents macro summary view model and keeps its related state and behavior together.
/// </summary>
public sealed record MacroSummaryViewModel(string Name, string Description);

/// <summary>
/// Lists the supported studio creation kind values used to make state explicit and type-safe.
/// </summary>
public enum StudioCreationKind { None, Mode, Plugin, Agent, Prompt }

/// <summary>
/// Represents studio project page view model and keeps its related state and behavior together.
/// </summary>
public sealed class StudioProjectPageViewModel : ObservableObject
{
    /// <summary>
    /// Stores project locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ContainerDefinition _project;
    /// <summary>
    /// Stores conversations locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IConversationRepository _conversations;
    /// <summary>
    /// Stores containers locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IContainerRepository _containers;
    /// <summary>
    /// Stores automations locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IAutomationRepository _automations;
    /// <summary>
    /// Stores workspace state locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IWorkspaceStateRepository _workspaceState;
    /// <summary>
    /// Stores intelligence locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IProjectIntelligenceService _intelligence;
    /// <summary>
    /// Stores open file locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Func<WorkspaceFileItemViewModel, Task> _openFile;
    /// <summary>
    /// Stores start chat locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Func<string, Task> _startChat;
    /// <summary>
    /// Stores mode registry locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IModeRegistry? _modeRegistry;
    /// <summary>
    /// Stores catalog locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ICatalogRepository? _catalog;
    /// <summary>
    /// Stores ollama locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IOllamaClient? _ollama;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = "Loading project state…";
    /// <summary>
    /// Stores state locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ProjectStateSnapshot? _state;
    /// <summary>
    /// Stores risk locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ReleaseRiskReport? _risk;
    /// <summary>
    /// Stores intent query locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _intentQuery = string.Empty;
    /// <summary>
    /// Stores intent results locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _intentResults = string.Empty;
    /// <summary>
    /// Stores bug command locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _bugCommand = string.Empty;
    /// <summary>
    /// Stores bug confirmed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _bugConfirmed;
    /// <summary>
    /// Stores decision title locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _decisionTitle = string.Empty;
    /// <summary>
    /// Stores decision text locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _decisionText = string.Empty;
    /// <summary>
    /// Stores decision alternatives locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _decisionAlternatives = string.Empty;
    /// <summary>
    /// Stores decision reasoning locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _decisionReasoning = string.Empty;
    /// <summary>
    /// Stores decision evidence locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _decisionEvidence = string.Empty;
    /// <summary>
    /// Stores decision consequences locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _decisionConsequences = string.Empty;
    /// <summary>
    /// Stores git remote url locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _gitRemoteUrl = string.Empty;
    /// <summary>
    /// Stores is in create mode locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isInCreateMode;
    /// <summary>
    /// Stores creation kind locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private StudioCreationKind _creationKind = StudioCreationKind.None;
    /// <summary>
    /// Stores creation name locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _creationName = string.Empty;
    /// <summary>
    /// Stores creation description locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _creationDescription = string.Empty;
    /// <summary>
    /// Stores creation instructions locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _creationInstructions = string.Empty;
    /// <summary>
    /// Stores creation builder prompt locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _creationBuilderPrompt = string.Empty;
    /// <summary>
    /// Stores is in configure mode locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isInConfigureMode;
    /// <summary>
    /// Stores configure status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _configureStatus = string.Empty;

    public StudioProjectPageViewModel(ContainerDefinition project, IConversationRepository conversations, IContainerRepository containers,
        IAutomationRepository automations, IWorkspaceStateRepository workspaceState, IProjectIntelligenceService intelligence,
        Func<WorkspaceFileItemViewModel, Task> openFile, Func<string, Task> startChat,
        IModeRegistry? modeRegistry = null, ICatalogRepository? catalog = null, IOllamaClient? ollama = null)
    {
        _project = project;
        _conversations = conversations;
        _containers = containers;
        _automations = automations;
        _workspaceState = workspaceState;
        _intelligence = intelligence;
        _openFile = openFile;
        _startChat = startChat;
        _modeRegistry = modeRegistry;
        _catalog = catalog;
        _ollama = ollama;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenFileCommand = new AsyncRelayCommand<WorkspaceFileItemViewModel>(item => item is null ? Task.CompletedTask : _openFile(item));
        AskAiAboutFileCommand = new AsyncRelayCommand<WorkspaceFileItemViewModel>(item => item is null ? Task.CompletedTask : _startChat($"Analyze this file and explain what it does: {item.RelativePath}"));
        RevealInExplorerCommand = new RelayCommand<WorkspaceFileItemViewModel>(item =>
        {
            if (item is null) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{item.FullPath}\"") { UseShellExecute = true });
        });
        OpenEditorCommand = new AsyncRelayCommand(() => WithRoot(_intelligence.LaunchEditorAsync));
        OpenTerminalCommand = new AsyncRelayCommand(() => WithRoot(_intelligence.LaunchTerminalAsync));
        StartServerCommand = new AsyncRelayCommand(() => WithRoot(_intelligence.LaunchLocalServerAsync));
        BuildCommand = new AsyncRelayCommand(BuildAsync);
        TestCommand = new AsyncRelayCommand(TestAsync);
        StartChatCommand = new AsyncRelayCommand(() => _startChat(string.Empty));
        InitializeGitCommand = new AsyncRelayCommand(InitializeGitAsync);
        ConnectGitCommand = new AsyncRelayCommand(ConnectGitAsync, () => !string.IsNullOrWhiteSpace(GitRemoteUrl));
        ForecastRiskCommand = new AsyncRelayCommand(ForecastRiskAsync);
        IntentSearchCommand = new AsyncRelayCommand(IntentSearchAsync, () => !string.IsNullOrWhiteSpace(IntentQuery));
        BugTimeMachineCommand = new AsyncRelayCommand(BugTimeMachineAsync, () => BugConfirmed && !string.IsNullOrWhiteSpace(BugCommand));
        SaveDecisionCommand = new AsyncRelayCommand(SaveDecisionAsync, () => !string.IsNullOrWhiteSpace(DecisionTitle) && !string.IsNullOrWhiteSpace(DecisionText));
        DeleteDecisionCommand = new AsyncRelayCommand<DecisionItemViewModel>(DeleteDecisionAsync);
        UseFeatureCommand = new AsyncRelayCommand<ProjectFeatureCardViewModel>(item => item is null ? Task.CompletedTask : _startChat(item.Prompt));
        ArchiveProjectCommand = new AsyncRelayCommand(ArchiveProjectAsync);
        SwitchToCreateCommand = new RelayCommand(() => { IsInCreateMode = true; IsInConfigureMode = false; });
        SwitchToConfigureCommand = new RelayCommand(() => { IsInCreateMode = false; IsInConfigureMode = true; });
        SwitchToOverviewCommand = new RelayCommand(() => { IsInCreateMode = false; IsInConfigureMode = false; CreationKind = StudioCreationKind.None; });
        StartModeCreationCommand = new RelayCommand(() => { CreationKind = StudioCreationKind.Mode; IsInCreateMode = true; });
        StartPluginCreationCommand = new RelayCommand(() => { CreationKind = StudioCreationKind.Plugin; IsInCreateMode = true; });
        StartAgentCreationCommand = new RelayCommand(() => { CreationKind = StudioCreationKind.Agent; IsInCreateMode = true; });
        StartPromptCreationCommand = new RelayCommand(() => { CreationKind = StudioCreationKind.Prompt; IsInCreateMode = true; });
        CreateItemCommand = new AsyncRelayCommand(CreateItemAsync, () => !string.IsNullOrWhiteSpace(CreationName) && !string.IsNullOrWhiteSpace(CreationDescription));
        BuildWithAiCommand = new AsyncRelayCommand(BuildWithAiAsync, () => !string.IsNullOrWhiteSpace(CreationBuilderPrompt));
        CancelCreationCommand = new RelayCommand(() => { CreationKind = StudioCreationKind.None; IsInCreateMode = false; CreationName = CreationDescription = CreationInstructions = CreationBuilderPrompt = string.Empty; });
        Features =
        [
            new("Requirement Extractor", "Turn a rough request into requirements, constraints, and acceptance checks.", ">Rigid Extract clear requirements, constraints, unknowns, and acceptance checks for this project request: "),
            new("AI Test Generator", "Create targeted tests from a feature or bug report.", "@Test >Inspect Derive targeted tests from these requirements or this bug report, then run the safe relevant tests: "),
            new("Automatic Error Context", "Gather only relevant logs, settings, and recent actions.", ">Debug Gather only relevant project context and explain this failure in plain English: "),
            new("Project State Summary", "Explain what changed, what is unfinished, and what needs attention.", ">Report Summarise this project's current state, unfinished work, risks, and recommended next action with evidence."),
            new("Smart Defaults", "Recommend project and device settings without applying them automatically.", ">Inspect Recommend initial settings for this project and device. Explain each recommendation and wait for my approval before changing anything."),
            new("Feature Discovery", "Find an existing feature or workflow that already solves the problem.", ">Inspect Identify existing project or Haven features that could solve my current problem before adding anything new."),
            new("Repetitive Work Detector", "Spot tedious repeated work and propose a reviewable script.", ">Inspect Look for repetitive, tedious project work and propose a safe script or macro, but do not create it without my approval."),
            new("Haven Extension Builder", "Create a limited-scope Browse extension manifest and content script.", ">Rigid Build a Haven Browse extension with a declarative manifest, explicit allowed origins, and no privileged or native APIs. Validate its scope before importing it.")
        ];
        _ = RefreshAsync();
    }

    /// <summary>
    /// Gets or updates project id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid ProjectId => _project.Id;
    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public ContainerDefinition Definition => _project;
    /// <summary>
    /// Gets or updates project name, the bindable or domain state represented by this property.
    /// </summary>
    public string ProjectName => _project.Name;
    /// <summary>
    /// Gets or updates root path, the bindable or domain state represented by this property.
    /// </summary>
    public string RootPath => _project.RootPath ?? string.Empty;
    /// <summary>
    /// Reports whether has root is true for the current state.
    /// </summary>
    public bool HasRoot => Directory.Exists(RootPath);
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Gets or updates branch, the bindable or domain state represented by this property.
    /// </summary>
    public string Branch => _state?.Branch ?? "No Git branch";
    /// <summary>
    /// Gets or updates work state, the bindable or domain state represented by this property.
    /// </summary>
    public string WorkState => _state is null ? "Not inspected" : _state.HasUncommittedWork ? "Uncommitted changes" : "Working tree clean";
    /// <summary>
    /// Gets or updates last commit, the bindable or domain state represented by this property.
    /// </summary>
    public string LastCommit => _state?.LastCommit ?? "No commit found";
    /// <summary>
    /// Gets or updates last build, the bindable or domain state represented by this property.
    /// </summary>
    public string LastBuild => _state?.LastBuildResult ?? "Not run";
    /// <summary>
    /// Gets or updates latest error, the bindable or domain state represented by this property.
    /// </summary>
    public string LatestError => _state?.MostRecentError ?? "No recent error found";
    /// <summary>
    /// Gets or updates recommended action, the bindable or domain state represented by this property.
    /// </summary>
    public string RecommendedAction => _state?.RecommendedAction ?? "Connect a project folder";
    /// <summary>
    /// Gets or updates last meaningful task, the bindable or domain state represented by this property.
    /// </summary>
    public string LastMeaningfulTask { get; private set; } = "No project conversation yet";
    /// <summary>
    /// Gets or updates relevant conversation, the bindable or domain state represented by this property.
    /// </summary>
    public string RelevantConversation { get; private set; } = "No relevant conversation";
    /// <summary>
    /// Gets or updates adaptive help, the bindable or domain state represented by this property.
    /// </summary>
    public string AdaptiveHelp => _state is null ? "Choose a project folder to enable builds, file editing, intent search, and developer intelligence."
        : _state.HasUncommittedWork ? "Review the changed files and run the Release Risk Forecaster before publishing."
        : "Start from the recommended action, or open a file directly in Haven's editor.";
    /// <summary>
    /// Gets or updates files, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<WorkspaceFileItemViewModel> Files { get; } = [];
    /// <summary>
    /// Gets or updates decisions, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<DecisionItemViewModel> Decisions { get; } = [];
    /// <summary>
    /// Gets or updates active automations, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<AutomationSummaryViewModel> ActiveAutomations { get; } = [];
    /// <summary>
    /// Gets or updates features, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<ProjectFeatureCardViewModel> Features { get; }
    /// <summary>
    /// Gets or updates risk summary, the bindable or domain state represented by this property.
    /// </summary>
    public string RiskSummary => _risk is null ? "Not forecast yet" : $"{_risk.Level} · {_risk.Score}% risk";
    /// <summary>
    /// Gets or updates risk details, the bindable or domain state represented by this property.
    /// </summary>
    public string RiskDetails => _risk is null ? "Run before a release or publish operation." : string.Join("\n", _risk.RiskAreas.Concat(_risk.RecommendedTests.Select(item => "Test: " + item)));
    /// <summary>
    /// Gets or updates intent query, the bindable or domain state represented by this property.
    /// </summary>
    public string IntentQuery { get => _intentQuery; set { if (SetProperty(ref _intentQuery, value)) IntentSearchCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates intent results, the bindable or domain state represented by this property.
    /// </summary>
    public string IntentResults { get => _intentResults; private set => SetProperty(ref _intentResults, value); }
    /// <summary>
    /// Gets or updates bug command, the bindable or domain state represented by this property.
    /// </summary>
    public string BugCommand { get => _bugCommand; set { if (SetProperty(ref _bugCommand, value)) BugTimeMachineCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates bug confirmed, the bindable or domain state represented by this property.
    /// </summary>
    public bool BugConfirmed { get => _bugConfirmed; set { if (SetProperty(ref _bugConfirmed, value)) BugTimeMachineCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates decision title, the bindable or domain state represented by this property.
    /// </summary>
    public string DecisionTitle { get => _decisionTitle; set { if (SetProperty(ref _decisionTitle, value)) SaveDecisionCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates decision text, the bindable or domain state represented by this property.
    /// </summary>
    public string DecisionText { get => _decisionText; set { if (SetProperty(ref _decisionText, value)) SaveDecisionCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates decision alternatives, the bindable or domain state represented by this property.
    /// </summary>
    public string DecisionAlternatives { get => _decisionAlternatives; set => SetProperty(ref _decisionAlternatives, value); }
    /// <summary>
    /// Gets or updates decision reasoning, the bindable or domain state represented by this property.
    /// </summary>
    public string DecisionReasoning { get => _decisionReasoning; set => SetProperty(ref _decisionReasoning, value); }
    /// <summary>
    /// Gets or updates decision evidence, the bindable or domain state represented by this property.
    /// </summary>
    public string DecisionEvidence { get => _decisionEvidence; set => SetProperty(ref _decisionEvidence, value); }
    /// <summary>
    /// Gets or updates decision consequences, the bindable or domain state represented by this property.
    /// </summary>
    public string DecisionConsequences { get => _decisionConsequences; set => SetProperty(ref _decisionConsequences, value); }
    /// <summary>
    /// Gets or updates git remote url, the bindable or domain state represented by this property.
    /// </summary>
    public string GitRemoteUrl { get => _gitRemoteUrl; set { if (SetProperty(ref _gitRemoteUrl, value)) ConnectGitCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates refresh command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>
    /// Gets or updates open file command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<WorkspaceFileItemViewModel> OpenFileCommand { get; }
    /// <summary>
    /// Gets or updates ask ai about file command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<WorkspaceFileItemViewModel> AskAiAboutFileCommand { get; }
    /// <summary>
    /// Gets or updates reveal in explorer command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<WorkspaceFileItemViewModel> RevealInExplorerCommand { get; }
    /// <summary>
    /// Gets or updates open editor command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand OpenEditorCommand { get; }
    /// <summary>
    /// Gets or updates open terminal command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand OpenTerminalCommand { get; }
    /// <summary>
    /// Gets or updates start server command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand StartServerCommand { get; }
    /// <summary>
    /// Builds command from the currently available inputs.
    /// </summary>
    public AsyncRelayCommand BuildCommand { get; }
    /// <summary>
    /// Gets or updates test command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand TestCommand { get; }
    /// <summary>
    /// Gets or updates start chat command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand StartChatCommand { get; }
    /// <summary>
    /// Gets or updates initialize git command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand InitializeGitCommand { get; }
    /// <summary>
    /// Gets or updates connect git command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ConnectGitCommand { get; }
    /// <summary>
    /// Gets or updates forecast risk command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ForecastRiskCommand { get; }
    /// <summary>
    /// Gets or updates intent search command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand IntentSearchCommand { get; }
    /// <summary>
    /// Gets or updates bug time machine command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand BugTimeMachineCommand { get; }
    /// <summary>
    /// Gets or updates save decision command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand SaveDecisionCommand { get; }
    /// <summary>
    /// Gets or updates delete decision command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<DecisionItemViewModel> DeleteDecisionCommand { get; }
    /// <summary>
    /// Gets or updates use feature command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<ProjectFeatureCardViewModel> UseFeatureCommand { get; }
    /// <summary>
    /// Gets or updates archive project command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ArchiveProjectCommand { get; }
    /// <summary>
    /// Gets or updates switch to create command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand SwitchToCreateCommand { get; }
    /// <summary>
    /// Gets or updates switch to configure command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand SwitchToConfigureCommand { get; }
    /// <summary>
    /// Gets or updates switch to overview command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand SwitchToOverviewCommand { get; }
    /// <summary>
    /// Gets or updates start mode creation command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand StartModeCreationCommand { get; }
    /// <summary>
    /// Gets or updates start plugin creation command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand StartPluginCreationCommand { get; }
    /// <summary>
    /// Gets or updates start agent creation command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand StartAgentCreationCommand { get; }
    /// <summary>
    /// Gets or updates start prompt creation command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand StartPromptCreationCommand { get; }
    /// <summary>
    /// Creates item command with the invariants required by its callers.
    /// </summary>
    public AsyncRelayCommand CreateItemCommand { get; }
    /// <summary>
    /// Builds with ai command from the currently available inputs.
    /// </summary>
    public AsyncRelayCommand BuildWithAiCommand { get; }
    /// <summary>
    /// Reports whether cancel creation command is true for the current state.
    /// </summary>
    public RelayCommand CancelCreationCommand { get; }
    /// <summary>
    /// Reports whether is in create mode is true for the current state.
    /// </summary>
    public bool IsInCreateMode { get => _isInCreateMode; set { if (SetProperty(ref _isInCreateMode, value)) { RaisePropertyChanged(nameof(IsInOverview)); RaisePropertyChanged(nameof(CreationTitle)); RaisePropertyChanged(nameof(CreationHint)); } } }
    /// <summary>
    /// Reports whether is in configure mode is true for the current state.
    /// </summary>
    public bool IsInConfigureMode { get => _isInConfigureMode; set => SetProperty(ref _isInConfigureMode, value); }
    /// <summary>
    /// Reports whether is in overview is true for the current state.
    /// </summary>
    public bool IsInOverview => !IsInCreateMode && !IsInConfigureMode;
    /// <summary>
    /// Gets or updates creation kind, the bindable or domain state represented by this property.
    /// </summary>
    public StudioCreationKind CreationKind { get => _creationKind; set { if (SetProperty(ref _creationKind, value)) { RaisePropertyChanged(nameof(CreationTitle)); RaisePropertyChanged(nameof(CreationHint)); RaisePropertyChanged(nameof(IsCreatingMode)); RaisePropertyChanged(nameof(IsCreatingPlugin)); RaisePropertyChanged(nameof(IsCreatingAgent)); RaisePropertyChanged(nameof(IsCreatingPrompt)); RaisePropertyChanged(nameof(HasCreationKind)); } } }
    /// <summary>
    /// Gets or updates creation title, the bindable or domain state represented by this property.
    /// </summary>
    public string CreationTitle => CreationKind switch { StudioCreationKind.Mode => "Create Mode", StudioCreationKind.Plugin => "Create Plugin", StudioCreationKind.Agent => "Create Agent", StudioCreationKind.Prompt => "Create Prompt", _ => "Create" };
    /// <summary>
    /// Gets or updates creation hint, the bindable or domain state represented by this property.
    /// </summary>
    public string CreationHint => CreationKind switch { StudioCreationKind.Mode => "Define a new Haven mode with custom surfaces, tools, and system prompt.", StudioCreationKind.Plugin => "Create a functional plugin with capabilities and constraints.", StudioCreationKind.Agent => "Define a specialised assistant with instructions and model preferences.", StudioCreationKind.Prompt => "Create a reusable instruction prompt.", _ => "Choose what to create." };
    /// <summary>
    /// Reports whether is creating mode is true for the current state.
    /// </summary>
    public bool IsCreatingMode => CreationKind == StudioCreationKind.Mode;
    /// <summary>
    /// Reports whether is creating plugin is true for the current state.
    /// </summary>
    public bool IsCreatingPlugin => CreationKind == StudioCreationKind.Plugin;
    /// <summary>
    /// Reports whether is creating agent is true for the current state.
    /// </summary>
    public bool IsCreatingAgent => CreationKind == StudioCreationKind.Agent;
    /// <summary>
    /// Reports whether is creating prompt is true for the current state.
    /// </summary>
    public bool IsCreatingPrompt => CreationKind == StudioCreationKind.Prompt;
    /// <summary>
    /// Reports whether has creation kind is true for the current state.
    /// </summary>
    public bool HasCreationKind => CreationKind != StudioCreationKind.None;
    /// <summary>
    /// Gets or updates creation name, the bindable or domain state represented by this property.
    /// </summary>
    public string CreationName { get => _creationName; set { if (SetProperty(ref _creationName, value)) CreateItemCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates creation description, the bindable or domain state represented by this property.
    /// </summary>
    public string CreationDescription { get => _creationDescription; set { if (SetProperty(ref _creationDescription, value)) CreateItemCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates creation instructions, the bindable or domain state represented by this property.
    /// </summary>
    public string CreationInstructions { get => _creationInstructions; set => SetProperty(ref _creationInstructions, value); }
    /// <summary>
    /// Gets or updates creation builder prompt, the bindable or domain state represented by this property.
    /// </summary>
    public string CreationBuilderPrompt { get => _creationBuilderPrompt; set { if (SetProperty(ref _creationBuilderPrompt, value)) BuildWithAiCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates configure status, the bindable or domain state represented by this property.
    /// </summary>
    public string ConfigureStatus { get => _configureStatus; private set => SetProperty(ref _configureStatus, value); }

    /// <summary>
    /// Performs refresh async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RefreshAsync()
    {
        if (!HasRoot) { Status = "This project has no accessible folder. Open Project settings to connect one."; return; }
        _state = await _intelligence.GetStateAsync(RootPath, CancellationToken.None);
        RaiseStateProperties();
        var recent = (await _conversations.GetRecentAsync(HavenMode.Studio, 300, CancellationToken.None)).Where(item => item.ContainerId == ProjectId).ToArray();
        LastMeaningfulTask = recent.FirstOrDefault()?.Title ?? "No project conversation yet";
        RelevantConversation = recent.FirstOrDefault()?.Title ?? "Start a project chat";
        RaisePropertyChanged(nameof(LastMeaningfulTask));
        RaisePropertyChanged(nameof(RelevantConversation));

        Files.Clear();
        foreach (var file in EnumerateSupportedFiles(RootPath, 2500, CancellationToken.None)) Files.Add(file);
        await RefreshDecisionsAsync();
        ActiveAutomations.Clear();
        foreach (var item in (await _automations.GetAllAsync(CancellationToken.None)).Where(item => item.IsEnabled && item.ContainerId == ProjectId))
            ActiveAutomations.Add(new(item.Name, item.Instruction, item.NextRunAt?.LocalDateTime.ToString("g") ?? "Waiting for trigger"));
        Status = $"Project state captured at {_state.CapturedAt.LocalDateTime:t}.";
    }

    /// <summary>
    /// Builds async from the currently available inputs.
    /// </summary>
    private async Task BuildAsync()
    {
        if (!HasRoot) return;
        Status = "Forecasting release risk before build…";
        _risk = await _intelligence.ForecastReleaseRiskAsync(RootPath, CancellationToken.None);
        RaisePropertyChanged(nameof(RiskSummary));
        RaisePropertyChanged(nameof(RiskDetails));
        Status = $"Release risk {_risk.Level.ToLowerInvariant()} ({_risk.Score}%). Building project…";
        var result = await _intelligence.RunBuildAsync(RootPath, CancellationToken.None);
        Status = result.ExitCode == 0 ? $"Build passed in {result.Duration.TotalSeconds:0.0}s." : $"Build failed with exit code {result.ExitCode}: {Tail(result.StandardError, 600)}";
        await RefreshAsync();
    }

    /// <summary>
    /// Performs forecast risk async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ForecastRiskAsync()
    {
        if (!HasRoot) return;
        _risk = await _intelligence.ForecastReleaseRiskAsync(RootPath, CancellationToken.None);
        RaisePropertyChanged(nameof(RiskSummary));
        RaisePropertyChanged(nameof(RiskDetails));
        Status = $"Release risk is {_risk.Level.ToLowerInvariant()} ({_risk.Score}%). Critical findings are surfaced separately and minor cleanup still requires approval.";
    }

    /// <summary>
    /// Performs test async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task TestAsync()
    {
        if (!HasRoot) return;
        Status = "Running the project's detected test command…";
        try
        {
            var result = await _intelligence.RunTestsAsync(RootPath, CancellationToken.None);
            Status = result.ExitCode == 0
                ? $"Tests passed in {result.Duration.TotalSeconds:0.0}s."
                : $"Tests failed with exit code {result.ExitCode}: {Tail(result.StandardError + result.StandardOutput, 900)}";
        }
        catch (Exception ex) { Status = "Tests could not start: " + ex.Message; }
    }

    /// <summary>
    /// Performs initialize git async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task InitializeGitAsync()
    {
        if (!HasRoot) return;
        try
        {
            var result = await _intelligence.InitializeGitAsync(RootPath, CancellationToken.None);
            Status = result.ExitCode == 0 ? "Git repository ready." : "Git could not be initialized: " + Tail(result.StandardError, 700);
            await RefreshAsync();
        }
        catch (Exception ex) { Status = "Git could not be initialized: " + ex.Message; }
    }

    /// <summary>
    /// Performs connect git async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ConnectGitAsync()
    {
        if (!HasRoot) return;
        try
        {
            var result = await _intelligence.ConnectGitRemoteAsync(RootPath, GitRemoteUrl, CancellationToken.None);
            Status = result.ExitCode == 0 ? "Git remote 'origin' connected." : "Git remote was not changed: " + Tail(result.StandardError, 700);
            if (result.ExitCode == 0) GitRemoteUrl = string.Empty;
            await RefreshAsync();
        }
        catch (Exception ex) { Status = "Git remote was not changed: " + ex.Message; }
    }

    /// <summary>
    /// Performs intent search async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task IntentSearchAsync()
    {
        if (!HasRoot) return;
        IntentResults = await _intelligence.FindIntentMatchesAsync(RootPath, IntentQuery, CancellationToken.None);
    }

    /// <summary>
    /// Performs bug time machine async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task BugTimeMachineAsync()
    {
        if (!HasRoot || !BugConfirmed) return;
        Status = "Bug Time Machine is checking Git history. The clean working tree will be restored when it finishes…";
        try
        {
            var result = await _intelligence.RunBugTimeMachineAsync(RootPath, BugCommand, CancellationToken.None);
            if (result.ExitCode == 0)
            {
                Status = "Bug Time Machine completed and restored the clean working tree. A regression review is ready in Studio chat.";
                var evidence = result.StandardOutput.Length <= 10_000 ? result.StandardOutput : result.StandardOutput[..10_000] + "\n[additional diff omitted from chat draft]";
                await _startChat($">Inspect Bug Time Machine completed for this reproduction command:\n{BugCommand}\n\nEvidence:\n{evidence}\n\nExplain the first failing commit's meaningful differences. If this is a regression, compare the last working implementation with the current code and apply a current-framework-safe fix using workspace tools. Do not copy outdated framework patterns; ask me before applying a questionable or obsolete approach. Run the reproduction and targeted tests after any fix.");
            }
            else
            {
                Status = "Bug Time Machine stopped: " + Tail(result.StandardError + result.StandardOutput, 900);
            }
        }
        catch (Exception ex) { Status = $"Bug Time Machine did not start: {ex.Message}"; }
        finally { BugConfirmed = false; }
    }

    /// <summary>
    /// Performs save decision async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task SaveDecisionAsync()
    {
        var now = DateTimeOffset.UtcNow;
        await _workspaceState.UpsertDecisionAsync(new DecisionRecord(Guid.NewGuid(), ProjectId, DecisionTitle.Trim(), DecisionText.Trim(),
            DecisionAlternatives.Trim(), DecisionReasoning.Trim(), DecisionEvidence.Trim(), DecisionConsequences.Trim(), now, now), CancellationToken.None);
        DecisionTitle = DecisionText = DecisionAlternatives = DecisionReasoning = DecisionEvidence = DecisionConsequences = string.Empty;
        await RefreshDecisionsAsync();
        Status = "Decision saved with its alternatives, evidence, and consequences. Haven will warn before contradicting it.";
    }

    /// <summary>
    /// Performs delete decision async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task DeleteDecisionAsync(DecisionItemViewModel? item)
    {
        if (item is null) return;
        await _workspaceState.DeleteDecisionAsync(item.Definition.Id, CancellationToken.None);
        await RefreshDecisionsAsync();
    }

    /// <summary>
    /// Performs refresh decisions async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RefreshDecisionsAsync()
    {
        Decisions.Clear();
        foreach (var item in await _workspaceState.GetDecisionsAsync(ProjectId, CancellationToken.None)) Decisions.Add(new(item));
    }

    /// <summary>
    /// Performs archive project async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ArchiveProjectAsync()
    {
        await _containers.UpsertAsync(_project with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        Status = "Project archived. Restore it from Archive when needed.";
    }

    /// <summary>
    /// Performs the with root step owned by this component.
    /// </summary>
    private async Task WithRoot(Func<string, CancellationToken, Task> action)
    {
        if (!HasRoot) { Status = "Connect a project folder first."; return; }
        try { await action(RootPath, CancellationToken.None); Status = "Launch requested."; }
        catch (Exception ex) { Status = $"Could not launch: {ex.Message}"; }
    }

    /// <summary>
    /// Creates item async with the invariants required by its callers.
    /// </summary>
    private async Task CreateItemAsync()
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            switch (CreationKind)
            {
                case StudioCreationKind.Mode:
                    if (_modeRegistry is not null)
                    {
                        var key = CreationName.Trim().ToLowerInvariant().Replace(" ", "-");
                        var mode = new ModeDefinition(Guid.NewGuid(), key, CreationName.Trim(), CreationDescription.Trim(),
                            "puzzle", HavenMode.Do, "[\"Do\"]", "[]", "[]", "[]", CreationInstructions.Trim(),
                            ModeSource.Created, ModeInstallState.InstalledByUser, "User", "1.0.0", "[]", now, now);
                        await _modeRegistry.UpsertModeAsync(mode, CancellationToken.None);
                        await _modeRegistry.AddVersionAsync(new ModeVersion(Guid.NewGuid(), mode.Id, 1, 0, 0, "{}", "Initial version", now), CancellationToken.None);
                        Status = $"Mode '{CreationName}' created. It is available in the Mode Library.";
                    }
                    break;
                case StudioCreationKind.Plugin:
                    if (_catalog is not null)
                    {
                        await _catalog.UpsertPluginAsync(new PluginDefinition(Guid.NewGuid(), CreationName.Trim(), CreationDescription.Trim(),
                            "plugin-custom", CreationInstructions.Trim(), "[]", "[]", false, false, true, now), CancellationToken.None);
                        Status = $"Plugin '{CreationName}' created.";
                    }
                    break;
                case StudioCreationKind.Agent:
                    if (_catalog is not null)
                    {
                        await _catalog.UpsertAgentAsync(new AgentDefinition(Guid.NewGuid(), CreationName.Trim(), CreationDescription.Trim(),
                            CreationInstructions.Trim(), "agent-custom", "default", null, "", "{\"mode\":\"ask\"}", false, true, now), CancellationToken.None);
                        Status = $"Agent '{CreationName}' created.";
                    }
                    break;
                case StudioCreationKind.Prompt:
                    if (_catalog is not null)
                    {
                        await _catalog.UpsertPromptAsync(new PromptDefinition(Guid.NewGuid(), CreationName.Trim(), CreationDescription.Trim(),
                            "prompt-custom", CreationInstructions.Trim(), false, false, true, now), CancellationToken.None);
                        Status = $"Prompt '{CreationName}' created.";
                    }
                    break;
            }
            CreationName = CreationDescription = CreationInstructions = CreationBuilderPrompt = string.Empty;
            CreationKind = StudioCreationKind.None;
            IsInCreateMode = false;
        }
        catch (Exception ex)
        {
            Status = $"Could not create: {ex.Message}";
        }
    }

    /// <summary>
    /// Builds with ai async from the currently available inputs.
    /// </summary>
    private async Task BuildWithAiAsync()
    {
        try
        {
            Status = "Asking a local model to draft the configuration...";
            if (_ollama is null) { Status = "AI client not available."; return; }
            var models = await _ollama.GetModelsAsync(CancellationToken.None);
            var model = models.FirstOrDefault(m => m.Supports(ToolCapability.Text)) ?? models.FirstOrDefault();
            if (model is null) { Status = "No local model available."; return; }
            var kind = CreationKind switch { StudioCreationKind.Mode => "mode", StudioCreationKind.Plugin => "plugin", StudioCreationKind.Agent => "agent", _ => "prompt" };
            var result = await _ollama.CompleteAsync(new OllamaChatRequest(
                model.Name,
                [new OllamaMessage("user", $"Write concise, production-ready system instructions for a Haven {kind} with this purpose: {CreationBuilderPrompt.Trim()}\nReturn only the instruction text.")],
                EffortLevel.Medium), CancellationToken.None);
            CreationInstructions = result.Trim();
            if (string.IsNullOrWhiteSpace(CreationDescription)) CreationDescription = CreationBuilderPrompt.Trim();
            Status = "Draft ready. Review the fields and create it.";
        }
        catch (Exception ex)
        {
            Status = $"AI draft failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Performs the raise state properties step owned by this component.
    /// </summary>
    private void RaiseStateProperties()
    {
        RaisePropertyChanged(nameof(Branch));
        RaisePropertyChanged(nameof(WorkState));
        RaisePropertyChanged(nameof(LastCommit));
        RaisePropertyChanged(nameof(LastBuild));
        RaisePropertyChanged(nameof(LatestError));
        RaisePropertyChanged(nameof(RecommendedAction));
        RaisePropertyChanged(nameof(AdaptiveHelp));
    }

    /// <summary>
    /// Performs the enumerate supported files step owned by this component.
    /// </summary>
    private static IEnumerable<WorkspaceFileItemViewModel> EnumerateSupportedFiles(string root, int limit, CancellationToken cancellationToken)
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".git", ".vs", "bin", "obj", "node_modules", "dist", "artifacts", "packages" };
        var extensions = new HashSet<string>(new[] { ".cs", ".fs", ".vb", ".cpp", ".h", ".axaml", ".xaml", ".xml", ".json", ".md", ".txt", ".ps1", ".js", ".ts", ".tsx", ".jsx", ".css", ".html", ".py", ".go", ".rs", ".yaml", ".yml", ".toml", ".props", ".targets", ".csproj" }, StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(root);
        var count = 0;
        while (pending.Count > 0 && count < limit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            IEnumerable<string> dirs;
            IEnumerable<string> files;
            try { dirs = Directory.EnumerateDirectories(directory); files = Directory.EnumerateFiles(directory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var child in dirs)
            {
                var info = new DirectoryInfo(child);
                if (!ignored.Contains(info.Name) && !info.Attributes.HasFlag(FileAttributes.ReparsePoint)) pending.Push(child);
            }
            foreach (var path in files.Where(path => extensions.Contains(Path.GetExtension(path))).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (++count > limit) yield break;
                yield return new WorkspaceFileItemViewModel(root, Path.GetRelativePath(root, path));
            }
        }
    }

    /// <summary>
    /// Performs the tail step owned by this component.
    /// </summary>
    private static string Tail(string value, int length) => string.IsNullOrWhiteSpace(value) ? "No diagnostic output." : value.Length <= length ? value.Trim() : value[^length..].Trim();
}

/// <summary>
/// Represents project feature card view model and keeps its related state and behavior together.
/// </summary>
public sealed record ProjectFeatureCardViewModel(string Name, string Description, string Prompt);
/// <summary>
/// Represents decision item view model and keeps its related state and behavior together.
/// </summary>
public sealed class DecisionItemViewModel(DecisionRecord definition)
{
    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public DecisionRecord Definition => definition;
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title => definition.Title;
    /// <summary>
    /// Gets or updates decision, the bindable or domain state represented by this property.
    /// </summary>
    public string Decision => definition.Decision;
    /// <summary>
    /// Gets or updates reasoning, the bindable or domain state represented by this property.
    /// </summary>
    public string Reasoning => definition.Reasoning;
    /// <summary>
    /// Gets or updates evidence, the bindable or domain state represented by this property.
    /// </summary>
    public string Evidence => definition.Evidence;
    /// <summary>
    /// Gets or updates consequences, the bindable or domain state represented by this property.
    /// </summary>
    public string Consequences => definition.Consequences;
    /// <summary>
    /// Gets or updates updated, the bindable or domain state represented by this property.
    /// </summary>
    public string Updated => definition.UpdatedAt.LocalDateTime.ToString("g");
}

/// <summary>
/// Represents workspace file item view model and keeps its related state and behavior together.
/// </summary>
public sealed class WorkspaceFileItemViewModel(string root, string relativePath)
{
    /// <summary>
    /// Gets or updates root, the bindable or domain state represented by this property.
    /// </summary>
    public string Root => root;
    /// <summary>
    /// Gets or updates relative path, the bindable or domain state represented by this property.
    /// </summary>
    public string RelativePath => relativePath.Replace(Path.DirectorySeparatorChar, '/');
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => Path.GetFileName(relativePath);
    /// <summary>
    /// Gets or updates folder, the bindable or domain state represented by this property.
    /// </summary>
    public string Folder => Path.GetDirectoryName(relativePath)?.Replace(Path.DirectorySeparatorChar, '/') ?? string.Empty;
    /// <summary>
    /// Gets or updates full path, the bindable or domain state represented by this property.
    /// </summary>
    public string FullPath => Path.GetFullPath(Path.Combine(root, relativePath));
    /// <summary>
    /// Performs the to string step owned by this component.
    /// </summary>
    public override string ToString() => RelativePath;
}

/// <summary>
/// Represents workspace editor page view model and keeps its related state and behavior together.
/// </summary>
public sealed class WorkspaceEditorPageViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Stores container locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ContainerDefinition _container;
    /// <summary>
    /// Stores conversation id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Guid? _conversationId;
    /// <summary>
    /// Stores tools locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IWorkspaceToolService _tools;
    /// <summary>
    /// Stores history locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IWorkspaceStateRepository _history;
    /// <summary>
    /// Stores conversations locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IConversationRepository _conversations;
    /// <summary>
    /// Stores branch locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Func<Task> _branch;
    /// <summary>
    /// Stores interrupt locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Action _interrupt;
    /// <summary>
    /// Stores undo locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Stack<WorkspaceVersion> _undo = new();
    /// <summary>
    /// Stores redo locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Stack<WorkspaceVersion> _redo = new();
    /// <summary>
    /// Stores watcher locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private FileSystemWatcher? _watcher;
    /// <summary>
    /// Stores file locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private WorkspaceFileItemViewModel _file;
    /// <summary>
    /// Stores content locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _content = string.Empty;
    /// <summary>
    /// Stores saved content locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _savedContent = string.Empty;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = "Loading file…";
    /// <summary>
    /// Stores requires branch after rollback locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _requiresBranchAfterRollback;
    /// <summary>
    /// Stores rollforward content locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string? _rollforwardContent;
    /// <summary>
    /// Stores selected version locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private WorkspaceVersionItemViewModel? _selectedVersion;
    /// <summary>
    /// Stores comment prompt locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _commentPrompt = string.Empty;
    /// <summary>
    /// Stores selected snippet locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _selectedSnippet = string.Empty;
    /// <summary>
    /// Stores show diff locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _showDiff;
    /// <summary>
    /// Stores diff text locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _diffText = string.Empty;

    public WorkspaceEditorPageViewModel(ContainerDefinition container, Guid? conversationId, WorkspaceFileItemViewModel file,
        IWorkspaceToolService tools, IWorkspaceStateRepository history, IConversationRepository conversations, Func<Task> branch, Action interrupt)
    {
        _container = container;
        _conversationId = conversationId;
        _file = file;
        _tools = tools;
        _history = history;
        _conversations = conversations;
        _branch = branch;
        _interrupt = interrupt;
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
        _ = LoadAsync();
    }

    /// <summary>
    /// Gets or updates container, the bindable or domain state represented by this property.
    /// </summary>
    public ContainerDefinition Container => _container;

    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title => _file.Name;
    /// <summary>
    /// Gets or updates relative path, the bindable or domain state represented by this property.
    /// </summary>
    public string RelativePath => _file.RelativePath;
    /// <summary>
    /// Gets or updates project name, the bindable or domain state represented by this property.
    /// </summary>
    public string ProjectName => _container.Name;
    /// <summary>
    /// Gets or updates content, the bindable or domain state represented by this property.
    /// </summary>
    public string Content { get => _content; set { if (!SetProperty(ref _content, value)) return; RaiseDirtyProperties(); } }
    /// <summary>
    /// Reports whether is dirty is true for the current state.
    /// </summary>
    public bool IsDirty => !string.Equals(Content, _savedContent, StringComparison.Ordinal);
    /// <summary>
    /// Gets or updates dirty label, the bindable or domain state represented by this property.
    /// </summary>
    public string DirtyLabel => IsDirty ? "Unsaved changes" : "Saved";
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Gets or updates requires branch after rollback, the bindable or domain state represented by this property.
    /// </summary>
    public bool RequiresBranchAfterRollback { get => _requiresBranchAfterRollback; private set { if (!SetProperty(ref _requiresBranchAfterRollback, value)) return; RaisePropertyChanged(nameof(CanEdit)); BranchAfterRollbackCommand.RaiseCanExecuteChanged(); SaveCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Reports whether can edit is true for the current state.
    /// </summary>
    public bool CanEdit => !RequiresBranchAfterRollback;
    /// <summary>
    /// Reports whether can rollforward is true for the current state.
    /// </summary>
    public bool CanRollforward => !string.IsNullOrEmpty(_rollforwardContent);
    /// <summary>
    /// Gets or updates selected version, the bindable or domain state represented by this property.
    /// </summary>
    public WorkspaceVersionItemViewModel? SelectedVersion { get => _selectedVersion; set { if (SetProperty(ref _selectedVersion, value)) RollbackCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates comment prompt, the bindable or domain state represented by this property.
    /// </summary>
    public string CommentPrompt { get => _commentPrompt; set { if (SetProperty(ref _commentPrompt, value)) AddCommentCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates selected snippet, the bindable or domain state represented by this property.
    /// </summary>
    public string SelectedSnippet { get => _selectedSnippet; private set => SetProperty(ref _selectedSnippet, value); }
    /// <summary>
    /// Gets or updates versions, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<WorkspaceVersionItemViewModel> Versions { get; } = [];
    /// <summary>
    /// Gets or updates comments, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<EditorCommentViewModel> Comments { get; } = [];
    /// <summary>
    /// Gets or updates changelog, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<string> Changelog { get; } = [];
    /// <summary>
    /// Gets or updates save command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand SaveCommand { get; }
    /// <summary>
    /// Gets or updates undo command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand UndoCommand { get; }
    /// <summary>
    /// Gets or updates redo command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RedoCommand { get; }
    /// <summary>
    /// Gets or updates rollback command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RollbackCommand { get; }
    /// <summary>
    /// Gets or updates rollforward command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RollforwardCommand { get; }
    /// <summary>
    /// Gets or updates branch after rollback command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand BranchAfterRollbackCommand { get; }
    /// <summary>
    /// Gets or updates add comment command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand AddCommentCommand { get; }
    /// <summary>
    /// Gets or updates interrupt command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand InterruptCommand { get; }
    /// <summary>
    /// Gets or updates reload command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ReloadCommand { get; }
    /// <summary>
    /// Gets or updates toggle diff command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ToggleDiffCommand { get; }
    /// <summary>
    /// Gets or updates show diff, the bindable or domain state represented by this property.
    /// </summary>
    public bool ShowDiff { get => _showDiff; set => SetProperty(ref _showDiff, value); }
    /// <summary>
    /// Gets or updates diff text, the bindable or domain state represented by this property.
    /// </summary>
    public string DiffText { get => _diffText; set => SetProperty(ref _diffText, value); }

    /// <summary>
    /// Performs the set selection step owned by this component.
    /// </summary>
    public void SetSelection(string text) => SelectedSnippet = text;

    /// <summary>
    /// Performs load async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs save async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs undo async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task UndoAsync()
    {
        if (_undo.Count == 0) return;
        var version = _undo.Pop();
        await WriteHistoryStateAsync(version.BeforeContent, WorkspaceVersionKind.Undo, "Undid " + version.Summary);
        _redo.Push(version);
        RaiseHistoryCommands();
    }

    /// <summary>
    /// Performs redo async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RedoAsync()
    {
        if (_redo.Count == 0) return;
        var version = _redo.Pop();
        await WriteHistoryStateAsync(version.AfterContent, WorkspaceVersionKind.Redo, "Redid " + version.Summary);
        _undo.Push(version);
        RaiseHistoryCommands();
    }

    /// <summary>
    /// Performs rollback async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs rollforward async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs branch after rollback async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task BranchAfterRollbackAsync()
    {
        await _branch();
        RequiresBranchAfterRollback = false;
        Status = "Branched the chat. New edits can continue without overwriting the original history.";
    }

    /// <summary>
    /// Performs write history state async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs add comment async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs refresh versions async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RefreshVersionsAsync()
    {
        Versions.Clear();
        foreach (var version in await _history.GetVersionsAsync(_container.Id, _file.RelativePath, 100, CancellationToken.None)) Versions.Add(new(version));
        SelectedVersion = Versions.FirstOrDefault();
    }

    /// <summary>
    /// Performs the start watcher step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Handles the file changed event raised by the UI or runtime.
    /// </summary>
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

    /// <summary>
    /// Performs the raise dirty properties step owned by this component.
    /// </summary>
    private void RaiseDirtyProperties()
    {
        RaisePropertyChanged(nameof(IsDirty));
        RaisePropertyChanged(nameof(DirtyLabel));
        SaveCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Performs the raise history commands step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the truncate step owned by this component.
    /// </summary>
    private static string Truncate(string value, int limit) => value.Length <= limit ? value : value[..limit] + "…";

    /// <summary>
    /// Performs the toggle diff step owned by this component.
    /// </summary>
    private void ToggleDiff()
    {
        ShowDiff = !ShowDiff;
        if (!ShowDiff) { DiffText = string.Empty; return; }
        var before = _savedContent;
        var after = Content;
        if (string.IsNullOrEmpty(before) && string.IsNullOrEmpty(after)) { DiffText = "(no changes to compare)"; return; }
        var oldLines = (before ?? "").Replace("\r\n", "\n").Split('\n');
        var newLines = (after ?? "").Replace("\r\n", "\n").Split('\n');
        var sb = new System.Text.StringBuilder();
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

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _watcher?.Dispose();
}

/// <summary>
/// Represents workspace version item view model and keeps its related state and behavior together.
/// </summary>
public sealed class WorkspaceVersionItemViewModel(WorkspaceVersion definition)
{
    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public WorkspaceVersion Definition => definition;
    /// <summary>
    /// Gets or updates label, the bindable or domain state represented by this property.
    /// </summary>
    public string Label => $"{definition.CreatedAt.LocalDateTime:g} · {definition.Kind}";
    /// <summary>
    /// Gets or updates summary, the bindable or domain state represented by this property.
    /// </summary>
    public string Summary => definition.Summary;
    /// <summary>
    /// Gets or updates changes, the bindable or domain state represented by this property.
    /// </summary>
    public string Changes => $"+{definition.LinesAdded}/-{definition.LinesRemoved}";
    /// <summary>
    /// Performs the to string step owned by this component.
    /// </summary>
    public override string ToString() => Label;
}

/// <summary>
/// Represents editor comment view model and keeps its related state and behavior together.
/// </summary>
public sealed record EditorCommentViewModel(Guid Id, string Selection, string Prompt, DateTimeOffset CreatedAt)
{
    /// <summary>
    /// Gets or updates time, the bindable or domain state represented by this property.
    /// </summary>
    public string Time => CreatedAt.ToString("t");
}

/// <summary>
/// Represents macros page view model and keeps its related state and behavior together.
/// </summary>
public sealed class MacrosPageViewModel : ObservableObject
{
    /// <summary>
    /// Stores repository locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IWorkspaceStateRepository _repository;
    /// <summary>
    /// Stores container id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Guid? _containerId;
    /// <summary>
    /// Stores invoke locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Func<string, Task> _invoke;
    /// <summary>
    /// Stores name locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _name = string.Empty;
    /// <summary>
    /// Stores description locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _description = string.Empty;
    /// <summary>
    /// Stores instruction locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _instruction = string.Empty;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = "Macros run only when clicked or explicitly invoked with @Macro.";

    public MacrosPageViewModel(IWorkspaceStateRepository repository, Guid? containerId, Func<string, Task> invoke)
    {
        _repository = repository;
        _containerId = containerId;
        _invoke = invoke;
        CreateCommand = new AsyncRelayCommand(CreateAsync, () => !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Instruction));
        InvokeCommand = new AsyncRelayCommand<MacroItemViewModel>(item => item is null ? Task.CompletedTask : _invoke(item.Definition.Instruction));
        DeleteCommand = new AsyncRelayCommand<MacroItemViewModel>(DeleteAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        _ = RefreshAsync();
    }

    /// <summary>
    /// Gets or updates items, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<MacroItemViewModel> Items { get; } = [];
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name { get => _name; set { if (SetProperty(ref _name, value)) CreateCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates description, the bindable or domain state represented by this property.
    /// </summary>
    public string Description { get => _description; set => SetProperty(ref _description, value); }
    /// <summary>
    /// Gets or updates instruction, the bindable or domain state represented by this property.
    /// </summary>
    public string Instruction { get => _instruction; set { if (SetProperty(ref _instruction, value)) CreateCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Creates command with the invariants required by its callers.
    /// </summary>
    public AsyncRelayCommand CreateCommand { get; }
    /// <summary>
    /// Gets or updates invoke command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<MacroItemViewModel> InvokeCommand { get; }
    /// <summary>
    /// Gets or updates delete command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<MacroItemViewModel> DeleteCommand { get; }
    /// <summary>
    /// Gets or updates refresh command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>
    /// Performs refresh async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RefreshAsync()
    {
        Items.Clear();
        foreach (var item in await _repository.GetMacrosAsync(_containerId, CancellationToken.None)) Items.Add(new(item));
    }

    /// <summary>
    /// Creates async with the invariants required by its callers.
    /// </summary>
    private async Task CreateAsync()
    {
        var now = DateTimeOffset.UtcNow;
        await _repository.UpsertMacroAsync(new MacroDefinition(Guid.NewGuid(), Name.Trim(), Description.Trim(), Instruction.Trim(), _containerId, true, now, now), CancellationToken.None);
        Name = Description = Instruction = string.Empty;
        await RefreshAsync();
        Status = "Macro created. It remains inert until clicked.";
    }

    /// <summary>
    /// Performs delete async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task DeleteAsync(MacroItemViewModel? item)
    {
        if (item is null) return;
        await _repository.DeleteMacroAsync(item.Definition.Id, CancellationToken.None);
        await RefreshAsync();
    }
}

/// <summary>
/// Represents macro item view model and keeps its related state and behavior together.
/// </summary>
public sealed class MacroItemViewModel(MacroDefinition definition)
{
    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public MacroDefinition Definition => definition;
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => definition.Name;
    /// <summary>
    /// Gets or updates description, the bindable or domain state represented by this property.
    /// </summary>
    public string Description => definition.Description;
    /// <summary>
    /// Gets or updates instruction, the bindable or domain state represented by this property.
    /// </summary>
    public string Instruction => definition.Instruction;
}

/// <summary>
/// Represents archive page view model and keeps its related state and behavior together.
/// </summary>
public sealed class ArchivePageViewModel : ObservableObject
{
    /// <summary>
    /// Stores mode locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly HavenMode _mode;
    /// <summary>
    /// Stores conversations locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IConversationRepository _conversations;
    /// <summary>
    /// Stores containers locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IContainerRepository _containers;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Gets or updates items, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ArchiveItemViewModel> Items { get; } = [];
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title => $"{_mode} Archive";
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Gets or updates restore command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<ArchiveItemViewModel> RestoreCommand { get; }
    /// <summary>
    /// Gets or updates delete forever command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<ArchiveItemViewModel> DeleteForeverCommand { get; }
    /// <summary>
    /// Gets or updates refresh command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>
    /// Performs refresh async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RefreshAsync()
    {
        Items.Clear();
        foreach (var item in await _conversations.GetArchivedAsync(_mode, 500, CancellationToken.None)) Items.Add(ArchiveItemViewModel.ForConversation(item));
        foreach (var item in await _containers.GetArchivedByModeAsync(_mode, 500, CancellationToken.None)) Items.Add(ArchiveItemViewModel.ForContainer(item));
        Status = Items.Count == 0 ? "Archive is empty." : $"{Items.Count} archived item{(Items.Count == 1 ? string.Empty : "s")}.";
    }

    /// <summary>
    /// Performs restore async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RestoreAsync(ArchiveItemViewModel? item)
    {
        if (item?.Conversation is not null) await _conversations.UpsertConversationAsync(item.Conversation with { IsArchived = false, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        if (item?.Container is not null) await _containers.UpsertAsync(item.Container with { IsArchived = false, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        await RefreshAsync();
    }

    /// <summary>
    /// Performs delete forever async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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
    /// <summary>
    /// Stores is delete confirming locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title { get; }
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public string Kind { get; }
    /// <summary>
    /// Gets or updates updated, the bindable or domain state represented by this property.
    /// </summary>
    public string Updated { get; }
    /// <summary>
    /// Gets or updates conversation, the bindable or domain state represented by this property.
    /// </summary>
    public Conversation? Conversation { get; }
    /// <summary>
    /// Gets or updates container, the bindable or domain state represented by this property.
    /// </summary>
    public ContainerDefinition? Container { get; }
    /// <summary>
    /// Reports whether is delete confirming is true for the current state.
    /// </summary>
    public bool IsDeleteConfirming { get => _isDeleteConfirming; set { if (SetProperty(ref _isDeleteConfirming, value)) RaisePropertyChanged(nameof(IsNormal)); } }
    /// <summary>
    /// Reports whether is normal is true for the current state.
    /// </summary>
    public bool IsNormal => !IsDeleteConfirming;
    /// <summary>
    /// Gets or updates begin delete command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand BeginDeleteCommand { get; }
    /// <summary>
    /// Reports whether cancel delete command is true for the current state.
    /// </summary>
    public RelayCommand CancelDeleteCommand { get; }
    /// <summary>
    /// Performs the for conversation step owned by this component.
    /// </summary>
    public static ArchiveItemViewModel ForConversation(Conversation item) => new(item.Title, "Conversation", item.UpdatedAt, item, null);
    /// <summary>
    /// Performs the for container step owned by this component.
    /// </summary>
    public static ArchiveItemViewModel ForContainer(ContainerDefinition item) => new(item.Name, item.Mode == HavenMode.Studio ? "Project" : "Group", item.UpdatedAt, null, item);
}
