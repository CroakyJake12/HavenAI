/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/UtilityPagesViewModels.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns CatalogPageKind, CatalogPageViewModel, CatalogCardViewModel, PluginImportManifest, AutomationsPageViewModel, AutomationCardViewModel, SettingsPageViewModel, ModelSettingsItemViewModel, LessonSettingsPageViewModel, ModeLibraryPageViewModel, ModeCardViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Automations;
using Haven.Browser;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Lists the supported catalog page kind values used to make state explicit and type-safe.
/// </summary>
public enum CatalogPageKind { Agents, Plugins, Prompts }

/// <summary>
/// Represents catalog page view model and keeps its related state and behavior together.
/// </summary>
public sealed class CatalogPageViewModel : ObservableObject
{
    /// <summary>
    /// Stores catalog locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ICatalogRepository _catalog;
    /// <summary>
    /// Stores ollama locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IOllamaClient _ollama;
    /// <summary>
    /// Stores allow studio creators locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly bool _allowStudioCreators;
    /// <summary>
    /// Stores loaded locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _loaded;
    /// <summary>
    /// Stores is creating locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isCreating;
    /// <summary>
    /// Stores builder prompt locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _builderPrompt = string.Empty;
    /// <summary>
    /// Stores new name locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _newName = string.Empty;
    /// <summary>
    /// Stores new description locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _newDescription = string.Empty;
    /// <summary>
    /// Stores new instructions locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _newInstructions = string.Empty;
    /// <summary>
    /// Stores new model locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _newModel = string.Empty;
    /// <summary>
    /// Stores new persists locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _newPersists = true;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public CatalogPageKind Kind { get; }
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title => Kind switch { CatalogPageKind.Agents => "Agents", CatalogPageKind.Plugins => "Plugins", _ => "Instruction Library" };
    /// <summary>
    /// Gets or updates subtitle, the bindable or domain state represented by this property.
    /// </summary>
    public string Subtitle => Kind switch
    {
        CatalogPageKind.Agents => "Choose specialised local assistants and model preferences.",
        CatalogPageKind.Plugins => "Functional, capability-backed tools invoked with @.",
        _ => "Reusable built-in and custom instructions invoked with >."
    };
    /// <summary>
    /// Creates label with the invariants required by its callers.
    /// </summary>
    public string CreateLabel => Kind switch { CatalogPageKind.Agents => "Create agent", CatalogPageKind.Plugins => "Create plugin", _ => "Create instruction" };
    /// <summary>
    /// Builds er title from the currently available inputs.
    /// </summary>
    public string BuilderTitle => Kind switch { CatalogPageKind.Agents => "AGENT CREATOR", CatalogPageKind.Plugins => "PLUGIN CREATOR", _ => "INSTRUCTION CREATOR" };
    /// <summary>
    /// Builds er hint from the currently available inputs.
    /// </summary>
    public string BuilderHint => Kind switch { CatalogPageKind.Agents => "Describe the assistant you want Haven to create", CatalogPageKind.Plugins => "Describe the functional capability and constraints", _ => "Describe the reusable instruction behaviour" };
    /// <summary>
    /// Reports whether agent catalog applies to the current state.
    /// </summary>
    public bool IsAgentCatalog => Kind == CatalogPageKind.Agents;
    /// <summary>
    /// Reports whether plugin catalog applies to the current state.
    /// </summary>
    public bool IsPluginCatalog => Kind == CatalogPageKind.Plugins;
    /// <summary>
    /// Reports whether prompt catalog applies to the current state.
    /// </summary>
    public bool IsPromptCatalog => Kind == CatalogPageKind.Prompts;
    /// <summary>
    /// Reports whether create items applies to the current state.
    /// </summary>
    public bool CanCreateItems => IsPromptCatalog || _allowStudioCreators;
    /// <summary>
    /// Reports whether upload plugin applies to the current state.
    /// </summary>
    public bool CanUploadPlugin => IsPluginCatalog && _allowStudioCreators;
    /// <summary>
    /// Gets or updates items, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<CatalogCardViewModel> Items { get; } = [];
    /// <summary>
    /// Gets or updates refresh command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>
    /// Gets or updates toggle create command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ToggleCreateCommand { get; }
    /// <summary>
    /// Creates command with the invariants required by its callers.
    /// </summary>
    public AsyncRelayCommand CreateCommand { get; }
    /// <summary>
    /// Builds with ai command from the currently available inputs.
    /// </summary>
    public AsyncRelayCommand BuildWithAiCommand { get; }
    /// <summary>
    /// Gets or updates duplicate command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<CatalogCardViewModel> DuplicateCommand { get; }
    /// <summary>
    /// Gets or updates delete command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<CatalogCardViewModel> DeleteCommand { get; }
    /// <summary>
    /// Reports whether loaded applies to the current state.
    /// </summary>
    public bool IsLoaded { get => _loaded; private set => SetProperty(ref _loaded, value); }
    /// <summary>
    /// Reports whether creating applies to the current state.
    /// </summary>
    public bool IsCreating { get => _isCreating; set { if (SetProperty(ref _isCreating, value)) RaisePropertyChanged(nameof(IsNotCreating)); } }
    /// <summary>
    /// Reports whether not creating applies to the current state.
    /// </summary>
    public bool IsNotCreating => !IsCreating;
    /// <summary>
    /// Builds er prompt from the currently available inputs.
    /// </summary>
    public string BuilderPrompt { get => _builderPrompt; set { if (SetProperty(ref _builderPrompt, value)) BuildWithAiCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates new name, the bindable or domain state represented by this property.
    /// </summary>
    public string NewName { get => _newName; set { if (SetProperty(ref _newName, value)) CreateCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates new description, the bindable or domain state represented by this property.
    /// </summary>
    public string NewDescription { get => _newDescription; set { if (SetProperty(ref _newDescription, value)) CreateCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates new instructions, the bindable or domain state represented by this property.
    /// </summary>
    public string NewInstructions { get => _newInstructions; set { if (SetProperty(ref _newInstructions, value)) CreateCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates new model, the bindable or domain state represented by this property.
    /// </summary>
    public string NewModel { get => _newModel; set => SetProperty(ref _newModel, value); }
    /// <summary>
    /// Gets or updates new persists, the bindable or domain state represented by this property.
    /// </summary>
    public bool NewPersists { get => _newPersists; set => SetProperty(ref _newPersists, value); }
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>
    /// Performs refresh asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Reports whether create applies to the current state.
    /// </summary>
    private bool CanCreate() => !string.IsNullOrWhiteSpace(NewName)
                                && !string.IsNullOrWhiteSpace(NewDescription)
                                && !string.IsNullOrWhiteSpace(NewInstructions);

    /// <summary>
    /// Creates async with the invariants required by its callers.
    /// </summary>
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

    /// <summary>
    /// Builds with ai async from the currently available inputs.
    /// </summary>
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

    /// <summary>
    /// Performs import plugin asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs duplicate asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs delete asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

/// <summary>
/// Represents catalog card view model and keeps its related state and behavior together.
/// </summary>
public sealed record CatalogCardViewModel(Guid Id, CatalogPageKind Kind, string Name, string IconKey, string Description, string Meta, bool IsEnabled, bool IsBuiltIn)
{
    /// <summary>
    /// Reports whether duplicate applies to the current state.
    /// </summary>
    public bool CanDuplicate => Kind == CatalogPageKind.Agents;
    /// <summary>
    /// Reports whether delete applies to the current state.
    /// </summary>
    public bool CanDelete => !IsBuiltIn;
}

/// <summary>
/// Represents plugin import manifest and keeps its related state and behavior together.
/// </summary>
public sealed class PluginImportManifest
{
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>
    /// Gets or updates description, the bindable or domain state represented by this property.
    /// </summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>
    /// Gets or updates instructions, the bindable or domain state represented by this property.
    /// </summary>
    public string Instructions { get; init; } = string.Empty;
    /// <summary>
    /// Gets or updates icon key, the bindable or domain state represented by this property.
    /// </summary>
    public string IconKey { get; init; } = string.Empty;
    /// <summary>
    /// Gets or updates capabilities, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    /// <summary>
    /// Gets or updates conflicts, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<string> Conflicts { get; init; } = [];
    /// <summary>
    /// Gets or updates allowed modes, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<string> AllowedModes { get; init; } = [];
    /// <summary>
    /// Gets or updates dashboard tiles, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<DashboardPluginTileManifest> DashboardTiles { get; init; } = [];
    /// <summary>
    /// Gets or updates persists, the bindable or domain state represented by this property.
    /// </summary>
    public bool Persists { get; init; }
    /// <summary>
    /// Reports whether agentic applies to the current state.
    /// </summary>
    public bool IsAgentic { get; init; }
}

/// <summary>
/// Represents automations page view model and keeps its related state and behavior together.
/// </summary>
public sealed class AutomationsPageViewModel : ObservableObject
{
    /// <summary>
    /// Stores repository locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IAutomationRepository _repository;
    /// <summary>
    /// Stores registration locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly WindowsAutomationRegistrationService _registration;
    /// <summary>
    /// Stores runner locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly AutomationRunner _runner;
    /// <summary>
    /// Stores schedules locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ScheduleCalculator _schedules;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = "Loading…";
    /// <summary>
    /// Stores new name locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _newName = string.Empty;
    /// <summary>
    /// Stores new instruction locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _newInstruction = string.Empty;
    /// <summary>
    /// Stores new mode locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private HavenMode _newMode = HavenMode.Chat;
    /// <summary>
    /// Stores new schedule kind locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private AutomationScheduleKind _newScheduleKind = AutomationScheduleKind.Daily;
    /// <summary>
    /// Stores new schedule json locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Gets or updates items, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<AutomationCardViewModel> Items { get; } = [];
    /// <summary>
    /// Gets or updates modes, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<HavenMode> Modes { get; } = Enum.GetValues<HavenMode>();
    /// <summary>
    /// Gets or updates schedule kinds, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<AutomationScheduleKind> ScheduleKinds { get; } = Enum.GetValues<AutomationScheduleKind>();
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Gets or updates new name, the bindable or domain state represented by this property.
    /// </summary>
    public string NewName { get => _newName; set { if (SetProperty(ref _newName, value)) CreateCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates new instruction, the bindable or domain state represented by this property.
    /// </summary>
    public string NewInstruction { get => _newInstruction; set { if (SetProperty(ref _newInstruction, value)) CreateCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates new mode, the bindable or domain state represented by this property.
    /// </summary>
    public HavenMode NewMode { get => _newMode; set => SetProperty(ref _newMode, value); }
    /// <summary>
    /// Gets or updates new schedule kind, the bindable or domain state represented by this property.
    /// </summary>
    public AutomationScheduleKind NewScheduleKind { get => _newScheduleKind; set => SetProperty(ref _newScheduleKind, value); }
    /// <summary>
    /// Gets or updates new schedule json, the bindable or domain state represented by this property.
    /// </summary>
    public string NewScheduleJson { get => _newScheduleJson; set => SetProperty(ref _newScheduleJson, value); }
    /// <summary>
    /// Gets or updates refresh command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>
    /// Gets or updates register worker command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RegisterWorkerCommand { get; }
    /// <summary>
    /// Creates command with the invariants required by its callers.
    /// </summary>
    public AsyncRelayCommand CreateCommand { get; }
    /// <summary>
    /// Gets or updates toggle command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<AutomationCardViewModel> ToggleCommand { get; }
    /// <summary>
    /// Gets or updates delete command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<AutomationCardViewModel> DeleteCommand { get; }
    /// <summary>
    /// Runs run now command while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    public RelayCommand<AutomationCardViewModel> RunNowCommand { get; }

    /// <summary>
    /// Performs refresh asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RefreshAsync()
    {
        Items.Clear();
        foreach (var item in await _repository.GetAllAsync(CancellationToken.None)) Items.Add(new AutomationCardViewModel(item));
        Status = Items.Count == 0 ? "No automations yet." : $"{Items.Count} automation{(Items.Count == 1 ? "" : "s")}";
    }

    /// <summary>
    /// Creates async with the invariants required by its callers.
    /// </summary>
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

    /// <summary>
    /// Performs toggle asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ToggleAsync(AutomationCardViewModel? item)
    {
        if (item is null) return;
        var now = DateTimeOffset.UtcNow;
        var enabled = !item.Definition.IsEnabled;
        var next = enabled ? _schedules.GetNextRun(item.Definition with { IsEnabled = true }, now) : null;
        await _repository.UpsertAsync(item.Definition with { IsEnabled = enabled, NextRunAt = next, UpdatedAt = now }, CancellationToken.None);
        await RefreshAsync();
    }

    /// <summary>
    /// Performs delete asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task DeleteAsync(AutomationCardViewModel? item)
    {
        if (item is null) return;
        await _repository.DeleteAsync(item.Definition.Id, CancellationToken.None);
        await RefreshAsync();
        Status = "Automation deleted.";
    }

    /// <summary>
    /// Runs run now async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private async Task RunNowAsync(AutomationCardViewModel? item)
    {
        if (item is null) return;
        var now = DateTimeOffset.UtcNow;
        await _repository.UpsertAsync(item.Definition with { IsEnabled = true, NextRunAt = now, UpdatedAt = now }, CancellationToken.None);
        var result = await _runner.RunDueAsync(now, CancellationToken.None);
        await RefreshAsync();
        Status = $"Run pass: {result.Succeeded} succeeded, {result.Failed} failed, {result.Skipped} skipped.";
    }

    /// <summary>
    /// Performs register worker asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RegisterWorkerAsync()
    {
        var worker = Path.Combine(AppContext.BaseDirectory, "Haven.AutomationWorker.exe");
        var result = await _registration.RegisterAsync(worker, CancellationToken.None);
        Status = result.Message;
    }
}

/// <summary>
/// Represents automation card view model and keeps its related state and behavior together.
/// </summary>
public sealed class AutomationCardViewModel(AutomationDefinition definition)
{
    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public AutomationDefinition Definition => definition;
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => definition.Name;
    /// <summary>
    /// Gets or updates mode, the bindable or domain state represented by this property.
    /// </summary>
    public string Mode => definition.Mode.ToString();
    /// <summary>
    /// Gets or updates instruction, the bindable or domain state represented by this property.
    /// </summary>
    public string Instruction => definition.Instruction;
    /// <summary>
    /// Gets or updates next run, the bindable or domain state represented by this property.
    /// </summary>
    public string NextRun => definition.NextRunAt?.LocalDateTime.ToString("g") ?? "Not scheduled";
    /// <summary>
    /// Reports whether enabled applies to the current state.
    /// </summary>
    public bool IsEnabled => definition.IsEnabled;
    /// <summary>
    /// Gets or updates toggle label, the bindable or domain state represented by this property.
    /// </summary>
    public string ToggleLabel => definition.IsEnabled ? "Pause" : "Resume";
    /// <summary>
    /// Gets or updates state label, the bindable or domain state represented by this property.
    /// </summary>
    public string StateLabel => definition.IsEnabled ? "Enabled" : "Paused";
}

/// <summary>
/// Represents settings page view model and keeps its related state and behavior together.
/// </summary>
public sealed class SettingsPageViewModel : ObservableObject
{
    /// <summary>
    /// Stores preferences locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly UserPreferencesService _preferences;
    /// <summary>
    /// Stores ollama locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IOllamaClient _ollama;
    /// <summary>
    /// Stores applied locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Action<string?, EffortLevel> _applied;
    /// <summary>
    /// Stores selected model locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ModelDescriptor? _selectedModel;
    /// <summary>
    /// Stores selected effort locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private EffortLevel _selectedEffort;
    /// <summary>
    /// Stores active theme id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _activeThemeId;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = "Loading local settings…";
    /// <summary>
    /// Stores model search locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _modelSearch = string.Empty;
    /// <summary>
    /// Stores install model name locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _installModelName = string.Empty;
    /// <summary>
    /// Stores install progress locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private double _installProgress;
    /// <summary>
    /// Stores temperature locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private double _temperature;
    /// <summary>
    /// Stores context limit locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _contextLimit;
    /// <summary>
    /// Stores action limit locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _actionLimit;
    /// <summary>
    /// Stores auto switch locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _autoSwitch;
    /// <summary>
    /// Stores show agentic in chat locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _showAgenticInChat;
    /// <summary>
    /// Stores vertical tabs locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _verticalTabs;
    /// <summary>
    /// Stores confidence meter locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _confidenceMeter;
    /// <summary>
    /// Stores auto compact locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _autoCompact;
    /// <summary>
    /// Stores compact at percent locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _compactAtPercent;
    /// <summary>
    /// Stores adaptive help locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _adaptiveHelp;
    /// <summary>
    /// Stores browser side assistant locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _browserSideAssistant;
    /// <summary>Stores the user's on-send Ollama wake preference.</summary>
    private bool _autoWakeOllama;
    /// <summary>
    /// Stores file permission locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private PermissionMode _filePermission;
    /// <summary>
    /// Stores command permission locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private PermissionMode _commandPermission;
    /// <summary>
    /// Stores browser permission locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private PermissionMode _browserPermission;
    /// <summary>
    /// Stores computer permission locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private PermissionMode _computerPermission;
    /// <summary>
    /// Stores theme name locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _themeName = "My theme";
    /// <summary>
    /// Stores theme background locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _themeBackground = "#111111";
    /// <summary>
    /// Stores theme panel locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _themePanel = "#1A1A1A";
    /// <summary>
    /// Stores theme panel2 locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _themePanel2 = "#202020";
    /// <summary>
    /// Stores theme text locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _themeText = "#F5F5F5";
    /// <summary>
    /// Stores theme muted locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _themeMuted = "#8A8A8A";
    /// <summary>
    /// Stores theme accent locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _themeAccent = "#60CDFF";
    /// <summary>
    /// Stores theme blue locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _themeBlue = "#98EBFF";
    /// <summary>
    /// Stores theme is light locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _themeIsLight;
    /// <summary>
    /// Stores theme nub color locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _themeNubColor = "#60CDFF";
    /// <summary>
    /// Stores theme card border locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _themeCardBorder;
    /// <summary>
    /// Stores is model delete confirming locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isModelDeleteConfirming;
    /// <summary>
    /// Stores allow theme creator locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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
        _autoWakeOllama = snapshot.AutoWakeOllama;
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

    /// <summary>
    /// Gets or updates themes, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<HavenThemePreset> Themes { get; } = [];
    /// <summary>
    /// Gets or updates models, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ModelDescriptor> Models { get; } = [];
    /// <summary>
    /// Gets or updates filtered models, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ModelSettingsItemViewModel> FilteredModels { get; } = [];
    /// <summary>
    /// Gets or updates effort levels, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<EffortLevel> EffortLevels { get; } = Enum.GetValues<EffortLevel>();
    /// <summary>
    /// Gets or updates permission modes, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<PermissionMode> PermissionModes { get; } = Enum.GetValues<PermissionMode>();
    /// <summary>
    /// Reports whether create theme applies to the current state.
    /// </summary>
    public bool CanCreateTheme => _allowThemeCreator;
    /// <summary>
    /// Gets or updates selected model, the bindable or domain state represented by this property.
    /// </summary>
    public ModelDescriptor? SelectedModel { get => _selectedModel; set { if (!SetProperty(ref _selectedModel, value)) return; IsModelDeleteConfirming = false; DeleteModelCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates selected effort, the bindable or domain state represented by this property.
    /// </summary>
    public EffortLevel SelectedEffort { get => _selectedEffort; set => SetProperty(ref _selectedEffort, value); }
    /// <summary>
    /// Gets or updates active theme id, the bindable or domain state represented by this property.
    /// </summary>
    public string ActiveThemeId { get => _activeThemeId; private set => SetProperty(ref _activeThemeId, value); }
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Gets or updates model search, the bindable or domain state represented by this property.
    /// </summary>
    public string ModelSearch { get => _modelSearch; set { if (SetProperty(ref _modelSearch, value)) FilterModelList(); } }
    /// <summary>
    /// Gets or updates install model name, the bindable or domain state represented by this property.
    /// </summary>
    public string InstallModelName { get => _installModelName; set { if (SetProperty(ref _installModelName, value)) InstallModelCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates install progress, the bindable or domain state represented by this property.
    /// </summary>
    public double InstallProgress { get => _installProgress; private set => SetProperty(ref _installProgress, value); }
    /// <summary>
    /// Gets or updates temperature, the bindable or domain state represented by this property.
    /// </summary>
    public double Temperature { get => _temperature; set => SetProperty(ref _temperature, value); }
    /// <summary>
    /// Gets or updates context limit, the bindable or domain state represented by this property.
    /// </summary>
    public int ContextLimit { get => _contextLimit; set => SetProperty(ref _contextLimit, value); }
    /// <summary>
    /// Gets or updates action limit, the bindable or domain state represented by this property.
    /// </summary>
    public int ActionLimit { get => _actionLimit; set => SetProperty(ref _actionLimit, value); }
    /// <summary>
    /// Gets or updates auto switch, the bindable or domain state represented by this property.
    /// </summary>
    public bool AutoSwitch { get => _autoSwitch; set => SetProperty(ref _autoSwitch, value); }
    /// <summary>
    /// Gets or updates show agentic in chat, the bindable or domain state represented by this property.
    /// </summary>
    public bool ShowAgenticInChat { get => _showAgenticInChat; set => SetProperty(ref _showAgenticInChat, value); }
    /// <summary>
    /// Gets or updates vertical tabs, the bindable or domain state represented by this property.
    /// </summary>
    public bool VerticalTabs { get => _verticalTabs; set => SetProperty(ref _verticalTabs, value); }
    /// <summary>
    /// Gets or updates confidence meter, the bindable or domain state represented by this property.
    /// </summary>
    public bool ConfidenceMeter { get => _confidenceMeter; set => SetProperty(ref _confidenceMeter, value); }
    /// <summary>
    /// Gets or updates auto compact, the bindable or domain state represented by this property.
    /// </summary>
    public bool AutoCompact { get => _autoCompact; set => SetProperty(ref _autoCompact, value); }
    /// <summary>
    /// Gets or updates compact at percent, the bindable or domain state represented by this property.
    /// </summary>
    public int CompactAtPercent { get => _compactAtPercent; set => SetProperty(ref _compactAtPercent, value); }
    /// <summary>
    /// Gets or updates adaptive help, the bindable or domain state represented by this property.
    /// </summary>
    public bool AdaptiveHelp { get => _adaptiveHelp; set => SetProperty(ref _adaptiveHelp, value); }
    /// <summary>
    /// Gets or updates browser side assistant, the bindable or domain state represented by this property.
    /// </summary>
    public bool BrowserSideAssistant { get => _browserSideAssistant; set => SetProperty(ref _browserSideAssistant, value); }
    /// <summary>Gets or sets whether Haven may wake Ollama for an offline local-model send.</summary>
    public bool AutoWakeOllama { get => _autoWakeOllama; set => SetProperty(ref _autoWakeOllama, value); }
    /// <summary>
    /// Gets or updates file permission, the bindable or domain state represented by this property.
    /// </summary>
    public PermissionMode FilePermission { get => _filePermission; set => SetProperty(ref _filePermission, value); }
    /// <summary>
    /// Gets or updates command permission, the bindable or domain state represented by this property.
    /// </summary>
    public PermissionMode CommandPermission { get => _commandPermission; set => SetProperty(ref _commandPermission, value); }
    /// <summary>
    /// Gets or updates browser permission, the bindable or domain state represented by this property.
    /// </summary>
    public PermissionMode BrowserPermission { get => _browserPermission; set => SetProperty(ref _browserPermission, value); }
    /// <summary>
    /// Gets or updates computer permission, the bindable or domain state represented by this property.
    /// </summary>
    public PermissionMode ComputerPermission { get => _computerPermission; set => SetProperty(ref _computerPermission, value); }
    /// <summary>
    /// Gets or updates theme name, the bindable or domain state represented by this property.
    /// </summary>
    public string ThemeName { get => _themeName; set => SetProperty(ref _themeName, value); }
    /// <summary>
    /// Gets or updates theme background, the bindable or domain state represented by this property.
    /// </summary>
    public string ThemeBackground { get => _themeBackground; set => SetProperty(ref _themeBackground, value); }
    /// <summary>
    /// Gets or updates theme panel, the bindable or domain state represented by this property.
    /// </summary>
    public string ThemePanel { get => _themePanel; set => SetProperty(ref _themePanel, value); }
    /// <summary>
    /// Gets or updates theme panel2, the bindable or domain state represented by this property.
    /// </summary>
    public string ThemePanel2 { get => _themePanel2; set => SetProperty(ref _themePanel2, value); }
    /// <summary>
    /// Gets or updates theme text, the bindable or domain state represented by this property.
    /// </summary>
    public string ThemeText { get => _themeText; set => SetProperty(ref _themeText, value); }
    /// <summary>
    /// Gets or updates theme muted, the bindable or domain state represented by this property.
    /// </summary>
    public string ThemeMuted { get => _themeMuted; set => SetProperty(ref _themeMuted, value); }
    /// <summary>
    /// Gets or updates theme accent, the bindable or domain state represented by this property.
    /// </summary>
    public string ThemeAccent { get => _themeAccent; set => SetProperty(ref _themeAccent, value); }
    /// <summary>
    /// Gets or updates theme blue, the bindable or domain state represented by this property.
    /// </summary>
    public string ThemeBlue { get => _themeBlue; set => SetProperty(ref _themeBlue, value); }
    /// <summary>
    /// Gets or updates theme is light, the bindable or domain state represented by this property.
    /// </summary>
    public bool ThemeIsLight { get => _themeIsLight; set => SetProperty(ref _themeIsLight, value); }
    /// <summary>
    /// Gets or updates theme nub color, the bindable or domain state represented by this property.
    /// </summary>
    public string ThemeNubColor { get => _themeNubColor; set => SetProperty(ref _themeNubColor, value); }
    /// <summary>
    /// Gets or updates theme card border, the bindable or domain state represented by this property.
    /// </summary>
    public bool ThemeCardBorder { get => _themeCardBorder; set => SetProperty(ref _themeCardBorder, value); }
    /// <summary>
    /// Reports whether model delete confirming applies to the current state.
    /// </summary>
    public bool IsModelDeleteConfirming { get => _isModelDeleteConfirming; private set => SetProperty(ref _isModelDeleteConfirming, value); }
    /// <summary>
    /// Gets or updates apply theme command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<HavenThemePreset> ApplyThemeCommand { get; }
    /// <summary>
    /// Gets or updates save model defaults command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand SaveModelDefaultsCommand { get; }
    /// <summary>
    /// Gets or updates save advanced command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand SaveAdvancedCommand { get; }
    /// <summary>
    /// Gets or updates save features command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand SaveFeaturesCommand { get; }
    /// <summary>
    /// Gets or updates save permissions command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand SavePermissionsCommand { get; }
    /// <summary>
    /// Gets or updates save custom theme command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand SaveCustomThemeCommand { get; }
    /// <summary>
    /// Gets or updates refresh models command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RefreshModelsCommand { get; }
    /// <summary>
    /// Gets or updates install model command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand InstallModelCommand { get; }
    /// <summary>
    /// Gets or updates delete model command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand DeleteModelCommand { get; }
    /// <summary>
    /// Gets or updates request delete model command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand RequestDeleteModelCommand { get; }
    /// <summary>
    /// Reports whether cancel delete model command is true for the current state.
    /// </summary>
    public RelayCommand CancelDeleteModelCommand { get; }
    /// <summary>
    /// Gets or updates select model command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<ModelSettingsItemViewModel> SelectModelCommand { get; }
    /// <summary>
    /// Gets or updates data statement, the bindable or domain state represented by this property.
    /// </summary>
    public string DataStatement => "Haven stores chats, agents, plugins, preferences, and automation history in local files and SQLite. Ollama requests stay on the configured local endpoint.";
    /// <summary>
    /// Gets or updates browser statement, the bindable or domain state represented by this property.
    /// </summary>
    public string BrowserStatement => "The embedded browser runs in Haven's native WebView session rather than controlling your normal browser window.";
    /// <summary>
    /// Gets or updates safety statement, the bindable or domain state represented by this property.
    /// </summary>
    public string SafetyStatement => "File tools are confined to the selected project folder. Commands start there, use a bounded timeout, and report their real exit code and output.";

    /// <summary>
    /// Performs the apply theme step owned by this component.
    /// </summary>
    private void ApplyTheme(HavenThemePreset? theme)
    {
        if (theme is null) return;
        _preferences.ApplyTheme(theme.Id);
        ActiveThemeId = theme.Id;
        Status = $"Applied {theme.Name}.";
    }

    /// <summary>
    /// Performs the save model defaults step owned by this component.
    /// </summary>
    private void SaveModelDefaults()
    {
        _preferences.SetModelDefaults(SelectedModel?.Name, SelectedEffort);
        _applied(SelectedModel?.Name, SelectedEffort);
        Status = "Default model settings saved.";
    }

    /// <summary>
    /// Performs the save advanced step owned by this component.
    /// </summary>
    private void SaveAdvanced()
    {
        _preferences.SetAdvancedModelOptions(Temperature, ContextLimit, ActionLimit);
        Status = "Advanced generation limits saved.";
    }

    /// <summary>
    /// Performs the save features step owned by this component.
    /// </summary>
    private void SaveFeatures()
    {
        _preferences.SetFeatureOptions(AutoSwitch, ShowAgenticInChat, VerticalTabs, ConfidenceMeter, AutoCompact,
            CompactAtPercent, AdaptiveHelp, BrowserSideAssistant, AutoWakeOllama);
        Status = "Feature preferences saved. Reopen a surface to apply layout-only changes.";
    }

    /// <summary>
    /// Performs the save permissions step owned by this component.
    /// </summary>
    private void SavePermissions()
    {
        _preferences.SetToolPermissions(FilePermission, CommandPermission, BrowserPermission, ComputerPermission);
        Status = "Tool permission defaults saved.";
    }

    /// <summary>
    /// Performs the save custom theme step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs install model asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs delete model asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs refresh models asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs the filter model list step owned by this component.
    /// </summary>
    private void FilterModelList()
    {
        FilteredModels.Clear();
        foreach (var model in Models.Where(model => string.IsNullOrWhiteSpace(ModelSearch) ||
                     model.Name.Contains(ModelSearch, StringComparison.OrdinalIgnoreCase) || model.Family.Contains(ModelSearch, StringComparison.OrdinalIgnoreCase)))
            FilteredModels.Add(new ModelSettingsItemViewModel(model));
    }

    /// <summary>
    /// Performs the refresh themes step owned by this component.
    /// </summary>
    private void RefreshThemes()
    {
        Themes.Clear();
        foreach (var theme in _preferences.Themes) Themes.Add(theme);
    }
}

/// <summary>
/// Represents model settings item view model and keeps its related state and behavior together.
/// </summary>
public sealed class ModelSettingsItemViewModel(ModelDescriptor definition)
{
    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public ModelDescriptor Definition => definition;
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => definition.Name;
    /// <summary>
    /// Gets or updates details, the bindable or domain state represented by this property.
    /// </summary>
    public string Details => string.Join(" · ", new[] { definition.Family, definition.ParameterSize, definition.Quantization }.Where(value => !string.IsNullOrWhiteSpace(value)));
    /// <summary>
    /// Gets or updates download size, the bindable or domain state represented by this property.
    /// </summary>
    public string DownloadSize => FormatBytes(definition.SizeBytes);
    /// <summary>
    /// Gets or updates estimated ram, the bindable or domain state represented by this property.
    /// </summary>
    public string EstimatedRam => $"Approx. {FormatBytes((long)(definition.SizeBytes * 1.25))} RAM";
    /// <summary>
    /// Gets or updates capabilities, the bindable or domain state represented by this property.
    /// </summary>
    public string Capabilities => definition.Capabilities.Count == 0 ? "Chat" : string.Join(", ", definition.Capabilities);

    /// <summary>
    /// Performs the format bytes step owned by this component.
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
}

/// <summary>
/// Represents lesson settings page view model and keeps its related state and behavior together.
/// </summary>
public sealed class LessonSettingsPageViewModel : ObservableObject
{
    /// <summary>
    /// Stores item locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly LessonItemViewModel _item;
    /// <summary>
    /// Stores repository locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IContainerRepository _repository;
    /// <summary>
    /// Stores saved locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Func<Task> _saved;
    /// <summary>
    /// Stores name locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _name;
    /// <summary>
    /// Stores topic group locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _topicGroup;
    /// <summary>
    /// Stores structure json locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _structureJson;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name { get => _name; set { if (SetProperty(ref _name, value)) SaveCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates topic group, the bindable or domain state represented by this property.
    /// </summary>
    public string TopicGroup { get => _topicGroup; set => SetProperty(ref _topicGroup, value); }
    /// <summary>
    /// Gets or updates structure json, the bindable or domain state represented by this property.
    /// </summary>
    public string StructureJson { get => _structureJson; set => SetProperty(ref _structureJson, value); }
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Gets or updates save command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand SaveCommand { get; }

    /// <summary>
    /// Performs save asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

/// <summary>
/// Represents mode library page view model and keeps its related state and behavior together.
/// </summary>
public sealed class ModeLibraryPageViewModel : ObservableObject
{
    /// <summary>
    /// Stores modes locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IModeRegistry _modes;
    /// <summary>
    /// Stores usage locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IModeUsageRepository _usage;
    /// <summary>
    /// Stores pins locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IPinRepository _pins;
    /// <summary>
    /// Stores loaded locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _loaded;
    /// <summary>
    /// Stores search query locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _searchQuery = string.Empty;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Stores open in studio locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event Action? OpenInStudio;

    /// <summary>
    /// Gets or updates items, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ModeCardViewModel> Items { get; } = [];
    /// <summary>
    /// Gets or updates refresh command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>
    /// Gets or updates pin command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<ModeCardViewModel> PinCommand { get; }
    /// <summary>
    /// Creates in studio command with the invariants required by its callers.
    /// </summary>
    public RelayCommand CreateInStudioCommand { get; }
    /// <summary>
    /// Reports whether loaded applies to the current state.
    /// </summary>
    public bool IsLoaded { get => _loaded; private set => SetProperty(ref _loaded, value); }
    /// <summary>
    /// Gets or updates search query, the bindable or domain state represented by this property.
    /// </summary>
    public string SearchQuery { get => _searchQuery; set { if (SetProperty(ref _searchQuery, value)) _ = ApplyFilterAsync(); } }
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>
    /// Stores all items locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private IReadOnlyList<ModeCardViewModel> _allItems = [];

    /// <summary>
    /// Performs refresh asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RefreshAsync()
    {
        var modes = await _modes.GetModesAsync(CancellationToken.None);
        var pins = (await _pins.GetPinsAsync(CancellationToken.None)).ToDictionary(p => p.ModeId);
        var usageData = await _usage.GetRecentUsageAsync(30, CancellationToken.None);
        var usageByMode = usageData.GroupBy(u => u.ModeId).ToDictionary(g => g.Key, g => g.Sum(u => u.TurnCount));

        _allItems = modes
            .Where(m => !m.Key.Equals("do", StringComparison.OrdinalIgnoreCase)
                        && !m.Name.Equals("Do", StringComparison.OrdinalIgnoreCase)
                        && !m.Key.Equals("teach", StringComparison.OrdinalIgnoreCase)
                        && !m.Name.Equals("Teach", StringComparison.OrdinalIgnoreCase))
            .Select(m => new ModeCardViewModel(
            m.Id, m.Key, m.Name, m.Description, m.IconKey, m.BaseMode,
            m.Source, m.InstallState, m.Author, m.Version,
            usageByMode.TryGetValue(m.Id, out var count) ? count : 0,
            pins.ContainsKey(m.Id),
            m.IsEnabled)).ToArray();

        await ApplyFilterAsync();
        IsLoaded = true;
        Status = $"{Items.Count} modes available.";
    }

    /// <summary>
    /// Performs apply filter asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs pin asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

/// <summary>
/// Represents mode card view model and keeps its related state and behavior together.
/// </summary>
public sealed record ModeCardViewModel(
    Guid Id, string Key, string Name, string Description, string IconKey,
    HavenMode BaseMode, ModeSource Source, ModeInstallState InstallState,
    string Author, string Version, int UseCount, bool IsPinned, bool IsEnabled)
{
    /// <summary>
    /// Gets or updates pin label, the bindable or domain state represented by this property.
    /// </summary>
    public string PinLabel => IsPinned ? "Unpin" : "Pin";
    /// <summary>
    /// Gets or updates source label, the bindable or domain state represented by this property.
    /// </summary>
    public string SourceLabel => Source switch { ModeSource.BuiltIn => "Built-in", ModeSource.Community => "Community", _ => "Custom" };
}
