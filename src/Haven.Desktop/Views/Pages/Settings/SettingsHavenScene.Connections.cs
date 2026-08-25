using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Settings;

internal sealed record ProviderConnectionSnapshot(
    string Id,
    string DisplayName,
    string KindLabel,
    string Endpoint,
    string ConnectionLabel,
    string Status,
    string ModelSummary,
    bool IsConnected,
    bool IsHealthy,
    bool IsBusy);

internal sealed record ServiceConnectionSnapshot(
    CalendarProviderKind Kind,
    string DisplayName,
    string CapabilityLabel,
    string ConnectionLabel,
    string AccountLabel,
    string LastSyncLabel,
    string Status,
    bool IsConfigured,
    bool IsConnected,
    bool IsBusy);

internal sealed record McpSuggestionSnapshot(
    string Key,
    string DisplayName,
    string Description,
    string Endpoint,
    string SetupMethod,
    string ActionLabel);

internal sealed record McpConnectionSnapshot(
    Guid Id,
    string DisplayName,
    string ProviderLabel,
    string TransportLabel,
    string ConnectionLabel,
    string EnabledLabel,
    string Status,
    string ServerLabel,
    string ProtocolLabel,
    string AuthenticationLabel,
    bool IsBusy);

internal sealed partial class SettingsHavenScene
{
    private Container _servicesHost = null!;
    private Container _providersHost = null!;
    private Container _mcpSuggestionsHost = null!;
    private Container _mcpHost = null!;
    private HavenText _connectionsStatus = null!;
    private HavenText _mcpStatus = null!;
    private HavenButton _refreshConnectionsButton = null!;
    private readonly Dictionary<string, Input> _mcpNameInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Input> _mcpEndpointInputs = new(StringComparer.Ordinal);
    private readonly HashSet<Guid> _pendingMcpRemovalIds = [];
    private IReadOnlyList<McpConnectionSnapshot> _mcpConnections = [];

    public event Func<Task>? RefreshConnectionsRequested;
    public event Func<CalendarProviderKind, Task>? ConnectServiceRequested;
    public event Func<CalendarProviderKind, Task>? DisconnectServiceRequested;
    public event Func<string, Task>? TestProviderRequested;
    public event Func<string, Task>? DisconnectProviderRequested;
    public event Func<string, string, string, Task>? UpdateProviderRequested;
    public event Func<Guid, Task>? RefreshMcpConnectionRequested;
    public event Func<Guid, Task>? RemoveMcpConnectionRequested;
    public event Func<string, string, string, Task>? ConnectSuggestedMcpRequested;

