using Haven.Core;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Pages.Settings;

public sealed partial class SettingsHavenPage
{
    private ProviderConnectionsViewModel? _connections;

    private void InitializeConnections()
    {
        _connections = App.Services?.GetService<ProviderConnectionsViewModel>();
        _route.RefreshConnectionsRequested += RefreshConnectionsAsync;
        _route.ConnectServiceRequested += ConnectServiceAsync;
        _route.DisconnectServiceRequested += DisconnectServiceAsync;
        _route.TestProviderRequested += TestProviderAsync;
        _route.DisconnectProviderRequested += DisconnectProviderAsync;
        _route.UpdateProviderRequested += UpdateProviderAsync;
        _route.RefreshMcpConnectionRequested += RefreshMcpConnectionAsync;
        _route.RemoveMcpConnectionRequested += RemoveMcpConnectionAsync;
        _route.ConnectSuggestedMcpRequested += ConnectSuggestedMcpAsync;
        if (_connections is not null)
            SeedMcpSuggestions();
        _ = RefreshConnectionsAsync();
    }

    private void SeedMcpSuggestions()
    {
        if (_connections is null) return;
        _route.SetMcpSuggestions(_connections.SuggestedConnections
            .Select(item => new McpSuggestionSnapshot(
                item.Key,
                item.DisplayName,
                item.Description,
                item.Endpoint,
                item.SetupMethod,
                item.ActionLabel))
            .ToArray());
    }

    private async Task RefreshConnectionsAsync()
    {
        if (_connections is null)
        {
            _route.SetConnections([], [], [], "External connection services are unavailable in this session.", "Connection services are unavailable in this session.");
            return;
        }

        await _connections.RefreshCommand.ExecuteAsync();
        PushConnectionState("Connection status is up to date.");
    }

    private async Task ConnectServiceAsync(CalendarProviderKind kind)
    {
        if (_connections is null) return;
        var service = _connections.Services.FirstOrDefault(item => item.Kind == kind);
        if (service is null) return;
        await _connections.ConnectServiceCommand.ExecuteAsync(service);
        PushConnectionState(service.Status);
    }

    private async Task DisconnectServiceAsync(CalendarProviderKind kind)
    {
        if (_connections is null) return;
        var service = _connections.Services.FirstOrDefault(item => item.Kind == kind);
        if (service is null) return;
        await _connections.DisconnectServiceCommand.ExecuteAsync(service);
        PushConnectionState(service.Status);
    }

    private async Task TestProviderAsync(string id)
    {
        if (_connections is null) return;
        var provider = _connections.Items.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (provider is null) return;
        await _connections.TestCommand.ExecuteAsync(provider);
        PushConnectionState(provider.Status);
    }

    private async Task DisconnectProviderAsync(string id)
    {
        if (_connections is null) return;
        var provider = _connections.Items.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (provider is null) return;
        await _connections.DisconnectCommand.ExecuteAsync(provider);
        PushConnectionState(provider.Status);
    }

    private async Task UpdateProviderAsync(string id, string endpoint, string apiKey)
    {
        if (_connections is null) return;
        var provider = _connections.Items.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (provider is null) return;

        provider.Endpoint = endpoint;
        provider.ApiKey = apiKey;
        await _connections.ConnectCommand.ExecuteAsync(provider);
        PushConnectionState(provider.Status);
    }

    private async Task RefreshMcpConnectionAsync(Guid id)
    {
        if (_connections is null) return;
        var connection = _connections.ExternalConnections.FirstOrDefault(item => item.Id == id);
        if (connection is null) return;
        await _connections.RefreshExternalConnectionCommand.ExecuteAsync(connection);
        PushConnectionState(connection.Detail);
    }

    private async Task RemoveMcpConnectionAsync(Guid id)
    {
        if (_connections is null) return;
        var connection = _connections.ExternalConnections.FirstOrDefault(item => item.Id == id);
        if (connection is null) return;
        await _connections.RemoveExternalConnectionCommand.ExecuteAsync(connection);
        PushConnectionState($"{connection.DisplayName} removed.");
    }

    private async Task ConnectSuggestedMcpAsync(string key, string name, string endpoint)
    {
        if (_connections is null) return;
        var suggestion = _connections.SuggestedConnections.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
        if (suggestion is null) return;
        if (!string.IsNullOrWhiteSpace(name)) suggestion.ConnectionName = name.Trim();
        if (!string.IsNullOrWhiteSpace(endpoint)) suggestion.Endpoint = endpoint.Trim();
        await _connections.ConnectSuggestedConnectionCommand.ExecuteAsync(suggestion);
        PushConnectionState(suggestion.Status);
    }

    private void PushConnectionState(string? status)
    {
        if (_connections is null) return;

        var services = _connections.Services
            .Select(item => new ServiceConnectionSnapshot(
                item.Kind,
                item.DisplayName,
                item.CapabilityLabel,
                item.ConnectionLabel,
                item.AccountLabel,
                item.LastSyncLabel,
                item.Status,
                item.IsConfigured,
                item.IsConnected,
                item.IsBusy))
            .ToArray();

        var providers = _connections.Items
            .Select(item => new ProviderConnectionSnapshot(
                item.Id,
                item.DisplayName,
                item.KindLabel,
                item.Endpoint,
                item.ConnectionLabel,
                item.Status,
                item.ModelSummary,
                item.IsConnected,
                item.IsHealthy,
                item.IsBusy))
            .ToArray();

        var mcpConnections = _connections.ExternalConnections
            .Select(item => new McpConnectionSnapshot(
                item.Id,
                item.DisplayName,
                item.ProviderLabel,
                item.TransportLabel,
                item.ConnectionLabel,
                item.EnabledLabel,
                item.Detail,
                item.ServerLabel,
                item.ProtocolLabel,
                item.AuthenticationLabel,
                item.IsBusy))
            .ToArray();

        _route.SetConnections(
            services,
            providers,
            mcpConnections,
            _connections.ExternalConnectionsStatus,
            string.IsNullOrWhiteSpace(status) ? "Connections ready." : status);
    }
}
