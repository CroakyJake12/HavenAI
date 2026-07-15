using System.Collections.ObjectModel;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

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

    public string Title => _mode == HavenMode.Studio ? "Studio Home" : "Do Home";
    public string Subtitle => _mode == HavenMode.Studio
        ? "Projects, live state, active Scheduled Actions, and the next useful step."
        : "Task Groups, click-to-run macros, Scheduled Actions, and recent work.";
    public string CollectionHeading => _mode == HavenMode.Studio ? "Projects" : "Task Groups";
    public string CreateLabel => _mode == HavenMode.Studio ? "New project" : "New Task Group";
    public bool IsStudio => _mode == HavenMode.Studio;
    public bool IsDo => _mode == HavenMode.Do;
    public bool HasAutomations => ActiveAutomations.Count > 0;
    public bool HasItems => Items.Count > 0;
    public bool HasMacros => Macros.Count > 0;
    public ObservableCollection<WorkspaceHomeCardViewModel> Items { get; } = [];
    public ObservableCollection<AutomationSummaryViewModel> ActiveAutomations { get; } = [];
    public ObservableCollection<MacroSummaryViewModel> Macros { get; } = [];
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

        Macros.Clear();
        if (_mode == HavenMode.Do)
            foreach (var macro in (await _workspaceState.GetMacrosAsync(null, CancellationToken.None)).Take(12))
                Macros.Add(new MacroSummaryViewModel(macro.Name, macro.Description));
        RaisePropertyChanged(nameof(HasAutomations));
        RaisePropertyChanged(nameof(HasItems));
        RaisePropertyChanged(nameof(HasMacros));
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
    public string Accent => definition.Mode == HavenMode.Studio ? "STUDIO" : "DO";
}

public sealed record AutomationSummaryViewModel(string Name, string Instruction, string NextRun);
public sealed record MacroSummaryViewModel(string Name, string Description);

public enum StudioCreationKind { None, Mode, Plugin, Agent, Prompt }

public sealed class StudioProjectPageViewModel : ObservableObject
{
    private readonly ContainerDefinition _project;
    private readonly IConversationRepository _conversations;
    private readonly IContainerRepository _containers;
    private readonly IAutomationRepository _automations;
    private readonly IWorkspaceStateRepository _workspaceState;
    private readonly IProjectIntelligenceService _intelligence;
    private readonly Func<WorkspaceFileItemViewModel, Task> _openFile;
    private readonly Func<string, Task> _startChat;
    private readonly IModeRegistry? _modeRegistry;
    private readonly ICatalogRepository? _catalog;
    private readonly IOllamaClient? _ollama;
    private string _status = "Loading project state…";
    private ProjectStateSnapshot? _state;
    private ReleaseRiskReport? _risk;
    private string _intentQuery = string.Empty;
    private string _intentResults = string.Empty;
    private string _bugCommand = string.Empty;
    private bool _bugConfirmed;
    private string _decisionTitle = string.Empty;
    private string _decisionText = string.Empty;
    private string _decisionAlternatives = string.Empty;
    private string _decisionReasoning = string.Empty;
    private string _decisionEvidence = string.Empty;
    private string _decisionConsequences = string.Empty;
    private string _gitRemoteUrl = string.Empty;
    private bool _isInCreateMode;
    private StudioCreationKind _creationKind = StudioCreationKind.None;
    private string _creationName = string.Empty;
    private string _creationDescription = string.Empty;
    private string _creationInstructions = string.Empty;
    private string _creationBuilderPrompt = string.Empty;
    private bool _isInConfigureMode;
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

