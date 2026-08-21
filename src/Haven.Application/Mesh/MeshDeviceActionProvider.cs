using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Application;

public sealed class MeshDeviceActionProvider(MeshCoordinator mesh) : IDeviceActionProvider
{
    public const string MeshProviderId = "haven.mesh";
    public string ProviderId => MeshProviderId;

    public bool CanHandle(DeviceTargetDescriptor target) =>
        target.Kind == DeviceTargetKind.MeshDevice &&
        (string.IsNullOrWhiteSpace(target.ProviderId) || string.Equals(target.ProviderId, ProviderId, StringComparison.OrdinalIgnoreCase)) &&
        Guid.TryParse(target.Id, out _);

    public async Task<DeviceCapabilitySnapshot> GetSnapshotAsync(DeviceTargetDescriptor target, CancellationToken cancellationToken)
    {
        if (!CanHandle(target) || !Guid.TryParse(target.Id, out var deviceId)) return new(target, false, DateTimeOffset.UtcNow, []);
        try
        {
            var remote = await mesh.GetRemoteDeviceSnapshotAsync(deviceId, cancellationToken).ConfigureAwait(false);
            return remote with
            {
                Target = target,
                Actions = remote.Actions.Select(action => action with { ProviderId = ProviderId }).ToArray()
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new DeviceCapabilitySnapshot(target, false, DateTimeOffset.UtcNow, []);
        }
    }

    public async Task<DeviceActionResult> ExecuteAsync(DeviceActionRequest request, CancellationToken cancellationToken)
    {
        if (!CanHandle(request.Target) || !Guid.TryParse(request.Target.Id, out var deviceId))
            return new(DeviceActionResultStatus.DeviceUnavailable, request.ActionKey, request.Target.Id, "The selected Mesh target is invalid or unavailable.");
        var snapshot = await GetSnapshotAsync(request.Target, cancellationToken).ConfigureAwait(false);
        if (!snapshot.IsReachable) return new(DeviceActionResultStatus.DeviceUnavailable, request.ActionKey, request.Target.Id, "The Mesh device is offline or did not return a capability snapshot.");
        var action = snapshot.Actions.FirstOrDefault(item => string.Equals(item.Key, request.ActionKey, StringComparison.OrdinalIgnoreCase));
        if (action is null) return new(DeviceActionResultStatus.Unsupported, request.ActionKey, request.Target.Id, "The remote device does not advertise this DEVICE action.");
        var result = await mesh.ExecuteRemoteDeviceActionAsync(deviceId, action.Key, action.CapabilityKey, request.Parameters, request.PermissionGranted, cancellationToken).ConfigureAwait(false);
        return result with { TargetId = request.Target.Id };
    }
}
