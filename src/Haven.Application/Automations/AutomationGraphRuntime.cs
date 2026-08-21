namespace Haven.Application.Automations;

public enum AutomationGraphRunMode
{
    Real = 0,
    Test = 1
}

public enum AutomationGraphTraceStatus
{
    Succeeded = 0,
    Failed = 1,
    Skipped = 2
}

public sealed record AutomationGraphValidationIssue(string Code, string Message, Guid? NodeId = null, Guid? EdgeId = null);
public sealed record AutomationGraphNodeExecutionContext(AutomationGraphNodeDefinition Node, AutomationGraphRunMode Mode, IReadOnlyDictionary<Guid, string?> Inputs);
public sealed record AutomationGraphNodeExecutionResult(bool Succeeded, string Message, string? Output = null, string? Branch = null);
public sealed record AutomationGraphNodeTrace(Guid NodeId, string Category, AutomationGraphTraceStatus Status, string Message, string? Output = null, string? Branch = null, Dictionary<Guid, string?>? Inputs = null);
public sealed record AutomationGraphRunResult(AutomationGraphRunMode Mode, bool Succeeded, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, IReadOnlyList<AutomationGraphValidationIssue> ValidationIssues, IReadOnlyList<AutomationGraphNodeTrace> Trace, string? FailureMessage = null);

public interface IAutomationGraphNodeExecutor
{
    bool CanExecute(AutomationGraphNodeDefinition node);
    Task<AutomationGraphNodeExecutionResult> ExecuteAsync(AutomationGraphNodeExecutionContext context, CancellationToken cancellationToken);
}

/// <summary>Validates a graph completely before deterministically executing any node.</summary>
public sealed class AutomationGraphRunner(IEnumerable<IAutomationGraphNodeExecutor> executors)
{
    private readonly IReadOnlyList<IAutomationGraphNodeExecutor> _executors = executors?.ToArray() ?? [];

    public IReadOnlyList<AutomationGraphValidationIssue> Validate(AutomationGraphDefinition graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var issues = new List<AutomationGraphValidationIssue>();
        var nodes = graph.Nodes ?? [];
        var edges = graph.Edges ?? [];
        if (nodes.Count == 0) issues.Add(new("graph.empty", "The workflow graph does not contain any nodes."));

        var ids = new HashSet<Guid>();
        foreach (var node in nodes)
        {
            if (node.Id == Guid.Empty) issues.Add(new("node.id.empty", "Workflow nodes must have a stable ID.", node.Id));
            else if (!ids.Add(node.Id)) issues.Add(new("node.id.duplicate", $"Workflow node ID {node.Id} is duplicated.", node.Id));
            if (string.IsNullOrWhiteSpace(node.Category)) issues.Add(new("node.category.missing", "Workflow nodes must declare a category.", node.Id));

            var portIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var port in node.EffectivePorts)
            {
                if (string.IsNullOrWhiteSpace(port.Id)) issues.Add(new("port.id.missing", "Node ports must have an ID.", node.Id));
                else if (!portIds.Add(port.Id)) issues.Add(new("port.id.duplicate", $"Port '{port.Id}' is duplicated on this node.", node.Id));
            }
            if (!IsBuiltIn(node) && !_executors.Any(executor => executor.CanExecute(node))) issues.Add(new("node.unsupported", $"No runtime executor is registered for node category '{node.Category}'.", node.Id));
        }

        var nodeById = nodes.Where(node => node.Id != Guid.Empty).GroupBy(node => node.Id).ToDictionary(group => group.Key, group => group.First());
        var edgeIds = new HashSet<Guid>();
        foreach (var edge in edges)
        {
            if (edge.Id != Guid.Empty && !edgeIds.Add(edge.Id)) issues.Add(new("edge.id.duplicate", $"Workflow edge ID {edge.Id} is duplicated.", EdgeId: edge.Id));
            if (!nodeById.TryGetValue(edge.FromNodeId, out var from)) { issues.Add(new("edge.from.missing", "An edge starts at a node that does not exist.", EdgeId: edge.Id)); continue; }
            if (!nodeById.TryGetValue(edge.ToNodeId, out var to)) { issues.Add(new("edge.to.missing", "An edge ends at a node that does not exist.", EdgeId: edge.Id)); continue; }
            if (edge.FromNodeId == edge.ToNodeId) issues.Add(new("edge.self", "A workflow node cannot connect directly to itself.", edge.FromNodeId, edge.Id));

            var fromPort = from.EffectivePorts.FirstOrDefault(port => string.Equals(port.Id, edge.FromPortId, StringComparison.OrdinalIgnoreCase));
            var toPort = to.EffectivePorts.FirstOrDefault(port => string.Equals(port.Id, edge.ToPortId, StringComparison.OrdinalIgnoreCase));
            if (fromPort is null) issues.Add(new("edge.from-port.missing", $"Output port '{edge.FromPortId}' does not exist.", from.Id, edge.Id));
            else if (fromPort.Direction != AutomationGraphPortDirection.Output) issues.Add(new("edge.from-port.direction", $"Port '{edge.FromPortId}' is not an output port.", from.Id, edge.Id));
            if (toPort is null) issues.Add(new("edge.to-port.missing", $"Input port '{edge.ToPortId}' does not exist.", to.Id, edge.Id));
            else if (toPort.Direction != AutomationGraphPortDirection.Input) issues.Add(new("edge.to-port.direction", $"Port '{edge.ToPortId}' is not an input port.", to.Id, edge.Id));
            if (fromPort is not null && toPort is not null && !string.Equals(fromPort.DataType, "any", StringComparison.OrdinalIgnoreCase) && !string.Equals(toPort.DataType, "any", StringComparison.OrdinalIgnoreCase) && !string.Equals(fromPort.DataType, toPort.DataType, StringComparison.OrdinalIgnoreCase)) issues.Add(new("edge.type", $"Port types '{fromPort.DataType}' and '{toPort.DataType}' are incompatible.", to.Id, edge.Id));
        }

