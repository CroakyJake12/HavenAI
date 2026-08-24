using System.Text.Json;
using Haven.Application;
using Haven.Core;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Haven.Infrastructure;

/// <summary>Adapter over the maintained MCP C# SDK; SDK types do not escape Infrastructure.</summary>
public sealed class McpConnectionClient(IProviderSecretStore secrets) : IMcpConnectionClient
{
    private const int MaxSchemaCharacters = 256_000;
    private const int MaxResultCharacters = 2_000_000;
    private readonly Dictionary<Guid, SemaphoreSlim> _serializedGates = [];

    public async Task<(McpServerIdentity Identity, IReadOnlyList<McpExternalTool> Tools)> DiscoverAsync(ExternalConnection connection, CancellationToken cancellationToken)
    {
        var configuration = ReadConfiguration(connection);
        await using var client = await CreateClientAsync(connection, configuration, cancellationToken).ConfigureAwait(false);
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var mapped = tools.Select(tool => MapTool(tool.Name, tool.Description, tool.JsonSchema)).ToArray();
        return (new McpServerIdentity(client.ServerInfo?.Name, client.ServerInfo?.Version, client.NegotiatedProtocolVersion,
            BoundedJson(client.ServerCapabilities, MaxSchemaCharacters)), mapped);
    }

    public async Task<McpToolInvocationResult> InvokeAsync(ExternalConnection connection, string toolName, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
    {
        var configuration = ReadConfiguration(connection);
        var gate = configuration.SerializeInvocations ? GetGate(connection.Id) : null;
        if (gate is not null) await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var client = await CreateClientAsync(connection, configuration, cancellationToken).ConfigureAwait(false);
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var tool = tools.FirstOrDefault(item => item.Name.Equals(toolName, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"MCP tool '{toolName}' is no longer advertised by this server. Refresh the connection.");
            var values = arguments.ToDictionary(pair => pair.Key, pair => (object?)JsonSerializer.Deserialize<object>(pair.Value.GetRawText()), StringComparer.Ordinal);
            var result = await tool.CallAsync(values, cancellationToken: cancellationToken).ConfigureAwait(false);
            var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
            var contentJson = BoundedJson(result.Content, MaxResultCharacters);
            JsonElement? structured = result.StructuredContent is { } value ? BoundElement(value, MaxResultCharacters) : null;
            if (string.IsNullOrWhiteSpace(text) && structured is { } structuredValue) text = structuredValue.GetRawText();
            if (text.Length > MaxResultCharacters) text = text[..MaxResultCharacters] + "... [truncated by Haven]";
            return new McpToolInvocationResult(result.IsError is not true, text, structured, contentJson, result.IsError is true ? text : null);
        }
        finally { gate?.Release(); }
    }

    private async Task<McpClient> CreateClientAsync(ExternalConnection connection, McpConnectionConfiguration configuration, CancellationToken cancellationToken)
    {
        ExternalConnectionRegistryService.ValidateMcpConfiguration(configuration, connection.PresetKey.Equals("uefn", StringComparison.OrdinalIgnoreCase));
        IClientTransport transport;
        if (configuration.Transport == McpTransportKind.StreamableHttp)
        {
            var options = new HttpClientTransportOptions
            {
                Name = ExternalConnectionNaming.PluginName(connection.Name),
                Endpoint = new Uri(configuration.Endpoint!, UriKind.Absolute),
                TransportMode = HttpTransportMode.AutoDetect,
                ConnectionTimeout = TimeSpan.FromSeconds(configuration.TimeoutSeconds),
                EnableStandaloneGetStream = false
            };
            if (configuration.UseOAuth)
            {
                options.OAuth = new ClientOAuthOptions
                {
                    RedirectUri = new Uri(configuration.OAuthRedirectUri, UriKind.Absolute),
                    ClientId = string.IsNullOrWhiteSpace(configuration.OAuthClientId) ? null : configuration.OAuthClientId.Trim(),
                    ClientMetadataDocumentUri = string.IsNullOrWhiteSpace(configuration.OAuthClientMetadataDocumentUri) ? null : new Uri(configuration.OAuthClientMetadataDocumentUri, UriKind.Absolute),
                    Scopes = configuration.OAuthScopes,
                    TokenCache = new McpOAuthTokenCache(secrets, connection.Id),
                    AuthorizationCallbackHandler = McpOAuthBrowserAuthorization.AuthorizeAsync
                };
            }
            transport = new HttpClientTransport(options);
        }
        else if (configuration.Transport == McpTransportKind.Stdio)
        {
            transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = ExternalConnectionNaming.PluginName(connection.Name), Command = configuration.Command!, Arguments = configuration.Arguments?.ToArray() ?? [],
                WorkingDirectory = configuration.WorkingDirectory, InheritEnvironmentVariables = false,
                EnvironmentVariables = StdioClientTransportOptions.GetDefaultEnvironmentVariables(), ShutdownTimeout = TimeSpan.FromSeconds(Math.Min(10, configuration.TimeoutSeconds))
            });
        }
        else throw new InvalidOperationException("Unsupported MCP transport.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(configuration.UseOAuth
            ? TimeSpan.FromMinutes(5)
            : TimeSpan.FromSeconds(configuration.TimeoutSeconds));
        return await McpClient.CreateAsync(transport, cancellationToken: timeout.Token).ConfigureAwait(false);
    }

    private static McpConnectionConfiguration ReadConfiguration(ExternalConnection connection) =>
        JsonSerializer.Deserialize<McpConnectionConfiguration>(connection.ConfigurationJson) ?? throw new InvalidOperationException("The MCP connection configuration is invalid.");

    private static McpExternalTool MapTool(string name, string? description, JsonElement schema) =>
        new(Bound(name, 200), Bound(description ?? string.Empty, 2_000), BoundElement(schema, MaxSchemaCharacters));

    private SemaphoreSlim GetGate(Guid connectionId)
    {
        lock (_serializedGates) return _serializedGates.TryGetValue(connectionId, out var existing) ? existing : (_serializedGates[connectionId] = new SemaphoreSlim(1, 1));
    }

    private static JsonElement BoundElement(JsonElement element, int maxCharacters)
    {
        if (element.GetRawText().Length > maxCharacters) throw new InvalidOperationException("MCP schema or structured result exceeded Haven's safety size limit.");
        return element.Clone();
    }

    private static string BoundedJson<T>(T value, int maxCharacters)
    {
        var json = JsonSerializer.Serialize(value);
        if (json.Length <= maxCharacters) return json;
        return JsonSerializer.Serialize(new
        {
            source = "untrusted external MCP JSON",
            truncated = true,
            reason = "MCP JSON exceeded Haven's safety size limit."
        });
    }

    private static string Bound(string value, int max) => value.Length <= max ? value : value[..max];
}
