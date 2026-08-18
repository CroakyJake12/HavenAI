using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class DeviceAutomationActionTests
{
    private static readonly DeviceTargetDescriptor ThisPc = new("current", "This PC", CapabilityPlatform.Windows, DeviceTargetKind.CurrentDevice);

    [Fact]
    public async Task Snapshot_ReflectsRealCapabilityAndUnsupportedSettings()
    {
        var provider = CreateProvider(new FakeComputerToolService(), CapabilityAvailability.PermissionRequired);
        var snapshot = await provider.GetSnapshotAsync(ThisPc, CancellationToken.None);
        Assert.True(snapshot.IsReachable);
        Assert.Equal("DEVICE", new DeviceAutomationNodeDefinition(Guid.NewGuid(), ThisPc, "applications.launch", new Dictionary<string,string>()).Category);
        Assert.Equal(DeviceActionAvailability.PermissionRequired, snapshot.Actions.Single(x => x.Key == "applications.launch").Availability);
        Assert.Equal(DeviceActionAvailability.Unsupported, snapshot.Actions.Single(x => x.Key == "connectivity.wifi").Availability);
        Assert.Equal(DeviceActionAvailability.Unsupported, snapshot.Actions.Single(x => x.Key == "audio.volume").Availability);
    }

    [Fact]
    public async Task Execute_RequiresPermissionBeforeNativeService()
    {
        var computer = new FakeComputerToolService();
        var provider = CreateProvider(computer, CapabilityAvailability.PermissionRequired);
        var result = await provider.ExecuteAsync(new DeviceActionRequest(ThisPc, "applications.launch", new Dictionary<string,string>{{"name","Calculator"}}), CancellationToken.None);
        Assert.Equal(DeviceActionResultStatus.PermissionRequired, result.Status);
        Assert.Null(computer.LaunchedApp);
    }

    [Fact]
    public async Task Execute_LaunchesThroughExistingComputerToolService()
    {
        var computer = new FakeComputerToolService();
        var provider = CreateProvider(computer, CapabilityAvailability.PermissionRequired);
        var result = await provider.ExecuteAsync(new DeviceActionRequest(ThisPc, "applications.launch", new Dictionary<string,string>{{"name","Calculator"}}, true), CancellationToken.None);
        Assert.Equal(DeviceActionResultStatus.Success, result.Status);
        Assert.Equal("Calculator", computer.LaunchedApp);
        Assert.Equal("launched:Calculator", result.Output);
    }

    [Fact]
    public async Task Router_DoesNotPretendWindowsProviderCanControlAndroidMeshTarget()
    {
        var router = new DeviceActionRouter([CreateProvider(new FakeComputerToolService(), CapabilityAvailability.Available)]);
        var target = new DeviceTargetDescriptor("phone","Phone",CapabilityPlatform.Android,DeviceTargetKind.MeshDevice);
        var snapshot = await router.GetSnapshotAsync(target, CancellationToken.None);
        var result = await router.ExecuteAsync(new DeviceActionRequest(target, "audio.volume", PermissionGranted:true), CancellationToken.None);
        Assert.False(snapshot.IsReachable); Assert.Empty(snapshot.Actions); Assert.Equal(DeviceActionResultStatus.DeviceUnavailable, result.Status);
    }
    private static WindowsComputerDeviceActionProvider CreateProvider(FakeComputerToolService computer, CapabilityAvailability availability)
    {
        var definition = CapabilityRegistryCatalog.BuiltIns.Single(x => x.Key == WindowsComputerDeviceActionProvider.CapabilityKey) with { Availability = availability };
        return new WindowsComputerDeviceActionProvider(computer, new CapabilityRegistryService(new FakeCapabilityRepository(definition)));
    }

    private sealed class FakeCapabilityRepository(CapabilityDefinition definition) : ICapabilityRepository
    {
        public Task<IReadOnlyList<CapabilityDefinition>> GetCapabilitiesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CapabilityDefinition>>([definition]);
        public Task UpsertCapabilityAsync(CapabilityDefinition capability, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetCapabilityEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteCustomCapabilityAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeComputerToolService : IComputerToolService
    {
        public string? LaunchedApp { get; private set; }
        public Task<string> SnapshotAsync(CancellationToken cancellationToken) => Task.FromResult("snapshot");
        public Task<string> ListWindowsAsync(CancellationToken cancellationToken) => Task.FromResult("windows");
        public Task<string> LaunchAppAsync(string name, CancellationToken cancellationToken) { LaunchedApp=name; return Task.FromResult($"launched:{name}"); }
        public Task<string> FocusWindowAsync(string title, CancellationToken cancellationToken) => Task.FromResult($"focused:{title}");
        public Task<string> InvokeAsync(string windowTitle,string name,string automationId,CancellationToken cancellationToken) => Task.FromResult("invoked");
        public Task<string> ClickAsync(string windowTitle,int x,int y,string button,CancellationToken cancellationToken) => Task.FromResult("clicked");
        public Task<string> TypeAsync(string windowTitle,string text,CancellationToken cancellationToken) => Task.FromResult("typed");
        public Task<string> PressAsync(string windowTitle,string keys,CancellationToken cancellationToken) => Task.FromResult("pressed");
        public Task<string> CloseWindowAsync(string title,CancellationToken cancellationToken) => Task.FromResult("closed");
    }
}