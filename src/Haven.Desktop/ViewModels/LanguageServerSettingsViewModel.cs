/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/LanguageServerSettingsViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns LanguageServerSettingsViewModel, LanguageServerSettingsItemViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents language server settings view model and keeps its related state and behavior together.
/// </summary>
public sealed class LanguageServerSettingsViewModel : ObservableObject
{
    /// <summary>
    /// Stores store locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ILanguageServerConfigurationStore _store;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = "Loading language-server settings…";
    /// <summary>
    /// Stores is busy locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Gets or updates items, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<LanguageServerSettingsItemViewModel> Items { get; } = [];
    /// <summary>
    /// Gets or updates refresh command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>
    /// Gets or updates add command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand AddCommand { get; }
    /// <summary>
    /// Gets or updates save command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<LanguageServerSettingsItemViewModel> SaveCommand { get; }
    /// <summary>
    /// Gets or updates delete command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<LanguageServerSettingsItemViewModel> DeleteCommand { get; }
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Reports whether is busy is true for the current state.
    /// </summary>
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    /// <summary>
    /// Performs refresh async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs the add step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs save async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs delete async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

/// <summary>
/// Represents language server settings item view model and keeps its related state and behavior together.
/// </summary>
public sealed class LanguageServerSettingsItemViewModel : ObservableObject
{
    /// <summary>
    /// Stores definition locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private LanguageServerDefinition _definition;
    /// <summary>
    /// Stores id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _id;
    /// <summary>
    /// Stores display name locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _displayName;
    /// <summary>
    /// Stores command locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _command;
    /// <summary>
    /// Stores arguments locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _arguments;
    /// <summary>
    /// Stores language id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _languageId;
    /// <summary>
    /// Stores extensions text locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _extensionsText;
    /// <summary>
    /// Stores is enabled locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isEnabled;
    /// <summary>
    /// Stores request timeout seconds locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _requestTimeoutSeconds;
    /// <summary>
    /// Stores initialization options json locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _initializationOptionsJson;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status;
    /// <summary>
    /// Stores is busy locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public LanguageServerDefinition Definition { get => _definition; set => SetProperty(ref _definition, value); }
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public string Id { get => _id; set => SetProperty(ref _id, value); }
    /// <summary>
    /// Gets or updates display name, the bindable or domain state represented by this property.
    /// </summary>
    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
    /// <summary>
    /// Gets or updates command, the bindable or domain state represented by this property.
    /// </summary>
    public string Command { get => _command; set => SetProperty(ref _command, value); }
    /// <summary>
    /// Gets or updates arguments, the bindable or domain state represented by this property.
    /// </summary>
    public string Arguments { get => _arguments; set => SetProperty(ref _arguments, value); }
    /// <summary>
    /// Gets or updates language id, the bindable or domain state represented by this property.
    /// </summary>
    public string LanguageId { get => _languageId; set => SetProperty(ref _languageId, value); }
    /// <summary>
    /// Gets or updates extensions text, the bindable or domain state represented by this property.
    /// </summary>
    public string ExtensionsText { get => _extensionsText; set => SetProperty(ref _extensionsText, value); }
    /// <summary>
    /// Reports whether is enabled is true for the current state.
    /// </summary>
    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
    /// <summary>
    /// Gets or updates request timeout seconds, the bindable or domain state represented by this property.
    /// </summary>
    public int RequestTimeoutSeconds { get => _requestTimeoutSeconds; set => SetProperty(ref _requestTimeoutSeconds, Math.Clamp(value, 2, 120)); }
    /// <summary>
    /// Gets or updates initialization options json, the bindable or domain state represented by this property.
    /// </summary>
    public string InitializationOptionsJson { get => _initializationOptionsJson; set => SetProperty(ref _initializationOptionsJson, value); }
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    /// <summary>
    /// Reports whether is busy is true for the current state.
    /// </summary>
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }
}
