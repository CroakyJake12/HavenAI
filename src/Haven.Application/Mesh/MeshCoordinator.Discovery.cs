using System.Collections.Concurrent;
using Haven.Core;

namespace Haven.Application;

public sealed partial class MeshCoordinator
{
    private static readonly TimeSpan DiscoveryCandidateLifetime = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DiscoveryReconnectBackoff = TimeSpan.FromSeconds(10);
    private readonly ConcurrentDictionary<Guid, MeshDiscoveryCandidate> _nearby = new();
    private readonly ConcurrentDictionary<Guid, byte> _discoveryReconnects = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _discoveryReconnectNotBefore = new();
    private IMeshDiscoveryService? _discovery;

    public MeshCoordinator(
        IMeshStateStore stateStore,
        IMeshIdentitySecretStore identitySecrets,
        IMeshTransport transport,
        IMeshCapabilitySource capabilities,
        IMeshResourceMergeService merge,
        IMeshInboundDeviceActionExecutor inboundDeviceActions,
        IMeshDiscoveryService discovery)
        : this(stateStore, identitySecrets, transport, capabilities, merge, inboundDeviceActions)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _discovery.CandidateObserved += OnDiscoveryCandidateObserved;
    }

    private async Task StartDiscoveryAsync(CancellationToken cancellationToken)
    {
        if (_discovery is null || _state.LocalIdentity is null || string.IsNullOrWhiteSpace(_transport.LocalEndpoint)) return;
        await _discovery.StartAsync(_state.LocalIdentity, _transport.LocalEndpoint, cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<MeshDiscoveryCandidate> GetNearbyCandidates()
    {
        var threshold = DateTimeOffset.UtcNow - DiscoveryCandidateLifetime;
        foreach (var pair in _nearby.ToArray())
            if (pair.Value.ObservedAt < threshold) _nearby.TryRemove(pair.Key, out _);
        var trustedIds = _state.TrustedPeers.Where(peer => peer.TrustState == MeshPeerTrustState.Trusted).Select(peer => peer.DeviceId).ToHashSet();
        return _nearby.Values
            .Where(candidate => !trustedIds.Contains(candidate.DeviceId) && candidate.ObservedAt >= threshold)
            .OrderBy(candidate => candidate.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private void OnDiscoveryCandidateObserved(MeshDiscoveryCandidate candidate)
    {
        if (_state.LocalIdentity?.DeviceId == candidate.DeviceId) return;

        var known = _state.TrustedPeers.FirstOrDefault(peer => peer.DeviceId == candidate.DeviceId);
        if (known is { TrustState: MeshPeerTrustState.Revoked })
        {
            _nearby.TryRemove(candidate.DeviceId, out _);
            return;
        }

        if (known is { TrustState: MeshPeerTrustState.Trusted })
        {
            _nearby.TryRemove(candidate.DeviceId, out _);

            // Discovery is unauthenticated. It may provide a reconnect hint only when the
            // advertised identity fingerprint matches the already-pinned trusted identity.
            // The endpoint is persisted only after the TLS/device-ID handshake succeeds.
            if (!string.Equals(known.PublicKeyFingerprint, candidate.PublicKeyFingerprint, StringComparison.OrdinalIgnoreCase))
                return;
            if (GetPresence(known).Connection == MeshConnectionState.Connected)
                return;
            if (_discoveryReconnectNotBefore.TryGetValue(candidate.DeviceId, out var notBefore) && notBefore > DateTimeOffset.UtcNow)
                return;
            if (_discoveryReconnects.TryAdd(candidate.DeviceId, 0))
                _ = ReconnectDiscoveredTrustedPeerAsync(candidate);
            return;
        }

        _nearby[candidate.DeviceId] = candidate;
        StateChanged?.Invoke();
    }

    private async Task ReconnectDiscoveredTrustedPeerAsync(MeshDiscoveryCandidate candidate)
    {
        try
        {
            MeshPeerRecord peer;
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                peer = _state.TrustedPeers.FirstOrDefault(item => item.DeviceId == candidate.DeviceId)
                    ?? throw new UnauthorizedAccessException("The discovered Mesh device is no longer known.");
                if (peer.TrustState != MeshPeerTrustState.Trusted)
                    throw new UnauthorizedAccessException("The discovered Mesh device is no longer trusted.");
                if (!string.Equals(peer.PublicKeyFingerprint, candidate.PublicKeyFingerprint, StringComparison.OrdinalIgnoreCase))
                    throw new System.Security.Authentication.AuthenticationException("The discovered Mesh identity fingerprint does not match the trusted peer.");
            }
            finally { _gate.Release(); }

            var now = DateTimeOffset.UtcNow;
            var current = GetPresence(peer);
            _presence[peer.DeviceId] = current with
            {
                Connection = MeshConnectionState.Reconnecting,
                Presence = MeshPresenceState.Stale,
                ObservedAt = now,
                Activity = "Verifying discovered endpoint"
            };
            StateChanged?.Invoke();

            var reconnectCandidate = peer with { LastKnownEndpoint = candidate.Endpoint };
            await _transport.ConnectAsync(reconnectCandidate, CancellationToken.None).ConfigureAwait(false);

            // ConnectAsync verifies both the pinned certificate fingerprint and device ID.
            // Only now may discovery refresh the durable endpoint used on future restarts.
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var verified = _state.TrustedPeers.FirstOrDefault(item => item.DeviceId == candidate.DeviceId);
                if (verified is { TrustState: MeshPeerTrustState.Trusted }
                    && string.Equals(verified.PublicKeyFingerprint, candidate.PublicKeyFingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    await ReplacePeerAsync(verified with { LastKnownEndpoint = candidate.Endpoint }, CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally { _gate.Release(); }
        }
        catch (Exception ex) when (ex is IOException
                                   or System.Net.Sockets.SocketException
                                   or System.Security.Authentication.AuthenticationException
                                   or UnauthorizedAccessException
                                   or InvalidDataException
                                   or InvalidOperationException)
        {
            var known = _state.TrustedPeers.FirstOrDefault(item => item.DeviceId == candidate.DeviceId);
            if (known is { TrustState: MeshPeerTrustState.Trusted })
            {
                var now = DateTimeOffset.UtcNow;
                var existing = GetPresence(known);
                _presence[candidate.DeviceId] = existing with
                {
                    Connection = MeshConnectionState.Failed,
                    Presence = MeshPresenceState.Offline,
                    ObservedAt = now,
                    Activity = "Discovered endpoint could not be verified"
                };
                StateChanged?.Invoke();
            }
        }
        finally
        {
            _discoveryReconnectNotBefore[candidate.DeviceId] = DateTimeOffset.UtcNow.Add(DiscoveryReconnectBackoff);
            _discoveryReconnects.TryRemove(candidate.DeviceId, out _);
        }
    }

    private async Task StopDiscoveryAsync()
    {
        if (_discovery is null) return;
        _discovery.CandidateObserved -= OnDiscoveryCandidateObserved;
        try { if (_discovery.IsRunning) await _discovery.StopAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
        await _discovery.DisposeAsync().ConfigureAwait(false);
    }
}