    public Guid ProjectId => _project.Id;
    public ContainerDefinition Definition => _project;
    public string ProjectName => _project.Name;
    public string RootPath => _project.RootPath ?? string.Empty;
    public bool HasRoot => Directory.Exists(RootPath);
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Branch => _state?.Branch ?? "No Git branch";
    public string WorkState => _state is null ? "Not inspected" : _state.HasUncommittedWork ? "Uncommitted changes" : "Working tree clean";
    public string LastCommit => _state?.LastCommit ?? "No commit found";
    public string LastBuild => _state?.LastBuildResult ?? "Not run";
    public string LatestError => _state?.MostRecentError ?? "No recent error found";
    public string RecommendedAction => _state?.RecommendedAction ?? "Connect a project folder";
    public string LastMeaningfulTask { get; private set; } = "No project conversation yet";
    public string RelevantConversation { get; private set; } = "No relevant conversation";
    public string AdaptiveHelp => _state is null ? "Choose a project folder to enable builds, file editing, intent search, and developer intelligence."
        : _state.HasUncommittedWork ? "Review the changed files and run the Release Risk Forecaster before publishing."
        : "Start from the recommended action, or open a file directly in Haven's editor.";
    public ObservableCollection<WorkspaceFileItemViewModel> Files { get; } = [];
    public ObservableCollection<DecisionItemViewModel> Decisions { get; } = [];
    public ObservableCollection<AutomationSummaryViewModel> ActiveAutomations { get; } = [];
    public IReadOnlyList<ProjectFeatureCardViewModel> Features { get; }
    public string RiskSummary => _risk is null ? "Not forecast yet" : $"{_risk.Level} · {_risk.Score}% risk";
    public string RiskDetails => _risk is null ? "Run before a release or publish operation." : string.Join("\n", _risk.RiskAreas.Concat(_risk.RecommendedTests.Select(item => "Test: " + item)));
    public string IntentQuery { get => _intentQuery; set { if (SetProperty(ref _intentQuery, value)) IntentSearchCommand.RaiseCanExecuteChanged(); } }
    public string IntentResults { get => _intentResults; private set => SetProperty(ref _intentResults, value); }
    public string BugCommand { get => _bugCommand; set { if (SetProperty(ref _bugCommand, value)) BugTimeMachineCommand.RaiseCanExecuteChanged(); } }
    public bool BugConfirmed { get => _bugConfirmed; set { if (SetProperty(ref _bugConfirmed, value)) BugTimeMachineCommand.RaiseCanExecuteChanged(); } }
    public string DecisionTitle { get => _decisionTitle; set { if (SetProperty(ref _decisionTitle, value)) SaveDecisionCommand.RaiseCanExecuteChanged(); } }
    public string DecisionText { get => _decisionText; set { if (SetProperty(ref _decisionText, value)) SaveDecisionCommand.RaiseCanExecuteChanged(); } }
    public string DecisionAlternatives { get => _decisionAlternatives; set => SetProperty(ref _decisionAlternatives, value); }
    public string DecisionReasoning { get => _decisionReasoning; set => SetProperty(ref _decisionReasoning, value); }
    public string DecisionEvidence { get => _decisionEvidence; set => SetProperty(ref _decisionEvidence, value); }
    public string DecisionConsequences { get => _decisionConsequences; set => SetProperty(ref _decisionConsequences, value); }
    public string GitRemoteUrl { get => _gitRemoteUrl; set { if (SetProperty(ref _gitRemoteUrl, value)) ConnectGitCommand.RaiseCanExecuteChanged(); } }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand<WorkspaceFileItemViewModel> OpenFileCommand { get; }
    public AsyncRelayCommand<WorkspaceFileItemViewModel> AskAiAboutFileCommand { get; }
    public RelayCommand<WorkspaceFileItemViewModel> RevealInExplorerCommand { get; }
    public AsyncRelayCommand OpenEditorCommand { get; }
    public AsyncRelayCommand OpenTerminalCommand { get; }
    public AsyncRelayCommand StartServerCommand { get; }
    public AsyncRelayCommand BuildCommand { get; }
    public AsyncRelayCommand TestCommand { get; }
    public AsyncRelayCommand StartChatCommand { get; }
    public AsyncRelayCommand InitializeGitCommand { get; }
    public AsyncRelayCommand ConnectGitCommand { get; }
    public AsyncRelayCommand ForecastRiskCommand { get; }
    public AsyncRelayCommand IntentSearchCommand { get; }
    public AsyncRelayCommand BugTimeMachineCommand { get; }
    public AsyncRelayCommand SaveDecisionCommand { get; }
    public AsyncRelayCommand<DecisionItemViewModel> DeleteDecisionCommand { get; }
    public AsyncRelayCommand<ProjectFeatureCardViewModel> UseFeatureCommand { get; }
    public AsyncRelayCommand ArchiveProjectCommand { get; }
    public RelayCommand SwitchToCreateCommand { get; }
    public RelayCommand SwitchToConfigureCommand { get; }
    public RelayCommand SwitchToOverviewCommand { get; }
    public RelayCommand StartModeCreationCommand { get; }
    public RelayCommand StartPluginCreationCommand { get; }
    public RelayCommand StartAgentCreationCommand { get; }
    public RelayCommand StartPromptCreationCommand { get; }
    public AsyncRelayCommand CreateItemCommand { get; }
    public AsyncRelayCommand BuildWithAiCommand { get; }
    public RelayCommand CancelCreationCommand { get; }
    public bool IsInCreateMode { get => _isInCreateMode; set { if (SetProperty(ref _isInCreateMode, value)) { RaisePropertyChanged(nameof(IsInOverview)); RaisePropertyChanged(nameof(CreationTitle)); RaisePropertyChanged(nameof(CreationHint)); } } }
    public bool IsInConfigureMode { get => _isInConfigureMode; set => SetProperty(ref _isInConfigureMode, value); }
    public bool IsInOverview => !IsInCreateMode && !IsInConfigureMode;
    public StudioCreationKind CreationKind { get => _creationKind; set { if (SetProperty(ref _creationKind, value)) { RaisePropertyChanged(nameof(CreationTitle)); RaisePropertyChanged(nameof(CreationHint)); RaisePropertyChanged(nameof(IsCreatingMode)); RaisePropertyChanged(nameof(IsCreatingPlugin)); RaisePropertyChanged(nameof(IsCreatingAgent)); RaisePropertyChanged(nameof(IsCreatingPrompt)); RaisePropertyChanged(nameof(HasCreationKind)); } } }
    public string CreationTitle => CreationKind switch { StudioCreationKind.Mode => "Create Mode", StudioCreationKind.Plugin => "Create Plugin", StudioCreationKind.Agent => "Create Agent", StudioCreationKind.Prompt => "Create Prompt", _ => "Create" };
    public string CreationHint => CreationKind switch { StudioCreationKind.Mode => "Define a new Haven mode with custom surfaces, tools, and system prompt.", StudioCreationKind.Plugin => "Create a functional plugin with capabilities and constraints.", StudioCreationKind.Agent => "Define a specialised assistant with instructions and model preferences.", StudioCreationKind.Prompt => "Create a reusable instruction prompt.", _ => "Choose what to create." };
    public bool IsCreatingMode => CreationKind == StudioCreationKind.Mode;
    public bool IsCreatingPlugin => CreationKind == StudioCreationKind.Plugin;
    public bool IsCreatingAgent => CreationKind == StudioCreationKind.Agent;
    public bool IsCreatingPrompt => CreationKind == StudioCreationKind.Prompt;
    public bool HasCreationKind => CreationKind != StudioCreationKind.None;
    public string CreationName { get => _creationName; set { if (SetProperty(ref _creationName, value)) CreateItemCommand.RaiseCanExecuteChanged(); } }
    public string CreationDescription { get => _creationDescription; set { if (SetProperty(ref _creationDescription, value)) CreateItemCommand.RaiseCanExecuteChanged(); } }
    public string CreationInstructions { get => _creationInstructions; set => SetProperty(ref _creationInstructions, value); }
    public string CreationBuilderPrompt { get => _creationBuilderPrompt; set { if (SetProperty(ref _creationBuilderPrompt, value)) BuildWithAiCommand.RaiseCanExecuteChanged(); } }
    public string ConfigureStatus { get => _configureStatus; private set => SetProperty(ref _configureStatus, value); }

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

