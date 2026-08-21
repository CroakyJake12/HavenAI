using Haven.Application.Automations;
using Haven.Core;
using Haven.UI.Components;

namespace Haven.Desktop.Views.Pages.Automations;

internal static class AutomationGraphEditorAdapter
{
    private const string ParameterPrefix = "parameter.";
    private const string DeviceTargetId = "device.target.id";
    private const string DeviceTargetName = "device.target.name";
    private const string DeviceTargetPlatform = "device.target.platform";
    private const string DeviceTargetKind = "device.target.kind";
    private const string DeviceTargetProvider = "device.target.provider";
    private const string DeviceAction = "device.action";

    public static IReadOnlyList<NodeEditorTemplate> Templates { get; } =
    [
        new("Trigger", "Manual trigger", "Starts the workflow on demand.", OutputPorts()),
        new("Schedule", "Schedule", "Starts at a configured time.", OutputPorts(), Metadata(("parameter.schedule", ""))),
        new("Schedule", "Recurrence", "Starts on a repeating schedule.", OutputPorts(), Metadata(("parameter.recurrence", ""))),
        new("ConditionWatch", "Condition watch", "Starts when a watched condition becomes true.", OutputPorts(), Metadata(("parameter.watch", ""), ("parameter.intervalMinutes", "60"))),
        new("Condition", "Condition", "Routes execution through true or false.", ConditionPorts(), Metadata(("parameter.expression", "true"))),
        new("Branch", "Branch", "Chooses a true or false execution path.", ConditionPorts(), Metadata(("parameter.expression", "true"))),
        new(BuiltInAutomationNodeCategory.App, "Launch app", "Launches a named application through Haven's device capability router.", FlowPorts(), Metadata(("parameter.action", "launch"), ("parameter.name", ""))),
        new(BuiltInAutomationNodeCategory.File, "Read file", "Reads a workspace-scoped text file through Haven's logged filesystem service.", FlowPorts(), Metadata(("parameter.operation", "read"), ("parameter.workspaceRoot", ""), ("parameter.path", ""))),
        new(BuiltInAutomationNodeCategory.File, "Search files", "Searches filenames inside a configured workspace.", FlowPorts(), Metadata(("parameter.operation", "search"), ("parameter.workspaceRoot", ""), ("parameter.pattern", ""))),
        new(BuiltInAutomationNodeCategory.Action, "Emit value", "Emits a deterministic value into the graph trace.", FlowPorts(), Metadata(("parameter.action", "emit"), ("parameter.value", ""))),
        new(BuiltInAutomationNodeCategory.Action, "Delay", "Waits for a bounded duration during a real run.", FlowPorts(), Metadata(("parameter.action", "delay"), ("parameter.milliseconds", "1000"))),
        new(DeviceAutomationNodeCategory.Key, "Device action", "Runs a supported action on a selected device.", FlowPorts())
    ];

