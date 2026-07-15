using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Automations;
using Haven.Browser;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public enum CatalogPageKind { Agents, Plugins, Prompts }

public sealed class CatalogPageViewModel : ObservableObject
{
    private readonly ICatalogRepository _catalog;
    private readonly IOllamaClient _ollama;
    private readonly bool _allowStudioCreators;
    private bool _loaded;
    private bool _isCreating;
    private string _builderPrompt = string.Empty;
    private string _newName = string.Empty;
    private string _newDescription = string.Empty;
    private string _newInstructions = string.Empty;
    private string _newModel = string.Empty;
    private bool _newPersists = true;
    private string _status = string.Empty;

    public CatalogPageViewModel(CatalogPageKind kind, ICatalogRepository catalog, IOllamaClient ollama, bool allowStudioCreators = false)
    {
        Kind = kind;
        _catalog = catalog;
        _ollama = ollama;
        _allowStudioCreators = allowStudioCreators;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ToggleCreateCommand = new RelayCommand(() => IsCreating = !IsCreating);
        CreateCommand = new AsyncRelayCommand(CreateAsync, CanCreate);
        BuildWithAiCommand = new AsyncRelayCommand(BuildWithAiAsync, () => !string.IsNullOrWhiteSpace(BuilderPrompt));
        DuplicateCommand = new AsyncRelayCommand<CatalogCardViewModel>(DuplicateAsync);
        DeleteCommand = new AsyncRelayCommand<CatalogCardViewModel>(DeleteAsync);
        _ = RefreshAsync();
    }