    private async Task ForecastRiskAsync()
    {
        if (!HasRoot) return;
        _risk = await _intelligence.ForecastReleaseRiskAsync(RootPath, CancellationToken.None);
        RaisePropertyChanged(nameof(RiskSummary));
        RaisePropertyChanged(nameof(RiskDetails));
        Status = $"Release risk is {_risk.Level.ToLowerInvariant()} ({_risk.Score}%). Critical findings are surfaced separately and minor cleanup still requires approval.";
    }

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

    private async Task IntentSearchAsync()
    {
        if (!HasRoot) return;
        IntentResults = await _intelligence.FindIntentMatchesAsync(RootPath, IntentQuery, CancellationToken.None);
    }

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

    private async Task SaveDecisionAsync()
    {
        var now = DateTimeOffset.UtcNow;
        await _workspaceState.UpsertDecisionAsync(new DecisionRecord(Guid.NewGuid(), ProjectId, DecisionTitle.Trim(), DecisionText.Trim(),
            DecisionAlternatives.Trim(), DecisionReasoning.Trim(), DecisionEvidence.Trim(), DecisionConsequences.Trim(), now, now), CancellationToken.None);
        DecisionTitle = DecisionText = DecisionAlternatives = DecisionReasoning = DecisionEvidence = DecisionConsequences = string.Empty;
        await RefreshDecisionsAsync();
        Status = "Decision saved with its alternatives, evidence, and consequences. Haven will warn before contradicting it.";
    }

    private async Task DeleteDecisionAsync(DecisionItemViewModel? item)
    {
        if (item is null) return;
        await _workspaceState.DeleteDecisionAsync(item.Definition.Id, CancellationToken.None);
        await RefreshDecisionsAsync();
    }

    private async Task RefreshDecisionsAsync()
    {
        Decisions.Clear();
        foreach (var item in await _workspaceState.GetDecisionsAsync(ProjectId, CancellationToken.None)) Decisions.Add(new(item));
    }