    private Container BuildConnectionsSection()
    {
        var section = Section("Settings.Integrations");

        var intro = Card("Settings.Integrations.Overview");
        intro.Add(Heading("Settings.Integrations.Heading", "Connections", 20));
        intro.Add(Muted("Settings.Integrations.Description",
            "Connect services Haven can actually use. Every card shows what access it receives and whether the connection is currently usable."));
        _connectionsStatus = Muted("Settings.Integrations.Status", "Loading connection status…");
        _refreshConnectionsButton = new HavenButton
        {
            Name = "Settings.Integrations.Refresh",
            Content = "Refresh connections",
            Variant = ButtonVariant.Secondary
        };
        _refreshConnectionsButton.Invoked += async (_, _) =>
        {
            if (RefreshConnectionsRequested is { } refresh)
                await refresh();
        };
        intro.Add(_connectionsStatus);
        intro.Add(_refreshConnectionsButton);
        section.Add(intro);

        var services = Card("Settings.Integrations.Services");
        services.Add(Heading("Settings.Integrations.ServicesHeading", "Connected services", 18));
        services.Add(Muted("Settings.Integrations.ServicesDescription",
            "Calendar connections can read calendar data and create, update or delete calendar items when Haven performs a calendar action. Sign-in happens with the provider in its own authentication flow; Haven does not ask for or store your Google or Microsoft password."));
        _servicesHost = new Container { Name = "Settings.Integrations.ServicesHost", Layout = HavenLayout.Vertical };
        _servicesHost.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        _servicesHost.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        services.Add(_servicesHost);
        section.Add(services);

        var providers = Card("Settings.Integrations.ModelProviders");
        providers.Add(Heading("Settings.Integrations.ModelProvidersHeading", "Cloud model providers", 18));
        providers.Add(Muted("Settings.Integrations.ModelProvidersDescription",
            "When a cloud model is selected, Haven sends the request content needed to answer that model request to the configured provider. Saved API keys live in Windows Credential Manager and are never redisplayed here."));
        providers.Add(Muted("Settings.Integrations.SecretNotice",
            "API keys use Haven.UI secure input: characters are masked, clipboard export is disabled, and platform automation receives no secret value."));
        _providersHost = new Container { Name = "Settings.Integrations.ProvidersHost", Layout = HavenLayout.Vertical };
        _providersHost.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        _providersHost.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        providers.Add(_providersHost);
        section.Add(providers);

        var mcp = Card("Settings.Integrations.Mcp");
        mcp.Add(Heading("Settings.Integrations.McpHeading", "MCP & external connections", 18));
        mcp.Add(Muted("Settings.Integrations.McpDescription",
            "Model Context Protocol servers connect Haven to tools from other apps and services. Remote HTTPS servers sign in with OAuth 2.1 in your browser; local loopback and preset servers connect without OAuth."));
        _mcpSuggestionsHost = new Container { Name = "Settings.Integrations.Mcp.SuggestionsHost", Layout = HavenLayout.Vertical };
        _mcpSuggestionsHost.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        _mcpSuggestionsHost.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        mcp.Add(_mcpSuggestionsHost);
        _mcpStatus = Muted("Settings.Integrations.Mcp.Status", "Loading MCP connections…");
        mcp.Add(_mcpStatus);
        _mcpHost = new Container { Name = "Settings.Integrations.Mcp.Host", Layout = HavenLayout.Vertical };
        _mcpHost.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        _mcpHost.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        mcp.Add(_mcpHost);
        section.Add(mcp);

        return section;
    }

    public void SetConnections(
        IReadOnlyList<ServiceConnectionSnapshot> services,
        IReadOnlyList<ProviderConnectionSnapshot> providers,
        IReadOnlyList<McpConnectionSnapshot> mcpConnections,
        string mcpStatus,
        string status)
    {
        _connectionsStatus.Content = status;
        RebuildServices(services);
        RebuildProviders(providers);
        SetMcpConnections(mcpConnections, mcpStatus);
    }

    public void SetMcpSuggestions(IReadOnlyList<McpSuggestionSnapshot> suggestions)
    {
        foreach (var child in _mcpSuggestionsHost.Children.ToArray())
            _mcpSuggestionsHost.Remove(child);
        _mcpNameInputs.Clear();
        _mcpEndpointInputs.Clear();

        foreach (var suggestion in suggestions)
        {
            var card = ConnectionCard($"Settings.Integrations.Mcp.Suggest.{suggestion.Key}");
            card.Add(Heading(null, suggestion.DisplayName, 16));
            card.Add(Muted(null, suggestion.Description));
            card.Add(Muted(null, suggestion.SetupMethod));

            var name = new Input
            {
                Name = $"Settings.Integrations.Mcp.Suggest.{suggestion.Key}.Name",
                Placeholder = "Server name"
            };
            name.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            name.Accessibility.AccessibleName = $"{suggestion.DisplayName} server name";
            _mcpNameInputs[suggestion.Key] = name;
            card.Add(name);

            var endpoint = new Input
            {
                Name = $"Settings.Integrations.Mcp.Suggest.{suggestion.Key}.Endpoint",
                Text = suggestion.Endpoint,
                Placeholder = "https://example.com/mcp"
            };
            endpoint.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            endpoint.Accessibility.AccessibleName = $"{suggestion.DisplayName} endpoint";
            _mcpEndpointInputs[suggestion.Key] = endpoint;
            card.Add(endpoint);

            var actions = ActionRow();
            var connect = new HavenButton
            {
                Name = $"Settings.Integrations.Mcp.Suggest.{suggestion.Key}.Connect",
                Content = suggestion.ActionLabel,
                Variant = ButtonVariant.Primary
            };
            connect.Accessibility.AccessibleName = suggestion.ActionLabel;
            connect.Invoked += async (_, _) =>
            {
                if (ConnectSuggestedMcpRequested is { } handler)
                    await handler(suggestion.Key, name.Text, endpoint.Text);
            };
            actions.Add(connect);
            card.Add(actions);
            _mcpSuggestionsHost.Add(card);
        }
    }

