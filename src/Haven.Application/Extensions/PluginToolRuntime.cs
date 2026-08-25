/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Extensions/PluginToolRuntime.cs, in the Application layer.
 * What: Owns PluginToolBinding and PluginToolRuntime — the tool-loop adapter that lets registered
 *       native-plugin capabilities execute through the SAME planning/permission/Action Graph path as
 *       every other runtime.
 * How: Capability ImplementationKeys of form "native-plugin:{package}:{capability}" become tool
 *      definitions; invocation delegates to NativePluginRuntime, which enforces granted permissions.
 * Why: Plugin capabilities must not be a parallel execution system — they join the shared one.
 * Maintenance: Keep tool-name sanitisation stable so conversations survive restarts; permission
 *              enforcement stays inside NativePluginRuntime (single authority).
 */

using System.Text.RegularExpressions;
using Haven.Core;

namespace Haven.Application;

/// <summary>One executable plugin capability bound to its generated tool definition.</summary>
public sealed record PluginToolBinding(
    OllamaToolDefinition Definition,
    string PackageId,
    string CapabilityId,
    ExtensionPermission RequiredPermissions,
    string RegistryCapabilityKey);

public sealed partial class PluginToolRuntime(NativePluginRuntime runtime)
{
    public const string ImplementationKeyPrefix = "native-plugin:";

    [GeneratedRegex("[^a-zA-Z0-9_]")]
    private static partial Regex UnsafeCharacters();

    /// <summary>Builds tool bindings for the turn's selected plugin-backed capabilities.</summary>
    public IReadOnlyList<PluginToolBinding> GetBindings(IReadOnlyCollection<ActiveCapability> selectedCapabilities)
    {
        var bindings = new List<PluginToolBinding>();
        foreach (var capability in selectedCapabilities)
        {
            if (string.IsNullOrWhiteSpace(capability.ImplementationKey) ||
                !capability.ImplementationKey.StartsWith(ImplementationKeyPrefix, StringComparison.Ordinal)) continue;

            var parts = capability.ImplementationKey[ImplementationKeyPrefix.Length..].Split(':', 2);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1])) continue;

            var toolName = SanitiseToolName(parts[1]);
            bindings.Add(new PluginToolBinding(
                new OllamaToolDefinition(
                    toolName,
                    $"Plugin capability {capability.Name}. {(string.IsNullOrWhiteSpace(capability.Instructions) ? "Arguments must match the plugin's documented schema." : capability.Instructions.Trim())}",
                    new Dictionary<string, object>(StringComparer.Ordinal),
                    [],
                    InputSchema: ParseObjectSchema()),
                parts[0],
                parts[1],
                ExtensionPermission.None,
                capability.Key));
        }
        return bindings;
    }

    public async Task<WorkspaceToolResult> ExecuteAsync(
        PluginToolBinding binding,
        OllamaToolCall call,
        Guid executionId,
        Guid? parentActionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        try
        {
            var result = await runtime.InvokeAsync(
                binding.PackageId,
                binding.CapabilityId,
                SerializeArguments(call.Arguments),
                ExtensionPermission.None,
                executionId,
                parentActionId,
                cancellationToken).ConfigureAwait(false);
            return new WorkspaceToolResult(
                new ToolActivity(Guid.NewGuid(), call.Name.Replace('_', ' '), "Plugin action completed.", true, TimeSpan.Zero, DateTimeOffset.UtcNow),
                SensitiveTextRedactor.Redact(result, 8_000));
        }
        catch (UnauthorizedAccessException ex)
        {
            return new WorkspaceToolResult(
                new ToolActivity(Guid.NewGuid(), call.Name.Replace('_', ' '), ex.Message, false, TimeSpan.Zero, DateTimeOffset.UtcNow),
                "Tool error: " + ex.Message,
                new ToolFailureDescriptor(
                    "PLUGIN_PERMISSION_DENIED", ToolFailureKind.PermissionRequired, ex.Message,
                    binding.PackageId, "Plugin permissions",
                    new RecoveryRiskAssessment(true, false, false, false, true, false, false, 0.9),
                    Retryable: false));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return new WorkspaceToolResult(
                new ToolActivity(Guid.NewGuid(), call.Name.Replace('_', ' '), ex.Message, false, TimeSpan.Zero, DateTimeOffset.UtcNow),
                "Tool error: " + ex.Message);
        }
    }

    private static string SerializeArguments(IReadOnlyDictionary<string, System.Text.Json.JsonElement> arguments)
    {
        if (arguments.Count == 0) return "{}";
        return System.Text.Json.JsonSerializer.Serialize(arguments);
    }

    private static string SanitiseToolName(string capabilityId)
    {
        var cleaned = UnsafeCharacters().Replace(capabilityId.Trim(), "_").Trim('_');
        if (cleaned.Length == 0) cleaned = "action";
        var name = $"plugin_{cleaned}";
        return name.Length <= 48 ? name : name[..48];
    }

    private static System.Text.Json.JsonElement? ParseObjectSchema()
    {
        return System.Text.Json.JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();
    }
}