        foreach (var group in edges.GroupBy(edge => (edge.ToNodeId, Port: edge.ToPortId.ToUpperInvariant())))
        {
            if (!nodeById.TryGetValue(group.Key.ToNodeId, out var node)) continue;
            var port = node.EffectivePorts.FirstOrDefault(value => string.Equals(value.Id, group.First().ToPortId, StringComparison.OrdinalIgnoreCase));
            if (port is { AllowsMultipleConnections: false } && group.Count() > 1) issues.Add(new("port.multiple", $"Input port '{port.Id}' accepts only one connection.", node.Id));
        }

        if (issues.All(issue => !issue.Code.StartsWith("edge.", StringComparison.Ordinal) && !issue.Code.StartsWith("node.id.", StringComparison.Ordinal)))
        {
            var indegree = nodes.ToDictionary(node => node.Id, _ => 0);
            foreach (var edge in edges) if (indegree.ContainsKey(edge.ToNodeId)) indegree[edge.ToNodeId]++;
            var queue = new Queue<Guid>(nodes.Where(node => indegree[node.Id] == 0).Select(node => node.Id));
            var visited = 0;
            while (queue.Count > 0)
            {
                var id = queue.Dequeue(); visited++;
                foreach (var edge in edges.Where(edge => edge.FromNodeId == id))
                {
                    if (!indegree.ContainsKey(edge.ToNodeId)) continue;
                    if (--indegree[edge.ToNodeId] == 0) queue.Enqueue(edge.ToNodeId);
                }
            }
            if (visited != nodes.Count) issues.Add(new("graph.cycle", "Workflow graphs must not contain execution cycles."));
        }
        return issues;
    }

    public async Task<AutomationGraphRunResult> RunAsync(AutomationGraphDefinition graph, AutomationGraphRunMode mode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var startedAt = DateTimeOffset.UtcNow;
        var issues = Validate(graph);
        if (issues.Count > 0) return new(mode, false, startedAt, DateTimeOffset.UtcNow, issues, [], "Workflow validation failed before execution.");

        var order = TopologicalOrder(graph.Nodes, graph.Edges);
        var activatedEdges = new HashSet<Guid>();
        var outputs = new Dictionary<Guid, string?>();
        var trace = new List<AutomationGraphNodeTrace>(graph.Nodes.Count);
        foreach (var node in order)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var incoming = graph.Edges.Where(edge => edge.ToNodeId == node.Id).ToArray();
            if (incoming.Length > 0 && !incoming.Any(edge => activatedEdges.Contains(edge.Id)))
            {
                trace.Add(new(node.Id, node.Category, AutomationGraphTraceStatus.Skipped, "Skipped because no incoming branch was selected."));
                continue;
            }
            var inputs = incoming.Where(edge => activatedEdges.Contains(edge.Id) && outputs.ContainsKey(edge.FromNodeId)).GroupBy(edge => edge.FromNodeId).ToDictionary(group => group.Key, group => outputs[group.Key]);
            AutomationGraphNodeExecutionResult result;
            try { result = await ExecuteNodeAsync(new(node, mode, inputs), cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { result = new(false, ex.Message); }
            if (!result.Succeeded)
            {
                trace.Add(new(node.Id, node.Category, AutomationGraphTraceStatus.Failed, result.Message, result.Output, result.Branch, new Dictionary<Guid, string?>(inputs)));
                foreach (var remaining in order.Where(candidate => trace.All(item => item.NodeId != candidate.Id))) trace.Add(new(remaining.Id, remaining.Category, AutomationGraphTraceStatus.Skipped, $"Not run because workflow failed at node {node.Id}."));
                return new(mode, false, startedAt, DateTimeOffset.UtcNow, [], trace, result.Message);
            }
            outputs[node.Id] = result.Output;
            trace.Add(new(node.Id, node.Category, AutomationGraphTraceStatus.Succeeded, result.Message, result.Output, result.Branch, new Dictionary<Guid, string?>(inputs)));
            foreach (var edge in graph.Edges.Where(edge => edge.FromNodeId == node.Id))
                if (string.IsNullOrWhiteSpace(edge.Branch) || (!string.IsNullOrWhiteSpace(result.Branch) && string.Equals(edge.Branch, result.Branch, StringComparison.OrdinalIgnoreCase))) activatedEdges.Add(edge.Id);
        }
        return new(mode, true, startedAt, DateTimeOffset.UtcNow, [], trace);
    }

    private async Task<AutomationGraphNodeExecutionResult> ExecuteNodeAsync(AutomationGraphNodeExecutionContext context, CancellationToken cancellationToken)
    {
        if (IsTrigger(context.Node)) return new(true, context.Mode == AutomationGraphRunMode.Test ? "Trigger would start the workflow." : "Trigger started the workflow.");
        if (IsCondition(context.Node)) return EvaluateCondition(context.Node);
        return await _executors.First(value => value.CanExecute(context.Node)).ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private static AutomationGraphNodeExecutionResult EvaluateCondition(AutomationGraphNodeDefinition node)
    {
        var parameters = node.Parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (TryParameter(parameters, "expression", out var expression) || TryParameter(parameters, "value", out expression))
        {
            if (!bool.TryParse(expression, out var parsed)) return new(false, $"Condition value '{expression}' is not true or false.");
            var branch = parsed ? "true" : "false";
            return new(true, $"Condition evaluated to {branch}.", branch, branch);
        }
        if (!TryParameter(parameters, "left", out var left) || !TryParameter(parameters, "operator", out var op)) return new(false, "Condition nodes require either expression=true/false or left/operator/right parameters.");
        _ = TryParameter(parameters, "right", out var right);
        bool matched;
        switch (op.Trim().ToLowerInvariant())
        {
            case "equals": case "==": matched = string.Equals(left, right, StringComparison.OrdinalIgnoreCase); break;
            case "notequals": case "!=": matched = !string.Equals(left, right, StringComparison.OrdinalIgnoreCase); break;
            case "contains": matched = left.Contains(right ?? string.Empty, StringComparison.OrdinalIgnoreCase); break;
            case "notcontains": matched = !left.Contains(right ?? string.Empty, StringComparison.OrdinalIgnoreCase); break;
            case "exists": matched = !string.IsNullOrWhiteSpace(left); break;
            case "empty": matched = string.IsNullOrWhiteSpace(left); break;
            case "greaterthan": case ">": matched = TryNumber(left, out var leftNumber) && TryNumber(right, out var rightNumber) && leftNumber > rightNumber; break;
            case "lessthan": case "<": matched = TryNumber(left, out leftNumber) && TryNumber(right, out rightNumber) && leftNumber < rightNumber; break;
            default: return new(false, $"Unsupported condition operator '{op}'.");
        }
        var branchName = matched ? "true" : "false";
        return new(true, $"Condition evaluated to {branchName}.", branchName, branchName);
    }

    private static IReadOnlyList<AutomationGraphNodeDefinition> TopologicalOrder(IReadOnlyList<AutomationGraphNodeDefinition> nodes, IReadOnlyList<AutomationGraphEdgeDefinition> edges)
    {
        var sourceIndex = nodes.Select((node, index) => (node.Id, index)).ToDictionary(value => value.Id, value => value.index);
        var indegree = nodes.ToDictionary(node => node.Id, _ => 0);
        foreach (var edge in edges) indegree[edge.ToNodeId]++;
        var ready = nodes.Where(node => indegree[node.Id] == 0).OrderBy(node => sourceIndex[node.Id]).ToList();
        var result = new List<AutomationGraphNodeDefinition>(nodes.Count);
        while (ready.Count > 0)
        {
            var node = ready[0]; ready.RemoveAt(0); result.Add(node);
            foreach (var edge in edges.Where(edge => edge.FromNodeId == node.Id))
            {
                if (--indegree[edge.ToNodeId] != 0) continue;
                ready.Add(nodes.First(candidate => candidate.Id == edge.ToNodeId));
                ready.Sort((left, right) => sourceIndex[left.Id].CompareTo(sourceIndex[right.Id]));
            }
        }
        return result;
    }

    private static bool IsBuiltIn(AutomationGraphNodeDefinition node) => IsTrigger(node) || IsCondition(node);
    private static bool IsTrigger(AutomationGraphNodeDefinition node) => node.Category.Equals("Trigger", StringComparison.OrdinalIgnoreCase) || node.Category.Equals("Schedule", StringComparison.OrdinalIgnoreCase) || node.Category.Equals("ConditionWatch", StringComparison.OrdinalIgnoreCase) || node.Category.Equals("Condition Watch", StringComparison.OrdinalIgnoreCase);
    private static bool IsCondition(AutomationGraphNodeDefinition node) => node.Category.Equals("Condition", StringComparison.OrdinalIgnoreCase) || node.Category.Equals("Branch", StringComparison.OrdinalIgnoreCase);
    private static bool TryParameter(IReadOnlyDictionary<string, string> values, string key, out string value) { foreach (var pair in values) if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) { value = pair.Value ?? string.Empty; return true; } value = string.Empty; return false; }
    private static bool TryNumber(string? value, out double number) => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number);
}