    public void SetMcpConnections(IReadOnlyList<McpConnectionSnapshot> connections, string status)
    {
        _mcpConnections = connections;
        _pendingMcpRemovalIds.IntersectWith(connections.Select(item => item.Id));
        _mcpStatus.Content = string.IsNullOrWhiteSpace(status) ? "MCP connection status is unavailable." : status;
        RebuildMcp();
    }

    private void RebuildMcp()
    {
        foreach (var child in _mcpHost.Children.ToArray())
            _mcpHost.Remove(child);

        if (_mcpConnections.Count == 0)
        {
            _mcpHost.Add(Muted("Settings.Integrations.Mcp.Empty",
                "No MCP connections are configured yet. Connect a preset above to add one."));
            return;
        }

        foreach (var connection in _mcpConnections)
        {
            var card = ConnectionCard($"Settings.Integrations.Mcp.{connection.Id}");
            card.Add(Heading(null, connection.DisplayName, 16));
            card.Add(Muted(null, $"{connection.ProviderLabel} · {connection.TransportLabel} · {connection.ConnectionLabel} · {connection.EnabledLabel}"));
            card.Add(Muted(null, connection.ServerLabel));
            card.Add(Muted(null, connection.ProtocolLabel));
            card.Add(Muted(null, connection.AuthenticationLabel));
            if (!string.IsNullOrWhiteSpace(connection.Status))
                card.Add(Muted(null, connection.Status));

            var actions = ActionRow();
            var refresh = new HavenButton
            {
                Name = $"Settings.Integrations.Mcp.{connection.Id}.Refresh",
                Content = "Refresh / test",
                Variant = ButtonVariant.Secondary
            };
            refresh.Accessibility.AccessibleName = $"Refresh or test {connection.DisplayName}";
            refresh.SetValue(HavenProperties.Enabled, !connection.IsBusy);
            refresh.Invoked += async (_, _) =>
            {
                if (RefreshMcpConnectionRequested is { } handler)
                    await handler(connection.Id);
            };
            actions.Add(refresh);

            if (_pendingMcpRemovalIds.Contains(connection.Id))
                card.Add(BuildMcpRemovalConfirmation(connection));
            else
                actions.Add(BuildMcpRemoveButton(connection));

            card.Add(actions);
            _mcpHost.Add(card);
        }
    }

    private HavenButton BuildMcpRemoveButton(McpConnectionSnapshot connection)
    {
        var remove = new HavenButton
        {
            Name = $"Settings.Integrations.Mcp.{connection.Id}.Remove",
            Content = "Remove",
            Variant = ButtonVariant.Danger
        };
        remove.Accessibility.AccessibleName = $"Remove {connection.DisplayName}";
        remove.SetValue(HavenProperties.Enabled, !connection.IsBusy);
        remove.Invoked += (_, _) =>
        {
            _pendingMcpRemovalIds.Add(connection.Id);
            RebuildMcp();
        };
        return remove;
    }

