using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class MeshInboundDeviceActionExecutor : IMeshInboundDeviceActionExecutor
{
    private readonly WindowsComputerDeviceActionProvider _provider;
    private readonly DeviceTargetDescriptor _target;

    public MeshInboundDeviceActionExecutor(IComputerToolService computer, CapabilityRegistryService capabilities)
    {
        _provider = new WindowsComputerDeviceActionProvider(computer, capabilities);
        _target = new DeviceTargetDescriptor("current", Environment.MachineName, CapabilityPlatform.Windows, DeviceTargetKind.CurrentDevice, WindowsComputerDeviceActionProvider.NativeProviderId);
    }

    public Task<DeviceCapabilitySnapshot> GetSnapshotAsync(CancellationToken cancellationToken) => _provider.GetSnapshotAsync(_target, cancellationToken);

    public Task<DeviceActionResult> ExecuteAsync(string actionKey, IReadOnlyDictionary<string, string>? parameters, bool permissionGranted, CancellationToken cancellationToken) =>
        _provider.ExecuteAsync(new DeviceActionRequest(_target, actionKey, parameters, permissionGranted), cancellationToken);
}
