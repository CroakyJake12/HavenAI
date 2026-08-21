using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Views.Pages.Automations;

public sealed partial class AutomationsPage
{
    private const string GraphHistorySettingsKey = "automations.graph-run-history.v1";
    private readonly SemaphoreSlim _graphHistoryGate = new(1, 1);
    private IVersionedSettingsStore? _graphHistorySettings;
    private AutomationGraphHistoryState _graphHistory = new(AutomationGraphHistoryJournal.CurrentVersion, []);
    private bool _graphHistoryLoaded;

    private async Task EnsureGraphHistoryLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_graphHistoryLoaded) return;
        await _graphHistoryGate.WaitAsync(cancellationToken);
        try
        {
            if (_graphHistoryLoaded) return;
            var stored = _graphHistorySettings is null
                ? null
                : await _graphHistorySettings.GetAsync<AutomationGraphHistoryState>(GraphHistorySettingsKey, cancellationToken);
            _graphHistory = AutomationGraphHistoryJournal.Normalize(stored);
            _graphHistoryLoaded = true;
        }
        finally
        {
            _graphHistoryGate.Release();
        }
    }

    private async Task RecordGraphHistoryAsync(
        ReusableTaskDefinition workflow,
        string graphJson,
        AutomationGraphRunResult result)
    {
        await EnsureGraphHistoryLoadedAsync(CancellationToken.None);
        var entry = AutomationGraphHistoryJournal.Capture(
            workflow.Id,
            workflow.ContainerId,
            workflow.Name,
            workflow.Instruction,
            graphJson,
            result);

        await _graphHistoryGate.WaitAsync();
        try
        {
            _graphHistory = AutomationGraphHistoryJournal.Append(_graphHistory, entry);
            if (_graphHistorySettings is not null)
                await _graphHistorySettings.SetAsync(GraphHistorySettingsKey, _graphHistory, CancellationToken.None);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Automations] Could not persist graph history: {ex.Message}");
        }
        finally
        {
            _graphHistoryGate.Release();
        }
    }

    private async Task RefreshGraphHistoryRowsAsync()
    {
        await EnsureGraphHistoryLoadedAsync(CancellationToken.None);
        foreach (var entry in AutomationGraphHistoryJournal.ForContainer(_graphHistory, _containerId, 50))
            _historyItems.Children.Add(GraphHistoryCard(entry));
    }

    private Control GraphHistoryCard(AutomationGraphHistoryEntry entry)
    {
        var outcome = entry.Succeeded ? "Succeeded" : "Failed";
        var mode = entry.Mode == AutomationGraphRunMode.Test ? "TEST" : "REAL";
        var timestamp = entry.CompletedAt.LocalDateTime.ToString("g");
        var summary = entry.Succeeded
            ? $"{entry.Trace.Count} node{(entry.Trace.Count == 1 ? string.Empty : "s")} traced"
            : entry.FailureMessage ?? entry.ValidationIssues.FirstOrDefault()?.Message ?? "Graph run failed.";

        var details = new StackPanel { Spacing = 6, IsVisible = false };
        foreach (var issue in entry.ValidationIssues)
            details.Children.Add(GraphTraceRow("Validation", issue.Message));
        foreach (var trace in entry.Trace)
        {
            var detail = trace.Message;
            var inputSummary = FormatHistoryTraceInputs(trace.Inputs);
            if (!string.IsNullOrWhiteSpace(inputSummary)) detail += $" · inputs: {inputSummary}";
            if (!string.IsNullOrWhiteSpace(trace.Output)) detail += $" · output: {trace.Output}";
            if (!string.IsNullOrWhiteSpace(trace.Branch)) detail += $" · branch: {trace.Branch}";
            details.Children.Add(GraphTraceRow($"{trace.Status}: {trace.Category}", detail));
        }
        if (details.Children.Count == 0) details.Children.Add(Muted("No node trace was produced."));

        var toggle = SoftButton("Trace");
        toggle.Click += (_, _) =>
        {
            details.IsVisible = !details.IsVisible;
            toggle.Content = details.IsVisible ? "Hide trace" : "Trace";
        };
        var open = SoftButton("Open snapshot");
        open.Click += (_, _) => OpenGraphHistoryEntry(entry);
        var retry = AccentButton(entry.Mode == AutomationGraphRunMode.Test ? "Retest" : "Retry");
        retry.Click += async (_, _) => await RetryGraphHistoryAsync(entry);

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
            Children =
            {
                new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        new TextBlock { Text = entry.WorkflowName, FontSize = 14, FontWeight = Avalonia.Media.FontWeight.ExtraBold },
                        new TextBlock { Text = $"{mode} · {outcome} · {timestamp}", FontSize = 11, Foreground = MutedBrush },
                        new TextBlock { Text = summary, FontSize = 11, Foreground = MutedBrush, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
                    }
                },
                Column(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { toggle, open, retry } }, 1)
            }
        };

        var card = new HavenCard
        {
            Tag = entry.WorkflowName,
            Padding = new Avalonia.Thickness(14, 10),
            CornerRadius = new Avalonia.CornerRadius(18),
            Child = new StackPanel { Spacing = 9, Children = { header, details } }
        };
        AutomationProperties.SetName(card, $"{entry.WorkflowName} {mode} graph run {outcome}");
        return card;
    }

    private static string FormatHistoryTraceInputs(Dictionary<Guid, string?>? inputs)
    {
        if (inputs is not { Count: > 0 }) return string.Empty;
        return string.Join(", ", inputs.Select(pair => $"{pair.Key.ToString("N")[..8]}={pair.Value ?? "null"}"));
    }

    private void OpenGraphHistoryEntry(AutomationGraphHistoryEntry entry)
    {
        var workflow = HistoryWorkflow(entry);
        ShowEditor(workflow);
        ShowGraphTestResult(ToRunResult(entry));
        _status.Text = $"Opened captured {entry.Mode.ToString().ToLowerInvariant()} graph run from {entry.CompletedAt.LocalDateTime:g}.";
    }

    private async Task RetryGraphHistoryAsync(AutomationGraphHistoryEntry entry)
    {
        if (!AutomationGraphCodec.TryDeserialize(entry.GraphJson, out var graph))
        {
            _status.Text = "The captured graph snapshot is unreadable, so Haven did not retry it.";
            return;
        }

        _status.Text = entry.Mode == AutomationGraphRunMode.Test
            ? "Retesting captured graph snapshot…"
            : "Retrying captured graph snapshot…";

        AutomationGraphRunResult? result;
        if (entry.Mode == AutomationGraphRunMode.Test)
        {
            result = await AutomationGraphTestRunner.RunAsync(graph, CancellationToken.None);
        }
        else
        {
            if (_deviceWorkflowRunner is null)
            {
                _status.Text = "The real graph runtime is unavailable. Haven did not route this retry to Tasks or perform a substitute action.";
                return;
            }
            var run = await _deviceWorkflowRunner.RunAsync(HistoryWorkflow(entry), permissionGranted: false, CancellationToken.None);
            result = run.GraphResult;
            if (result is null)
            {
                _status.Text = "This captured real graph no longer has a compatible graph executor. Haven did not route it to Tasks.";
                return;
            }
        }

        await RecordGraphHistoryAsync(HistoryWorkflow(entry), entry.GraphJson, result);
        _status.Text = result.Succeeded
            ? $"{(entry.Mode == AutomationGraphRunMode.Test ? "Retest" : "Retry")} succeeded with {result.Trace.Count} traced nodes."
            : result.FailureMessage ?? "Graph retry failed.";
        await RefreshAsync();
    }

    private static ReusableTaskDefinition HistoryWorkflow(AutomationGraphHistoryEntry entry) => new(
        entry.WorkflowId == Guid.Empty ? Guid.NewGuid() : entry.WorkflowId,
        entry.WorkflowName,
        "Captured graph history snapshot",
        entry.Instruction,
        entry.ContainerId,
        true,
        entry.StartedAt,
        entry.CompletedAt,
        entry.GraphJson);

    private static AutomationGraphRunResult ToRunResult(AutomationGraphHistoryEntry entry) => new(
        entry.Mode,
        entry.Succeeded,
        entry.StartedAt,
        entry.CompletedAt,
        entry.ValidationIssues,
        entry.Trace,
        entry.FailureMessage);
}
