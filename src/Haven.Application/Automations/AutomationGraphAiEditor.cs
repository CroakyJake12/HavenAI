using Haven.Core;

namespace Haven.Application.Automations;

public interface IAutomationGraphAiEditor
{
    Task<AutomationGraphAiEditResult> ProposeEditAsync(
        AutomationGraphDefinition current,
        string instruction,
        CancellationToken cancellationToken);
}

public sealed record AutomationGraphAiEditResult(
    bool Succeeded,
    string Status,
    string? ModelName,
    AutomationGraphDefinition? Graph);

/// <summary>Uses Haven's provider model runtime to propose typed Automation graph changes.</summary>
public sealed class AutomationGraphAiEditor(IProviderModelClient models) : IAutomationGraphAiEditor
{
    private static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Trigger", "Schedule", "ConditionWatch", "Condition Watch", "Condition", "Branch",
        BuiltInAutomationNodeCategory.App, BuiltInAutomationNodeCategory.File,
        BuiltInAutomationNodeCategory.Action, DeviceAutomationNodeCategory.Key
    };

    public async Task<AutomationGraphAiEditResult> ProposeEditAsync(
        AutomationGraphDefinition current,
        string instruction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (string.IsNullOrWhiteSpace(instruction))
            return Fail("Describe the graph change you want Haven to make.");

        ModelDescriptor? model;
        try
        {
            var available = await models.GetModelsAsync(cancellationToken).ConfigureAwait(false);
            model = available.FirstOrDefault(item => item.Supports(ToolCapability.Text));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            return Fail("The graph edit could not discover an available model: " + exception.Message);
        }
        if (model is null) return Fail("No text-capable model is currently available.");

        var currentJson = AutomationGraphCodec.Serialize(current);
        var prompt = $$"""
            Edit this Haven Automation graph according to the user's instruction.

            User instruction:
            {{instruction.Trim()}}

            Current graph JSON:
            {{currentJson}}

            Return one complete AutomationGraphDefinition JSON object only.
            Required version: {{AutomationGraphDefinition.CurrentVersion}}.
            Allowed node categories only: Trigger, Schedule, ConditionWatch, Condition, Branch, App, File, Action, DEVICE.
            Preserve existing node and edge IDs unless the corresponding object is intentionally removed.
            New nodes and edges need non-empty UUIDs.
            Use flow ports. Trigger/Schedule/ConditionWatch nodes have output port "out".
            Condition/Branch nodes have input "in" and outputs "true" and "false".
            Other nodes have input "in" and output "out".
            Keep positions finite, width >= 120, height >= 80.
            Do not invent DEVICE credentials, provider IDs, target IDs, or permission grants.
            For an existing DEVICE node, preserve its DeviceTarget and ActionKey unless the user explicitly asks to remove the node.
            App parameters: action=launch and name.
            File parameters: operation=read with workspaceRoot/path, or operation=search with workspaceRoot/pattern.
            Action parameters: action=emit with value, or action=delay with milliseconds.
            Condition/Branch parameters should use expression=true/false unless the user supplied another supported comparison.
            Do not include markdown fences or commentary.
            """;

        try
        {
            var response = await models.CompleteAsync(
                new OllamaChatRequest(
                    model.Name,
                    [new OllamaMessage("user", prompt)],
                    EffortLevel.Medium,
                    "You are Haven Automations' typed graph editor. Return strict JSON only. Never fabricate provider credentials or permission grants."),
                cancellationToken).ConfigureAwait(false);

            if (!TryParse(response, out var proposed))
                return Fail("The model did not return a readable Automation graph.", model.Name);

            var normalized = Normalize(proposed, current);
            var categoryIssue = normalized.Nodes.FirstOrDefault(node => !AllowedCategories.Contains(node.Category));
            if (categoryIssue is not null)
                return Fail($"The model proposed unsupported node category '{categoryIssue.Category}'. The graph was not changed.", model.Name);

            var validation = new AutomationGraphRunner([new StructuralExecutor()]).Validate(normalized);
            if (validation.Count > 0)
                return Fail("The proposed graph was rejected: " + validation[0].Message, model.Name);

            return new(true, $"Haven proposed a validated typed graph edit using {model.Name}.", model.Name, normalized);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            return Fail("The graph edit failed without changing the workflow: " + exception.Message, model.Name);
        }
    }

    private static bool TryParse(string value, out AutomationGraphDefinition graph)
    {
        graph = AutomationGraphDefinition.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var start = value.IndexOf('{');
        var end = value.LastIndexOf('}');
        return start >= 0 && end > start && AutomationGraphCodec.TryDeserialize(value[start..(end + 1)], out graph);
    }

    private static AutomationGraphDefinition Normalize(AutomationGraphDefinition proposed, AutomationGraphDefinition current)
    {
        var currentNodes = current.Nodes.ToDictionary(node => node.Id);
        var nodes = proposed.Nodes.Select(node =>
        {
            currentNodes.TryGetValue(node.Id, out var previous);
            var category = node.Category?.Trim() ?? string.Empty;
            var ports = node.Ports is { Count: > 0 } ? node.Ports : PortsFor(category);
            var deviceTarget = category.Equals(DeviceAutomationNodeCategory.Key, StringComparison.OrdinalIgnoreCase)
                ? previous?.DeviceTarget
                : node.DeviceTarget;
            var actionKey = category.Equals(DeviceAutomationNodeCategory.Key, StringComparison.OrdinalIgnoreCase)
                ? previous?.ActionKey
                : node.ActionKey;
            return node with
            {
                Category = category,
                DeviceTarget = deviceTarget,
                ActionKey = actionKey,
                Parameters = node.Parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Metadata = node.Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal),
                Ports = ports,
                X = double.IsFinite(node.X) ? node.X : previous?.X ?? 0,
                Y = double.IsFinite(node.Y) ? node.Y : previous?.Y ?? 0,
                Width = Math.Max(120, double.IsFinite(node.Width) ? node.Width : previous?.Width ?? 220),
                Height = Math.Max(80, double.IsFinite(node.Height) ? node.Height : previous?.Height ?? 118)
            };
        }).ToArray();
        var edges = proposed.Edges.Select(edge => edge with
        {
            Id = edge.Id == Guid.Empty ? Guid.NewGuid() : edge.Id,
            Metadata = edge.Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal)
        }).ToArray();
        return new(AutomationGraphDefinition.CurrentVersion, nodes, edges);
    }

    private static IReadOnlyList<AutomationGraphPortDefinition> PortsFor(string category)
    {
        if (category.Equals("Trigger", StringComparison.OrdinalIgnoreCase)
            || category.Equals("Schedule", StringComparison.OrdinalIgnoreCase)
            || category.Equals("ConditionWatch", StringComparison.OrdinalIgnoreCase)
            || category.Equals("Condition Watch", StringComparison.OrdinalIgnoreCase))
            return [new("out", "Out", AutomationGraphPortDirection.Output, "flow", true)];

        if (category.Equals("Condition", StringComparison.OrdinalIgnoreCase)
            || category.Equals("Branch", StringComparison.OrdinalIgnoreCase))
            return
            [
                new("in", "In", AutomationGraphPortDirection.Input, "flow", false),
                new("true", "True", AutomationGraphPortDirection.Output, "flow", true),
                new("false", "False", AutomationGraphPortDirection.Output, "flow", true)
            ];

        return
        [
            new("in", "In", AutomationGraphPortDirection.Input, "flow", false),
            new("out", "Out", AutomationGraphPortDirection.Output, "flow", true)
        ];
    }

    private static AutomationGraphAiEditResult Fail(string status, string? model = null) =>
        new(false, status, model, null);

    private sealed class StructuralExecutor : IAutomationGraphNodeExecutor
    {
        public bool CanExecute(AutomationGraphNodeDefinition node) => true;
        public Task<AutomationGraphNodeExecutionResult> ExecuteAsync(
            AutomationGraphNodeExecutionContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AutomationGraphNodeExecutionResult(true, "Structural validation only."));
    }
}
