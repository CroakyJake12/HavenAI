using System.Text.Json;

namespace Haven.Application.Automations;

public sealed record AutomationGraphDefinition(int Version, IReadOnlyList<AutomationGraphNodeDefinition> Nodes, IReadOnlyList<AutomationGraphEdgeDefinition> Edges)
{
    public const int CurrentVersion = 1;
    public static AutomationGraphDefinition Empty { get; } = new(CurrentVersion, [], []);
}

public sealed record AutomationGraphNodeDefinition(Guid Id, string Category, DeviceTargetDescriptor? DeviceTarget, string? ActionKey, IReadOnlyDictionary<string, string> Parameters)
{
    public static AutomationGraphNodeDefinition FromDevice(DeviceAutomationNodeDefinition node) => new(node.Id, DeviceAutomationNodeCategory.Key, node.Target, node.ActionKey, node.Parameters);
    public DeviceAutomationNodeDefinition? ToDevice() => string.Equals(Category, DeviceAutomationNodeCategory.Key, StringComparison.OrdinalIgnoreCase) && DeviceTarget is not null && !string.IsNullOrWhiteSpace(ActionKey) ? new DeviceAutomationNodeDefinition(Id, DeviceTarget, ActionKey, Parameters) : null;
}

public sealed record AutomationGraphEdgeDefinition(Guid FromNodeId, Guid ToNodeId);

public static class AutomationGraphCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(AutomationGraphDefinition graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        Validate(graph);
        return JsonSerializer.Serialize(graph, Options);
    }

    public static bool TryDeserialize(string? json, out AutomationGraphDefinition graph)
    {
        graph = AutomationGraphDefinition.Empty;
        if (string.IsNullOrWhiteSpace(json)) return true;
        try
        {
            var parsed = JsonSerializer.Deserialize<AutomationGraphDefinition>(json, Options);
            if (parsed is null) return false;
            Validate(parsed);
            graph = parsed;
            return true;
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static void Validate(AutomationGraphDefinition graph)
    {
        if (graph.Version != AutomationGraphDefinition.CurrentVersion)
            throw new InvalidOperationException($"Unsupported Automation graph version {graph.Version}.");
        if (graph.Nodes.Select(node => node.Id).Distinct().Count() != graph.Nodes.Count)
            throw new InvalidOperationException("Automation graph node IDs must be unique.");
        var ids = graph.Nodes.Select(node => node.Id).ToHashSet();
        if (graph.Edges.Any(edge => !ids.Contains(edge.FromNodeId) || !ids.Contains(edge.ToNodeId)))
            throw new InvalidOperationException("Automation graph edges must reference existing nodes.");
    }
}
