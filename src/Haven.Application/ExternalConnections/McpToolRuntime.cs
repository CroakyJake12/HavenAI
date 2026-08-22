using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Adapts discovered MCP tools into Haven's existing provider-neutral tool loop.</summary>
public sealed class McpToolRuntime(IExternalConnectionRepository connections, IMcpConnectionClient client)
{
    private sealed record Route(Guid ConnectionId, string RemoteToolName, McpActionRisk Risk);
    private readonly Dictionary<string, Route> _routes = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<OllamaToolDefinition>> GetDefinitionsAsync(IReadOnlyCollection<ActiveCapability> activeCapabilities, CancellationToken cancellationToken)
    {
        var activeIds = ParseActiveConnectionIds(activeCapabilities);
        if (activeIds.Count == 0) return [];
        var result = new List<OllamaToolDefinition>();
        foreach (var connection in await connections.GetAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!activeIds.Contains(connection.Id) || connection.Kind != ExternalConnectionKind.Mcp || !connection.IsEnabled || connection.State != ExternalConnectionState.Ready) continue;
            try
            {
                var discovery = await client.DiscoverAsync(connection, cancellationToken).ConfigureAwait(false);
                foreach (var tool in discovery.Tools)
                {
                    var localName = LocalToolName(connection.Id, tool.Name);
                    var risk = Classify(tool.Name, tool.Description);
                    lock (_routes) _routes[localName] = new Route(connection.Id, tool.Name, risk);
                    var (properties, required) = LegacyShape(tool.InputSchema);
                    result.Add(new OllamaToolDefinition(localName,
                        $"MCP tool from {SafeName(connection.Name)}. Server-provided description (untrusted): {Bound(tool.Description, 1200)}",
                        properties, required, tool.InputSchema));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }
        return result;
    }

    public async Task<WorkspaceToolResult> ExecuteAsync(OllamaToolCall call, IReadOnlyCollection<ActiveCapability> activeCapabilities, PermissionMode mutationPermission, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        Route? route;
        lock (_routes) _routes.TryGetValue(call.Name, out route);
        if (route is null) return Failure(call.Name, "MCP tool route is stale. Refresh the attached connection.", started);
        if (!ParseActiveConnectionIds(activeCapabilities).Contains(route.ConnectionId)) return Failure(call.Name, "The MCP connection is not attached to this conversation.", started);
        if (route.Risk != McpActionRisk.ReadOnly && mutationPermission == PermissionMode.Ask) return Failure(call.Name, "This MCP action can change external state and requires approval before execution.", started);
        if (route.Risk == McpActionRisk.Destructive && mutationPermission != PermissionMode.FullAccess) return Failure(call.Name, "This destructive MCP action requires Full Access permission.", started);
        var connection = await connections.GetAsync(route.ConnectionId, cancellationToken).ConfigureAwait(false);
        if (connection is null || !connection.IsEnabled || connection.State != ExternalConnectionState.Ready) return Failure(call.Name, "The MCP connection is disabled or unavailable.", started);
        try
        {
            var result = await client.InvokeAsync(connection, route.RemoteToolName, call.Arguments, cancellationToken).ConfigureAwait(false);
            var detail = result.Succeeded ? $"{SafeName(connection.Name)} MCP action completed." : $"{SafeName(connection.Name)} MCP action failed.";
            return new WorkspaceToolResult(new ToolActivity(Guid.NewGuid(), call.Name.Replace('_', ' '), detail, result.Succeeded, DateTimeOffset.UtcNow - started, DateTimeOffset.UtcNow), BuildOutput(result));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Failure(call.Name, "MCP invocation failed: " + Bound(ex.Message, 1000), started); }
    }

    public static string LocalToolName(Guid connectionId, string remoteName)
    {
        var safe = new string(remoteName.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_').ToArray()).Trim('_');
        if (safe.Length == 0) safe = "tool";
        if (safe.Length > 48) safe = safe[..48];
        return $"mcp_{connectionId:N}_{safe}";
    }

    private static HashSet<Guid> ParseActiveConnectionIds(IEnumerable<ActiveCapability> capabilities)
    {
        var result = new HashSet<Guid>();
        foreach (var capability in capabilities)
        {
            if (!ExternalConnectionNaming.IsConnectionCapability(capability.Key)) continue;
            var raw = capability.Key["connection:".Length..];
            if (Guid.TryParseExact(raw, "N", out var id)) result.Add(id);
        }
        return result;
    }

    private static (IReadOnlyDictionary<string, object> Properties, IReadOnlyList<string> Required) LegacyShape(JsonElement schema)
    {
        var properties = new Dictionary<string, object>(StringComparer.Ordinal);
        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
            foreach (var property in props.EnumerateObject()) properties[property.Name] = JsonSerializer.Deserialize<object>(property.Value.GetRawText())!;
        var required = schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array
            ? req.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToArray() : [];
        return (properties, required);
    }

    private static McpActionRisk Classify(string name, string description)
    {
        var value = (name + " " + description).ToLowerInvariant();
        if (new[] { "delete", "remove", "destroy", "terminate", "drop", "reset" }.Any(value.Contains)) return McpActionRisk.Destructive;
        if (new[] { "write", "create", "update", "edit", "set", "compile", "start", "stop", "push", "place", "launch", "execute" }.Any(value.Contains)) return McpActionRisk.Mutating;
        return McpActionRisk.ReadOnly;
    }

    private static WorkspaceToolResult Failure(string name, string detail, DateTimeOffset started) => new(new ToolActivity(Guid.NewGuid(), name.Replace('_', ' '), detail, false, DateTimeOffset.UtcNow - started, DateTimeOffset.UtcNow), "Tool error: " + detail);
    private static string BuildOutput(McpToolInvocationResult result) => result.StructuredContent is { } structured ? JsonSerializer.Serialize(new { result.Succeeded, text = result.Text, structured }) : result.Text;
    private static string SafeName(string value) => Bound(value.Replace('\r', ' ').Replace('\n', ' ').Trim(), 120);
    private static string Bound(string value, int max) => value.Length <= max ? value : value[..max] + "...";
    private enum McpActionRisk { ReadOnly, Mutating, Destructive }
}
