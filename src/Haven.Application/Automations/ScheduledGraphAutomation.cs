using System.Globalization;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application.Automations;

public sealed record AutomationGraphScheduleBinding(
    Guid TriggerNodeId,
    AutomationScheduleKind ScheduleKind,
    string ScheduleJson,
    string Description,
    string? WatchCondition);

public static class AutomationGraphScheduleBinder
{
    public static bool TryBind(
        AutomationGraphDefinition graph,
        DateTimeOffset now,
        out AutomationGraphScheduleBinding? binding,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(graph);
        binding = null;
        error = null;
        var candidates = graph.Nodes.Where(IsSchedulingNode).ToArray();
        if (candidates.Length == 0) return true;
        if (candidates.Length > 1)
        {
            error = "A workflow can have only one Schedule, Recurrence, or Condition Watch trigger.";
            return false;
        }

        var node = candidates[0];
        if (graph.Edges.Any(edge => edge.ToNodeId == node.Id))
        {
            error = "The scheduling node must be a root trigger with no incoming connection.";
            return false;
        }

        if (IsConditionWatch(node)) return TryBindConditionWatch(node, now, out binding, out error);
        if (IsRecurrence(node)) return TryBindRecurrence(node, now, out binding, out error);
        return TryBindOnce(node, now, out binding, out error);
    }

    private static bool TryBindOnce(AutomationGraphNodeDefinition node, DateTimeOffset now, out AutomationGraphScheduleBinding? binding, out string? error)
    {
        binding = null;
        error = null;
        var text = ReadParameter(node, "schedule");
        if (string.IsNullOrWhiteSpace(text) || !TryParseDate(text, out var at))
        {
            error = "Schedule needs a date/time such as 2026-08-21T09:00:00+01:00.";
            return false;
        }
        if (at <= now)
        {
            error = "Schedule time must be in the future.";
            return false;
        }
        var draft = DefaultDraft(now) with { OnceAt = at };
        binding = Create(node.Id, AutomationScheduleKind.Once, draft, null);
        return true;
    }

    private static bool TryBindRecurrence(AutomationGraphNodeDefinition node, DateTimeOffset now, out AutomationGraphScheduleBinding? binding, out string? error)
    {
        binding = null;
        error = null;
        var text = ReadParameter(node, "recurrence")?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Recurrence needs a value such as 'hourly', 'every 2 hours', 'daily 08:30', or 'weekly Monday 08:30'.";
            return false;
        }
        var normalized = text.Replace(" at ", " ", StringComparison.OrdinalIgnoreCase);
        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var draft = DefaultDraft(now);
        AutomationScheduleKind kind;
        if (parts.Length == 1 && parts[0].Equals("hourly", StringComparison.OrdinalIgnoreCase))
        {
            kind = AutomationScheduleKind.Hourly;
        }
        else if (parts.Length >= 3 && parts[0].Equals("every", StringComparison.OrdinalIgnoreCase)
                 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours)
                 && parts[2].StartsWith("hour", StringComparison.OrdinalIgnoreCase))
        {
            if (hours is < 1 or > 168)
            {
                error = "Hourly recurrence must be between 1 and 168 hours.";
                return false;
            }
            kind = AutomationScheduleKind.Hourly;
            draft = draft with { IntervalHours = hours };
        }
        else if (parts.Length >= 2 && parts[0].Equals("daily", StringComparison.OrdinalIgnoreCase) && TryParseTime(parts[^1], out var dailyTime))
        {
            kind = AutomationScheduleKind.Daily;
            draft = draft with { Time = dailyTime };
        }
        else if (parts.Length >= 3 && parts[0].Equals("weekly", StringComparison.OrdinalIgnoreCase)
                 && Enum.TryParse<DayOfWeek>(parts[1], true, out var day)
                 && TryParseTime(parts[^1], out var weeklyTime))
        {
            kind = AutomationScheduleKind.Weekly;
            draft = draft with { DayOfWeek = day, Time = weeklyTime };
        }
        else
        {
            error = "Recurrence format is invalid. Use 'hourly', 'every 2 hours', 'daily 08:30', or 'weekly Monday 08:30'.";
            return false;
        }
        binding = Create(node.Id, kind, draft, null);
        return true;
    }

    private static bool TryBindConditionWatch(AutomationGraphNodeDefinition node, DateTimeOffset now, out AutomationGraphScheduleBinding? binding, out string? error)
    {
        binding = null;
        error = null;
        var condition = ReadParameter(node, "watch")?.Trim();
        if (string.IsNullOrWhiteSpace(condition))
        {
            error = "Condition Watch needs a condition to check.";
            return false;
        }
        var minutes = 60;
        var interval = ReadParameter(node, "intervalMinutes");
        if (!string.IsNullOrWhiteSpace(interval) && (!int.TryParse(interval, NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes) || minutes is < 60 or > 10_080))
        {
            error = "Condition Watch interval must be between 60 and 10080 minutes.";
            return false;
        }
        var draft = DefaultDraft(now) with { ConditionIntervalMinutes = minutes };
        binding = Create(node.Id, AutomationScheduleKind.ConditionWatch, draft, condition);
        return true;
    }

    private static AutomationGraphScheduleBinding Create(Guid nodeId, AutomationScheduleKind kind, ScheduledTaskScheduleDraft draft, string? condition) =>
        new(nodeId, kind, ScheduledTaskScheduleComposer.Compose(kind, draft), ScheduledTaskScheduleComposer.Describe(kind, draft), condition);

    private static ScheduledTaskScheduleDraft DefaultDraft(DateTimeOffset now) =>
        new(now.AddHours(1), new TimeOnly(8, 0), DayOfWeek.Monday, 1, 60);

    private static bool IsSchedulingNode(AutomationGraphNodeDefinition node) =>
        node.Category.Equals("Schedule", StringComparison.OrdinalIgnoreCase) || IsConditionWatch(node);

    private static bool IsConditionWatch(AutomationGraphNodeDefinition node) =>
        node.Category.Equals("ConditionWatch", StringComparison.OrdinalIgnoreCase) || node.Category.Equals("Condition Watch", StringComparison.OrdinalIgnoreCase);

    private static bool IsRecurrence(AutomationGraphNodeDefinition node) =>
        node.Title.Equals("Recurrence", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(ReadParameter(node, "recurrence"));

    private static string? ReadParameter(AutomationGraphNodeDefinition node, string key) =>
        node.Parameters.TryGetValue(key, out var value) ? value : null;

    private static bool TryParseDate(string text, out DateTimeOffset value) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out value)
        || DateTimeOffset.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out value);

    private static bool TryParseTime(string text, out TimeOnly value) =>
        TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out value)
        || TimeOnly.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out value);
}