    private Container BuildMcpRemovalConfirmation(McpConnectionSnapshot connection)
    {
        var confirmation = new Container { Name = $"Settings.Integrations.Mcp.{connection.Id}.Confirm", Layout = HavenLayout.Vertical };
        confirmation.SetValue(HavenProperties.Background, "Surface");
        confirmation.SetValue(HavenProperties.BorderColor, "Danger");
        confirmation.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        confirmation.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        confirmation.SetValue(HavenProperties.Padding, HavenThickness.Parse("13px"));
        confirmation.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        confirmation.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        confirmation.Add(Muted($"Settings.Integrations.Mcp.{connection.Id}.RemoveWarning",
            $"Removing {connection.DisplayName} disconnects its tools from Haven and cannot be undone from this page."));
        var confirmActions = ActionRow();

        var confirm = new HavenButton
        {
            Name = $"Settings.Integrations.Mcp.{connection.Id}.ConfirmRemove",
            Content = "Confirm removal",
            Variant = ButtonVariant.Danger
        };
        confirm.Accessibility.AccessibleName = $"Confirm removing {connection.DisplayName}";
        confirm.SetValue(HavenProperties.Enabled, !connection.IsBusy);
        confirm.Invoked += async (_, _) =>
        {
            if (RemoveMcpConnectionRequested is { } handler)
                await handler(connection.Id);
        };
        confirmActions.Add(confirm);

        var cancel = new HavenButton
        {
            Name = $"Settings.Integrations.Mcp.{connection.Id}.CancelRemove",
            Content = "Cancel",
            Variant = ButtonVariant.Ghost
        };
        cancel.Accessibility.AccessibleName = $"Cancel removing {connection.DisplayName}";
        cancel.Invoked += (_, _) =>
        {
            _pendingMcpRemovalIds.Remove(connection.Id);
            RebuildMcp();
        };
        confirmActions.Add(cancel);

        confirmation.Add(confirmActions);
        return confirmation;
    }

    private void RebuildServices(IReadOnlyList<ServiceConnectionSnapshot> services)
    {
        foreach (var child in _servicesHost.Children.ToArray())
            _servicesHost.Remove(child);

        if (services.Count == 0)
        {
            _servicesHost.Add(Muted("Settings.Integrations.ServicesEmpty",
                "No supported service providers are currently registered."));
            return;
        }

        foreach (var service in services)
        {
            var card = ConnectionCard($"Settings.Integrations.Service.{service.Kind}");
            card.Add(Heading(null, service.DisplayName, 16));
            card.Add(Muted(null, $"{service.CapabilityLabel} · {service.ConnectionLabel}"));
            card.Add(Muted(null, service.AccountLabel));
            card.Add(Muted(null, service.LastSyncLabel));
            if (!string.IsNullOrWhiteSpace(service.Status))
                card.Add(Muted(null, service.Status));

            var actions = ActionRow();
            var connect = new HavenButton
            {
                Name = $"Settings.Integrations.Service.{service.Kind}.Connect",
                Content = service.IsConnected ? "Connected" : "Connect",
                Variant = ButtonVariant.Primary
            };
            connect.SetValue(HavenProperties.Enabled, service.IsConfigured && !service.IsConnected && !service.IsBusy);
            connect.Invoked += async (_, _) =>
            {
                if (ConnectServiceRequested is { } handler)
                    await handler(service.Kind);
            };
            actions.Add(connect);

            var disconnect = new HavenButton
            {
                Name = $"Settings.Integrations.Service.{service.Kind}.Disconnect",
                Content = "Disconnect",
                Variant = ButtonVariant.Danger
            };
            disconnect.SetValue(HavenProperties.Enabled, service.IsConnected && !service.IsBusy);
            disconnect.Invoked += async (_, _) =>
            {
                if (DisconnectServiceRequested is { } handler)
                    await handler(service.Kind);
            };
            actions.Add(disconnect);
            card.Add(actions);
            _servicesHost.Add(card);
        }
    }

