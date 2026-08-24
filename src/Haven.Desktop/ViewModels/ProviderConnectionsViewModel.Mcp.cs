using System.Collections.ObjectModel;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed partial class ProviderConnectionsViewModel
{
    private ExternalConnectionRegistryService? _externalConnections;
    private string _externalConnectionsStatus = "External connections are loading.";

    public ObservableCollection<SuggestedConnectionItemViewModel> SuggestedConnections { get; } = [];
    public ObservableCollection<ExternalConnectionItemViewModel> ExternalConnections { get; } = [];
    public AsyncRelayCommand<SuggestedConnectionItemViewModel> ConnectSuggestedConnectionCommand { get; private set; } = null!;
    public AsyncRelayCommand<ExternalConnectionItemViewModel> RefreshExternalConnectionCommand { get; private set; } = null!;
    public AsyncRelayCommand<ExternalConnectionItemViewModel> RemoveExternalConnectionCommand { get; private set; } = null!;
    public string ExternalConnectionsStatus { get => _externalConnectionsStatus; private set => SetProperty(ref _externalConnectionsStatus, value); }

    private void InitializeExternalConnections(ExternalConnectionRegistryService? registry)
    {
        _externalConnections = registry;
        SuggestedConnections.Add(new SuggestedConnectionItemViewModel(
            "uefn", "Unreal Editor for Fortnite (UEFN)",
            "Connect Haven to UEFN for Verse, devices, Scene Graph and official MCP playtesting capabilities. This preset is local-only and does not use OAuth.",
            "http://127.0.0.1:8000/mcp", "Connect UEFN", isAdvanced: false));
        SuggestedConnections.Add(new SuggestedConnectionItemViewModel(
            "custom-mcp", "Custom MCP Server",
            "Connect another MCP server using Streamable HTTP. Remote HTTPS servers use browser OAuth 2.1; loopback servers can connect without OAuth.",
            "https://example.com/mcp", "Connect MCP Server", isAdvanced: true));
        ConnectSuggestedConnectionCommand = new AsyncRelayCommand<SuggestedConnectionItemViewModel>(ConnectSuggestedConnectionAsync);
        RefreshExternalConnectionCommand = new AsyncRelayCommand<ExternalConnectionItemViewModel>(RefreshExternalConnectionAsync);
        RemoveExternalConnectionCommand = new AsyncRelayCommand<ExternalConnectionItemViewModel>(RemoveExternalConnectionAsync);
    }

    private async Task RefreshExternalConnectionsAsync()
    {
        if (_externalConnections is null) { ExternalConnectionsStatus = "External connection registry is unavailable in this build."; return; }
        var items = await _externalConnections.GetAllAsync(CancellationToken.None);
        ExternalConnections.Clear();
        foreach (var item in items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)) ExternalConnections.Add(new ExternalConnectionItemViewModel(item));
        ExternalConnectionsStatus = items.Count == 0 ? "No MCP connections configured yet." : items.Count == 1 ? "1 MCP connection configured." : $"{items.Count} MCP connections configured.";
    }

    private async Task ConnectSuggestedConnectionAsync(SuggestedConnectionItemViewModel? item)
    {
        if (item is null || _externalConnections is null) return;
        try
        {
            item.IsBusy = true;
            item.Status = item.Key == "uefn" ? "Checking the local Unreal MCP server..." : item.RequiresOAuth ? "Opening secure browser sign-in for the MCP server..." : "Connecting to local MCP server...";
            ExternalConnection connection;
            if (item.Key == "uefn") connection = await _externalConnections.ConnectUefnAsync(item.Endpoint, CancellationToken.None);
            else connection = await _externalConnections.AddMcpAsync(string.IsNullOrWhiteSpace(item.ConnectionName) ? "My MCP Server" : item.ConnectionName.Trim(),
                new McpConnectionConfiguration(McpTransportKind.StreamableHttp, item.Endpoint.Trim(), TimeoutSeconds: 30, UseOAuth: item.RequiresOAuth), CancellationToken.None);
            item.Status = connection.Status;
            await RefreshExternalConnectionsAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { item.Status = "Connection failed: " + ex.Message; }
        finally { item.IsBusy = false; }
    }

    private async Task RefreshExternalConnectionAsync(ExternalConnectionItemViewModel? item)
    {
        if (item is null || _externalConnections is null) return;
        var current = (await _externalConnections.GetAllAsync(CancellationToken.None)).FirstOrDefault(connection => connection.Id == item.Id);
        if (current is null) return;
        item.IsBusy = true;
        try { await _externalConnections.RefreshMcpAsync(current, CancellationToken.None); await RefreshExternalConnectionsAsync(); }
        finally { item.IsBusy = false; }
    }

    private async Task RemoveExternalConnectionAsync(ExternalConnectionItemViewModel? item)
    {
        if (item is null || _externalConnections is null) return;
        item.IsBusy = true;
        try { await _externalConnections.RemoveAsync(item.Id, CancellationToken.None); await RefreshExternalConnectionsAsync(); }
        finally { item.IsBusy = false; }
    }
}

