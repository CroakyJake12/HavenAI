using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Automations;

namespace Haven.Desktop.Views.Shell.NativePresentation;

/// <summary>
/// Compatibility host for the Automations route. Every visible control is owned by Haven.UI
/// through one HavenSceneControl; this class only bridges repositories and runtime services.
/// </summary>
internal sealed class NativeAutomationsPage : ContentControl, IDisposable
{
    private const string GraphHistorySettingsKey = "automations.graph-run-history.v1";
    private readonly IWorkspaceStateRepository _tasks;
    private readonly IAutomationRepository _automations;
    private readonly Guid? _containerId;
    private readonly Func<Task> _startOneTimeTask;
    private readonly Func<string, Task> _runTask;
    private readonly DeviceActionRouter? _deviceActions;
    private readonly IAutomationGraphAiEditor? _graphAiEditor;
    private readonly ReusableDeviceWorkflowRunner? _workflowRunner;
    private readonly IVersionedSettingsStore? _historySettings;
    private readonly AutomationsHavenScene _scene;
    private readonly HavenSceneControl _sceneHost;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _historyGate = new(1, 1);
    private AutomationGraphHistoryState _history = new(AutomationGraphHistoryJournal.CurrentVersion, []);
    private bool _historyLoaded;
    private bool _refreshing;
    private bool _refreshPending;
    private bool _disposed;
    private IReadOnlyList<ReusableTaskDefinition> _workflows = [];
    private IReadOnlyList<AutomationDefinition> _scheduled = [];

    public NativeAutomationsPage(
        IWorkspaceStateRepository tasks,
        IAutomationRepository automations,
        Guid? containerId,
        Func<Task> startOneTimeTask,
        Func<string, Task> runTask,
        IVersionedSettingsStore? versionedSettings = null)
    {
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _automations = automations ?? throw new ArgumentNullException(nameof(automations));
        _containerId = containerId;
        _startOneTimeTask = startOneTimeTask ?? throw new ArgumentNullException(nameof(startOneTimeTask));
        _runTask = runTask ?? throw new ArgumentNullException(nameof(runTask));
        _historySettings = versionedSettings ?? Haven.Desktop.App.Services?.GetService(typeof(IVersionedSettingsStore)) as IVersionedSettingsStore;
        _deviceActions = Haven.Desktop.App.Services?.GetService(typeof(DeviceActionRouter)) as DeviceActionRouter;
        _graphAiEditor = Haven.Desktop.App.Services?.GetService(typeof(IAutomationGraphAiEditor)) as IAutomationGraphAiEditor;
        var deviceExecutor = Haven.Desktop.App.Services?.GetService(typeof(DeviceAutomationNodeExecutor)) as DeviceAutomationNodeExecutor;
        var builtInExecutor = Haven.Desktop.App.Services?.GetService(typeof(BuiltInAutomationActionNodeExecutor)) as BuiltInAutomationActionNodeExecutor;
        _workflowRunner = deviceExecutor is null && builtInExecutor is null ? null : new ReusableDeviceWorkflowRunner(deviceExecutor, builtInExecutor);

        _scene = new AutomationsHavenScene();
        _sceneHost = new HavenSceneControl { Root = _scene.Root };
        Content = _sceneHost;
        Background = Brushes.Transparent;

        _scene.RefreshRequested += OnRefreshRequested;
        _scene.OpenTasksRequested += OnOpenTasksRequested;
        _scene.NewWorkflowRequested += OnNewWorkflowRequested;
        _scene.RunWorkflowRequested += OnRunWorkflowRequested;
        _scene.EditWorkflowRequested += OnEditWorkflowRequested;
        _scene.TestWorkflowRequested += OnTestWorkflowRequested;
        _scene.DeleteWorkflowRequested += OnDeleteWorkflowRequested;
        _scene.SetWorkflowEnabledRequested += OnSetWorkflowEnabledRequested;
        _scene.OpenScheduledRequested += OnOpenScheduledRequested;
        _scene.BackRequested += OnBackRequested;
        _scene.SaveRequested += OnSaveRequested;
        _scene.TestGraphRequested += OnTestGraphRequested;
        _scene.AiEditRequested += OnAiEditRequested;
        AttachedToVisualTree += OnAttached;
    }

