using Avalonia.Controls;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;

namespace Haven.Desktop.Views.Pages.Tasks;

public sealed partial class TasksPage
{
    private readonly Dictionary<Guid, Conversation> _havenHistory = [];
    private TasksHavenScene? _havenScene;
    private HavenSceneControl? _havenSceneHost;
    private bool _havenRefreshBusy;

    private void InitializeHavenScene(Grid host)
    {
        _havenScene = new TasksHavenScene();
        _havenSceneHost = new HavenSceneControl { Root = _havenScene.Root };
        host.Children.Add(_havenSceneHost);

        _havenScene.RefreshRequested += (_, _) => _ = RefreshHavenSceneAsync();
        _havenScene.StartOneTimeRequested += (_, _) => _ = StartHavenOneTimeTaskAsync();
        _havenScene.RunRequested += (_, args) => _ = RunHavenInstructionAsync(args.Instruction, "Opening this task in its persistent Tasks conversation…");
        _havenScene.TestRequested += (_, args) => _ = RunHavenInstructionAsync(args.Instruction, "Opening a safe task test…");
        _havenScene.AssistantRequested += (_, args) => _ = RunHavenInstructionAsync(args.Instruction, "Opening Haven to improve this task draft…");
        _havenScene.OpenHistoryRequested += (_, args) => _ = OpenHavenHistoryAsync(args.TaskId);
        _havenScene.SaveRequested += (_, args) => _ = SaveHavenReusableTaskAsync(args);
        _havenScene.DeleteRequested += (_, args) => _ = DeleteHavenReusableTaskAsync(args.TaskId);

        Loaded += (_, _) => _ = RefreshHavenSceneAsync();
    }

    private async Task RefreshHavenSceneAsync()
    {
        if (_havenScene is null || _havenRefreshBusy) return;
        _havenRefreshBusy = true;
        _havenScene.SetStatus("Loading Haven Tasks…");
        try
        {
            var reusableTask = _tasks.GetReusableTasksAsync(_containerId, CancellationToken.None);
            var scheduledTask = _automations.GetAllAsync(CancellationToken.None);
            var historyTask = _conversations.GetRecentAsync(HavenMode.Tasks, 50, CancellationToken.None);
            await Task.WhenAll(reusableTask, scheduledTask, historyTask);

            var reusable = reusableTask.Result
                .Where(item => item.IsEnabled)
                .Select(item => new TasksHavenReusableItem(item.Id, item.Name, item.Description, item.Instruction, item.UpdatedAt))
                .ToArray();
            var scheduled = scheduledTask.Result
                .Where(item => item.IsEnabled && item.ContainerId == _containerId)
                .OrderBy(item => item.NextRunAt)
                .Select(item => new TasksHavenScheduledItem(
                    item.Id,
                    item.Name,
                    item.Instruction,
                    item.NextRunAt is null
                        ? "Waiting for trigger"
                        : "Next " + item.NextRunAt.Value.LocalDateTime.ToString("g")))
                .ToArray();
            var history = historyTask.Result
                .Where(item => !item.IsArchived)
                .ToArray();

            _havenHistory.Clear();
            foreach (var conversation in history) _havenHistory[conversation.Id] = conversation;

            _havenScene.SetData(
                reusable,
                scheduled,
                history.Select(item => new TasksHavenHistoryItem(item.Id, item.Title, item.UpdatedAt)));
            _havenScene.SetStatus($"{reusable.Length} reusable and {scheduled.Length} automatic task{(reusable.Length + scheduled.Length == 1 ? string.Empty : "s")} available.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _havenScene.SetStatus("Haven Tasks could not be loaded: " + ex.Message, isError: true);
        }
        finally
        {
            _havenRefreshBusy = false;
        }
    }

    private async Task SaveHavenReusableTaskAsync(TasksHavenDraftEventArgs draft)
    {
        if (_havenScene is null) return;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var existing = draft.TaskId is Guid taskId
                ? (await _tasks.GetReusableTasksAsync(_containerId, CancellationToken.None)).FirstOrDefault(item => item.Id == taskId)
                : null;
            var item = new ReusableTaskDefinition(
                draft.TaskId ?? Guid.NewGuid(),
                draft.Name,
                draft.Goal,
                draft.Instruction,
                _containerId,
                true,
                existing?.CreatedAt ?? now,
                now);

            await _tasks.UpsertReusableTaskAsync(item, CancellationToken.None);
            _havenScene.ShowDashboard();
            await RefreshHavenSceneAsync();
            _havenScene.SetStatus($"Saved {draft.Name}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _havenScene.SetEditorStatus("The task could not be saved: " + ex.Message, isError: true);
        }
    }

    private async Task DeleteHavenReusableTaskAsync(Guid taskId)
    {
        if (_havenScene is null) return;
        try
        {
            await _tasks.DeleteReusableTaskAsync(taskId, CancellationToken.None);
            _havenScene.ShowDashboard();
            await RefreshHavenSceneAsync();
            _havenScene.SetStatus("Deleted task.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _havenScene.SetEditorStatus("The task could not be deleted: " + ex.Message, isError: true);
        }
    }

    private async Task OpenHavenHistoryAsync(Guid conversationId)
    {
        if (_havenScene is null || !_havenHistory.TryGetValue(conversationId, out var conversation)) return;
        try
        {
            _havenScene.SetStatus("Opening task history…");
            await _openTaskHistory(conversation);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _havenScene.SetStatus("The task history could not be opened: " + ex.Message, isError: true);
        }
    }

    private async Task RunHavenInstructionAsync(string instruction, string status)
    {
        if (_havenScene is null) return;
        _havenScene.SetStatus(status);
        try
        {
            await _runTask(instruction);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _havenScene.SetStatus("The task could not be opened: " + ex.Message, isError: true);
        }
    }

    private async Task StartHavenOneTimeTaskAsync()
    {
        if (_havenScene is null) return;
        _havenScene.SetStatus("Opening a new one-time task…");
        try
        {
            await _startOneTimeTask();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _havenScene.SetStatus("The one-time task could not be opened: " + ex.Message, isError: true);
        }
    }
}
