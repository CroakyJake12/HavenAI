using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class AutomationBuiltInActionNodeTests
{
    [Fact]
    public async Task Test_mode_previews_app_launch_without_requiring_live_capability()
    {
        var node = Node("App", ("action", "launch"), ("name", "Calculator"));
        var executor = new BuiltInAutomationActionNodeExecutor();

        var result = await executor.ExecuteAsync(Context(node, AutomationGraphRunMode.Test), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("would launch Calculator", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Real_file_read_uses_logged_workspace_scoped_filesystem_service()
    {
        var workspace = new RecordingWorkspaceTools { ReadContent = "hello from workspace" };
        var activity = new RecordingActivityLog();
        var filesystem = new FilesystemActionService(workspace, activity);
        var node = Node("File", ("operation", "read"), ("workspaceRoot", "C:/repo"), ("path", "notes.txt"));
        var executor = new BuiltInAutomationActionNodeExecutor(new DeviceActionRouter([]), filesystem);

        var result = await executor.ExecuteAsync(Context(node, AutomationGraphRunMode.Real), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("hello from workspace", result.Output);
        Assert.Equal(("C:/repo", "notes.txt"), workspace.LastRead);
        Assert.Single(activity.Events);
    }

    [Fact]
    public async Task Emit_action_produces_deterministic_output_in_real_mode()
    {
        var node = Node("Action", ("action", "emit"), ("value", "ready"));
        var executor = new BuiltInAutomationActionNodeExecutor();

        var result = await executor.ExecuteAsync(Context(node, AutomationGraphRunMode.Real), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("ready", result.Output);
    }

    [Theory]
    [InlineData("write")]
    [InlineData("delete")]
    [InlineData("run-command")]
    public void File_node_rejects_unpermissioned_mutation_or_command_operations(string operation)
    {
        var node = Node("File", ("operation", operation), ("workspaceRoot", "C:/repo"), ("path", "notes.txt"));
        var issues = BuiltInAutomationActionNodeExecutor.ValidateConfiguration(node);
        Assert.Contains(issues, issue => issue.Code == "file.operation.unsupported");
    }

    [Fact]
    public async Task Test_graph_runs_app_file_and_action_nodes_without_external_side_effects()
    {
        var trigger = Node("Trigger");
        var app = Node("App", ("action", "launch"), ("name", "Calculator"));
        var file = Node("File", ("operation", "read"), ("workspaceRoot", "C:/repo"), ("path", "notes.txt"));
        var action = Node("Action", ("action", "emit"), ("value", "ready"));
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [trigger, app, file, action],
            [new(trigger.Id, app.Id), new(app.Id, file.Id), new(file.Id, action.Id)]);

        var result = await AutomationGraphTestRunner.RunAsync(graph, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(4, result.Trace.Count);
        Assert.All(result.Trace, trace => Assert.Equal(AutomationGraphTraceStatus.Succeeded, trace.Status));
        Assert.Contains(result.Trace, trace => trace.NodeId == app.Id && trace.Message.Contains("would launch", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Trace, trace => trace.NodeId == file.Id && trace.Message.Contains("would read", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Trace, trace => trace.NodeId == action.Id && trace.Output == "ready");
    }

    [Fact]
    public async Task Reusable_real_graph_executes_deterministic_action_node_without_instruction_fallback()
    {
        var trigger = Node("Trigger");
        var action = Node("Action", ("action", "emit"), ("value", "ready"));
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [trigger, action], [new(trigger.Id, action.Id)]);
        var now = DateTimeOffset.UtcNow;
        var workflow = new ReusableTaskDefinition(Guid.NewGuid(), "Action workflow", string.Empty, "legacy instruction must not run", null, true, now, now, AutomationGraphCodec.Serialize(graph));
        var runner = new ReusableDeviceWorkflowRunner(null, new BuiltInAutomationActionNodeExecutor());

        var result = await runner.RunAsync(workflow, permissionGranted: false, CancellationToken.None);

        Assert.True(result.Handled);
        Assert.Equal(ReusableDeviceWorkflowRunKind.GraphWorkflow, result.Kind);
        Assert.True(result.GraphResult?.Succeeded);
        Assert.Contains(result.GraphResult!.Trace, trace => trace.NodeId == action.Id && trace.Output == "ready");
    }

    private static AutomationGraphNodeExecutionContext Context(AutomationGraphNodeDefinition node, AutomationGraphRunMode mode) =>
        new(node, mode, new Dictionary<Guid, string?>());

    private static AutomationGraphNodeDefinition Node(string category, params (string Key, string Value)[] parameters) =>
        new(Guid.NewGuid(), category, null, null, parameters.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase))
        {
            Title = category
        };

    private sealed class RecordingWorkspaceTools : IWorkspaceToolService
    {
        public string ReadContent { get; init; } = string.Empty;
        public (string Root, string Path)? LastRead { get; private set; }
        public string ResolveWorkspacePath(string workspaceRoot, string relativePath) => Path.Combine(workspaceRoot, relativePath);
        public Task<string> ReadTextAsync(string workspaceRoot, string relativePath, CancellationToken cancellationToken)
        {
            LastRead = (workspaceRoot, relativePath);
            return Task.FromResult(ReadContent);
        }
        public Task WriteTextAtomicAsync(string workspaceRoot, string relativePath, string content, CancellationToken cancellationToken) => throw new InvalidOperationException("Write should not be called.");
        public Task<IReadOnlyList<string>> SearchFilesAsync(string workspaceRoot, string searchPattern, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException("Process should not be called.");
    }

    private sealed class RecordingActivityLog : IActivityLogRepository
    {
        public List<ActivityEvent> Events { get; } = [];
        public Task<IReadOnlyList<ActivityEvent>> GetRecentAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ActivityEvent>>(Events.Take(limit).ToArray());
        public Task AddEventAsync(ActivityEvent activityEvent, CancellationToken cancellationToken)
        {
            Events.Add(activityEvent);
            return Task.CompletedTask;
        }
    }
}