    public CatalogPageKind Kind { get; }
    public string Title => Kind switch { CatalogPageKind.Agents => "Agents", CatalogPageKind.Plugins => "Plugins", _ => "Prompt Library" };
    public string Subtitle => Kind switch
    {
        CatalogPageKind.Agents => "Choose specialised local assistants and model preferences.",
        CatalogPageKind.Plugins => "Functional, capability-backed tools invoked with @.",
        _ => "Reusable built-in and custom instruction prompts invoked with >."
    };
    public string CreateLabel => Kind switch { CatalogPageKind.Agents => "Create agent", CatalogPageKind.Plugins => "Create plugin", _ => "Create prompt" };
    public string BuilderTitle => Kind switch { CatalogPageKind.Agents => "AGENT CREATOR", CatalogPageKind.Plugins => "PLUGIN CREATOR", _ => "PROMPT CREATOR" };
    public string BuilderHint => Kind switch { CatalogPageKind.Agents => "Describe the assistant you want Haven to create", CatalogPageKind.Plugins => "Describe the functional capability and constraints", _ => "Describe the reusable prompting behaviour" };
    public bool IsAgentCatalog => Kind == CatalogPageKind.Agents;
    public bool IsPluginCatalog => Kind == CatalogPageKind.Plugins;
    public bool IsPromptCatalog => Kind == CatalogPageKind.Prompts;
    public bool CanCreateItems => IsPromptCatalog || _allowStudioCreators;
    public bool CanUploadPlugin => IsPluginCatalog && _allowStudioCreators;
    public ObservableCollection<CatalogCardViewModel> Items { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand ToggleCreateCommand { get; }
    public AsyncRelayCommand CreateCommand { get; }
    public AsyncRelayCommand BuildWithAiCommand { get; }
    public AsyncRelayCommand<CatalogCardViewModel> DuplicateCommand { get; }
    public AsyncRelayCommand<CatalogCardViewModel> DeleteCommand { get; }
    public bool IsLoaded { get => _loaded; private set => SetProperty(ref _loaded, value); }
    public bool IsCreating { get => _isCreating; set { if (SetProperty(ref _isCreating, value)) RaisePropertyChanged(nameof(IsNotCreating)); } }
    public bool IsNotCreating => !IsCreating;
    public string BuilderPrompt { get => _builderPrompt; set { if (SetProperty(ref _builderPrompt, value)) BuildWithAiCommand.RaiseCanExecuteChanged(); } }
    public string NewName { get => _newName; set { if (SetProperty(ref _newName, value)) CreateCommand.RaiseCanExecuteChanged(); } }
    public string NewDescription { get => _newDescription; set { if (SetProperty(ref _newDescription, value)) CreateCommand.RaiseCanExecuteChanged(); } }
    public string NewInstructions { get => _newInstructions; set { if (SetProperty(ref _newInstructions, value)) CreateCommand.RaiseCanExecuteChanged(); } }
    public string NewModel { get => _newModel; set => SetProperty(ref _newModel, value); }
    public bool NewPersists { get => _newPersists; set => SetProperty(ref _newPersists, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    private async Task RefreshAsync()
    {
        Items.Clear();
        if (Kind == CatalogPageKind.Agents)
            foreach (var item in await _catalog.GetAgentsAsync(CancellationToken.None)) Items.Add(new(item.Id, Kind, item.Name, item.IconKey, item.Description, item.PreferredModel, item.IsEnabled, item.IsBuiltIn));
        else if (Kind == CatalogPageKind.Plugins)
            foreach (var item in await _catalog.GetPluginsAsync(CancellationToken.None)) Items.Add(new(item.Id, Kind, item.Name, item.IconKey, item.Description, item.Persists ? "Persistent" : "One-shot", item.IsEnabled, item.IsBuiltIn));
        else
            foreach (var item in await _catalog.GetPromptsAsync(CancellationToken.None)) Items.Add(new(item.Id, Kind, item.Name, item.IconKey, item.Description, item.Persists ? "Persistent" : "One-shot", item.IsEnabled, item.IsBuiltIn));
        IsLoaded = true;
        Status = $"{Items.Count} {Title.ToLowerInvariant()} available locally.";
    }

    private bool CanCreate() => !string.IsNullOrWhiteSpace(NewName)
                                && !string.IsNullOrWhiteSpace(NewDescription)
                                && !string.IsNullOrWhiteSpace(NewInstructions);

    private async Task CreateAsync()
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (Kind == CatalogPageKind.Agents)
            {
                await _catalog.UpsertAgentAsync(new AgentDefinition(
                    Guid.NewGuid(), NewName.Trim(), NewDescription.Trim(), NewInstructions.Trim(), "agent-custom",
                    string.IsNullOrWhiteSpace(NewModel) ? "default" : NewModel.Trim(), null, BuilderPrompt.Trim(),
                    "{\"mode\":\"ask\"}", false, true, now), CancellationToken.None);
            }
            else if (Kind == CatalogPageKind.Plugins)
            {
                await _catalog.UpsertPluginAsync(new PluginDefinition(
                    Guid.NewGuid(), NewName.Trim(), NewDescription.Trim(), "plugin-custom", NewInstructions.Trim(),
                    "[]", "[]", NewPersists, false, true, now), CancellationToken.None);
            }
            else
            {
                await _catalog.UpsertPromptAsync(new PromptDefinition(Guid.NewGuid(), NewName.Trim(), NewDescription.Trim(), "prompt-custom",
                    NewInstructions.Trim(), NewPersists, false, true, now), CancellationToken.None);
            }

            var createdName = NewName.Trim();
            NewName = string.Empty;
            NewDescription = string.Empty;
            NewInstructions = string.Empty;
            BuilderPrompt = string.Empty;
            IsCreating = false;
            await RefreshAsync();
            Status = $"Created {createdName}. It is ready to use in chat.";
        }
        catch (Exception ex)
        {
            Status = $"Could not create item: {ex.Message}";
        }
    }

    private async Task BuildWithAiAsync()
    {
        try
        {
            Status = "Asking a local model to draft the instructions…";
            var models = await _ollama.GetModelsAsync(CancellationToken.None);
            var model = models.FirstOrDefault(item => item.Supports(ToolCapability.Text)) ?? models.FirstOrDefault();
            if (model is null) throw new InvalidOperationException("No local Ollama model is installed.");
            NewModel = string.IsNullOrWhiteSpace(NewModel) ? model.Name : NewModel;
            var kind = Kind switch { CatalogPageKind.Agents => "agent", CatalogPageKind.Plugins => "functional plugin", _ => "prompt" };
            var result = await _ollama.CompleteAsync(new OllamaChatRequest(
                model.Name,
                [new OllamaMessage("user", $"Write concise, production-ready system instructions for a Haven {kind} with this purpose: {BuilderPrompt.Trim()}\nReturn only the instruction text.")],
                EffortLevel.Medium), CancellationToken.None);
            NewInstructions = result.Trim();
            if (string.IsNullOrWhiteSpace(NewDescription)) NewDescription = BuilderPrompt.Trim();
            Status = "Draft ready. Review the fields, add a name, then create it.";
        }
        catch (Exception ex)
        {
            Status = $"AI draft failed: {ex.Message}";
        }
    }

    public async Task ImportPluginAsync(string path)
    {
        if (!CanUploadPlugin) { Status = "Plugin imports are available from Haven Studio."; return; }
        try
        {
            var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
            var manifest = System.Text.Json.JsonSerializer.Deserialize<PluginImportManifest>(await File.ReadAllTextAsync(path), options)
                           ?? throw new InvalidOperationException("Plugin manifest is empty.");
            if (string.IsNullOrWhiteSpace(manifest.Name) || string.IsNullOrWhiteSpace(manifest.Description) || string.IsNullOrWhiteSpace(manifest.Instructions))
                throw new InvalidOperationException("Plugin manifest requires name, description, and instructions.");
            var validCapabilities = manifest.Capabilities.Where(value => Enum.TryParse<ToolCapability>(value, true, out _)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (validCapabilities.Length != manifest.Capabilities.Count) throw new InvalidOperationException("Manifest contains an unknown capability. Haven imports declarative, sandboxed plugins only.");
            DashboardTileManifestPolicy.ValidateForImport(manifest.DashboardTiles);
            var existingPlugin = (await _catalog.GetPluginsAsync(CancellationToken.None))
                .FirstOrDefault(item => item.Name.Equals(manifest.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            var pluginId = existingPlugin?.Id ?? GuidUtility.FromStableName("haven.imported.plugin." + manifest.Name.Trim().ToLowerInvariant());
            await _catalog.UpsertPluginAsync(new PluginDefinition(pluginId, manifest.Name.Trim(), manifest.Description.Trim(),
                string.IsNullOrWhiteSpace(manifest.IconKey) ? "plugin-custom" : manifest.IconKey.Trim(), manifest.Instructions.Trim(),
                System.Text.Json.JsonSerializer.Serialize(validCapabilities), System.Text.Json.JsonSerializer.Serialize(manifest.Conflicts), manifest.Persists,
                false, true, DateTimeOffset.UtcNow, manifest.IsAgentic, System.Text.Json.JsonSerializer.Serialize(manifest.AllowedModes),
                System.Text.Json.JsonSerializer.Serialize(manifest.DashboardTiles)), CancellationToken.None);
            await RefreshAsync();
            Status = $"Imported @{manifest.Name} from a declarative Haven plugin manifest. No executable code was loaded.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            Status = $"Could not import plugin: {ex.Message}";
        }
    }

    private async Task DuplicateAsync(CatalogCardViewModel? item)
    {
        if (item is null || Kind != CatalogPageKind.Agents) return;
        var source = (await _catalog.GetAgentsAsync(CancellationToken.None)).FirstOrDefault(agent => agent.Id == item.Id);
        if (source is null) return;
        var existing = (await _catalog.GetAgentsAsync(CancellationToken.None)).Select(agent => agent.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var baseName = source.Name + " Copy";
        var name = baseName;
        for (var number = 2; existing.Contains(name); number++) name = $"{baseName} {number}";
        await _catalog.UpsertAgentAsync(source with { Id = Guid.NewGuid(), Name = name, IsBuiltIn = false, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        await RefreshAsync();
        Status = $"Duplicated {source.Name} as {name}.";
    }

    private async Task DeleteAsync(CatalogCardViewModel? item)
    {
        if (item is null || item.IsBuiltIn) return;
        if (Kind == CatalogPageKind.Agents) await _catalog.DeleteCustomAgentAsync(item.Id, CancellationToken.None);
        else if (Kind == CatalogPageKind.Plugins) await _catalog.DeleteCustomPluginAsync(item.Id, CancellationToken.None);
        else await _catalog.DeleteCustomPromptAsync(item.Id, CancellationToken.None);
        await RefreshAsync();
        Status = $"Deleted {item.Name}.";
    }
}

public sealed record CatalogCardViewModel(Guid Id, CatalogPageKind Kind, string Name, string IconKey, string Description, string Meta, bool IsEnabled, bool IsBuiltIn)
{
    public bool CanDuplicate => Kind == CatalogPageKind.Agents;
    public bool CanDelete => !IsBuiltIn;
}

public sealed class PluginImportManifest
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Instructions { get; init; } = string.Empty;
    public string IconKey { get; init; } = string.Empty;
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public IReadOnlyList<string> Conflicts { get; init; } = [];
    public IReadOnlyList<string> AllowedModes { get; init; } = [];
    public IReadOnlyList<DashboardPluginTileManifest> DashboardTiles { get; init; } = [];
    public bool Persists { get; init; }
    public bool IsAgentic { get; init; }
}

public sealed class AutomationsPageViewModel : ObservableObject
{
    private readonly IAutomationRepository _repository;
    private readonly WindowsAutomationRegistrationService _registration;
    private readonly AutomationRunner _runner;
    private readonly ScheduleCalculator _schedules;
    private string _status = "Loading…";
    private string _newName = string.Empty;
    private string _newInstruction = string.Empty;
    private HavenMode _newMode = HavenMode.Chat;
    private AutomationScheduleKind _newScheduleKind = AutomationScheduleKind.Daily;
    private string _newScheduleJson = "{\"time\":\"08:00\"}";

    public AutomationsPageViewModel(
        IAutomationRepository repository,
        WindowsAutomationRegistrationService registration,
        AutomationRunner runner,
        ScheduleCalculator schedules)
    {
        _repository = repository;
        _registration = registration;
        _runner = runner;
        _schedules = schedules;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        RegisterWorkerCommand = new AsyncRelayCommand(RegisterWorkerAsync);
        CreateCommand = new AsyncRelayCommand(CreateAsync, () => !string.IsNullOrWhiteSpace(NewName) && !string.IsNullOrWhiteSpace(NewInstruction));
        ToggleCommand = new RelayCommand<AutomationCardViewModel>(item => _ = ToggleAsync(item));
        DeleteCommand = new RelayCommand<AutomationCardViewModel>(item => _ = DeleteAsync(item));
        RunNowCommand = new RelayCommand<AutomationCardViewModel>(item => _ = RunNowAsync(item));
        _ = RefreshAsync();
    }

    public ObservableCollection<AutomationCardViewModel> Items { get; } = [];
    public IReadOnlyList<HavenMode> Modes { get; } = Enum.GetValues<HavenMode>();
    public IReadOnlyList<AutomationScheduleKind> ScheduleKinds { get; } = Enum.GetValues<AutomationScheduleKind>();
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string NewName { get => _newName; set { if (SetProperty(ref _newName, value)) CreateCommand.RaiseCanExecuteChanged(); } }
    public string NewInstruction { get => _newInstruction; set { if (SetProperty(ref _newInstruction, value)) CreateCommand.RaiseCanExecuteChanged(); } }
    public HavenMode NewMode { get => _newMode; set => SetProperty(ref _newMode, value); }
    public AutomationScheduleKind NewScheduleKind { get => _newScheduleKind; set => SetProperty(ref _newScheduleKind, value); }
    public string NewScheduleJson { get => _newScheduleJson; set => SetProperty(ref _newScheduleJson, value); }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand RegisterWorkerCommand { get; }
    public AsyncRelayCommand CreateCommand { get; }
    public RelayCommand<AutomationCardViewModel> ToggleCommand { get; }
    public RelayCommand<AutomationCardViewModel> DeleteCommand { get; }
    public RelayCommand<AutomationCardViewModel> RunNowCommand { get; }

    private async Task RefreshAsync()
    {
        Items.Clear();
        foreach (var item in await _repository.GetAllAsync(CancellationToken.None)) Items.Add(new AutomationCardViewModel(item));
        Status = Items.Count == 0 ? "No automations yet." : $"{Items.Count} automation{(Items.Count == 1 ? "" : "s")}";
    }

    private async Task CreateAsync()
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var next = _schedules.GetInitialRun(NewScheduleKind, NewScheduleJson, now);
            var definition = new AutomationDefinition(Guid.NewGuid(), NewName.Trim(), NewMode, NewInstruction.Trim(), NewScheduleKind, NewScheduleJson, next, null, true, now, now);
            await _repository.UpsertAsync(definition, CancellationToken.None);
            NewName = string.Empty;
            NewInstruction = string.Empty;
            await RefreshAsync();
            Status = "Automation created. Register the background worker once to run it while Haven is closed.";
        }
        catch (Exception ex)
        {
            Status = $"Could not create automation: {ex.Message}";
        }
    }

    private async Task ToggleAsync(AutomationCardViewModel? item)
    {
        if (item is null) return;
        var now = DateTimeOffset.UtcNow;
        var enabled = !item.Definition.IsEnabled;
        var next = enabled ? _schedules.GetNextRun(item.Definition with { IsEnabled = true }, now) : null;
        await _repository.UpsertAsync(item.Definition with { IsEnabled = enabled, NextRunAt = next, UpdatedAt = now }, CancellationToken.None);
        await RefreshAsync();
    }

    private async Task DeleteAsync(AutomationCardViewModel? item)
    {
        if (item is null) return;
        await _repository.DeleteAsync(item.Definition.Id, CancellationToken.None);
        await RefreshAsync();
        Status = "Automation deleted.";
    }

    private async Task RunNowAsync(AutomationCardViewModel? item)
    {
        if (item is null) return;
        var now = DateTimeOffset.UtcNow;
        await _repository.UpsertAsync(item.Definition with { IsEnabled = true, NextRunAt = now, UpdatedAt = now }, CancellationToken.None);
        var result = await _runner.RunDueAsync(now, CancellationToken.None);
        await RefreshAsync();
        Status = $"Run pass: {result.Succeeded} succeeded, {result.Failed} failed, {result.Skipped} skipped.";
    }

    private async Task RegisterWorkerAsync()
    {
        var worker = Path.Combine(AppContext.BaseDirectory, "Haven.AutomationWorker.exe");
        var result = await _registration.RegisterAsync(worker, CancellationToken.None);
        Status = result.Message;
    }
}

public sealed class AutomationCardViewModel(AutomationDefinition definition)
{
    public AutomationDefinition Definition => definition;
    public string Name => definition.Name;
    public string Mode => definition.Mode.ToString();
    public string Instruction => definition.Instruction;
    public string NextRun => definition.NextRunAt?.LocalDateTime.ToString("g") ?? "Not scheduled";
    public bool IsEnabled => definition.IsEnabled;
    public string ToggleLabel => definition.IsEnabled ? "Pause" : "Resume";
    public string StateLabel => definition.IsEnabled ? "Enabled" : "Paused";
}

public sealed class SettingsPageViewModel : ObservableObject
{
    private readonly UserPreferencesService _preferences;
    private readonly IOllamaClient _ollama;
    private readonly Action<string?, EffortLevel> _applied;
    private ModelDescriptor? _selectedModel;
    private EffortLevel _selectedEffort;
    private string _activeThemeId;
    private string _status = "Loading local settings…";
    private string _modelSearch = string.Empty;
    private string _installModelName = string.Empty;
    private double _installProgress;
    private double _temperature;
    private int _contextLimit;
    private int _actionLimit;
    private bool _autoSwitch;
    private bool _showAgenticInChat;
    private bool _verticalTabs;
    private bool _confidenceMeter;
    private bool _autoCompact;
    private int _compactAtPercent;
    private bool _adaptiveHelp;
    private bool _browserSideAssistant;
    private PermissionMode _filePermission;
    private PermissionMode _commandPermission;
    private PermissionMode _browserPermission;
    private PermissionMode _computerPermission;
    private string _themeName = "My theme";
    private string _themeBackground = "#111111";
    private string _themePanel = "#1A1A1A";
    private string _themePanel2 = "#202020";
    private string _themeText = "#F5F5F5";
    private string _themeMuted = "#8A8A8A";
    private string _themeAccent = "#60CDFF";
    private string _themeBlue = "#98EBFF";
    private bool _themeIsLight;
    private string _themeNubColor = "#60CDFF";
    private bool _themeCardBorder;
    private bool _isModelDeleteConfirming;
    private readonly bool _allowThemeCreator;

    public SettingsPageViewModel(UserPreferencesService preferences, IOllamaClient ollama, Action<string?, EffortLevel> applied, bool allowThemeCreator = false)
    {
        _preferences = preferences;
        _ollama = ollama;
        _applied = applied;
        _allowThemeCreator = allowThemeCreator;
        _selectedEffort = preferences.DefaultEffort;
        _activeThemeId = preferences.ThemeId;
        var snapshot = preferences.Snapshot;
        _temperature = snapshot.Temperature;
        _contextLimit = snapshot.ContextLimit;
        _actionLimit = snapshot.ActionLimit;
        _autoSwitch = snapshot.AutoSwitchCompatibleModels;
        _showAgenticInChat = snapshot.ShowAgenticInChat;
        _verticalTabs = snapshot.VerticalTabs;
        _confidenceMeter = snapshot.ConfidenceMeter;
        _autoCompact = snapshot.AutoCompactContext;
        _compactAtPercent = snapshot.CompactAtPercent;
        _adaptiveHelp = snapshot.AdaptiveHelp;
        _browserSideAssistant = snapshot.BrowserSideAssistant;
        _filePermission = snapshot.FilePermission;
        _commandPermission = snapshot.CommandPermission;
        _browserPermission = snapshot.BrowserPermission;
        _computerPermission = snapshot.ComputerPermission;
        ApplyThemeCommand = new RelayCommand<HavenThemePreset>(ApplyTheme);
        SaveModelDefaultsCommand = new RelayCommand(SaveModelDefaults);
        SaveAdvancedCommand = new RelayCommand(SaveAdvanced);
        SaveFeaturesCommand = new RelayCommand(SaveFeatures);
        SavePermissionsCommand = new RelayCommand(SavePermissions);
        SaveCustomThemeCommand = new RelayCommand(SaveCustomTheme);
        RefreshModelsCommand = new AsyncRelayCommand(RefreshModelsAsync);
        InstallModelCommand = new AsyncRelayCommand(InstallModelAsync, () => !string.IsNullOrWhiteSpace(InstallModelName));
        DeleteModelCommand = new AsyncRelayCommand(DeleteModelAsync, () => SelectedModel is not null);
        RequestDeleteModelCommand = new RelayCommand(() => IsModelDeleteConfirming = SelectedModel is not null);
        CancelDeleteModelCommand = new RelayCommand(() => IsModelDeleteConfirming = false);
        SelectModelCommand = new RelayCommand<ModelSettingsItemViewModel>(item => SelectedModel = item?.Definition);
        _ = RefreshModelsAsync();
    }

    public ObservableCollection<HavenThemePreset> Themes { get; } = [];
    public ObservableCollection<ModelDescriptor> Models { get; } = [];
    public ObservableCollection<ModelSettingsItemViewModel> FilteredModels { get; } = [];
    public IReadOnlyList<EffortLevel> EffortLevels { get; } = Enum.GetValues<EffortLevel>();
    public IReadOnlyList<PermissionMode> PermissionModes { get; } = Enum.GetValues<PermissionMode>();
    public bool CanCreateTheme => _allowThemeCreator;
    public ModelDescriptor? SelectedModel { get => _selectedModel; set { if (!SetProperty(ref _selectedModel, value)) return; IsModelDeleteConfirming = false; DeleteModelCommand.RaiseCanExecuteChanged(); } }
    public EffortLevel SelectedEffort { get => _selectedEffort; set => SetProperty(ref _selectedEffort, value); }
    public string ActiveThemeId { get => _activeThemeId; private set => SetProperty(ref _activeThemeId, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string ModelSearch { get => _modelSearch; set { if (SetProperty(ref _modelSearch, value)) FilterModelList(); } }
    public string InstallModelName { get => _installModelName; set { if (SetProperty(ref _installModelName, value)) InstallModelCommand.RaiseCanExecuteChanged(); } }
    public double InstallProgress { get => _installProgress; private set => SetProperty(ref _installProgress, value); }
    public double Temperature { get => _temperature; set => SetProperty(ref _temperature, value); }
    public int ContextLimit { get => _contextLimit; set => SetProperty(ref _contextLimit, value); }
    public int ActionLimit { get => _actionLimit; set => SetProperty(ref _actionLimit, value); }
    public bool AutoSwitch { get => _autoSwitch; set => SetProperty(ref _autoSwitch, value); }
    public bool ShowAgenticInChat { get => _showAgenticInChat; set => SetProperty(ref _showAgenticInChat, value); }
    public bool VerticalTabs { get => _verticalTabs; set => SetProperty(ref _verticalTabs, value); }
    public bool ConfidenceMeter { get => _confidenceMeter; set => SetProperty(ref _confidenceMeter, value); }
    public bool AutoCompact { get => _autoCompact; set => SetProperty(ref _autoCompact, value); }
    public int CompactAtPercent { get => _compactAtPercent; set => SetProperty(ref _compactAtPercent, value); }
    public bool AdaptiveHelp { get => _adaptiveHelp; set => SetProperty(ref _adaptiveHelp, value); }
    public bool BrowserSideAssistant { get => _browserSideAssistant; set => SetProperty(ref _browserSideAssistant, value); }
    public PermissionMode FilePermission { get => _filePermission; set => SetProperty(ref _filePermission, value); }
    public PermissionMode CommandPermission { get => _commandPermission; set => SetProperty(ref _commandPermission, value); }
    public PermissionMode BrowserPermission { get => _browserPermission; set => SetProperty(ref _browserPermission, value); }
    public PermissionMode ComputerPermission { get => _computerPermission; set => SetProperty(ref _computerPermission, value); }
    public string ThemeName { get => _themeName; set => SetProperty(ref _themeName, value); }
    public string ThemeBackground { get => _themeBackground; set => SetProperty(ref _themeBackground, value); }
    public string ThemePanel { get => _themePanel; set => SetProperty(ref _themePanel, value); }
    public string ThemePanel2 { get => _themePanel2; set => SetProperty(ref _themePanel2, value); }
    public string ThemeText { get => _themeText; set => SetProperty(ref _themeText, value); }
    public string ThemeMuted { get => _themeMuted; set => SetProperty(ref _themeMuted, value); }
    public string ThemeAccent { get => _themeAccent; set => SetProperty(ref _themeAccent, value); }
    public string ThemeBlue { get => _themeBlue; set => SetProperty(ref _themeBlue, value); }
    public bool ThemeIsLight { get => _themeIsLight; set => SetProperty(ref _themeIsLight, value); }
    public string ThemeNubColor { get => _themeNubColor; set => SetProperty(ref _themeNubColor, value); }
    public bool ThemeCardBorder { get => _themeCardBorder; set => SetProperty(ref _themeCardBorder, value); }
    public bool IsModelDeleteConfirming { get => _isModelDeleteConfirming; private set => SetProperty(ref _isModelDeleteConfirming, value); }
    public RelayCommand<HavenThemePreset> ApplyThemeCommand { get; }
    public RelayCommand SaveModelDefaultsCommand { get; }
    public RelayCommand SaveAdvancedCommand { get; }
    public RelayCommand SaveFeaturesCommand { get; }
    public RelayCommand SavePermissionsCommand { get; }
    public RelayCommand SaveCustomThemeCommand { get; }
    public AsyncRelayCommand RefreshModelsCommand { get; }
    public AsyncRelayCommand InstallModelCommand { get; }
    public AsyncRelayCommand DeleteModelCommand { get; }
    public RelayCommand RequestDeleteModelCommand { get; }
    public RelayCommand CancelDeleteModelCommand { get; }
    public RelayCommand<ModelSettingsItemViewModel> SelectModelCommand { get; }
    public string DataStatement => "Haven stores chats, agents, plugins, preferences, and automation history in local files and SQLite. Ollama requests stay on the configured local endpoint.";
    public string BrowserStatement => "The embedded browser runs in Haven's native WebView session rather than controlling your normal browser window.";
    public string SafetyStatement => "File tools are confined to the selected project folder. Commands start there, use a bounded timeout, and report their real exit code and output.";

    private void ApplyTheme(HavenThemePreset? theme)
    {
        if (theme is null) return;
        _preferences.ApplyTheme(theme.Id);
        ActiveThemeId = theme.Id;
        Status = $"Applied {theme.Name}.";
    }

    private void SaveModelDefaults()
    {
        _preferences.SetModelDefaults(SelectedModel?.Name, SelectedEffort);
        _applied(SelectedModel?.Name, SelectedEffort);
        Status = "Default model settings saved.";
    }

    private void SaveAdvanced()
    {
        _preferences.SetAdvancedModelOptions(Temperature, ContextLimit, ActionLimit);
        Status = "Advanced generation limits saved.";
    }

    private void SaveFeatures()
    {
        _preferences.SetFeatureOptions(AutoSwitch, ShowAgenticInChat, VerticalTabs, ConfidenceMeter, AutoCompact,
            CompactAtPercent, AdaptiveHelp, BrowserSideAssistant);
        Status = "Feature preferences saved. Reopen a surface to apply layout-only changes.";
    }

    private void SavePermissions()
    {
        _preferences.SetToolPermissions(FilePermission, CommandPermission, BrowserPermission, ComputerPermission);
        Status = "Tool permission defaults saved.";
    }

    private void SaveCustomTheme()
    {
        try
        {
            var saved = _preferences.SaveCustomTheme(new HavenThemePreset(string.Empty, ThemeName, "Custom Haven theme",
                ThemeBackground, ThemePanel, ThemePanel2, ThemeText, ThemeMuted, ThemeAccent, ThemeBlue, ThemeIsLight,
                string.IsNullOrWhiteSpace(ThemeNubColor) ? "#00000000" : ThemeNubColor, ThemeCardBorder));
            RefreshThemes();
            ApplyTheme(saved);
        }
        catch (Exception ex) { Status = $"Theme could not be saved: {ex.Message}"; }
    }

    private async Task InstallModelAsync()
    {
        try
        {
            InstallProgress = 0;
            Status = $"Installing {InstallModelName.Trim()}…";
            var progress = new Progress<double>(value => InstallProgress = Math.Clamp(value, 0, 1));
            await _ollama.PullModelAsync(InstallModelName.Trim(), progress, CancellationToken.None);
            await RefreshModelsAsync();
            Status = $"Installed {InstallModelName.Trim()}.";
        }
        catch (Exception ex) { Status = $"Model installation failed: {ex.Message}"; }
    }

    private async Task DeleteModelAsync()
    {
        if (SelectedModel is null) return;
        try
        {
            var name = SelectedModel.Name;
            await _ollama.DeleteModelAsync(name, CancellationToken.None);
            IsModelDeleteConfirming = false;
            await RefreshModelsAsync();
            Status = $"Deleted {name}.";
        }
        catch (Exception ex) { Status = $"Model deletion failed: {ex.Message}"; }
    }

    private async Task RefreshModelsAsync()
    {
        try
        {
            RefreshThemes();
            Models.Clear();
            foreach (var model in await _ollama.GetModelsAsync(CancellationToken.None)) Models.Add(model);
            SelectedModel = Models.FirstOrDefault(model => model.Name.Equals(_preferences.DefaultModel, StringComparison.OrdinalIgnoreCase)) ?? Models.FirstOrDefault();
            FilterModelList();
            Status = Models.Count == 0 ? "Ollama is available but has no installed models." : $"{Models.Count} local models available.";
        }
        catch (Exception ex)
        {
            Status = $"Ollama unavailable: {ex.Message}";
        }
    }

    private void FilterModelList()
    {
        FilteredModels.Clear();
        foreach (var model in Models.Where(model => string.IsNullOrWhiteSpace(ModelSearch) ||
                     model.Name.Contains(ModelSearch, StringComparison.OrdinalIgnoreCase) || model.Family.Contains(ModelSearch, StringComparison.OrdinalIgnoreCase)))
            FilteredModels.Add(new ModelSettingsItemViewModel(model));
    }

    private void RefreshThemes()
    {
        Themes.Clear();
        foreach (var theme in _preferences.Themes) Themes.Add(theme);
    }
}

public sealed class ModelSettingsItemViewModel(ModelDescriptor definition)
{
    public ModelDescriptor Definition => definition;
    public string Name => definition.Name;
    public string Details => string.Join(" · ", new[] { definition.Family, definition.ParameterSize, definition.Quantization }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public string DownloadSize => FormatBytes(definition.SizeBytes);
    public string EstimatedRam => $"Approx. {FormatBytes((long)(definition.SizeBytes * 1.25))} RAM";
    public string Capabilities => definition.Capabilities.Count == 0 ? "Chat" : string.Join(", ", definition.Capabilities);

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
}

public sealed class ContainerSettingsPageViewModel : ObservableObject
{
    private readonly ContainerItemViewModel _item;
    private readonly IContainerRepository _repository;
    private readonly Func<Task> _saved;
    private string _name;
    private string _rootPath;
    private string _context;
    private string _instructions;
    private string _status = "Changes are stored locally.";
    private bool _isDeleted;
    private bool _isDeleteConfirming;

    public ContainerSettingsPageViewModel(ContainerItemViewModel item, IContainerRepository repository, Func<Task> saved)
    {
        _item = item;
        _repository = repository;
        _saved = saved;
        _name = item.Definition.Name;
        _rootPath = item.Definition.RootPath ?? string.Empty;
        _context = item.Definition.Context;
        _instructions = item.Definition.Instructions;
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsDeleted && !string.IsNullOrWhiteSpace(Name));
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => !IsDeleted);
        ArchiveCommand = new AsyncRelayCommand(ArchiveAsync, () => !IsDeleted);
        RequestDeleteCommand = new RelayCommand(() => IsDeleteConfirming = true);
        CancelDeleteCommand = new RelayCommand(() => IsDeleteConfirming = false);
    }

    public string Eyebrow => _item.Definition.Mode switch { HavenMode.Teach => "SUBJECT SETTINGS", HavenMode.Do => "WORKSPACE SETTINGS", _ => "PROJECT SETTINGS" };
    public string ItemLabel => _item.Definition.Mode switch { HavenMode.Chat => "chat group", HavenMode.Teach => "subject", HavenMode.Do => "task group", _ => "project" };
    public string ArchiveLabel => "Archive " + ItemLabel;
    public string DeleteLabel => "Delete " + ItemLabel;
    public string Name { get => _name; set { if (SetProperty(ref _name, value)) SaveCommand.RaiseCanExecuteChanged(); } }
    public string RootPath { get => _rootPath; set => SetProperty(ref _rootPath, value); }
    public string Context { get => _context; set => SetProperty(ref _context, value); }
    public string Instructions { get => _instructions; set => SetProperty(ref _instructions, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsDeleted { get => _isDeleted; private set { if (!SetProperty(ref _isDeleted, value)) return; SaveCommand.RaiseCanExecuteChanged(); DeleteCommand.RaiseCanExecuteChanged(); ArchiveCommand.RaiseCanExecuteChanged(); } }
    public bool IsDeleteConfirming { get => _isDeleteConfirming; private set => SetProperty(ref _isDeleteConfirming, value); }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public AsyncRelayCommand ArchiveCommand { get; }
    public RelayCommand RequestDeleteCommand { get; }
    public RelayCommand CancelDeleteCommand { get; }

    public void SetRootPath(string path) => RootPath = path;

    private async Task SaveAsync()
    {
        try
        {
            var definition = _item.Definition with
            {
                Name = Name.Trim(),
                RootPath = string.IsNullOrWhiteSpace(RootPath) ? null : Path.GetFullPath(RootPath.Trim()),
                Context = Context.Trim(),
                Instructions = Instructions.Trim(),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _repository.UpsertAsync(definition, CancellationToken.None);
            await _saved();
            Status = "Project settings saved.";
        }
        catch (Exception ex)
        {
            Status = $"Could not save settings: {ex.Message}";
        }
    }

    private async Task DeleteAsync()
    {
        try
        {
            await _repository.DeleteAsync(_item.Id, CancellationToken.None);
            IsDeleted = true;
            await _saved();
            Status = "Project deleted. Its saved conversations remain in history.";
        }
        catch (Exception ex)
        {
            Status = $"Could not delete project: {ex.Message}";
        }
    }

    private async Task ArchiveAsync()
    {
        try
        {
            await _repository.UpsertAsync(_item.Definition with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
            IsDeleted = true;
            await _saved();
            Status = $"{string.Concat(char.ToUpperInvariant(ItemLabel[0]), ItemLabel[1..])} archived. Restore it from Archive when needed.";
        }
        catch (Exception ex) { Status = $"Could not archive {ItemLabel}: {ex.Message}"; }
    }
}

public sealed class LessonSettingsPageViewModel : ObservableObject
{
    private readonly LessonItemViewModel _item;
    private readonly IContainerRepository _repository;
    private readonly Func<Task> _saved;
    private string _name;
    private string _topicGroup;
    private string _structureJson;
    private string _status = "Lesson structure is stored locally with the subject.";

    public LessonSettingsPageViewModel(LessonItemViewModel item, IContainerRepository repository, Func<Task> saved)
    {
        _item = item;
        _repository = repository;
        _saved = saved;
        _name = item.Definition.Name;
        _topicGroup = item.Definition.TopicGroup;
        _structureJson = item.Definition.StructureJson;
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !string.IsNullOrWhiteSpace(Name));
    }

    public string Name { get => _name; set { if (SetProperty(ref _name, value)) SaveCommand.RaiseCanExecuteChanged(); } }
    public string TopicGroup { get => _topicGroup; set => SetProperty(ref _topicGroup, value); }
    public string StructureJson { get => _structureJson; set => SetProperty(ref _structureJson, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public AsyncRelayCommand SaveCommand { get; }

    private async Task SaveAsync()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(StructureJson))
                System.Text.Json.JsonDocument.Parse(StructureJson).Dispose();
            await _repository.UpsertLessonAsync(_item.Definition with
            {
                Name = Name.Trim(),
                TopicGroup = string.IsNullOrWhiteSpace(TopicGroup) ? "General" : TopicGroup.Trim(),
                StructureJson = string.IsNullOrWhiteSpace(StructureJson) ? "{}" : StructureJson.Trim(),
                UpdatedAt = DateTimeOffset.UtcNow
            }, CancellationToken.None);
            await _saved();
            Status = "Lesson settings saved.";
        }
        catch (System.Text.Json.JsonException ex)
        {
            Status = $"Lesson structure is not valid JSON: {ex.Message}";
        }
        catch (Exception ex)
        {
            Status = $"Could not save the lesson: {ex.Message}";
        }
    }
}

public sealed class ModeLibraryPageViewModel : ObservableObject
{
    private readonly IModeRegistry _modes;
    private readonly IModeUsageRepository _usage;
    private readonly IPinRepository _pins;
    private bool _loaded;
    private string _searchQuery = string.Empty;
    private string _status = string.Empty;

    public ModeLibraryPageViewModel(IModeRegistry modes, IModeUsageRepository usage, IPinRepository pins)
    {
        _modes = modes;
        _usage = usage;
        _pins = pins;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        PinCommand = new AsyncRelayCommand<ModeCardViewModel>(PinAsync);
        CreateInStudioCommand = new RelayCommand(() => OpenInStudio?.Invoke());
        _ = RefreshAsync();
    }

    public event Action? OpenInStudio;

    public ObservableCollection<ModeCardViewModel> Items { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand<ModeCardViewModel> PinCommand { get; }
    public RelayCommand CreateInStudioCommand { get; }
    public bool IsLoaded { get => _loaded; private set => SetProperty(ref _loaded, value); }
    public string SearchQuery { get => _searchQuery; set { if (SetProperty(ref _searchQuery, value)) _ = ApplyFilterAsync(); } }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    private IReadOnlyList<ModeCardViewModel> _allItems = [];

    private async Task RefreshAsync()
    {
        var modes = await _modes.GetModesAsync(CancellationToken.None);
        var pins = (await _pins.GetPinsAsync(CancellationToken.None)).ToDictionary(p => p.ModeId);
        var usageData = await _usage.GetRecentUsageAsync(30, CancellationToken.None);
        var usageByMode = usageData.GroupBy(u => u.ModeId).ToDictionary(g => g.Key, g => g.Sum(u => u.TurnCount));

        _allItems = modes.Select(m => new ModeCardViewModel(
            m.Id, m.Key, m.Name, m.Description, m.IconKey, m.BaseMode,
            m.Source, m.InstallState, m.Author, m.Version,
            usageByMode.TryGetValue(m.Id, out var count) ? count : 0,
            pins.ContainsKey(m.Id),
            m.IsEnabled)).ToArray();

        await ApplyFilterAsync();
        IsLoaded = true;
        Status = $"{Items.Count} modes available.";
    }

    private Task ApplyFilterAsync()
    {
        Items.Clear();
        var filtered = string.IsNullOrWhiteSpace(SearchQuery)
            ? _allItems
            : _allItems.Where(m => m.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                                    m.Description.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
        foreach (var item in filtered) Items.Add(item);
        return Task.CompletedTask;
    }

    private async Task PinAsync(ModeCardViewModel? item)
    {
        if (item is null) return;
        if (item.IsPinned)
        {
            await _pins.DeletePinAsync(item.Id, CancellationToken.None);
        }
        else
        {
            var existingPins = await _pins.GetPinsAsync(CancellationToken.None);
            await _pins.UpsertPinAsync(new ModePin(
                Guid.NewGuid(), item.Id, existingPins.Count, DateTimeOffset.UtcNow), CancellationToken.None);
        }
        await RefreshAsync();
    }
}

public sealed record ModeCardViewModel(
    Guid Id, string Key, string Name, string Description, string IconKey,
    HavenMode BaseMode, ModeSource Source, ModeInstallState InstallState,
    string Author, string Version, int UseCount, bool IsPinned, bool IsEnabled)
{
    public string PinLabel => IsPinned ? "Unpin" : "Pin";
    public string SourceLabel => Source switch { ModeSource.BuiltIn => "Built-in", ModeSource.Community => "Community", _ => "Custom" };
}