    public static NodeEditorDocument ToEditor(AutomationGraphDefinition graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var nodes = graph.Nodes.Select(node =>
        {
            var metadata = node.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            foreach (var parameter in node.Parameters) metadata[ParameterPrefix + parameter.Key] = parameter.Value ?? string.Empty;
            if (node.DeviceTarget is { } target) metadata = WithDeviceTarget(metadata, target).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(node.ActionKey)) metadata[DeviceAction] = node.ActionKey;
            var ports = node.Ports is { Count: > 0 } ? node.Ports.Select(ToEditorPort).ToArray() : DefaultPorts(node.Category);
            return new NodeEditorNode(node.Id, node.Category, string.IsNullOrWhiteSpace(node.Title) ? DefaultTitle(node) : node.Title)
            { Subtitle = node.Subtitle ?? string.Empty, X = node.X, Y = node.Y, Width = node.Width, Height = node.Height, Ports = ports, Metadata = metadata };
        }).ToArray();
        var edges = graph.Edges.Select(edge => new NodeEditorEdge(edge.Id == Guid.Empty ? Guid.NewGuid() : edge.Id, edge.FromNodeId, edge.FromPortId, edge.ToNodeId, edge.ToPortId)
        { Branch = edge.Branch ?? string.Empty, Label = edge.Label ?? string.Empty, Metadata = edge.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) }).ToArray();
        return new NodeEditorDocument(nodes, edges);
    }

    public static AutomationGraphDefinition FromEditor(NodeEditorDocument document, AutomationGraphDefinition? previous = null)
    {
        ArgumentNullException.ThrowIfNull(document); previous ??= AutomationGraphDefinition.Empty;
        var previousNodes = previous.Nodes.GroupBy(node => node.Id).ToDictionary(group => group.Key, group => group.First());
        var previousEdges = previous.Edges.GroupBy(edge => edge.Id).ToDictionary(group => group.Key, group => group.First());
        var nodes = document.Nodes.Select(node =>
        {
            previousNodes.TryGetValue(node.Id, out var old);
            var parameters = old?.Parameters is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(old.Parameters, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in node.Metadata) if (pair.Key.StartsWith(ParameterPrefix, StringComparison.OrdinalIgnoreCase)) parameters[pair.Key[ParameterPrefix.Length..]] = pair.Value ?? string.Empty;
            var isDevice = string.Equals(node.Category, DeviceAutomationNodeCategory.Key, StringComparison.OrdinalIgnoreCase);
            return new AutomationGraphNodeDefinition(node.Id, node.Category, isDevice ? ReadDeviceTarget(node) ?? old?.DeviceTarget : null, isDevice ? ReadDeviceAction(node) ?? old?.ActionKey : null, parameters)
            { X = node.X, Y = node.Y, Width = node.Width, Height = node.Height, Title = node.Title, Subtitle = node.Subtitle, Ports = node.Ports.Select(ToAutomationPort).ToArray(), Metadata = node.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) };
        }).ToArray();
        var byId = nodes.ToDictionary(node => node.Id);
        var edges = document.Edges.Select(edge =>
        {
            previousEdges.TryGetValue(edge.Id, out var old); var branch = edge.Branch;
            if (string.IsNullOrWhiteSpace(branch) && byId.TryGetValue(edge.FromNodeId, out var from) && IsCondition(from.Category) && (string.Equals(edge.FromPortId, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(edge.FromPortId, "false", StringComparison.OrdinalIgnoreCase))) branch = edge.FromPortId.ToLowerInvariant();
            return new AutomationGraphEdgeDefinition(edge.FromNodeId, edge.ToNodeId) { Id = edge.Id == Guid.Empty ? old?.Id ?? Guid.NewGuid() : edge.Id, FromPortId = edge.FromPortId, ToPortId = edge.ToPortId, Branch = branch ?? old?.Branch ?? string.Empty, Label = edge.Label ?? old?.Label ?? string.Empty, Metadata = edge.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) };
        }).ToArray();
        return new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, nodes, edges);
    }

    public static IReadOnlyList<NodeEditorDiagnostic> ValidateConfiguration(AutomationGraphDefinition graph)
    {
        var diagnostics = new List<NodeEditorDiagnostic>();
        foreach (var node in graph.Nodes.Where(node => string.Equals(node.Category, DeviceAutomationNodeCategory.Key, StringComparison.OrdinalIgnoreCase)))
        {
            if (node.DeviceTarget is null) diagnostics.Add(new("device.target.missing", "Choose a target for this DEVICE node.", node.Id));
            if (string.IsNullOrWhiteSpace(node.ActionKey)) diagnostics.Add(new("device.action.missing", "Choose an action for this DEVICE node.", node.Id));
        }
        foreach (var node in graph.Nodes)
        {
            foreach (var issue in BuiltInAutomationActionNodeExecutor.ValidateConfiguration(node))
                diagnostics.Add(new NodeEditorDiagnostic(issue.Code, issue.Message, issue.NodeId));
        }
        return diagnostics;
    }

    public static IReadOnlyDictionary<string, string> WithDeviceTarget(IReadOnlyDictionary<string, string> source, DeviceTargetDescriptor target)
    {
        var metadata = Copy(source); metadata[DeviceTargetId] = target.Id; metadata[DeviceTargetName] = target.DisplayName; metadata[DeviceTargetPlatform] = target.Platform.ToString(); metadata[DeviceTargetKind] = target.Kind.ToString();
        if (string.IsNullOrWhiteSpace(target.ProviderId)) metadata.Remove(DeviceTargetProvider); else metadata[DeviceTargetProvider] = target.ProviderId; return metadata;
    }
    public static IReadOnlyDictionary<string, string> WithDeviceAction(IReadOnlyDictionary<string, string> source, string actionKey) { var metadata = Copy(source); metadata[DeviceAction] = actionKey ?? string.Empty; return metadata; }
    public static IReadOnlyDictionary<string, string> WithParameter(IReadOnlyDictionary<string, string> source, string key, string? value) { var metadata = Copy(source); metadata[ParameterPrefix + key] = value ?? string.Empty; return metadata; }
    public static string? ReadParameter(NodeEditorNode node, string key) => node.Metadata.TryGetValue(ParameterPrefix + key, out var value) ? value : null;
    public static string? ReadDeviceAction(NodeEditorNode node) => node.Metadata.TryGetValue(DeviceAction, out var action) && !string.IsNullOrWhiteSpace(action) ? action : null;
    public static DeviceTargetDescriptor? ReadDeviceTarget(NodeEditorNode node)
    {
        if (!node.Metadata.TryGetValue(DeviceTargetId, out var id) || string.IsNullOrWhiteSpace(id)) return null;
        if (!node.Metadata.TryGetValue(DeviceTargetName, out var name) || string.IsNullOrWhiteSpace(name)) name = id;
        if (!node.Metadata.TryGetValue(DeviceTargetPlatform, out var platformText) || !Enum.TryParse<CapabilityPlatform>(platformText, true, out var platform)) return null;
        if (!node.Metadata.TryGetValue(DeviceTargetKind, out var kindText) || !Enum.TryParse<DeviceTargetKind>(kindText, true, out var kind)) return null;
        node.Metadata.TryGetValue(DeviceTargetProvider, out var provider); return new(id, name, platform, kind, string.IsNullOrWhiteSpace(provider) ? null : provider);
    }

    private static Dictionary<string, string> Copy(IReadOnlyDictionary<string, string> source) => source.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    private static IReadOnlyDictionary<string, string> Metadata(params (string Key, string Value)[] values) => values.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);
    private static NodeEditorPort ToEditorPort(AutomationGraphPortDefinition port) => new(port.Id, port.Label, port.Direction == AutomationGraphPortDirection.Input ? NodeEditorPortDirection.Input : NodeEditorPortDirection.Output, port.DataType, port.AllowsMultipleConnections);
    private static AutomationGraphPortDefinition ToAutomationPort(NodeEditorPort port) => new(port.Id, port.Label, port.Direction == NodeEditorPortDirection.Input ? AutomationGraphPortDirection.Input : AutomationGraphPortDirection.Output, port.DataType, port.AllowsMultipleConnections);
    private static bool IsCondition(string category) => category.Equals("Condition", StringComparison.OrdinalIgnoreCase) || category.Equals("Branch", StringComparison.OrdinalIgnoreCase);
    private static bool IsTrigger(string category) => category.Equals("Trigger", StringComparison.OrdinalIgnoreCase) || category.Equals("Schedule", StringComparison.OrdinalIgnoreCase) || category.Equals("ConditionWatch", StringComparison.OrdinalIgnoreCase) || category.Equals("Condition Watch", StringComparison.OrdinalIgnoreCase);
    private static string DefaultTitle(AutomationGraphNodeDefinition node) => !string.IsNullOrWhiteSpace(node.ActionKey) ? node.ActionKey : node.Category;
    private static IReadOnlyList<NodeEditorPort> DefaultPorts(string category) => IsCondition(category) ? ConditionPorts() : IsTrigger(category) ? OutputPorts() : FlowPorts();
    private static IReadOnlyList<NodeEditorPort> FlowPorts() => [new("in", "In", NodeEditorPortDirection.Input, "flow", false), new("out", "Out", NodeEditorPortDirection.Output, "flow", true)];
    private static IReadOnlyList<NodeEditorPort> OutputPorts() => [new("out", "Out", NodeEditorPortDirection.Output, "flow", true)];
    private static IReadOnlyList<NodeEditorPort> ConditionPorts() => [new("in", "In", NodeEditorPortDirection.Input, "flow", false), new("true", "True", NodeEditorPortDirection.Output, "flow", true), new("false", "False", NodeEditorPortDirection.Output, "flow", true)];
}