public sealed record ScheduledGraphAutomationPayload(
    int Version,
    Guid WorkflowId,
    Guid TriggerNodeId,
    string WorkflowName,
    string GraphJson,
    string? WatchCondition);

public static class ScheduledGraphAutomationPayloadCodec
{
    public const int CurrentVersion = 1;
    private const string Prefix = "haven:scheduled-graph:v1:";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(Guid workflowId, Guid triggerNodeId, string workflowName, string graphJson, string? watchCondition)
    {
        if (workflowId == Guid.Empty) throw new ArgumentException("Workflow ID is required.", nameof(workflowId));
        if (triggerNodeId == Guid.Empty) throw new ArgumentException("Trigger node ID is required.", nameof(triggerNodeId));
        if (!AutomationGraphCodec.TryDeserialize(graphJson, out _)) throw new ArgumentException("Graph JSON is invalid.", nameof(graphJson));
        var payload = new ScheduledGraphAutomationPayload(CurrentVersion, workflowId, triggerNodeId, workflowName?.Trim() ?? string.Empty, graphJson, watchCondition?.Trim());
        return Prefix + JsonSerializer.Serialize(payload, Options);
    }

    public static bool IsPayload(string? instruction) =>
        !string.IsNullOrWhiteSpace(instruction) && instruction.StartsWith(Prefix, StringComparison.Ordinal);

    public static bool TryDeserialize(string? instruction, out ScheduledGraphAutomationPayload payload)
    {
        payload = new ScheduledGraphAutomationPayload(CurrentVersion, Guid.Empty, Guid.Empty, string.Empty, string.Empty, null);
        if (string.IsNullOrWhiteSpace(instruction) || !instruction.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        try
        {
            var parsed = JsonSerializer.Deserialize<ScheduledGraphAutomationPayload>(instruction[Prefix.Length..], Options);
            if (parsed is null || parsed.Version != CurrentVersion || parsed.WorkflowId == Guid.Empty || parsed.TriggerNodeId == Guid.Empty
                || string.IsNullOrWhiteSpace(parsed.GraphJson) || !AutomationGraphCodec.TryDeserialize(parsed.GraphJson, out _)) return false;
            payload = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public static class AutomationGraphTriggerScope
{
    public static bool TrySelect(AutomationGraphDefinition graph, Guid triggerNodeId, out AutomationGraphDefinition scoped, out string? error)
    {
        ArgumentNullException.ThrowIfNull(graph);
        scoped = AutomationGraphDefinition.Empty;
        error = null;
        if (!graph.Nodes.Any(node => node.Id == triggerNodeId))
        {
            error = "The scheduled trigger no longer exists in the captured graph.";
            return false;
        }

        var reachable = new HashSet<Guid> { triggerNodeId };
        var queue = new Queue<Guid>();
        queue.Enqueue(triggerNodeId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var edge in graph.Edges.Where(edge => edge.FromNodeId == current))
            {
                if (reachable.Add(edge.ToNodeId)) queue.Enqueue(edge.ToNodeId);
            }
        }

        scoped = new AutomationGraphDefinition(
            AutomationGraphDefinition.CurrentVersion,
            graph.Nodes.Where(node => reachable.Contains(node.Id)).ToArray(),
            graph.Edges.Where(edge => reachable.Contains(edge.FromNodeId) && reachable.Contains(edge.ToNodeId)).ToArray());
        return true;
    }
}
