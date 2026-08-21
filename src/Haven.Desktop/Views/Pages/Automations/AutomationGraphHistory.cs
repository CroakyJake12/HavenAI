using Haven.Application.Automations;

namespace Haven.Desktop.Views.Pages.Automations;

internal sealed record AutomationGraphHistoryEntry(
    Guid Id,
    Guid WorkflowId,
    Guid? ContainerId,
    string WorkflowName,
    string Instruction,
    string GraphJson,
    AutomationGraphRunMode Mode,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    bool Succeeded,
    string? FailureMessage,
    List<AutomationGraphValidationIssue> ValidationIssues,
    List<AutomationGraphNodeTrace> Trace);

internal sealed record AutomationGraphHistoryState(
    int Version,
    List<AutomationGraphHistoryEntry> Entries);

internal static class AutomationGraphHistoryJournal
{
    public const int CurrentVersion = 1;
    public const int MaxEntries = 100;

    public static AutomationGraphHistoryEntry Capture(
        Guid workflowId,
        Guid? containerId,
        string workflowName,
        string instruction,
        string graphJson,
        AutomationGraphRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(graphJson)) throw new ArgumentException("Graph JSON is required for retryable history.", nameof(graphJson));
        return new AutomationGraphHistoryEntry(
            Guid.NewGuid(),
            workflowId,
            containerId,
            string.IsNullOrWhiteSpace(workflowName) ? "Untitled workflow" : workflowName.Trim(),
            instruction ?? string.Empty,
            graphJson,
            result.Mode,
            result.StartedAt,
            result.CompletedAt,
            result.Succeeded,
            result.FailureMessage,
            result.ValidationIssues.ToList(),
            result.Trace.ToList());
    }

    public static AutomationGraphHistoryState Append(
        AutomationGraphHistoryState? state,
        AutomationGraphHistoryEntry entry,
        int maxEntries = MaxEntries)
    {
        ArgumentNullException.ThrowIfNull(entry);
        maxEntries = Math.Clamp(maxEntries, 1, MaxEntries);
        var entries = Normalize(state).Entries
            .Where(existing => existing.Id != entry.Id)
            .Append(entry)
            .OrderByDescending(existing => existing.StartedAt)
            .ThenByDescending(existing => existing.CompletedAt)
            .Take(maxEntries)
            .ToList();
        return new AutomationGraphHistoryState(CurrentVersion, entries);
    }

    public static AutomationGraphHistoryState Normalize(AutomationGraphHistoryState? state)
    {
        if (state is not { Version: CurrentVersion }) return new AutomationGraphHistoryState(CurrentVersion, []);
        var entries = (state.Entries ?? [])
            .Where(IsValid)
            .GroupBy(entry => entry.Id)
            .Select(group => group.OrderByDescending(entry => entry.CompletedAt).First())
            .OrderByDescending(entry => entry.StartedAt)
            .ThenByDescending(entry => entry.CompletedAt)
            .Take(MaxEntries)
            .ToList();
        return new AutomationGraphHistoryState(CurrentVersion, entries);
    }

    public static IReadOnlyList<AutomationGraphHistoryEntry> ForContainer(
        AutomationGraphHistoryState? state,
        Guid? containerId,
        int limit = 50)
    {
        limit = Math.Clamp(limit, 1, MaxEntries);
        return Normalize(state).Entries
            .Where(entry => entry.ContainerId == containerId)
            .Take(limit)
            .ToArray();
    }

    private static bool IsValid(AutomationGraphHistoryEntry? entry) =>
        entry is not null
        && entry.Id != Guid.Empty
        && !string.IsNullOrWhiteSpace(entry.WorkflowName)
        && !string.IsNullOrWhiteSpace(entry.GraphJson)
        && entry.CompletedAt >= entry.StartedAt;
}
