/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/DeclarativeModeHostViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns DeclarativeModeHostViewModel, DeclarativeStepViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents declarative mode host view model and keeps its related state and behavior together.
/// </summary>
public sealed class DeclarativeModeHostViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Stores registry locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IModeRegistry _registry;
    /// <summary>
    /// Stores validator locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IModePackageValidator _validator;
    /// <summary>
    /// Stores installer locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IModePackageInstaller _installer;
    /// <summary>
    /// Stores mode definition locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ModeDefinition _modeDefinition;
    /// <summary>
    /// Stores loaded definition locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private DeclarativeModeDefinition? _loadedDefinition;
    /// <summary>
    /// Stores is loading locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isLoading;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = string.Empty;
    /// <summary>
    /// Stores is expanded locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isExpanded;
    /// <summary>
    /// Stores selected step locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _selectedStep = string.Empty;

    public DeclarativeModeHostViewModel(
        ModeDefinition modeDefinition,
        IModeRegistry registry,
        IModePackageValidator validator,
        IModePackageInstaller installer)
    {
        _modeDefinition = modeDefinition;
        _registry = registry;
        _validator = validator;
        _installer = installer;
        Steps = [];
        Cards = [];
        Commands = [];
        LoadCommand = new AsyncRelayCommand(LoadAsync);
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        SelectStepCommand = new RelayCommand<string>(step => SelectedStep = step ?? string.Empty);
        ExecuteCommandCommand = new AsyncRelayCommand<DeclarativeCommandItem>(ExecuteCommandAsync);
    }

    /// <summary>
    /// Gets or updates steps, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<DeclarativeStepViewModel> Steps { get; }
    /// <summary>
    /// Gets or updates cards, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<DeclarativeCard> Cards { get; }
    /// <summary>
    /// Gets or updates commands, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<DeclarativeCommandItem> Commands { get; }

    /// <summary>
    /// Gets or updates mode name, the bindable or domain state represented by this property.
    /// </summary>
    public string ModeName => _modeDefinition.Name;
    /// <summary>
    /// Gets or updates mode key, the bindable or domain state represented by this property.
    /// </summary>
    public string ModeKey => _modeDefinition.Key;
    /// <summary>
    /// Gets or updates mode icon, the bindable or domain state represented by this property.
    /// </summary>
    public string ModeIcon => _modeDefinition.IconKey;
    /// <summary>
    /// Reports whether is loading is true for the current state.
    /// </summary>
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Reports whether is expanded is true for the current state.
    /// </summary>
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
    /// <summary>
    /// Gets or updates selected step, the bindable or domain state represented by this property.
    /// </summary>
    public string SelectedStep { get => _selectedStep; set => SetProperty(ref _selectedStep, value); }
    /// <summary>
    /// Reports whether has steps is true for the current state.
    /// </summary>
    public bool HasSteps => Steps.Count > 0;
    /// <summary>
    /// Reports whether has cards is true for the current state.
    /// </summary>
    public bool HasCards => Cards.Count > 0;
    /// <summary>
    /// Reports whether has commands is true for the current state.
    /// </summary>
    public bool HasCommands => Commands.Count > 0;
    /// <summary>
    /// Gets or updates loaded definition, the bindable or domain state represented by this property.
    /// </summary>
    public DeclarativeModeDefinition? LoadedDefinition => _loadedDefinition;

    /// <summary>
    /// Gets or updates load command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand LoadCommand { get; }
    /// <summary>
    /// Gets or updates refresh command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>
    /// Gets or updates select step command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<string> SelectStepCommand { get; }
    /// <summary>
    /// Runs execute command command while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    public AsyncRelayCommand<DeclarativeCommandItem> ExecuteCommandCommand { get; }

    /// <summary>
    /// Stores navigate requested locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler<string>? NavigateRequested;
#pragma warning disable CS0067
    /// <summary>
    /// Stores card action requested locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler<DeclarativeCard>? CardActionRequested;
#pragma warning restore CS0067

    /// <summary>
    /// Performs load async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task LoadAsync()
    {
        IsLoading = true;
        Status = "Loading…";
        try
        {
            var versions = await _registry.GetVersionsAsync(_modeDefinition.Id, CancellationToken.None);
            if (versions.Count == 0)
            {
                Status = "No version manifest found";
                return;
            }

            var latest = versions[0];
            _loadedDefinition = JsonSerializer.Deserialize<DeclarativeModeDefinition>(latest.ManifestJson);

            if (_loadedDefinition is null)
            {
                Status = "Invalid manifest";
                return;
            }

            Steps.Clear();
            foreach (var step in _loadedDefinition.Workflow.Steps)
                Steps.Add(new DeclarativeStepViewModel(step));

            Cards.Clear();
            foreach (var card in _loadedDefinition.Ui.Cards)
                Cards.Add(card);

            Commands.Clear();
            foreach (var cmd in _loadedDefinition.Ui.CommandBar.Items)
                Commands.Add(cmd);

            if (Steps.Count > 0)
                SelectedStep = Steps[0].Id;

            Status = $"Loaded {_loadedDefinition.Name} v{_loadedDefinition.Version}";
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    /// <summary>
    /// Runs execute command async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private Task ExecuteCommandAsync(DeclarativeCommandItem? command)
    {
        if (command?.Action is null) return Task.CompletedTask;
        NavigateRequested?.Invoke(this, command.Action);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() { }
}

/// <summary>
/// Represents declarative step view model and keeps its related state and behavior together.
/// </summary>
public sealed class DeclarativeStepViewModel : ObservableObject
{
    /// <summary>
    /// Stores step locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly DeclarativeWorkflowStep _step;
    /// <summary>
    /// Stores is active locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isActive;
    /// <summary>
    /// Stores is completed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isCompleted;
    /// <summary>
    /// Stores has error locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _hasError;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = string.Empty;

    public DeclarativeStepViewModel(DeclarativeWorkflowStep step) { _step = step; }

    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public string Id => _step.Id;
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => _step.Name;
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public string Kind => _step.Kind;
    /// <summary>
    /// Gets or updates system prompt, the bindable or domain state represented by this property.
    /// </summary>
    public string? SystemPrompt => _step.SystemPrompt;
    /// <summary>
    /// Gets or updates required capabilities, the bindable or domain state represented by this property.
    /// </summary>
    public string[]? RequiredCapabilities => _step.RequiredCapabilities;
    /// <summary>
    /// Reports whether is active is true for the current state.
    /// </summary>
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }
    /// <summary>
    /// Reports whether is completed is true for the current state.
    /// </summary>
    public bool IsCompleted { get => _isCompleted; set => SetProperty(ref _isCompleted, value); }
    /// <summary>
    /// Reports whether has error is true for the current state.
    /// </summary>
    public bool HasError { get => _hasError; set => SetProperty(ref _hasError, value); }
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; set => SetProperty(ref _status, value); }

    /// <summary>
    /// Gets or updates kind icon, the bindable or domain state represented by this property.
    /// </summary>
    public string KindIcon => _step.Kind switch
    {
        "chat" => "\uE8AB",
        "tool" => "\uE774",
        "browser" => "\uE774",
        "planner" => "\uE7BA",
        "filesystem" => "\uE7C3",
        "command" => "\uE756",
        _ => "\uE790"
    };
}