    internal AutomationsHavenScene Scene => _scene;
    internal HavenSceneControl SceneHost => _sceneHost;

    private async void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        await LoadDeviceCapabilityAsync();
        await RefreshAsync();
    }

    private void OnRefreshRequested(object? sender, EventArgs e) => _ = RefreshAsync();
    private void OnOpenTasksRequested(object? sender, EventArgs e) => _ = OpenTasksAsync();
    private void OnNewWorkflowRequested(object? sender, EventArgs e) => _scene.ShowEditor(null);
    private void OnRunWorkflowRequested(Guid id) => _ = RunWorkflowAsync(id);
    private void OnEditWorkflowRequested(Guid id) => OpenWorkflow(id);
    private void OnTestWorkflowRequested(Guid id) => _ = TestWorkflowAsync(id);
    private void OnDeleteWorkflowRequested(Guid id) => _ = DeleteWorkflowAsync(id);
    private void OnSetWorkflowEnabledRequested(Guid id, bool enabled) => _ = SetWorkflowEnabledAsync(id, enabled);
    private void OnOpenScheduledRequested(Guid id) => OpenScheduled(id);
    private void OnBackRequested(object? sender, EventArgs e) => _scene.ShowDashboard();
    private void OnSaveRequested(object? sender, EventArgs e) => _ = SaveEditorAsync();
    private void OnTestGraphRequested(object? sender, EventArgs e) => _ = TestEditorAsync();
    private void OnAiEditRequested(string instruction) => _ = ApplyAiEditAsync(instruction);

    private async Task RefreshAsync()
    {
        if (_disposed) return;
        if (_refreshing)
        {
            _refreshPending = true;
            return;
        }
        _refreshing = true;
        try
        {
            _scene.SetStatus("Loading Automations…");
            await EnsureHistoryLoadedAsync(_lifetime.Token);
            var workflowTask = _tasks.GetReusableTasksAsync(_containerId, _lifetime.Token);
            var scheduledTask = _automations.GetAllAsync(_lifetime.Token);
            await Task.WhenAll(workflowTask, scheduledTask);
            _workflows = workflowTask.Result;
            _scheduled = scheduledTask.Result.Where(item => item.ContainerId == _containerId).ToArray();

            var runs = new List<AutomationsRunCard>();
            foreach (var definition in _scheduled)
            {
                foreach (var run in await _automations.GetRunsAsync(definition.Id, 50, _lifetime.Token))
                {
                    var active = run.Status is AutomationRunStatus.Pending or AutomationRunStatus.Running;
                    var timestamp = run.CompletedAt ?? run.StartedAt ?? run.ScheduledFor;
                    var status = run.Status == AutomationRunStatus.SkippedDuplicate ? "Skipped duplicate" : run.Status.ToString();
                    var detail = run.Status switch
                    {
                        AutomationRunStatus.Pending => "Scheduled " + run.ScheduledFor.LocalDateTime.ToString("g"),
                        AutomationRunStatus.Running => "Started " + (run.StartedAt ?? run.ScheduledFor).LocalDateTime.ToString("g"),
                        AutomationRunStatus.Succeeded when !string.IsNullOrWhiteSpace(run.Result) => run.Result!,
                        AutomationRunStatus.Failed when !string.IsNullOrWhiteSpace(run.Error) => run.Error!,
                        _ => status + " " + timestamp.LocalDateTime.ToString("g")
                    };
                    runs.Add(new AutomationsRunCard(definition.Id, definition.Name, status, detail, active));
                }
            }

            var scheduledById = _scheduled.ToDictionary(item => item.Id);
            var workflowCards = _workflows.Select(workflow =>
            {
                var hasSchedule = scheduledById.TryGetValue(workflow.Id, out var automation) && ScheduledGraphAutomationPayloadCodec.IsPayload(automation.Instruction);
                var detail = !hasSchedule ? string.Empty : automation!.NextRunAt is null ? "Waiting for trigger" : "Next " + automation.NextRunAt.Value.LocalDateTime.ToString("g");
                return new AutomationsWorkflowCard(workflow.Id, workflow.Name, workflow.Description, workflow.IsEnabled, hasSchedule, detail);
            }).ToArray();
            var scheduledCards = _scheduled.Select(item => new AutomationsScheduledCard(
                item.Id, item.Name, item.NextRunAt is null ? "Waiting for trigger" : "Next " + item.NextRunAt.Value.LocalDateTime.ToString("g"), item.IsEnabled)).ToArray();
            var graphHistory = AutomationGraphHistoryJournal.ForContainer(_history, _containerId, 50);
            _scene.SetDashboardData(workflowCards, scheduledCards, runs.OrderByDescending(item => item.IsActive), graphHistory);
            _scene.SetStatus($"{_workflows.Count} reusable workflow{(_workflows.Count == 1 ? string.Empty : "s")} · {_scheduled.Count} scheduled automation{(_scheduled.Count == 1 ? string.Empty : "s")}");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _scene.SetStatus("Automations could not be loaded: " + ex.Message, true);
        }
        finally
        {
            _refreshing = false;
            if (_refreshPending && !_disposed)
            {
                _refreshPending = false;
                _ = RefreshAsync();
            }
        }
    }

    private async Task LoadDeviceCapabilityAsync()
    {
        if (_deviceActions is null || !OperatingSystem.IsWindows())
        {
            _scene.SetDeviceCapability(null);
            return;
        }
        var target = new DeviceTargetDescriptor("current", "This PC", CapabilityPlatform.Windows, DeviceTargetKind.CurrentDevice);
        try
        {
            _scene.SetDeviceCapability(await _deviceActions.GetSnapshotAsync(target, _lifetime.Token));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _scene.SetDeviceCapability(null);
            _scene.SetStatus("Device capabilities could not be discovered: " + ex.Message, true);
        }
    }

    private void OpenWorkflow(Guid id)
    {
        var workflow = _workflows.FirstOrDefault(item => item.Id == id);
        if (workflow is null)
        {
            _scene.SetStatus("That workflow no longer exists. Refresh Automations and try again.", true);
            return;
        }
        _scene.ShowEditor(workflow);
    }

    private async Task TestWorkflowAsync(Guid id)
    {
        OpenWorkflow(id);
        if (_scene.EditingWorkflow?.Id == id) await TestEditorAsync();
    }

    private async Task SaveEditorAsync()
    {
        if (string.IsNullOrWhiteSpace(_scene.WorkflowName))
        {
            _scene.SetStatus("Workflow name is required.", true);
            return;
        }
        if (!_scene.TryGetGraph(out var graph, out var graphError))
        {
            _scene.SetStatus(graphError ?? "The graph is not ready to save.", true);
            return;
        }

        var graphJson = graph.Nodes.Count == 0 && graph.Edges.Count == 0 ? null : AutomationGraphCodec.Serialize(graph);
        AutomationGraphScheduleBinding? scheduleBinding = null;
        if (!string.IsNullOrWhiteSpace(graphJson) && !AutomationGraphScheduleBinder.TryBind(graph, DateTimeOffset.UtcNow, out scheduleBinding, out var scheduleError))
        {
            _scene.SetStatus(scheduleError ?? "The graph schedule is not ready to save.", true);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var existing = _scene.EditingWorkflow;
        var workflow = new ReusableTaskDefinition(
            existing?.Id ?? Guid.NewGuid(),
            _scene.WorkflowName,
            _scene.WorkflowGoal,
            _scene.BuildInstructions(),
            _containerId,
            existing?.IsEnabled ?? true,
            existing?.CreatedAt ?? now,
            now,
            graphJson);
        try
        {
            await _tasks.UpsertReusableTaskAsync(workflow, _lifetime.Token);
            var scheduleDescription = await SyncScheduledGraphAsync(workflow, graphJson, scheduleBinding);
            _scene.SetStatus(scheduleDescription is null ? $"Saved {workflow.Name}." : $"Saved {workflow.Name} · {scheduleDescription}.");
            await RefreshAsync();
            _scene.ShowDashboard();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _scene.SetStatus("The workflow could not be fully saved: " + ex.Message, true);
        }
    }

    private async Task TestEditorAsync()
    {
        if (!_scene.TryGetGraph(out var graph, out var error))
        {
            _scene.SetStatus(error ?? "The graph is not ready to test.", true);
            return;
        }
        if (graph.Nodes.Count == 0)
        {
            _scene.SetStatus("Add at least one node before testing this workflow.", true);
            return;
        }
        _scene.SetStatus("Running non-destructive graph test…");
        var result = await AutomationGraphTestRunner.RunAsync(graph, _lifetime.Token);
        var graphJson = AutomationGraphCodec.Serialize(graph);
        var now = DateTimeOffset.UtcNow;
        var existing = _scene.EditingWorkflow;
        var draft = new ReusableTaskDefinition(
            existing?.Id ?? Guid.Empty,
            string.IsNullOrWhiteSpace(_scene.WorkflowName) ? "Unsaved workflow" : _scene.WorkflowName,
            _scene.WorkflowGoal,
            _scene.BuildInstructions(),
            _containerId,
            true,
            existing?.CreatedAt ?? now,
            now,
            graphJson);
        await RecordGraphHistoryAsync(draft, graphJson, result);
        _scene.SetGraphTestResult(result);
        _scene.SetStatus(result.Succeeded
            ? $"Test passed: {result.Trace.Count} node{(result.Trace.Count == 1 ? string.Empty : "s")} traced without external side effects."
            : result.FailureMessage ?? result.ValidationIssues.FirstOrDefault()?.Message ?? "Graph test failed.",
            !result.Succeeded);
    }

    private async Task ApplyAiEditAsync(string instruction)
    {
        if (_graphAiEditor is null)
        {
            _scene.SetStatus("The Automation graph AI editor is unavailable in this host.", true);
            return;
        }
        if (!_scene.TryGetGraph(out var current, out var validationError))
        {
            _scene.SetStatus(validationError ?? "Fix the current graph before applying an AI edit.", true);
            return;
        }
        _scene.SetStatus("Asking Haven for a typed graph edit…");
        try
        {
            var result = await _graphAiEditor.ProposeEditAsync(current, instruction, _lifetime.Token);
            if (!result.Succeeded || result.Graph is null)
            {
                _scene.SetStatus(result.Status, true);
                return;
            }
            _scene.ApplyAiGraph(result.Graph, result.Status);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _scene.SetStatus("The graph edit failed: " + ex.Message, true);
        }
    }

    private async Task RunWorkflowAsync(Guid id)
    {
        var workflow = _workflows.FirstOrDefault(item => item.Id == id);
        if (workflow is null)
        {
            _scene.SetStatus("That workflow no longer exists.", true);
            return;
        }
        if (!workflow.IsEnabled)
        {
            _scene.SetStatus($"{workflow.Name} is paused. Resume it before running.", true);
            return;
        }
        if (_workflowRunner is null)
        {
            if (string.IsNullOrWhiteSpace(workflow.GraphJson))
            {
                await InvokeTaskAsync(workflow.Instruction);
                return;
            }
            _scene.SetStatus("The graph runtime is unavailable. Haven did not route this graph to Tasks or perform a substitute instruction.", true);
            return;
        }

        _scene.SetStatus($"Running {workflow.Name}…");
        try
        {
            var run = await _workflowRunner.RunAsync(workflow, permissionGranted: false, _lifetime.Token);
            if (!run.Handled)
            {
                await InvokeTaskAsync(workflow.Instruction);
                return;
            }
            if (run.GraphResult is not null && !string.IsNullOrWhiteSpace(workflow.GraphJson))
                await RecordGraphHistoryAsync(workflow, workflow.GraphJson, run.GraphResult);
            _scene.SetStatus(FormatWorkflowRunStatus(run), run.GraphResult is { Succeeded: false });
            await RefreshAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _scene.SetStatus("The workflow could not run: " + ex.Message, true);
        }
    }

    private async Task SetWorkflowEnabledAsync(Guid id, bool enabled)
    {
        var workflow = _workflows.FirstOrDefault(item => item.Id == id);
        if (workflow is null)
        {
            _scene.SetStatus("That workflow no longer exists.", true);
            return;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            await _tasks.UpsertReusableTaskAsync(workflow with { IsEnabled = enabled, UpdatedAt = now }, _lifetime.Token);

            var linked = _scheduled.FirstOrDefault(item => item.Id == id);
            if (linked is not null && ScheduledGraphAutomationPayloadCodec.IsPayload(linked.Instruction))
            {
                var updated = linked with { IsEnabled = enabled, UpdatedAt = now, NextRunAt = null };
                if (enabled)
                    updated = updated with { NextRunAt = new ScheduledTaskScheduleCalculator().GetNextRun(updated, now.AddTicks(-1)) };
                await _automations.UpsertAsync(updated, _lifetime.Token);
            }

            await RefreshAsync();
            _scene.SetStatus(enabled ? $"Resumed {workflow.Name}." : $"Paused {workflow.Name}.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _scene.SetStatus($"{(enabled ? "Resume" : "Pause")} failed: {ex.Message}", true);
        }
    }

    private async Task DeleteWorkflowAsync(Guid id)
    {
        var workflow = _workflows.FirstOrDefault(item => item.Id == id);
        if (workflow is null) return;
        try
        {
            await DeleteLinkedScheduledGraphAsync(id);
            await _tasks.DeleteReusableTaskAsync(id, _lifetime.Token);
            await RefreshAsync();
            _scene.SetStatus($"Deleted {workflow.Name}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _scene.SetStatus("The workflow could not be deleted: " + ex.Message, true);
        }
    }

    private void OpenScheduled(Guid id)
    {
        var automation = _scheduled.FirstOrDefault(item => item.Id == id);
        if (automation is null) return;
        if (!ScheduledGraphAutomationPayloadCodec.IsPayload(automation.Instruction))
        {
            _ = InvokeTaskAsync(automation.Instruction);
            return;
        }
        var linked = _workflows.FirstOrDefault(workflow => workflow.Id == automation.Id);
        if (linked is not null)
        {
            _scene.ShowEditor(linked);
            _scene.SetStatus($"Opened scheduled graph {linked.Name}.");
            return;
        }
        if (!ScheduledGraphAutomationPayloadCodec.TryDeserialize(automation.Instruction, out var payload))
        {
            _scene.SetStatus("This scheduled graph payload is invalid. Haven did not route it to Tasks or perform a substitute instruction.", true);
            return;
        }
        var captured = new ReusableTaskDefinition(
            payload.WorkflowId,
            string.IsNullOrWhiteSpace(payload.WorkflowName) ? automation.Name : payload.WorkflowName,
            "Captured scheduled graph snapshot",
            string.Empty,
            automation.ContainerId,
            automation.IsEnabled,
            automation.CreatedAt,
            automation.UpdatedAt,
            payload.GraphJson);
        _scene.ShowEditor(captured);
        _scene.SetStatus($"Opened captured scheduled graph {captured.Name}.");
    }

    private async Task<string?> SyncScheduledGraphAsync(ReusableTaskDefinition workflow, string? graphJson, AutomationGraphScheduleBinding? binding)
    {
        var existing = (await _automations.GetAllAsync(_lifetime.Token)).FirstOrDefault(item => item.Id == workflow.Id);
        if (binding is null || string.IsNullOrWhiteSpace(graphJson))
        {
            if (existing is not null && ScheduledGraphAutomationPayloadCodec.IsPayload(existing.Instruction))
                await _automations.DeleteAsync(existing.Id, _lifetime.Token);
            return null;
        }
        if (existing is not null && !ScheduledGraphAutomationPayloadCodec.IsPayload(existing.Instruction))
            throw new InvalidOperationException("A legacy automation already uses this workflow ID. Haven left it unchanged instead of overwriting it.");
        var now = DateTimeOffset.UtcNow;
        var payload = ScheduledGraphAutomationPayloadCodec.Serialize(workflow.Id, binding.TriggerNodeId, workflow.Name, graphJson, binding.WatchCondition);
        var definition = new AutomationDefinition(
            workflow.Id, workflow.Name, HavenMode.Tasks, payload, binding.ScheduleKind, binding.ScheduleJson, null,
            workflow.ContainerId, workflow.IsEnabled, existing?.CreatedAt ?? workflow.CreatedAt, now);
        definition = definition with { NextRunAt = new ScheduledTaskScheduleCalculator().GetInitialRun(binding.ScheduleKind, binding.ScheduleJson, now) };
        await _automations.UpsertAsync(definition, _lifetime.Token);
        return binding.Description;
    }

    private async Task DeleteLinkedScheduledGraphAsync(Guid workflowId)
    {
        var existing = (await _automations.GetAllAsync(_lifetime.Token)).FirstOrDefault(item => item.Id == workflowId);
        if (existing is not null && ScheduledGraphAutomationPayloadCodec.IsPayload(existing.Instruction))
            await _automations.DeleteAsync(existing.Id, _lifetime.Token);
    }

    private async Task EnsureHistoryLoadedAsync(CancellationToken cancellationToken)
    {
        if (_historyLoaded) return;
        await _historyGate.WaitAsync(cancellationToken);
        try
        {
            if (_historyLoaded) return;
            var stored = _historySettings is null ? null : await _historySettings.GetAsync<AutomationGraphHistoryState>(GraphHistorySettingsKey, cancellationToken);
            _history = AutomationGraphHistoryJournal.Normalize(stored);
            _historyLoaded = true;
        }
        finally { _historyGate.Release(); }
    }

    private async Task RecordGraphHistoryAsync(ReusableTaskDefinition workflow, string graphJson, AutomationGraphRunResult result)
    {
        await EnsureHistoryLoadedAsync(_lifetime.Token);
        var entry = AutomationGraphHistoryJournal.Capture(workflow.Id, workflow.ContainerId, workflow.Name, workflow.Instruction, graphJson, result);
        await _historyGate.WaitAsync(_lifetime.Token);
        try
        {
            _history = AutomationGraphHistoryJournal.Append(_history, entry);
            if (_historySettings is not null) await _historySettings.SetAsync(GraphHistorySettingsKey, _history, _lifetime.Token);
        }
        finally { _historyGate.Release(); }
    }

    private async Task OpenTasksAsync()
    {
        try { await _startOneTimeTask(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { _scene.SetStatus("The one-time task could not be opened: " + ex.Message, true); }
    }

    private async Task InvokeTaskAsync(string instruction)
    {
        try { await _runTask(instruction); }
        catch (Exception ex) when (ex is not OperationCanceledException) { _scene.SetStatus("The task could not be opened: " + ex.Message, true); }
    }

    private static string FormatWorkflowRunStatus(ReusableDeviceWorkflowRunResult run)
    {
        if (run.Kind != ReusableDeviceWorkflowRunKind.DeviceAction || run.DeviceResult is null) return run.Message;
        var result = run.DeviceResult;
        var prefix = result.Status switch
        {
            DeviceActionResultStatus.Success => "DEVICE completed",
            DeviceActionResultStatus.Unsupported => "Unsupported",
            DeviceActionResultStatus.PermissionRequired => "Permission required",
            DeviceActionResultStatus.DeviceUnavailable => "Device unavailable",
            DeviceActionResultStatus.ConnectionLost => "Connection lost",
            DeviceActionResultStatus.ActionRejected => "Action rejected",
            DeviceActionResultStatus.PlatformError => "Platform error",
            _ => "DEVICE result"
        };
        return string.IsNullOrWhiteSpace(result.Output) ? $"{prefix}: {result.Message}" : $"{prefix}: {result.Message} {result.Output}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        AttachedToVisualTree -= OnAttached;
        _scene.RefreshRequested -= OnRefreshRequested;
        _scene.OpenTasksRequested -= OnOpenTasksRequested;
        _scene.NewWorkflowRequested -= OnNewWorkflowRequested;
        _scene.RunWorkflowRequested -= OnRunWorkflowRequested;
        _scene.EditWorkflowRequested -= OnEditWorkflowRequested;
        _scene.TestWorkflowRequested -= OnTestWorkflowRequested;
        _scene.DeleteWorkflowRequested -= OnDeleteWorkflowRequested;
        _scene.SetWorkflowEnabledRequested -= OnSetWorkflowEnabledRequested;
        _scene.OpenScheduledRequested -= OnOpenScheduledRequested;
        _scene.BackRequested -= OnBackRequested;
        _scene.SaveRequested -= OnSaveRequested;
        _scene.TestGraphRequested -= OnTestGraphRequested;
        _scene.AiEditRequested -= OnAiEditRequested;
        _scene.Dispose();
        _lifetime.Dispose();
        _historyGate.Dispose();
    }
}
