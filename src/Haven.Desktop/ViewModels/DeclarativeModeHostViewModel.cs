using System.Collections.ObjectModel;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed class DeclarativeModeHostViewModel : ObservableObject, IDisposable
{
    private readonly IModeRegistry _registry;
    private readonly IModePackageValidator _validator;
    private readonly IModePackageInstaller _installer;
    private readonly ModeDefinition _modeDefinition;
    private DeclarativeModeDefinition? _loadedDefinition;
    private bool _isLoading;
    private string _status = string.Empty;
    private bool _isExpanded;
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

    public ObservableCollection<DeclarativeStepViewModel> Steps { get; }
    public ObservableCollection<DeclarativeCard> Cards { get; }
    public ObservableCollection<DeclarativeCommandItem> Commands { get; }

    public string ModeName => _modeDefinition.Name;
    public string ModeKey => _modeDefinition.Key;
    public string ModeIcon => _modeDefinition.IconKey;
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
    public string SelectedStep { get => _selectedStep; set => SetProperty(ref _selectedStep, value); }
    public bool HasSteps => Steps.Count > 0;
    public bool HasCards => Cards.Count > 0;
    public bool HasCommands => Commands.Count > 0;
    public DeclarativeModeDefinition? LoadedDefinition => _loadedDefinition;

    public AsyncRelayCommand LoadCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand<string> SelectStepCommand { get; }
    public AsyncRelayCommand<DeclarativeCommandItem> ExecuteCommandCommand { get; }

    public event EventHandler<string>? NavigateRequested;
#pragma warning disable CS0067
    public event EventHandler<DeclarativeCard>? CardActionRequested;
#pragma warning restore CS0067

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

    private Task ExecuteCommandAsync(DeclarativeCommandItem? command)
    {
        if (command?.Action is null) return Task.CompletedTask;
        NavigateRequested?.Invoke(this, command.Action);
        return Task.CompletedTask;
    }

    public void Dispose() { }
}

public sealed class DeclarativeStepViewModel : ObservableObject
{
    private readonly DeclarativeWorkflowStep _step;
    private bool _isActive;
    private bool _isCompleted;
    private bool _hasError;
    private string _status = string.Empty;

    public DeclarativeStepViewModel(DeclarativeWorkflowStep step) { _step = step; }

    public string Id => _step.Id;
    public string Name => _step.Name;
    public string Kind => _step.Kind;
    public string? SystemPrompt => _step.SystemPrompt;
    public string[]? RequiredCapabilities => _step.RequiredCapabilities;
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }
    public bool IsCompleted { get => _isCompleted; set => SetProperty(ref _isCompleted, value); }
    public bool HasError { get => _hasError; set => SetProperty(ref _hasError, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }

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