public sealed class SuggestedConnectionItemViewModel : ObservableObject
{
    private string _endpoint;
    private string _connectionName = "My MCP Server";
    private string _status = string.Empty;
    private bool _isBusy;
    public SuggestedConnectionItemViewModel(string key, string name, string description, string endpoint, string actionLabel, bool isAdvanced)
    { Key = key; DisplayName = name; Description = description; _endpoint = endpoint; ActionLabel = actionLabel; IsAdvanced = isAdvanced; }
    public string Key { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string ActionLabel { get; }
    public bool IsAdvanced { get; }
    public bool RequiresOAuth => Key != "uefn" && Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint) && !endpoint.IsLoopback;
    public string SetupMethod => Key == "uefn" ? "MCP - local preset - no OAuth" : RequiresOAuth ? "MCP - Streamable HTTP - OAuth 2.1 browser sign-in" : "MCP - Streamable HTTP - local/no OAuth";
    public string Endpoint
    {
        get => _endpoint;
        set
        {
            if (!SetProperty(ref _endpoint, value)) return;
            RaisePropertyChanged(nameof(RequiresOAuth));
            RaisePropertyChanged(nameof(SetupMethod));
        }
    }
    public string ConnectionName { get => _connectionName; set => SetProperty(ref _connectionName, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public bool IsBusy { get => _isBusy; set { if (SetProperty(ref _isBusy, value)) RaisePropertyChanged(nameof(CanConnect)); } }
    public bool CanConnect => !IsBusy;
}

public sealed class ExternalConnectionItemViewModel : ObservableObject
{
    private bool _isBusy;
    public ExternalConnectionItemViewModel(ExternalConnection connection)
    {
        Id = connection.Id; DisplayName = connection.Name; ProviderLabel = connection.PresetKey == "uefn" ? "UEFN - MCP" : "MCP";
        ConnectionLabel = connection.State switch { ExternalConnectionState.Ready => "Connected", ExternalConnectionState.Offline => "Offline", ExternalConnectionState.Disabled => "Disabled", _ => "Needs attention" };
        Detail = connection.Status; ServerLabel = string.IsNullOrWhiteSpace(connection.ServerName) ? "Server identity not available" : $"{connection.ServerName} {connection.ServerVersion}".Trim();
        ProtocolLabel = string.IsNullOrWhiteSpace(connection.ProtocolVersion) ? "Protocol negotiated on connect" : "MCP " + connection.ProtocolVersion;
        AuthenticationLabel = DescribeAuthentication(connection);
    }
    public Guid Id { get; }
    public string DisplayName { get; }
    public string ProviderLabel { get; }
    public string ConnectionLabel { get; }
    public string Detail { get; }
    public string ServerLabel { get; }
    public string ProtocolLabel { get; }
    public string AuthenticationLabel { get; }
    public string PluginLabel => ExternalConnectionNaming.PluginName(DisplayName);
    public bool IsBusy { get => _isBusy; set { if (SetProperty(ref _isBusy, value)) RaisePropertyChanged(nameof(CanAct)); } }
    public bool CanAct => !IsBusy;

    private static string DescribeAuthentication(ExternalConnection connection)
    {
        if (connection.PresetKey.Equals("uefn", StringComparison.OrdinalIgnoreCase)) return "Authentication: none - local UEFN preset";
        try
        {
            var configuration = JsonSerializer.Deserialize<McpConnectionConfiguration>(connection.ConfigurationJson);
            return configuration?.UseOAuth == true
                ? "Authentication: OAuth 2.1 browser sign-in - tokens stored in Windows Credential Manager"
                : "Authentication: none - local connection";
        }
        catch (JsonException) { return "Authentication: configuration needs attention"; }
    }
}