    private async Task ArchiveProjectAsync()
    {
        await _containers.UpsertAsync(_project with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        Status = "Project archived. Restore it from Archive when needed.";
    }

    private async Task WithRoot(Func<string, CancellationToken, Task> action)
    {
        if (!HasRoot) { Status = "Connect a project folder first."; return; }
        try { await action(RootPath, CancellationToken.None); Status = "Launch requested."; }
        catch (Exception ex) { Status = $"Could not launch: {ex.Message}"; }
    }

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

    private static string Tail(string value, int length) => string.IsNullOrWhiteSpace(value) ? "No diagnostic output." : value.Length <= length ? value.Trim() : value[^length..].Trim();
}

public sealed record ProjectFeatureCardViewModel(string Name, string Description, string Prompt);
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

public sealed class WorkspaceFileItemViewModel(string root, string relativePath)
{
    public string Root => root;
    public string RelativePath => relativePath.Replace(Path.DirectorySeparatorChar, '/');
    public string Name => Path.GetFileName(relativePath);
    public string Folder => Path.GetDirectoryName(relativePath)?.Replace(Path.DirectorySeparatorChar, '/') ?? string.Empty;
    public string FullPath => Path.GetFullPath(Path.Combine(root, relativePath));
    public override string ToString() => RelativePath;
}

public sealed class WorkspaceEditorPageViewModel : ObservableObject, IDisposable
{
    private readonly ContainerDefinition _container;
    private readonly Guid? _conversationId;
    private readonly IWorkspaceToolService _tools;
    private readonly IWorkspaceStateRepository _history;
    private readonly IConversationRepository _conversations;
    private readonly Func<Task> _branch;
    private readonly Action _interrupt;
    private readonly Stack<WorkspaceVersion> _undo = new();
    private readonly Stack<WorkspaceVersion> _redo = new();
    private FileSystemWatcher? _watcher;
    private WorkspaceFileItemViewModel _file;
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

    public ContainerDefinition Container => _container;

    public string Title => _file.Name;
    public string RelativePath => _file.RelativePath;
    public string ProjectName => _container.Name;
    public string Content { get => _content; set { if (!SetProperty(ref _content, value)) return; RaiseDirtyProperties(); } }
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

    public void Dispose() => _watcher?.Dispose();
}

public sealed class WorkspaceVersionItemViewModel(WorkspaceVersion definition)
{
    public WorkspaceVersion Definition => definition;
    public string Label => $"{definition.CreatedAt.LocalDateTime:g} · {definition.Kind}";
    public string Summary => definition.Summary;
    public string Changes => $"+{definition.LinesAdded}/-{definition.LinesRemoved}";
    public override string ToString() => Label;
}

public sealed record EditorCommentViewModel(Guid Id, string Selection, string Prompt, DateTimeOffset CreatedAt)
{
    public string Time => CreatedAt.ToString("t");
}

public sealed class MacrosPageViewModel : ObservableObject
{
    private readonly IWorkspaceStateRepository _repository;
    private readonly Guid? _containerId;
    private readonly Func<string, Task> _invoke;
    private string _name = string.Empty;
    private string _description = string.Empty;
    private string _instruction = string.Empty;
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

    public ObservableCollection<MacroItemViewModel> Items { get; } = [];
    public string Name { get => _name; set { if (SetProperty(ref _name, value)) CreateCommand.RaiseCanExecuteChanged(); } }
    public string Description { get => _description; set => SetProperty(ref _description, value); }
    public string Instruction { get => _instruction; set { if (SetProperty(ref _instruction, value)) CreateCommand.RaiseCanExecuteChanged(); } }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public AsyncRelayCommand CreateCommand { get; }
    public AsyncRelayCommand<MacroItemViewModel> InvokeCommand { get; }
    public AsyncRelayCommand<MacroItemViewModel> DeleteCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    private async Task RefreshAsync()
    {
        Items.Clear();
        foreach (var item in await _repository.GetMacrosAsync(_containerId, CancellationToken.None)) Items.Add(new(item));
    }

    private async Task CreateAsync()
    {
        var now = DateTimeOffset.UtcNow;
        await _repository.UpsertMacroAsync(new MacroDefinition(Guid.NewGuid(), Name.Trim(), Description.Trim(), Instruction.Trim(), _containerId, true, now, now), CancellationToken.None);
        Name = Description = Instruction = string.Empty;
        await RefreshAsync();
        Status = "Macro created. It remains inert until clicked.";
    }

    private async Task DeleteAsync(MacroItemViewModel? item)
    {
        if (item is null) return;
        await _repository.DeleteMacroAsync(item.Definition.Id, CancellationToken.None);
        await RefreshAsync();
    }
}

public sealed class MacroItemViewModel(MacroDefinition definition)
{
    public MacroDefinition Definition => definition;
    public string Name => definition.Name;
    public string Description => definition.Description;
    public string Instruction => definition.Instruction;
}

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
        foreach (var item in await _containers.GetArchivedByModeAsync(_mode, CancellationToken.None)) Items.Add(ArchiveItemViewModel.ForContainer(item));
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
