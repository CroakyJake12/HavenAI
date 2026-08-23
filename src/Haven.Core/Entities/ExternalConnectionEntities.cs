using System.Text.Json;

namespace Haven.Core;

public enum ExternalConnectionKind { Calendar = 0, Mcp = 1 }
public enum McpTransportKind { StreamableHttp = 0, Stdio = 1 }
public enum ExternalConnectionState { Disconnected = 0, Connecting = 1, Ready = 2, Offline = 3, NeedsAttention = 4, Disabled = 5 }

/// <summary>Non-secret persisted metadata for one external service connection.</summary>
public sealed record ExternalConnection(
    Guid Id,
    string Name,
    string ProviderKey,
    ExternalConnectionKind Kind,
    string PresetKey,
    bool IsEnabled,
    ExternalConnectionState State,
    string Status,
    string ConfigurationJson,
    string? ServerName,
    string? ServerVersion,
    string? ProtocolVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>MCP transport configuration. Secrets are referenced separately and never stored here.</summary>
public sealed record McpConnectionConfiguration(
    McpTransportKind Transport,
    string? Endpoint = null,
    string? Command = null,
    IReadOnlyList<string>? Arguments = null,
    string? WorkingDirectory = null,
    bool LocalOnly = false,
    bool SerializeInvocations = false,
    int TimeoutSeconds = 30,
    bool UseOAuth = false,
    string? OAuthClientId = null,
    string? OAuthClientMetadataDocumentUri = null,
    IReadOnlyList<string>? OAuthScopes = null,
    string OAuthRedirectUri = "http://127.0.0.1:52117/mcp-oauth/")
{
    public static McpConnectionConfiguration UefnDefault { get; } = new(
        McpTransportKind.StreamableHttp,
        "http://127.0.0.1:8000/mcp",
        LocalOnly: true,
        SerializeInvocations: true,
        TimeoutSeconds: 30);
}

public sealed record McpServerIdentity(string? Name, string? Version, string? ProtocolVersion, string CapabilitiesJson);

public sealed record McpExternalTool(
    string Name,
    string Description,
    JsonElement InputSchema,
    JsonElement? OutputSchema = null,
    string MetadataJson = "{}");

public sealed record McpToolInvocationResult(
    bool Succeeded,
    string Text,
    JsonElement? StructuredContent,
    string ContentJson,
    string? Error = null);

public static class ExternalConnectionNaming
{
    public static string PluginName(string name)
    {
        var value = string.IsNullOrWhiteSpace(name) ? "External" : name.Trim();
        return value.EndsWith("Connection", StringComparison.OrdinalIgnoreCase) ? value : value + " Connection";
    }

    public static string CapabilityKey(Guid id) => "connection:" + id.ToString("N");
    public static string SecretProviderId(Guid id) => "mcp." + id.ToString("N");
    public const string OAuthTokenSecretName = "oauth.tokens";
    public static bool IsConnectionCapability(string key) => key.StartsWith("connection:", StringComparison.OrdinalIgnoreCase);
}
