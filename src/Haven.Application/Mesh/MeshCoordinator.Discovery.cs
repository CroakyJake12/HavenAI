using System.Collections.Concurrent;
using Haven.Core;

namespace Haven.Application;

public sealed partial class MeshCoordinator
{
    private static readonly TimeSpan DiscoveryCandidateLifetime = TimeSpan.FromSeconds(20);
    private readonly ConcurrentDictionary<Guid, MeshDiscoveryCandidate> _nearby = new();
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
        _nearby[candidate.DeviceId] = candidate;
        StateChanged?.Invoke();
    }

    private async Task StopDiscoveryAsync()
    {
        if (_discovery is null) return;
        _discovery.CandidateObserved -= OnDiscoveryCandidateObserved;
        try { if (_discovery.IsRunning) await _discovery.StopAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
        await _discovery.DisposeAsync().ConfigureAwait(false);
    }
}
