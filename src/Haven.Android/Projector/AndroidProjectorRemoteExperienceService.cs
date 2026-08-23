using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Android;

public sealed record AndroidProjectorRemoteTarget(string RuntimeId, string? StableIdentity, string Name);
public sealed record AndroidProjectorRemoteRouteResult(bool Succeeded, string Message);

public sealed class AndroidProjectorRemoteExperienceService : IProjectorExperienceProvider
{
    public const string EnumerateTargetsActionKey = "projector.targets";
    public const string RouteExperienceActionKey = "projector.route";
    public const string CapabilityKey = "computer-device-use";
    private const string ExperiencePrefix = "remote-projector:";
    private static readonly TimeSpan TargetCacheLifetime = TimeSpan.FromSeconds(10);

    private readonly MeshCoordinator _mesh;
    private readonly object _cacheGate = new();
    private readonly Dictionary<Guid, CachedTargets> _targetCache = [];

    public AndroidProjectorRemoteExperienceService(MeshCoordinator mesh)
    {
        _mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
    }

    public async ValueTask<IReadOnlyList<ProjectorExperience>> GetExperiencesAsync(
        ProjectorSessionSnapshot? session,
        CancellationToken cancellationToken)
    {
        MeshDashboardSnapshot dashboard;
        try
        {
            dashboard = await _mesh.GetDashboardAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            global::Android.Util.Log.Warn("HavenProjector", "Remote Projector discovery is unavailable: " + exception.Message);
            return [];
        }

        var experiences = new List<ProjectorExperience>();
        foreach (var peer in dashboard.TrustedPeers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (peer.Presence.Connection != MeshConnectionState.Connected
                || peer.Presence.Presence is MeshPresenceState.Offline or MeshPresenceState.Stale)
            {
                Invalidate(peer.Peer.DeviceId);
                continue;
            }

            var targets = await GetTargetsAsync(peer, forceRefresh: false, cancellationToken).ConfigureAwait(false);
            foreach (var target in targets)
            {
                experiences.Add(new ProjectorExperience(
                    ExperienceId(peer.Peer.DeviceId, target.RuntimeId),
                    $"Desktop · {peer.Peer.DisplayName} / {target.Name}",
                    $"Route Projector Desktop to {target.Name} on {peer.Peer.DisplayName}.",
                    "studio",
                    ArtworkKey: null,
                    ProjectorExperienceSource.RemoteDevice,
                    ProjectorLaunchStrategy.RemoteDevice,
                    ProjectorInteractionProfile.Desktop,
                    ProjectorExperiencePersistence.Session,
                    [],
                    ProjectorContentSensitivity.Private));
            }
        }

        return experiences;
    }

    public async Task<AndroidProjectorRemoteRouteResult> RouteAsync(
        ProjectorExperience experience,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(experience);
        if (!TryParseExperienceId(experience.Id, out var deviceId, out var runtimeId))
            return new(false, "The selected remote Projector destination is invalid.");

        MeshDashboardSnapshot dashboard;
        try
        {
            dashboard = await _mesh.GetDashboardAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new(false, "Mesh could not inspect the remote Projector destination: " + exception.Message);
        }

        var peer = dashboard.TrustedPeers.FirstOrDefault(candidate => candidate.Peer.DeviceId == deviceId);
        if (peer is null
            || peer.Presence.Connection != MeshConnectionState.Connected
            || peer.Presence.Presence is MeshPresenceState.Offline or MeshPresenceState.Stale)
        {
            Invalidate(deviceId);
            return new(false, "The remote device is disconnected. Reconnect it in Mesh, then choose the Projector destination again.");
        }

        var liveTargets = await GetTargetsAsync(peer, forceRefresh: true, cancellationToken).ConfigureAwait(false);
        if (!liveTargets.Any(target => string.Equals(target.RuntimeId, runtimeId, StringComparison.Ordinal)))
        {
            return new(false, "That remote Projector screen is no longer available. Refresh the Gallery after the display reconnects.");
        }

        DeviceActionResult result;
        try
        {
            result = await _mesh.ExecuteRemoteDeviceActionAsync(
                deviceId,
                RouteExperienceActionKey,
                CapabilityKey,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["runtimeId"] = runtimeId,
                    ["experienceId"] = "desktop"
                },
                sourcePermissionGranted: true,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Invalidate(deviceId);
            return new(false, "Remote Projector routing failed before acknowledgement: " + exception.Message);
        }

        if (!result.Succeeded)
        {
            if (result.Status is DeviceActionResultStatus.ConnectionLost or DeviceActionResultStatus.DeviceUnavailable)
                Invalidate(deviceId);
            return new(false, result.Message);
        }

        return new(true, result.Message);
    }

