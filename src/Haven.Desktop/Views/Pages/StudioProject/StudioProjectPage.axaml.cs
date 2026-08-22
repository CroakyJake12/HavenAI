using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Pages.StudioProject;

public sealed partial class StudioProjectPage : UserControl, INotifyPropertyChanged
{
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add { _propertyChanged += value; }
        remove { _propertyChanged -= value; }
    }
    private PropertyChangedEventHandler? _propertyChanged;

    private ContainerDefinition _project;
    private readonly IConversationRepository _conversations;
    private readonly IContainerRepository _containers;
    private readonly IAutomationRepository _automations;
    private readonly IWorkspaceStateRepository _workspaceState;
    private readonly IProjectIntelligenceService _intelligence;
    private readonly Func<WorkspaceFileItemViewModel, Task> _openFile;
    private readonly Func<string, Task> _startChat;
    private readonly Func<string, Task> _openTerminal;
    private readonly IModeRegistry? _modeRegistry;
    private readonly ICatalogRepository? _catalog;
    private readonly ICapabilityRepository? _capabilityRepository;
    private readonly IOllamaClient? _ollama;
    private readonly Func<Task> _backToProjects;
    private readonly Func<Conversation, Task> _openConversation;
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
    private string _projectNameDraft = string.Empty;
    private string _projectContextDraft = string.Empty;

    public StudioProjectPage(
        ContainerDefinition project,
        IConversationRepository conversations,
        IContainerRepository containers,
        IAutomationRepository automations,
        IWorkspaceStateRepository workspaceState,
        IProjectIntelligenceService intelligence,
        Func<WorkspaceFileItemViewModel, Task> openFile,
        Func<string, Task> startChat,
        IModeRegistry? modeRegistry = null,
        ICatalogRepository? catalog = null,
        IOllamaClient? ollama = null,
        Func<Task>? backToProjects = null,
        Func<Conversation, Task>? openConversation = null,
        Func<string, Task>? openTerminal = null)
    {
        _project = project;
        _conversations = conversations;
        _containers = containers;
        _automations = automations;
        _workspaceState = workspaceState;
        _intelligence = intelligence;
        _openFile = openFile;
        _startChat = startChat;
        _openTerminal = openTerminal ?? (root => _intelligence.LaunchTerminalAsync(root, CancellationToken.None));
        _modeRegistry = modeRegistry;
        _catalog = catalog;
        _capabilityRepository = App.Services?.GetService<ICapabilityRepository>();
        _ollama = ollama;
        _backToProjects = backToProjects ?? (() => Task.CompletedTask);
        _openConversation = openConversation ?? (_ => Task.CompletedTask);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenFileCommand = new AsyncRelayCommand<WorkspaceFileItemViewModel>(item => item is null ? Task.CompletedTask : _openFile(item));
        AskAiAboutFileCommand = new AsyncRelayCommand<WorkspaceFileItemViewModel>(item => item is null ? Task.CompletedTask : _startChat($"Analyze this file and explain what it does: {item.RelativePath}"));
        RevealInExplorerCommand = new RelayCommand<WorkspaceFileItemViewModel>(item =>
        {
            if (item is null) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{item.FullPath}\"") { UseShellExecute = true });
        });
        OpenEditorCommand = new AsyncRelayCommand(() => WithRoot(_intelligence.LaunchEditorAsync));
        OpenTerminalCommand = new AsyncRelayCommand(async () =>
        {
            if (!HasRoot) { Status = "Connect a project folder first."; return; }
            await _openTerminal(RootPath);
            Status = "Opened Haven Terminal at the project root.";
        });
        StartServerCommand = new AsyncRelayCommand(() => WithRoot(_intelligence.LaunchLocalServerAsync));
        BuildCommand = new AsyncRelayCommand(BuildAsync);
        TestCommand = new AsyncRelayCommand(TestAsync);
        StartChatCommand = new AsyncRelayCommand(() => _startChat(string.Empty));
        StartChatWithPromptCommand = new AsyncRelayCommand<string>(prompt =>
            _startChat(prompt ?? string.Empty));
        BackToProjectsCommand = new AsyncRelayCommand(_backToProjects);
        OpenConversationCommand = new AsyncRelayCommand<Conversation>(conversation =>
            conversation is null ? Task.CompletedTask : _openConversation(conversation));
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
        SwitchToConfigureCommand = new RelayCommand(OpenProjectSettings);
        SwitchToOverviewCommand = new RelayCommand(() => { IsInCreateMode = false; IsInConfigureMode = false; CreationKind = StudioCreationKind.None; });
        StartModeCreationCommand = new RelayCommand(() => { CreationKind = StudioCreationKind.Mode; IsInCreateMode = true; });
        StartCapabilityCreationCommand = new RelayCommand(() => { CreationKind = StudioCreationKind.Capability; IsInCreateMode = true; });
        StartAgentCreationCommand = new RelayCommand(() => { CreationKind = StudioCreationKind.Agent; IsInCreateMode = true; });
        StartPromptCreationCommand = new RelayCommand(() => { CreationKind = StudioCreationKind.Prompt; IsInCreateMode = true; });
        CreateItemCommand = new AsyncRelayCommand(CreateItemAsync, () => !string.IsNullOrWhiteSpace(CreationName) && !string.IsNullOrWhiteSpace(CreationDescription));
        BuildWithAiCommand = new AsyncRelayCommand(BuildWithAiAsync, () => !string.IsNullOrWhiteSpace(CreationBuilderPrompt));
        CancelCreationCommand = new RelayCommand(() => { CreationKind = StudioCreationKind.None; IsInCreateMode = false; CreationName = CreationDescription = CreationInstructions = CreationBuilderPrompt = string.Empty; });
        SaveProjectSettingsCommand = new AsyncRelayCommand(SaveProjectSettingsAsync, () => !string.IsNullOrWhiteSpace(ProjectNameDraft));
        CancelProjectSettingsCommand = new RelayCommand(() =>
        {
            ProjectNameDraft = _project.Name;
            ProjectContextDraft = _project.Context;
            IsInConfigureMode = false;
        });
        GenerateProjectContextCommand = new AsyncRelayCommand(GenerateProjectContextAsync);
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
        InitializeComponent();
        InnerView.DataContext = this;
        _ = RefreshAsync();
    }

    public Guid ProjectId => _project.Id;
    public ContainerDefinition Definition => _project;
    public string ProjectName => _project.Name;
    public string ProjectContext => _project.Context;
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
    public ObservableCollection<Conversation> ProjectConversations { get; } = [];
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
    public AsyncRelayCommand<string> StartChatWithPromptCommand { get; }
    public AsyncRelayCommand BackToProjectsCommand { get; }
    public AsyncRelayCommand<Conversation> OpenConversationCommand { get; }
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
    public RelayCommand StartCapabilityCreationCommand { get; }
    public RelayCommand StartAgentCreationCommand { get; }
    public RelayCommand StartPromptCreationCommand { get; }
    public AsyncRelayCommand CreateItemCommand { get; }
    public AsyncRelayCommand BuildWithAiCommand { get; }
    public RelayCommand CancelCreationCommand { get; }
    public bool IsInCreateMode { get => _isInCreateMode; set { if (SetProperty(ref _isInCreateMode, value)) { RaisePropertyChanged(nameof(IsInOverview)); RaisePropertyChanged(nameof(CreationTitle)); RaisePropertyChanged(nameof(CreationHint)); } } }
    public bool IsInConfigureMode { get => _isInConfigureMode; set => SetProperty(ref _isInConfigureMode, value); }
    public bool IsInOverview => !IsInCreateMode && !IsInConfigureMode;
    public StudioCreationKind CreationKind { get => _creationKind; set { if (SetProperty(ref _creationKind, value)) { RaisePropertyChanged(nameof(CreationTitle)); RaisePropertyChanged(nameof(CreationHint)); RaisePropertyChanged(nameof(IsCreatingMode)); RaisePropertyChanged(nameof(IsCreatingCapability)); RaisePropertyChanged(nameof(IsCreatingAgent)); RaisePropertyChanged(nameof(IsCreatingPrompt)); RaisePropertyChanged(nameof(HasCreationKind)); } } }
    public string CreationTitle => CreationKind switch { StudioCreationKind.Mode => "Create Mode", StudioCreationKind.Capability => "Create Capability", StudioCreationKind.Agent => "Create Agent", StudioCreationKind.Prompt => "Create Prompt", _ => "Create" };
    public string CreationHint => CreationKind switch { StudioCreationKind.Mode => "Define a new Haven mode with custom surfaces, tools, and system prompt.", StudioCreationKind.Capability => "Create a capability draft with instructions, constraints, and an implementation binding requirement.", StudioCreationKind.Agent => "Define a specialised assistant with instructions and model preferences.", StudioCreationKind.Prompt => "Create a reusable instruction prompt.", _ => "Choose what to create." };
    public bool IsCreatingMode => CreationKind == StudioCreationKind.Mode;
    public bool IsCreatingCapability => CreationKind == StudioCreationKind.Capability;
    public bool IsCreatingAgent => CreationKind == StudioCreationKind.Agent;
    public bool IsCreatingPrompt => CreationKind == StudioCreationKind.Prompt;
    public bool HasCreationKind => CreationKind != StudioCreationKind.None;
    public string CreationName { get => _creationName; set { if (SetProperty(ref _creationName, value)) CreateItemCommand.RaiseCanExecuteChanged(); } }
    public string CreationDescription { get => _creationDescription; set { if (SetProperty(ref _creationDescription, value)) CreateItemCommand.RaiseCanExecuteChanged(); } }
    public string CreationInstructions { get => _creationInstructions; set => SetProperty(ref _creationInstructions, value); }
    public string CreationBuilderPrompt { get => _creationBuilderPrompt; set { if (SetProperty(ref _creationBuilderPrompt, value)) BuildWithAiCommand.RaiseCanExecuteChanged(); } }
    public string ConfigureStatus { get => _configureStatus; private set => SetProperty(ref _configureStatus, value); }
    public string ProjectNameDraft
    {
        get => _projectNameDraft;
        set
        {
            if (SetProperty(ref _projectNameDraft, value))
                SaveProjectSettingsCommand.RaiseCanExecuteChanged();
        }
    }
    public string ProjectContextDraft { get => _projectContextDraft; set => SetProperty(ref _projectContextDraft, value); }
    public AsyncRelayCommand SaveProjectSettingsCommand { get; }
    public RelayCommand CancelProjectSettingsCommand { get; }
    public AsyncRelayCommand GenerateProjectContextCommand { get; }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private async Task RefreshAsync()
    {
        if (!HasRoot)
        {
            Status = "This project has no accessible folder. Open Project settings to connect one.";
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        Status = "Loading project state…";

        try
        {
            var state = await _intelligence.GetStateAsync(RootPath, token);
            var recent = (await _conversations.GetRecentAsync(HavenMode.Studio, 300, token))
                .Where(item => item.ContainerId == ProjectId)
                .OrderByDescending(item => item.UpdatedAt)
                .ToArray();
            var files = EnumerateSupportedFiles(RootPath, 2500, token).ToArray();
            var decisions = (await _workspaceState.GetDecisionsAsync(ProjectId, token))
                .Select(item => new DecisionItemViewModel(item))
                .ToArray();
            var automations = (await _automations.GetAllAsync(token))
                .Where(item => item.IsEnabled && item.ContainerId == ProjectId)
                .Select(item => new AutomationSummaryViewModel(
                    item.Name,
                    item.Instruction,
                    item.NextRunAt?.LocalDateTime.ToString("g") ?? "Waiting for trigger"))
                .ToArray();

            token.ThrowIfCancellationRequested();

            _state = state;
            RaiseStateProperties();

            ProjectConversations.Clear();
            foreach (var conversation in recent)
                ProjectConversations.Add(conversation);
            LastMeaningfulTask = recent.FirstOrDefault()?.Title ?? "No project conversation yet";
            RelevantConversation = recent.FirstOrDefault()?.Title ?? "Start a project chat";
            RaisePropertyChanged(nameof(LastMeaningfulTask));
            RaisePropertyChanged(nameof(RelevantConversation));

            Files.Clear();
            foreach (var file in files)
                Files.Add(file);

            Decisions.Clear();
            foreach (var decision in decisions)
                Decisions.Add(decision);

            ActiveAutomations.Clear();
            foreach (var automation in automations)
                ActiveAutomations.Add(automation);

            Status = $"Project state captured at {state.CapturedAt.LocalDateTime:t}.";
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            Status = "Project refresh timed out. Try Refresh again or check the project folder.";
        }
        catch (Exception ex)
        {
            Status = "Project refresh failed: " + ex.Message;
        }
    }

    private void OpenProjectSettings()
    {
        IsInCreateMode = false;
        ProjectNameDraft = _project.Name;
        ProjectContextDraft = _project.Context;
        ConfigureStatus = string.Empty;
        IsInConfigureMode = true;
    }

    private async Task SaveProjectSettingsAsync()
    {
        var name = ProjectNameDraft.Trim();
        if (name.Length == 0)
        {
            ConfigureStatus = "Project name is required.";
            return;
        }

        _project = _project with
        {
            Name = name,
            Context = ProjectContextDraft.Trim(),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _containers.UpsertAsync(_project, CancellationToken.None);
        RaisePropertyChanged(nameof(ProjectName));
        RaisePropertyChanged(nameof(ProjectContext));
        ConfigureStatus = "Project settings saved.";
        IsInConfigureMode = false;
    }

    private async Task GenerateProjectContextAsync()
    {
        if (_ollama is null)
        {
            ConfigureStatus = "A local model provider is not available.";
            return;
        }

        ConfigureStatus = "Generating project context from project chats…";
        try
        {
            var conversations = (await _conversations.GetRecentAsync(HavenMode.Studio, 80, CancellationToken.None))
                .Where(item => item.ContainerId == ProjectId && !item.IsArchived)
                .OrderByDescending(item => item.UpdatedAt)
                .Take(12)
                .ToArray();
            if (conversations.Length == 0)
            {
                ConfigureStatus = "There are no project chats to summarise yet.";
                return;
            }

            var transcript = new System.Text.StringBuilder();
            foreach (var conversation in conversations)
            {
                transcript.AppendLine("Conversation: " + conversation.Title);
                foreach (var message in await _conversations.GetMessagesAsync(conversation.Id, CancellationToken.None))
                {
                    if (transcript.Length >= 36_000) break;
                    transcript.Append(message.Role).Append(": ").AppendLine(message.Content);
                }
                if (transcript.Length >= 36_000) break;
            }

            var models = await _ollama.GetModelsAsync(CancellationToken.None);
            var model = models.FirstOrDefault(item => item.Supports(ToolCapability.Text)) ?? models.FirstOrDefault();
            if (model is null)
            {
                ConfigureStatus = "No compatible local text model is installed.";
                return;
            }

            var result = await _ollama.CompleteAsync(new OllamaChatRequest(
                model.Name,
                [new OllamaMessage(
                    "user",
                    "Create concise durable project context from these chats. Preserve the purpose, requirements, architecture, decisions, constraints, verification commands, and unresolved work. Do not invent anything. Return plain text with short headings.\n\n" + transcript)],
                EffortLevel.Medium), CancellationToken.None);
            ProjectContextDraft = result.Trim();
            ConfigureStatus = "Context draft generated. Review it before saving.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ConfigureStatus = "Project context could not be generated: " + ex.Message;
        }
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
                            "puzzle", HavenMode.Tasks, "[\"Tasks\"]", "[]", "[]", "[]", CreationInstructions.Trim(),
                            ModeSource.Created, ModeInstallState.InstalledByUser, "User", "1.0.0", "[]", now, now);
                        await _modeRegistry.UpsertModeAsync(mode, CancellationToken.None);
                        await _modeRegistry.AddVersionAsync(new ModeVersion(Guid.NewGuid(), mode.Id, 1, 0, 0, "{}", "Initial version", now), CancellationToken.None);
                        Status = $"App '{CreationName}' created. It is available in the App Library.";
                    }
                    break;
                case StudioCreationKind.Capability:
                    if (_capabilityRepository is not null)
                    {
                        await _capabilityRepository!.UpsertCapabilityAsync(new CapabilityDefinition(Guid.NewGuid(), CreationName.Trim().ToLowerInvariant().Replace(" ", "-"), CreationName.Trim(), CreationDescription.Trim(),
                            CapabilityRegistryCatalog.GeneralOwner, "capability-custom", CreationInstructions.Trim(), "user-defined", "[]", CapabilityPlatform.All, CapabilityRiskClass.Low, CapabilityAvailability.DependencyRequired, "[\"implementation binding\"]", "studio", false, false, false, true, now), CancellationToken.None);
                        Status = $"Capability '{CreationName}' draft created. Bind an implementation before it can be attached or used by an agent.";
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
            var kind = CreationKind switch { StudioCreationKind.Mode => "mode", StudioCreationKind.Capability => "capability", StudioCreationKind.Agent => "agent", _ => "prompt" };
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

    private void OnLoaded(object? sender, RoutedEventArgs e) { }
}

public enum StudioCreationKind { None, Mode, Capability, Agent, Prompt }
