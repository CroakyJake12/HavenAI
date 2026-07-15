using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed class LanguageServerSettingsViewModel : ObservableObject
{
    private readonly ILanguageServerConfigurationStore _store;
    private string _status = "Loading language-server settings…";
    private bool _isBusy;

    public LanguageServerSettingsViewModel(ILanguageServerConfigurationStore store)
    {
        _store = store;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        AddCommand = new RelayCommand(Add);
        SaveCommand = new AsyncRelayCommand<LanguageServerSettingsItemViewModel>(SaveAsync);
        DeleteCommand = new AsyncRelayCommand<LanguageServerSettingsItemViewModel>(DeleteAsync);
        _ = RefreshAsync();
    }

    public ObservableCollection<LanguageServerSettingsItemViewModel> Items { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand AddCommand { get; }
    public AsyncRelayCommand<LanguageServerSettingsItemViewModel> SaveCommand { get; }
    public AsyncRelayCommand<LanguageServerSettingsItemViewModel> DeleteCommand { get; }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    private async Task RefreshAsync()
    {
        try
        {
            IsBusy = true;
            Items.Clear();
            foreach (var definition in await _store.GetAllAsync(CancellationToken.None))
                Items.Add(new LanguageServerSettingsItemViewModel(definition));
            Status = $"{Items.Count} trusted language-server definition{(Items.Count == 1 ? string.Empty : "s")}. All built-in suggestions start disabled.";
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Status = "Could not load language-server settings: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Add()
    {
        var id = "custom-" + Guid.NewGuid().ToString("N")[..8];
        var definition = new LanguageServerDefinition(
            id,
            "Custom language server",
            "language-server-command",
            string.Empty,
            "plaintext",
            [".txt"],
            false,
            20,
            "{}");
        var item = new LanguageServerSettingsItemViewModel(definition) { Status = "Not saved." };
        Items.Insert(0, item);
    }

    private async Task SaveAsync(LanguageServerSettingsItemViewModel? item)
    {
        if (item is null) return;
        try
        {
            item.IsBusy = true;
            var extensions = item.ExtensionsText.Split([',', ';', ' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var definition = new LanguageServerDefinition(
                item.Id.Trim(),
                item.DisplayName.Trim(),
                item.Command.Trim(),
                item.Arguments.Trim(),
                item.LanguageId.Trim(),
                extensions,
                item.IsEnabled,
                item.RequestTimeoutSeconds,
                item.InitializationOptionsJson);
            await _store.UpsertAsync(definition, CancellationToken.None);
            item.Definition = definition;
            item.Status = item.IsEnabled
                ? "Saved and enabled. Haven will start this command only for matching files in a selected Studio workspace."
                : "Saved but disabled.";
            Status = $"Saved {definition.DisplayName}.";
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            item.Status = "Save failed: " + exception.Message;
            Status = $"Could not save {item.DisplayName}.";
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    private async Task DeleteAsync(LanguageServerSettingsItemViewModel? item)
    {
        if (item is null) return;
        try
        {
            item.IsBusy = true;
            await _store.DeleteAsync(item.Id, CancellationToken.None);
            Items.Remove(item);
            Status = $"Deleted {item.DisplayName}.";
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            item.Status = "Delete failed: " + exception.Message;
        }
        finally
        {
            item.IsBusy = false;
        }
    }
}

public sealed class LanguageServerSettingsItemViewModel : ObservableObject
{
    private LanguageServerDefinition _definition;
    private string _id;
    private string _displayName;
    private string _command;
    private string _arguments;
    private string _languageId;
    private string _extensionsText;
    private bool _isEnabled;
    private int _requestTimeoutSeconds;
    private string _initializationOptionsJson;
    private string _status;
    private bool _isBusy;

    public LanguageServerSettingsItemViewModel(LanguageServerDefinition definition)
    {
        _definition = definition;
        _id = definition.Id;
        _displayName = definition.DisplayName;
        _command = definition.Command;
        _arguments = definition.Arguments;
        _languageId = definition.LanguageId;
        _extensionsText = string.Join(", ", definition.Extensions);
        _isEnabled = definition.IsEnabled;
        _requestTimeoutSeconds = definition.RequestTimeoutSeconds;
        _initializationOptionsJson = definition.InitializationOptionsJson;
        _status = definition.IsEnabled ? "Enabled." : "Disabled.";
    }

    public LanguageServerDefinition Definition { get => _definition; set => SetProperty(ref _definition, value); }
    public string Id { get => _id; set => SetProperty(ref _id, value); }
    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
    public string Command { get => _command; set => SetProperty(ref _command, value); }
    public string Arguments { get => _arguments; set => SetProperty(ref _arguments, value); }
    public string LanguageId { get => _languageId; set => SetProperty(ref _languageId, value); }
    public string ExtensionsText { get => _extensionsText; set => SetProperty(ref _extensionsText, value); }
    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
    public int RequestTimeoutSeconds { get => _requestTimeoutSeconds; set => SetProperty(ref _requestTimeoutSeconds, Math.Clamp(value, 2, 120)); }
    public string InitializationOptionsJson { get => _initializationOptionsJson; set => SetProperty(ref _initializationOptionsJson, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }
}
