using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure;

/// <summary>
/// Executes an inbound Mesh DEVICE request through the native provider for this device.
/// Provider resolution is intentionally lazy so constructing MeshCoordinator does not create a
/// circular dependency through MeshDeviceActionProvider.
/// </summary>
public sealed class MeshInboundDeviceActionExecutor(IServiceProvider services) : IMeshInboundDeviceActionExecutor
{
    public async Task<DeviceCapabilitySnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var target = CurrentTarget();
        var provider = ResolveNativeProvider(target);
        return await provider.GetSnapshotAsync(target, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DeviceActionResult> ExecuteAsync(
        string actionKey,
        IReadOnlyDictionary<string, string>? parameters,
        bool permissionGranted,
        CancellationToken cancellationToken)
    {
        var target = CurrentTarget();
        var provider = ResolveNativeProvider(target);
        return await provider.ExecuteAsync(
            new DeviceActionRequest(target, actionKey, parameters, permissionGranted),
            cancellationToken).ConfigureAwait(false);
    }

    private IDeviceActionProvider ResolveNativeProvider(DeviceTargetDescriptor target)
    {
        var provider = services.GetServices<IDeviceActionProvider>().FirstOrDefault(candidate => candidate.CanHandle(target));
        return provider ?? throw new InvalidOperationException($"No native DEVICE provider is registered for {target.Platform}.");
    }

    private static DeviceTargetDescriptor CurrentTarget()
    {
        var platform = OperatingSystem.IsAndroid() ? CapabilityPlatform.Android : CapabilityPlatform.Windows;
        return new DeviceTargetDescriptor("current", Environment.MachineName, platform, DeviceTargetKind.CurrentDevice);
    }
}