    private async Task<IReadOnlyList<AndroidProjectorRemoteTarget>> GetTargetsAsync(
        MeshPeerSnapshot peer,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!forceRefresh)
        {
            lock (_cacheGate)
            {
                if (_targetCache.TryGetValue(peer.Peer.DeviceId, out var cached)
                    && DateTimeOffset.UtcNow - cached.CapturedAt < TargetCacheLifetime)
                    return cached.Targets;
            }
        }

        try
        {
            var snapshot = await _mesh.GetRemoteDeviceSnapshotAsync(peer.Peer.DeviceId, cancellationToken).ConfigureAwait(false);
            if (!snapshot.IsReachable
                || !Supports(snapshot, EnumerateTargetsActionKey)
                || !Supports(snapshot, RouteExperienceActionKey))
            {
                Invalidate(peer.Peer.DeviceId);
                return [];
            }

            var result = await _mesh.ExecuteRemoteDeviceActionAsync(
                peer.Peer.DeviceId,
                EnumerateTargetsActionKey,
                CapabilityKey,
                parameters: null,
                sourcePermissionGranted: true,
                cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Output))
            {
                Invalidate(peer.Peer.DeviceId);
                return [];
            }

            var parsed = JsonSerializer.Deserialize<AndroidProjectorRemoteTarget[]>(result.Output) ?? [];
            var targets = parsed
                .Where(target => !string.IsNullOrWhiteSpace(target.RuntimeId) && !string.IsNullOrWhiteSpace(target.Name))
                .GroupBy(target => target.RuntimeId, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            lock (_cacheGate)
                _targetCache[peer.Peer.DeviceId] = new(DateTimeOffset.UtcNow, targets);
            return targets;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or JsonException)
        {
            Invalidate(peer.Peer.DeviceId);
            global::Android.Util.Log.Warn("HavenProjector", "Could not discover remote Projector targets: " + exception.Message);
            return [];
        }
    }

    private static bool Supports(DeviceCapabilitySnapshot snapshot, string actionKey) => snapshot.Actions.Any(action =>
        string.Equals(action.Key, actionKey, StringComparison.OrdinalIgnoreCase)
        && action.Availability is DeviceActionAvailability.Supported or DeviceActionAvailability.PermissionRequired);

    private void Invalidate(Guid deviceId)
    {
        lock (_cacheGate)
            _targetCache.Remove(deviceId);
    }

    private static string ExperienceId(Guid deviceId, string runtimeId) =>
        ExperiencePrefix + deviceId.ToString("N") + ":" + Convert.ToHexString(Encoding.UTF8.GetBytes(runtimeId));

    private static bool TryParseExperienceId(string value, out Guid deviceId, out string runtimeId)
    {
        deviceId = Guid.Empty;
        runtimeId = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(ExperiencePrefix, StringComparison.OrdinalIgnoreCase))
            return false;
        var payload = value.AsSpan(ExperiencePrefix.Length);
        var separator = payload.IndexOf(':');
        if (separator <= 0 || separator == payload.Length - 1)
            return false;
        if (!Guid.TryParseExact(payload[..separator], "N", out deviceId))
            return false;
        try
        {
            runtimeId = Encoding.UTF8.GetString(Convert.FromHexString(payload[(separator + 1)..].ToString()));
            return !string.IsNullOrWhiteSpace(runtimeId) && runtimeId.Length <= 512;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed record CachedTargets(DateTimeOffset CapturedAt, IReadOnlyList<AndroidProjectorRemoteTarget> Targets);
}
