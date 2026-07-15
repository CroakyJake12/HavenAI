using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed class ProviderConnectionsViewModel : ObservableObject
{
    private readonly IModelProviderRegistry _providers;
    private readonly IProviderConfigurationStore _configurations;
    private readonly IProviderSecretStore _secrets;
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

    public ObservableCollection<ProviderConnectionItemViewModel> Items { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand<ProviderConnectionItemViewModel> ConnectCommand { get; }
    public AsyncRelayCommand<ProviderConnectionItemViewModel> TestCommand { get; }
    public AsyncRelayCommand<ProviderConnectionItemViewModel> DisconnectCommand { get; }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

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
            await TestCoreAsync(item);
        }
        catch (Exception ex)
        {
            item.Status = $"Connection failed: {ex.Message}";
            Status = $"Could not connect {item.DisplayName}.";
        }
        finally
        {
            item.IsBusy = false;
        }
    }

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
            item.Status = $"Test failed: {ex.Message}";
            Status = $"{item.DisplayName} did not pass its connection test.";
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    private async Task TestCoreAsync(ProviderConnectionItemViewModel item)
    {
        var provider = _providers.GetRequired(item.Id);
        item.Status = "Testing connection…";
        var health = await provider.CheckHealthAsync(CancellationToken.None);
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

public sealed class ProviderConnectionItemViewModel : ObservableObject
{
    private string _endpoint;
    private string _apiKey = string.Empty;
    private bool _isConnected;
    private bool _isBusy;
    private string _status;
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

    public string Id { get; }
    public string DisplayName { get; }
    public ModelProviderKind Kind { get; }
    public string KindLabel => Kind == ModelProviderKind.OpenAICompatible ? "CUSTOM OPENAI-COMPATIBLE" : Kind.ToString().ToUpperInvariant();
    public string Endpoint { get => _endpoint; set => SetProperty(ref _endpoint, value); }
    public string ApiKey { get => _apiKey; set => SetProperty(ref _apiKey, value); }
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (!SetProperty(ref _isConnected, value))
                return;
            RaisePropertyChanged(nameof(ConnectionLabel));
            RaisePropertyChanged(nameof(ConnectLabel));
            RaisePropertyChanged(nameof(ApiKeyHint));
        }
    }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public string ModelSummary { get => _modelSummary; set => SetProperty(ref _modelSummary, value); }
    public string ConnectionLabel => IsConnected ? "Connected" : "Not connected";
    public string ConnectLabel => IsConnected ? "Update connection" : "Connect";
    public string ApiKeyHint => IsConnected ? "Leave blank to keep the saved API key" : "API key";
}
