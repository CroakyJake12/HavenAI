using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class ReusableDeviceWorkflowRunnerTests
{
    private static readonly DeviceTargetDescriptor ThisPc =
        new("current", "This PC", CapabilityPlatform.Windows, DeviceTargetKind.CurrentDevice, "test.device");

    [Fact]
    public async Task RunAsync_WithoutGraph_UsesInstructionPath()
    {
        var provider = new RecordingProvider();
        var result = await CreateRunner(provider).RunAsync(Definition(null), false, CancellationToken.None);

        Assert.Equal(ReusableDeviceWorkflowRunKind.NotDeviceWorkflow, result.Kind);
        Assert.False(result.Handled);
        Assert.Equal(0, provider.ExecuteCount);
    }

    [Fact]
    public async Task RunAsync_InvalidGraph_DoesNotExecute()
    {
        var provider = new RecordingProvider();
        var result = await CreateRunner(provider).RunAsync(Definition("{not-json"), false, CancellationToken.None);

        Assert.Equal(ReusableDeviceWorkflowRunKind.InvalidGraph, result.Kind);
        Assert.True(result.Handled);
        Assert.Equal(0, provider.ExecuteCount);
    }

    [Fact]
    public async Task RunAsync_InstructionGraph_UsesInstructionPath()
    {
        var provider = new RecordingProvider();
        var graph = new AutomationGraphDefinition(
            AutomationGraphDefinition.CurrentVersion,
            [new AutomationGraphNodeDefinition(Guid.NewGuid(), "Instruction", null, null, new Dictionary<string, string>())],
            []);

        var result = await CreateRunner(provider).RunAsync(Definition(AutomationGraphCodec.Serialize(graph)), false, CancellationToken.None);

        Assert.Equal(ReusableDeviceWorkflowRunKind.NotDeviceWorkflow, result.Kind);
        Assert.Equal(0, provider.ExecuteCount);
    }

    [Fact]
    public async Task RunAsync_SingleDeviceNode_DelegatesWithoutGrantingPermission()
    {
        var provider = new RecordingProvider(new DeviceActionResult(
            DeviceActionResultStatus.Success, "ui.snapshot", "current", "Device action completed.", "snapshot"));
        var graphJson = DeviceGraph("ui.snapshot");

        var result = await CreateRunner(provider).RunAsync(Definition(graphJson), false, CancellationToken.None);

        Assert.Equal(ReusableDeviceWorkflowRunKind.DeviceAction, result.Kind);
        Assert.Equal(DeviceActionResultStatus.Success, result.DeviceResult?.Status);
        Assert.Equal(1, provider.ExecuteCount);
        Assert.NotNull(provider.LastRequest);
        Assert.False(provider.LastRequest!.PermissionGranted);
        Assert.Equal("ui.snapshot", provider.LastRequest.ActionKey);
        Assert.Equal("current", provider.LastRequest.Target.Id);
    }

    [Fact]
    public async Task RunAsync_PermissionRequired_IsPreserved()
    {
        var provider = new RecordingProvider(new DeviceActionResult(
            DeviceActionResultStatus.PermissionRequired, "applications.launch", "current", "Permission is required."));

        var result = await CreateRunner(provider).RunAsync(
            Definition(DeviceGraph("applications.launch", new Dictionary<string, string> { ["name"] = "Calculator" })),
            false,
            CancellationToken.None);

        Assert.Equal(ReusableDeviceWorkflowRunKind.DeviceAction, result.Kind);
        Assert.Equal(DeviceActionResultStatus.PermissionRequired, result.DeviceResult?.Status);
        Assert.Equal(1, provider.ExecuteCount);
        Assert.False(provider.LastRequest!.PermissionGranted);
    }

    [Fact]
    public async Task RunAsync_MultipleNodes_RefusesPartialDeviceExecution()
    {
        var provider = new RecordingProvider();
        var device = AutomationGraphNodeDefinition.FromDevice(
            new DeviceAutomationNodeDefinition(Guid.NewGuid(), ThisPc, "ui.snapshot", new Dictionary<string, string>()));
        var graph = new AutomationGraphDefinition(
            AutomationGraphDefinition.CurrentVersion,
            [device, new AutomationGraphNodeDefinition(Guid.NewGuid(), "Instruction", null, null, new Dictionary<string, string>())],
            []);

        var result = await CreateRunner(provider).RunAsync(Definition(AutomationGraphCodec.Serialize(graph)), false, CancellationToken.None);

        Assert.Equal(ReusableDeviceWorkflowRunKind.UnsupportedGraph, result.Kind);
        Assert.Equal(0, provider.ExecuteCount);
    }

    [Fact]
    public async Task RunAsync_MalformedDeviceNode_DoesNotExecute()
    {
        var provider = new RecordingProvider();
        var graph = new AutomationGraphDefinition(
            AutomationGraphDefinition.CurrentVersion,
            [new AutomationGraphNodeDefinition(Guid.NewGuid(), DeviceAutomationNodeCategory.Key, null, null, new Dictionary<string, string>())],
            []);

        var result = await CreateRunner(provider).RunAsync(Definition(AutomationGraphCodec.Serialize(graph)), false, CancellationToken.None);

        Assert.Equal(ReusableDeviceWorkflowRunKind.InvalidGraph, result.Kind);
        Assert.Equal(0, provider.ExecuteCount);
    }

    private static ReusableDeviceWorkflowRunner CreateRunner(RecordingProvider provider) =>
        new(new DeviceAutomationNodeExecutor(new DeviceActionRouter([provider])));

    private static string DeviceGraph(string actionKey, IReadOnlyDictionary<string, string>? parameters = null)
    {
        var node = new DeviceAutomationNodeDefinition(
            Guid.NewGuid(), ThisPc, actionKey, parameters ?? new Dictionary<string, string>());
        return AutomationGraphCodec.Serialize(new AutomationGraphDefinition(
            AutomationGraphDefinition.CurrentVersion,
            [AutomationGraphNodeDefinition.FromDevice(node)],
            []));
    }

    private static ReusableTaskDefinition Definition(string? graphJson)
    {
        var now = DateTimeOffset.UtcNow;
        return new ReusableTaskDefinition(
            Guid.NewGuid(), "Workflow", "Description", "Instruction", null, true, now, now, graphJson);
    }

    private sealed class RecordingProvider(DeviceActionResult? result = null) : IDeviceActionProvider
    {
        public string ProviderId => "test.device";
        public int ExecuteCount { get; private set; }
        public DeviceActionRequest? LastRequest { get; private set; }

        public bool CanHandle(DeviceTargetDescriptor target) =>
            string.Equals(target.ProviderId, ProviderId, StringComparison.OrdinalIgnoreCase);

        public Task<DeviceCapabilitySnapshot> GetSnapshotAsync(
            DeviceTargetDescriptor target,
            CancellationToken cancellationToken) =>
            Task.FromResult(new DeviceCapabilitySnapshot(target, true, DateTimeOffset.UtcNow, []));

        public Task<DeviceActionResult> ExecuteAsync(DeviceActionRequest request, CancellationToken cancellationToken)
        {
            ExecuteCount++;
            LastRequest = request;
            return Task.FromResult(result ?? new DeviceActionResult(
                DeviceActionResultStatus.Success, request.ActionKey, request.Target.Id, "Device action completed."));
        }
    }
}
