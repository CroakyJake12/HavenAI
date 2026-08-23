using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Single source of truth for persisted protocol/API connections owned by Haven.</summary>
public sealed class ExternalConnectionRegistryService(IExternalConnectionRepository repository, IMcpConnectionClient mcp, IProviderSecretStore? secrets = null)
{
    public Task<IReadOnlyList<ExternalConnection>> GetAllAsync(CancellationToken cancellationToken) => repository.GetAllAsync(cancellationToken);

    public async Task<ExternalConnection> ConnectUefnAsync(string? endpoint, CancellationToken cancellationToken)
    {
        var config = McpConnectionConfiguration.UefnDefault with
        {
            Endpoint = string.IsNullOrWhiteSpace(endpoint) ? McpConnectionConfiguration.UefnDefault.Endpoint : endpoint.Trim()
        };
        ValidateMcpConfiguration(config, requireLoopback: true);
        var existing = (await repository.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => item.Kind == ExternalConnectionKind.Mcp && item.PresetKey.Equals("uefn", StringComparison.OrdinalIgnoreCase));
        var now = DateTimeOffset.UtcNow;
        var candidate = existing is null
            ? new ExternalConnection(Guid.NewGuid(), "UEFN", "epic.uefn", ExternalConnectionKind.Mcp, "uefn", true, ExternalConnectionState.Connecting,
                "Checking Unreal MCP...", JsonSerializer.Serialize(config), null, null, null, now, now)
            : existing with { IsEnabled = true, State = ExternalConnectionState.Connecting, Status = "Checking Unreal MCP...", ConfigurationJson = JsonSerializer.Serialize(config), UpdatedAt = now };
        await repository.UpsertAsync(candidate, cancellationToken).ConfigureAwait(false);
        return await RefreshMcpAsync(candidate, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ExternalConnection> AddMcpAsync(string name, McpConnectionConfiguration configuration, CancellationToken cancellationToken)
    {
        ValidateMcpConfiguration(configuration, requireLoopback: false);
        var now = DateTimeOffset.UtcNow;
        var connection = new ExternalConnection(Guid.NewGuid(), name.Trim(), "mcp.custom", ExternalConnectionKind.Mcp, "custom-mcp", true, ExternalConnectionState.Connecting,
            "Connecting to MCP server...", JsonSerializer.Serialize(configuration), null, null, null, now, now);
        await repository.UpsertAsync(connection, cancellationToken).ConfigureAwait(false);
        return await RefreshMcpAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ExternalConnection> RefreshMcpAsync(ExternalConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            var discovery = await mcp.DiscoverAsync(connection, cancellationToken).ConfigureAwait(false);
            if (connection.PresetKey.Equals("uefn", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(discovery.Identity.Name, "unreal-mcp", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The endpoint responded, but it is not the UEFN Unreal MCP server (expected server identity 'unreal-mcp').");
            var updated = connection with
            {
                State = ExternalConnectionState.Ready,
                Status = discovery.Tools.Count == 1 ? "Connected - 1 MCP tool discovered." : $"Connected - {discovery.Tools.Count} MCP tools discovered.",
                ServerName = discovery.Identity.Name,
                ServerVersion = discovery.Identity.Version,
                ProtocolVersion = discovery.Identity.ProtocolVersion,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await repository.UpsertAsync(updated, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var message = Diagnose(connection, ex);
            var updated = connection with { State = ExternalConnectionState.Offline, Status = message, UpdatedAt = DateTimeOffset.UtcNow };
            await repository.UpsertAsync(updated, cancellationToken).ConfigureAwait(false);
            return updated;
        }
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        if (secrets is not null)
            await secrets.DeleteAsync(ExternalConnectionNaming.SecretProviderId(id), ExternalConnectionNaming.OAuthTokenSecretName, cancellationToken).ConfigureAwait(false);
        await repository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public static void ValidateMcpConfiguration(McpConnectionConfiguration configuration, bool requireLoopback)
    {
        if (configuration.TimeoutSeconds is < 1 or > 300) throw new ArgumentOutOfRangeException(nameof(configuration), "MCP timeout must be between 1 and 300 seconds.");
        if (configuration.Transport == McpTransportKind.StreamableHttp)
        {
            if (!Uri.TryCreate(configuration.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("Enter an absolute HTTP or HTTPS MCP endpoint.");
            var loopback = endpoint.IsLoopback;
            if ((configuration.LocalOnly || requireLoopback) && !loopback) throw new InvalidOperationException("This connection is local-only and must use a loopback address.");
            if (!loopback && endpoint.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("Remote MCP servers must use HTTPS.");
            if (requireLoopback && configuration.UseOAuth) throw new InvalidOperationException("The UEFN Unreal MCP preset is loopback-only and does not use OAuth.");
            if (configuration.UseOAuth)
            {
                if (!Uri.TryCreate(configuration.OAuthRedirectUri, UriKind.Absolute, out var redirect) || !redirect.IsLoopback || redirect.Scheme != Uri.UriSchemeHttp)
                    throw new InvalidOperationException("MCP OAuth requires an HTTP loopback redirect URI.");
                if (!string.IsNullOrWhiteSpace(configuration.OAuthClientMetadataDocumentUri) &&
                    (!Uri.TryCreate(configuration.OAuthClientMetadataDocumentUri, UriKind.Absolute, out var metadata) || metadata.Scheme != Uri.UriSchemeHttps))
                    throw new InvalidOperationException("MCP OAuth client metadata must use an absolute HTTPS URI.");
            }
        }
        else if (string.IsNullOrWhiteSpace(configuration.Command))
            throw new InvalidOperationException("A stdio MCP connection requires an executable command.");
    }

    private static string Diagnose(ExternalConnection connection, Exception exception)
    {
        var text = exception.Message;
        if (!connection.PresetKey.Equals("uefn", StringComparison.OrdinalIgnoreCase)) return "MCP connection unavailable: " + text;
        if (text.Contains("refused", StringComparison.OrdinalIgnoreCase) || text.Contains("actively refused", StringComparison.OrdinalIgnoreCase))
            return "UEFN MCP is not reachable. Make sure UEFN is running, Python Editor Scripting and UEFN MCP Toolsets are enabled, and the Unreal MCP server has started.";
        if (text.Contains("timed out", StringComparison.OrdinalIgnoreCase) || text.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return "UEFN MCP timed out. Check that the editor and MCP server are responsive, then verify the host, port and path in Advanced settings.";
        if (text.Contains("unreal-mcp", StringComparison.OrdinalIgnoreCase)) return text;
        return "UEFN MCP could not connect: " + text;
    }
}