    private void RebuildProviders(IReadOnlyList<ProviderConnectionSnapshot> providers)
    {
        foreach (var child in _providersHost.Children.ToArray())
            _providersHost.Remove(child);

        if (providers.Count == 0)
        {
            _providersHost.Add(Muted("Settings.Integrations.ProvidersEmpty",
                "No cloud model providers are currently configured."));
            return;
        }

        foreach (var provider in providers)
        {
            var card = ConnectionCard($"Settings.Integrations.Provider.{provider.Id}");
            card.Add(Heading(null, provider.DisplayName, 16));
            card.Add(Muted(null, $"{provider.KindLabel} · {provider.ConnectionLabel}"));
            if (!string.IsNullOrWhiteSpace(provider.ModelSummary))
                card.Add(Muted(null, provider.ModelSummary));
            if (!string.IsNullOrWhiteSpace(provider.Status))
                card.Add(Muted(null, provider.Status));

            var endpoint = new Input
            {
                Name = $"Settings.Integrations.Provider.{provider.Id}.Endpoint",
                Text = provider.Endpoint,
                Placeholder = "Provider endpoint"
            };
            endpoint.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            endpoint.SetValue(HavenProperties.Enabled, !provider.IsBusy);
            endpoint.Accessibility.AccessibleName = $"{provider.DisplayName} endpoint";
            card.Add(endpoint);

            var secret = new Input
            {
                Name = $"Settings.Integrations.Provider.{provider.Id}.ApiKey",
                Placeholder = provider.IsConnected ? "Leave blank to keep the saved API key" : "API key",
                IsSecret = true
            };
            secret.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            secret.SetValue(HavenProperties.Enabled, !provider.IsBusy);
            secret.Accessibility.AccessibleName = $"{provider.DisplayName} API key";
            card.Add(secret);

            var actions = ActionRow();
            var update = new HavenButton
            {
                Name = $"Settings.Integrations.Provider.{provider.Id}.Update",
                Content = provider.IsConnected ? "Update connection" : "Connect",
                Variant = ButtonVariant.Primary
            };
            update.SetValue(HavenProperties.Enabled, !provider.IsBusy);
            update.Invoked += async (_, _) =>
            {
                if (UpdateProviderRequested is { } handler)
                    await handler(provider.Id, endpoint.Text.Trim(), secret.Text);
            };
            actions.Add(update);

            var test = new HavenButton
            {
                Name = $"Settings.Integrations.Provider.{provider.Id}.Test",
                Content = "Test connection",
                Variant = ButtonVariant.Secondary
            };
            test.SetValue(HavenProperties.Enabled, provider.IsConnected && !provider.IsBusy);
            test.Invoked += async (_, _) =>
            {
                if (TestProviderRequested is { } handler)
                    await handler(provider.Id);
            };
            actions.Add(test);

            var disconnect = new HavenButton
            {
                Name = $"Settings.Integrations.Provider.{provider.Id}.Disconnect",
                Content = "Disconnect",
                Variant = ButtonVariant.Danger
            };
            disconnect.SetValue(HavenProperties.Enabled, provider.IsConnected && !provider.IsBusy);
            disconnect.Invoked += async (_, _) =>
            {
                if (DisconnectProviderRequested is { } handler)
                    await handler(provider.Id);
            };
            actions.Add(disconnect);
            card.Add(actions);

            _providersHost.Add(card);
        }
    }

    private static Container ConnectionCard(string name)
    {
        var card = new Container { Name = name, Layout = HavenLayout.Vertical };
        card.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        card.SetValue(HavenProperties.Padding, HavenThickness.Parse("13px"));
        card.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        card.SetValue(HavenProperties.Background, "Surface");
        card.SetValue(HavenProperties.BorderColor, "Border");
        card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        return card;
    }

    private static Container ActionRow()
    {
        var row = new Container { Layout = HavenLayout.Wrap };
        row.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        row.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        return row;
    }
}
