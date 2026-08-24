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
        _ = RefreshConnectionsAsync();
    }

    private async Task RefreshConnectionsAsync()
    {
        if (_connections is null)
        {
            _route.SetConnections([], [], "Connection services are unavailable in this session.");
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

        _route.SetConnections(
            services,
            providers,
            string.IsNullOrWhiteSpace(status) ? "Connections ready." : status);
    }
}
