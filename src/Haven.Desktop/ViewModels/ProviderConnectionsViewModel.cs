/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/ProviderConnectionsViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns ProviderConnectionsViewModel, ProviderConnectionItemViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents provider connections view model and keeps its related state and behavior together.
/// </summary>
public sealed class ProviderConnectionsViewModel : ObservableObject
{
    /// <summary>
    /// Stores providers locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IModelProviderRegistry _providers;
    /// <summary>
    /// Stores configurations locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IProviderConfigurationStore _configurations;
    /// <summary>
    /// Stores secrets locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IProviderSecretStore _secrets;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = "Loading provider settings…";

    public ProviderConnectionsViewModel(
        IModelProviderRegistry providers,
        IProviderConfigurationStore configurations,
        IProviderSecretStore secrets)
    {
        _providers = providers;
        _configurations = configurations;
        _secrets = secrets;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ConnectCommand = new AsyncRelayCommand<ProviderConnectionItemViewModel>(ConnectAsync);
        TestCommand = new AsyncRelayCommand<ProviderConnectionItemViewModel>(TestAsync);
        DisconnectCommand = new AsyncRelayCommand<ProviderConnectionItemViewModel>(DisconnectAsync);
        _ = RefreshAsync();
    }

    /// <summary>
    /// Gets or updates items, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ProviderConnectionItemViewModel> Items { get; } = [];
    /// <summary>
    /// Gets or updates refresh command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>
    /// Gets or updates connect command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<ProviderConnectionItemViewModel> ConnectCommand { get; }
    /// <summary>
    /// Gets or updates test command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<ProviderConnectionItemViewModel> TestCommand { get; }
    /// <summary>
    /// Gets or updates disconnect command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<ProviderConnectionItemViewModel> DisconnectCommand { get; }
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>
    /// Performs refresh async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RefreshAsync()
    {
        try
        {
            var configurations = await _configurations.GetAllAsync(CancellationToken.None);
            var existing = Items.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
            var refreshed = new List<ProviderConnectionItemViewModel>();

            foreach (var provider in _providers.Providers.Where(provider => !provider.IsLocal))
            {
                var configuration = configurations.FirstOrDefault(item => item.Id.Equals(provider.Id, StringComparison.OrdinalIgnoreCase));
                if (configuration is null)
                    continue;

                var hasSecret = !string.IsNullOrWhiteSpace(await _secrets.GetAsync(provider.Id, "api-key", CancellationToken.None));
                if (!existing.TryGetValue(provider.Id, out var item))
                {
                    item = new ProviderConnectionItemViewModel(
                        provider.Id,
                        provider.DisplayName,
                        provider.Kind,
                        configuration.Endpoint,
                        configuration.IsEnabled && hasSecret);
                }
                else
                {
                    item.Endpoint = configuration.Endpoint;
                    item.IsConnected = configuration.IsEnabled && hasSecret;
                }

                item.IsHealthy = false;
                item.Status = item.IsConnected
                    ? "Configured. Use Test to verify the saved credentials and discover models."
                    : "Not connected.";
                item.ModelSummary = "Models not checked yet";
                refreshed.Add(item);
            }

            Items.Clear();
            foreach (var item in refreshed.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
                Items.Add(item);

            Status = Items.Count == 0 ? "No cloud providers are registered." : $"{Items.Count} cloud providers available.";
        }
        catch (Exception ex)
        {
            Status = $"Could not load provider settings: {ex.Message}";
        }
    }

    /// <summary>
    /// Performs connect async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ConnectAsync(ProviderConnectionItemViewModel? item)
    {
        if (item is null)
            return;

        try
        {
            item.IsBusy = true;
            item.Status = "Saving connection…";

            if (!Uri.TryCreate(item.Endpoint.Trim(), UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("Enter an absolute HTTP or HTTPS endpoint.");

            var storedSecret = await _secrets.GetAsync(item.Id, "api-key", CancellationToken.None);
            if (string.IsNullOrWhiteSpace(item.ApiKey) && string.IsNullOrWhiteSpace(storedSecret))
                throw new InvalidOperationException("Enter an API key before connecting.");

            if (!string.IsNullOrWhiteSpace(item.ApiKey))
            {
                await _secrets.SetAsync(item.Id, "api-key", item.ApiKey.Trim(), CancellationToken.None);
                item.ApiKey = string.Empty;
            }

            var current = await _configurations.GetAsync(item.Id, CancellationToken.None);
            var configuration = new ProviderConfiguration(
                item.Id,
                item.Kind,
                item.DisplayName,
                endpoint.ToString(),
                true,
                false,
                current?.AllowCloudFallback ?? false,
                current?.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                DateTimeOffset.UtcNow);

            await _configurations.UpsertAsync(configuration, CancellationToken.None);
            item.Endpoint = configuration.Endpoint;
            item.IsConnected = true;
            item.IsHealthy = false;
            await TestCoreAsync(item);
        }
        catch (Exception ex)
        {
            item.IsHealthy = false;
            item.Status = $"Connection failed: {ex.Message}";
            Status = $"Could not connect {item.DisplayName}.";
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    /// <summary>
    /// Performs test async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task TestAsync(ProviderConnectionItemViewModel? item)
    {
        if (item is null)
            return;

        try
        {
            item.IsBusy = true;
            await TestCoreAsync(item);
        }
        catch (Exception ex)
        {
            item.IsHealthy = false;
            item.Status = $"Test failed: {ex.Message}";
            Status = $"{item.DisplayName} did not pass its connection test.";
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    /// <summary>
    /// Performs test core async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task TestCoreAsync(ProviderConnectionItemViewModel item)
    {
        var provider = _providers.GetRequired(item.Id);
        item.Status = "Testing connection…";
        var health = await provider.CheckHealthAsync(CancellationToken.None);
        item.IsHealthy = health.IsHealthy;
        item.Status = health.Message;

        if (!health.IsHealthy)
        {
            item.ModelSummary = "Model discovery unavailable";
            Status = $"{item.DisplayName} is configured but not healthy.";
            return;
        }

        var models = await provider.GetModelsAsync(CancellationToken.None);
        item.ModelSummary = models.Count == 1 ? "1 model discovered" : $"{models.Count} models discovered";
        item.IsConnected = true;
        Status = $"{item.DisplayName} is connected.";
    }

    /// <summary>
    /// Performs disconnect async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task DisconnectAsync(ProviderConnectionItemViewModel? item)
    {
        if (item is null)
            return;

        try
        {
            item.IsBusy = true;
            await _secrets.DeleteAsync(item.Id, "api-key", CancellationToken.None);
            var current = await _configurations.GetAsync(item.Id, CancellationToken.None);
            if (current is not null)
                await _configurations.UpsertAsync(current with { IsEnabled = false, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);

            item.ApiKey = string.Empty;
            item.IsConnected = false;
            item.IsHealthy = false;
            item.ModelSummary = "Models not checked yet";
            item.Status = "Disconnected. The saved API key was removed from Windows Credential Manager.";
            Status = $"Disconnected {item.DisplayName}.";
        }
        catch (Exception ex)
        {
            item.Status = $"Disconnect failed: {ex.Message}";
            Status = $"Could not disconnect {item.DisplayName}.";
        }
        finally
        {
            item.IsBusy = false;
        }
    }
}

/// <summary>
/// Represents provider connection item view model and keeps its related state and behavior together.
/// </summary>
public sealed class ProviderConnectionItemViewModel : ObservableObject
{
    /// <summary>
    /// Stores endpoint locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _endpoint;
    /// <summary>
    /// Stores api key locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _apiKey = string.Empty;
    /// <summary>
    /// Stores is connected locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isConnected;
    /// <summary>
    /// Stores is healthy locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isHealthy;
    /// <summary>
    /// Stores is busy locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isBusy;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status;
    /// <summary>
    /// Stores model summary locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _modelSummary = "Models not checked yet";

    public ProviderConnectionItemViewModel(
        string id,
        string displayName,
        ModelProviderKind kind,
        string endpoint,
        bool isConnected)
    {
        Id = id;
        DisplayName = displayName;
        Kind = kind;
        _endpoint = endpoint;
        _isConnected = isConnected;
        _status = isConnected ? "Configured." : "Not connected.";
    }

    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public string Id { get; }
    /// <summary>
    /// Gets or updates display name, the bindable or domain state represented by this property.
    /// </summary>
    public string DisplayName { get; }
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public ModelProviderKind Kind { get; }
    /// <summary>
    /// Gets or updates kind label, the bindable or domain state represented by this property.
    /// </summary>
    public string KindLabel => Kind == ModelProviderKind.OpenAICompatible ? "CUSTOM OPENAI-COMPATIBLE" : Kind.ToString().ToUpperInvariant();
    /// <summary>
    /// Gets or updates endpoint, the bindable or domain state represented by this property.
    /// </summary>
    public string Endpoint { get => _endpoint; set => SetProperty(ref _endpoint, value); }
    /// <summary>
    /// Gets or updates api key, the bindable or domain state represented by this property.
    /// </summary>
    public string ApiKey { get => _apiKey; set => SetProperty(ref _apiKey, value); }
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (!SetProperty(ref _isConnected, value))
                return;
            RaiseActionProperties();
            RaiseConnectionProperties();
        }
    }
    public bool IsHealthy
    {
        get => _isHealthy;
        set
        {
            if (!SetProperty(ref _isHealthy, value))
                return;
            RaisePropertyChanged(nameof(ConnectionLabel));
        }
    }
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (!SetProperty(ref _isBusy, value))
                return;
            RaiseActionProperties();
        }
    }
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    /// <summary>
    /// Gets or updates model summary, the bindable or domain state represented by this property.
    /// </summary>
    public string ModelSummary { get => _modelSummary; set => SetProperty(ref _modelSummary, value); }
    /// <summary>
    /// Gets or updates connection label, the bindable or domain state represented by this property.
    /// </summary>
    public string ConnectionLabel => !IsConnected ? "Not connected" : IsHealthy ? "Connected" : "Configured";
    /// <summary>
    /// Gets or updates connect label, the bindable or domain state represented by this property.
    /// </summary>
    public string ConnectLabel => IsConnected ? "Update connection" : "Connect";
    /// <summary>
    /// Gets or updates api key hint, the bindable or domain state represented by this property.
    /// </summary>
    public string ApiKeyHint => IsConnected ? "Leave blank to keep the saved API key" : "API key";
    /// <summary>
    /// Reports whether can connect is true for the current state.
    /// </summary>
    public bool CanConnect => !IsBusy;
    /// <summary>
    /// Reports whether can test is true for the current state.
    /// </summary>
    public bool CanTest => IsConnected && !IsBusy;
    /// <summary>
    /// Reports whether can disconnect is true for the current state.
    /// </summary>
    public bool CanDisconnect => IsConnected && !IsBusy;

    /// <summary>
    /// Performs the raise action properties step owned by this component.
    /// </summary>
    private void RaiseActionProperties()
    {
        RaisePropertyChanged(nameof(CanConnect));
        RaisePropertyChanged(nameof(CanTest));
        RaisePropertyChanged(nameof(CanDisconnect));
    }

    /// <summary>
    /// Performs the raise connection properties step owned by this component.
    /// </summary>
    private void RaiseConnectionProperties()
    {
        RaisePropertyChanged(nameof(ConnectionLabel));
        RaisePropertyChanged(nameof(ConnectLabel));
        RaisePropertyChanged(nameof(ApiKeyHint));
    }
}
