using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public sealed partial class MeshCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PresenceStaleAfter = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PresenceOfflineAfter = TimeSpan.FromMinutes(2);
    private readonly IMeshStateStore _stateStore;
    private readonly IMeshIdentitySecretStore _identitySecrets;
    private readonly IMeshTransport _transport;
    private readonly IMeshCapabilitySource _capabilities;
    private readonly IMeshResourceMergeService _merge;
    private readonly MeshSyncEngine _sync;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, MeshPresenceSnapshot> _presence = new();
    private readonly ConcurrentDictionary<Guid, MeshPairingChallenge> _challenges = new();
    private MeshPersistentState _state = MeshPersistentState.Empty;
    private bool _initialised;

    public MeshCoordinator(
        IMeshStateStore stateStore,
        IMeshIdentitySecretStore identitySecrets,
        IMeshTransport transport,
        IMeshCapabilitySource capabilities,
        IMeshResourceMergeService merge)
    {
        _stateStore = stateStore;
        _identitySecrets = identitySecrets;
        _transport = transport;
        _capabilities = capabilities;
        _merge = merge;
        _sync = new MeshSyncEngine();
        _transport.PeerObserved += OnPeerObserved;
        _transport.PairingCompleted += OnPairingCompleted;
        _transport.ConnectionChanged += OnConnectionChanged;
        _transport.MessageReceived += OnMessageReceived;
    }

    public event Action? StateChanged;

    public async Task InitialiseAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialised) return;
            _state = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (_state.Version > MeshPersistentState.CurrentVersion)
                throw new InvalidDataException($"Mesh state version {_state.Version} is newer than this Haven build supports.");
            if (_state.Version < MeshPersistentState.CurrentVersion || _state.WorkMembers is null || _state.WorkMessages is null || _state.WorkItems is null)
            {
                _state = _state with
                {
                    Version = MeshPersistentState.CurrentVersion,
                    WorkMembers = _state.WorkMembers ?? [],
                    WorkMessages = _state.WorkMessages ?? [],
                    WorkItems = _state.WorkItems ?? []
                };
                await _stateStore.SaveAsync(_state, cancellationToken).ConfigureAwait(false);
            }

            if (_state.LocalIdentity is null)
            {
                var deviceId = Guid.NewGuid();
                using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                var key = Convert.ToBase64String(signingKey.ExportPkcs8PrivateKey());
                var fingerprint = Convert.ToHexString(SHA256.HashData(signingKey.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
                var platform = OperatingSystem.IsWindows() ? CapabilityPlatform.Windows : CapabilityPlatform.Android;
                var deviceClass = OperatingSystem.IsWindows() ? MeshDeviceClass.Desktop : MeshDeviceClass.Phone;
                _state = _state with
                {
                    Version = MeshPersistentState.CurrentVersion,
                    LocalIdentity = new MeshLocalIdentity(deviceId, Environment.MachineName, deviceClass, platform, fingerprint, DateTimeOffset.UtcNow)
                };
                await _identitySecrets.SetPrivateKeyAsync(deviceId, key, cancellationToken).ConfigureAwait(false);
                await _stateStore.SaveAsync(_state, cancellationToken).ConfigureAwait(false);
            }

            var privateKey = await _identitySecrets.GetPrivateKeyAsync(_state.LocalIdentity.DeviceId, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(privateKey))
                throw new InvalidDataException("Mesh identity metadata exists, but its private identity secret is missing. Re-pair this Haven installation rather than silently replacing its identity.");

            await _transport.StartAsync(_state.LocalIdentity, privateKey, cancellationToken).ConfigureAwait(false);
            await _transport.SetTrustedPeersAsync(_state.TrustedPeers, cancellationToken).ConfigureAwait(false);
            await StartDiscoveryAsync(cancellationToken).ConfigureAwait(false);
            _initialised = true;
        }
        finally
        {
            _gate.Release();
        }
        StateChanged?.Invoke();
    }

    public async Task<MeshPairingOffer> CreatePairingOfferAsync(CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(_transport.LocalEndpoint)) throw new InvalidOperationException("Mesh has no reachable local endpoint to advertise for pairing.");
        var bytes = RandomNumberGenerator.GetBytes(4);
        var code = (BitConverter.ToUInt32(bytes) % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        var now = DateTimeOffset.UtcNow;
        var challenge = new MeshPairingChallenge(Guid.NewGuid(), code, now, now.Add(ChallengeLifetime), _state.LocalIdentity!.PublicKeyFingerprint);
        _challenges[challenge.Id] = challenge;
        await _transport.SetPairingChallengeAsync(challenge, cancellationToken).ConfigureAwait(false);
        return new MeshPairingOffer(challenge.Id, _transport.LocalEndpoint!, code, challenge.LocalFingerprint, challenge.ExpiresAt);
    }

    public async Task<MeshPairingResult> PairAsync(MeshPairingOffer offer, CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(offer);
        if (DateTimeOffset.UtcNow >= offer.ExpiresAt)
            return new(false, "The remote pairing offer expired. Ask the other device for a new offer.");
        if (string.IsNullOrWhiteSpace(offer.Endpoint) || string.IsNullOrWhiteSpace(offer.VerificationCode) || string.IsNullOrWhiteSpace(offer.DeviceFingerprint))
            return new(false, "The remote pairing offer is incomplete.");

        MeshTransportPeer observed;
        try
        {
            observed = await _transport.PairAsync(offer.Endpoint.Trim(), offer.ChallengeId, offer.VerificationCode.Trim(), offer.DeviceFingerprint.Trim(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Net.Sockets.SocketException or System.Security.Authentication.AuthenticationException)
        {
            return new(false, $"Pairing failed: {ex.Message}");
        }

        if (observed.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(observed.PublicKeyFingerprint))
            return new(false, "The remote peer did not present a valid stable identity.");
        if (observed.DeviceId == _state.LocalIdentity!.DeviceId)
            return new(false, "Haven cannot pair a device with itself.");

        var existing = _state.TrustedPeers.FirstOrDefault(peer => peer.DeviceId == observed.DeviceId);
        if (existing is { TrustState: MeshPeerTrustState.Revoked })
            return new(false, "This device was revoked. Remove the revoked identity explicitly before pairing it again.");
        if (existing is not null && !string.Equals(existing.PublicKeyFingerprint, observed.PublicKeyFingerprint, StringComparison.OrdinalIgnoreCase))
            return new(false, "The device ID matches a known peer but its identity fingerprint changed. Haven refused the pairing to prevent silent identity replacement.");

        var now = DateTimeOffset.UtcNow;
        var peer = new MeshPeerRecord(
            observed.DeviceId, observed.DisplayName, observed.DeviceClass, observed.Platform, observed.PublicKeyFingerprint,
            MeshPeerTrustState.Trusted, existing?.FirstSeenAt ?? now, now, null, observed.Endpoint);
        await ReplacePeerAsync(peer, cancellationToken).ConfigureAwait(false);
        UpdatePresence(observed, MeshPresenceState.Available, MeshConnectionState.Connected, now);
        StateChanged?.Invoke();
        return new(true, $"Trusted {peer.DisplayName}. Future connections must present the same device identity fingerprint.", peer);
    }

    public async Task RevokeAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        var peer = RequireKnownPeer(deviceId);
        var revoked = peer with { TrustState = MeshPeerTrustState.Revoked, RevokedAt = DateTimeOffset.UtcNow };
        await ReplacePeerAsync(revoked, cancellationToken).ConfigureAwait(false);
        await _transport.DisconnectAsync(deviceId, cancellationToken).ConfigureAwait(false);
        _presence[deviceId] = OfflinePresence(revoked, DateTimeOffset.UtcNow);
        StateChanged?.Invoke();
    }

    public async Task ConnectAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        var peer = RequireTrustedPeer(deviceId);
        await _transport.ConnectAsync(peer, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MeshTaskReceipt> DelegateTaskAsync(
        Guid targetDeviceId,
        string instruction,
        IReadOnlyList<string> requiredCapabilities,
        IReadOnlyList<MeshTaskReference>? references,
        HavenSurface sourceSurface,
        CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        var peer = RequireTrustedPeer(targetDeviceId);
        if (string.IsNullOrWhiteSpace(instruction)) throw new ArgumentException("A task instruction is required.", nameof(instruction));

        var snapshot = GetPresence(peer);
        if (snapshot.Connection != MeshConnectionState.Connected || snapshot.Presence is MeshPresenceState.Offline or MeshPresenceState.Stale)
            return await RecordTaskAsync(new MeshTaskReceipt(Guid.NewGuid(), MeshTaskStatus.Queued, DateTimeOffset.UtcNow, $"{peer.DisplayName} is offline. The task remains queued and will not execute on another device automatically.", FailureCode: "mesh-peer-offline"), cancellationToken).ConfigureAwait(false);

        var advertised = snapshot.Capabilities.Select(item => item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = requiredCapabilities.Where(required => !advertised.Contains(required)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (missing.Length > 0)
            return await RecordTaskAsync(new MeshTaskReceipt(Guid.NewGuid(), MeshTaskStatus.Failed, DateTimeOffset.UtcNow, $"{peer.DisplayName} does not currently advertise the required capability: {string.Join(", ", missing)}.", FailureCode: "mesh-capability-mismatch"), cancellationToken).ConfigureAwait(false);

        var taskId = Guid.NewGuid();
        var envelope = new MeshTaskEnvelope(
            taskId, _state.LocalIdentity!.DeviceId, targetDeviceId, instruction.Trim(), requiredCapabilities, references ?? [],
            $"mesh-task:{_state.LocalIdentity.DeviceId:N}:{taskId:N}", DateTimeOffset.UtcNow, sourceSurface);
        var receipt = await RecordTaskAsync(new MeshTaskReceipt(taskId, MeshTaskStatus.Sending, DateTimeOffset.UtcNow, $"Sending to {peer.DisplayName}."), cancellationToken).ConfigureAwait(false);
        try
        {
            await _transport.SendAsync(targetDeviceId, "task.request", JsonSerializer.Serialize(envelope), cancellationToken).ConfigureAwait(false);
            receipt = receipt with { Status = MeshTaskStatus.Sending, UpdatedAt = DateTimeOffset.UtcNow, Message = $"Task sent to {peer.DisplayName}; waiting for the target device to accept or reject it." };
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException)
        {
            receipt = receipt with { Status = MeshTaskStatus.Queued, UpdatedAt = DateTimeOffset.UtcNow, Message = $"Connection to {peer.DisplayName} was lost. The task remains queued for explicit retry.", FailureCode = "mesh-connection-lost" };
        }
        return await RecordTaskAsync(receipt, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MeshTaskReceipt> CancelTaskAsync(Guid taskId, CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        var current = _state.RemoteTasks.FirstOrDefault(task => task.TaskId == taskId)
            ?? throw new KeyNotFoundException("The Mesh task does not exist.");
        if (current.Status is MeshTaskStatus.Succeeded or MeshTaskStatus.Failed or MeshTaskStatus.Cancelled) return current;
        var receipt = current with { Status = MeshTaskStatus.CancelRequested, UpdatedAt = DateTimeOffset.UtcNow, Message = "Cancellation requested." };
        await RecordTaskAsync(receipt, cancellationToken).ConfigureAwait(false);
        await BroadcastTaskControlAsync(taskId, "task.cancel", cancellationToken).ConfigureAwait(false);
        return receipt;
    }

    public async Task<MeshHandoffReceipt> HandoffAsync(MeshHandoffEnvelope handoff, CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        _ = RequireTrustedPeer(handoff.TargetDeviceId);
        var presence = GetPresence(RequireTrustedPeer(handoff.TargetDeviceId));
        if (presence.Connection != MeshConnectionState.Connected)
            return new(handoff.HandoffId, MeshHandoffStatus.Failed, DateTimeOffset.UtcNow, "The target device is offline; Haven kept the current activity on this device.");
        await _transport.SendAsync(handoff.TargetDeviceId, "handoff.request", JsonSerializer.Serialize(handoff), cancellationToken).ConfigureAwait(false);
        return new(handoff.HandoffId, MeshHandoffStatus.Sent, DateTimeOffset.UtcNow, "Handoff sent with its resource, task, references and source surface context.");
    }

    public async Task<MeshSyncDecision> ApplySyncAsync(MeshSyncMutation mutation, CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        _ = RequireTrustedPeer(mutation.OriginDeviceId);
        var current = await _merge.GetCurrentAsync(mutation.ResourceType, mutation.ResourceId, cancellationToken).ConfigureAwait(false);
        var applied = _state.AppliedSyncOperations.ToHashSet();
        var decision = _sync.Evaluate(current, mutation, applied);
        if (decision.Kind == MeshSyncDecisionKind.Apply)
        {
            if (!await _merge.TryApplyAsync(mutation, cancellationToken).ConfigureAwait(false))
                decision = decision with { Kind = MeshSyncDecisionKind.Conflict, Reason = "The feature-specific merge service could not safely apply this change." };
            else
            {
                _state = _state with { AppliedSyncOperations = _state.AppliedSyncOperations.Append(mutation.OperationId).Distinct().TakeLast(4096).ToArray() };
                await _stateStore.SaveAsync(_state, cancellationToken).ConfigureAwait(false);
            }
        }
        if (decision.Kind == MeshSyncDecisionKind.Conflict && current is not null)
        {
            var conflict = new MeshConflict(Guid.NewGuid(), mutation.ResourceId, mutation.ResourceType, current, mutation, DateTimeOffset.UtcNow, decision.Reason);
            _state = _state with { Conflicts = _state.Conflicts.Append(conflict).TakeLast(256).ToArray() };
            await _stateStore.SaveAsync(_state, cancellationToken).ConfigureAwait(false);
        }
        StateChanged?.Invoke();
        return decision;
    }

    public async Task<MeshDashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var peers = _state.TrustedPeers
            .Where(peer => peer.TrustState == MeshPeerTrustState.Trusted)
            .Select(peer => new MeshPeerSnapshot(peer, ClassifyPresence(GetPresence(peer), now)))
            .OrderByDescending(item => item.Presence.Presence == MeshPresenceState.Available)
            .ThenBy(item => item.Peer.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        return new(_state.LocalIdentity!, peers, _state.RemoteTasks.OrderByDescending(task => task.UpdatedAt).Take(100).ToArray(), _state.Conflicts.OrderByDescending(conflict => conflict.DetectedAt).Take(100).ToArray(), _transport.IsRunning, GetNearbyCandidates());
    }

    private async Task EnsureInitialisedAsync(CancellationToken cancellationToken)
    {
        if (!_initialised) await InitialiseAsync(cancellationToken).ConfigureAwait(false);
    }

    private MeshPeerRecord RequireKnownPeer(Guid deviceId) => _state.TrustedPeers.FirstOrDefault(peer => peer.DeviceId == deviceId)
        ?? throw new UnauthorizedAccessException("The remote device is unknown to this Haven installation.");

    private MeshPeerRecord RequireTrustedPeer(Guid deviceId)
    {
        var peer = RequireKnownPeer(deviceId);
        if (peer.TrustState != MeshPeerTrustState.Trusted) throw new UnauthorizedAccessException("The remote device is not trusted.");
        return peer;
    }

    private MeshPresenceSnapshot GetPresence(MeshPeerRecord peer) => _presence.TryGetValue(peer.DeviceId, out var current)
        ? current
        : OfflinePresence(peer, DateTimeOffset.UtcNow);

    private static MeshPresenceSnapshot OfflinePresence(MeshPeerRecord peer, DateTimeOffset now) => new(peer.DeviceId, MeshPresenceState.Offline, MeshConnectionState.Disconnected, now, null, []);

    private static MeshPresenceSnapshot ClassifyPresence(MeshPresenceSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.Connection == MeshConnectionState.Connected)
        {
            var age = now - snapshot.ObservedAt;
            if (age >= PresenceOfflineAfter) return snapshot with { Presence = MeshPresenceState.Offline, Connection = MeshConnectionState.Disconnected };
            if (age >= PresenceStaleAfter) return snapshot with { Presence = MeshPresenceState.Stale };
        }
        return snapshot;
    }

    private async Task ReplacePeerAsync(MeshPeerRecord peer, CancellationToken cancellationToken)
    {
        var peers = _state.TrustedPeers.Where(item => item.DeviceId != peer.DeviceId).Append(peer).ToArray();
        _state = _state with { TrustedPeers = peers };
        await _stateStore.SaveAsync(_state, cancellationToken).ConfigureAwait(false);
        await _transport.SetTrustedPeersAsync(_state.TrustedPeers, cancellationToken).ConfigureAwait(false);
    }

    private async Task<MeshTaskReceipt> RecordTaskAsync(MeshTaskReceipt receipt, CancellationToken cancellationToken)
    {
        var tasks = _state.RemoteTasks.Where(task => task.TaskId != receipt.TaskId).Append(receipt).OrderByDescending(task => task.UpdatedAt).Take(512).ToArray();
        _state = _state with { RemoteTasks = tasks };
        await _stateStore.SaveAsync(_state, cancellationToken).ConfigureAwait(false);
        StateChanged?.Invoke();
        return receipt;
    }

    private async Task BroadcastTaskControlAsync(Guid taskId, string kind, CancellationToken cancellationToken)
    {
        foreach (var peer in _state.TrustedPeers.Where(peer => peer.TrustState == MeshPeerTrustState.Trusted))
        {
            if (GetPresence(peer).Connection != MeshConnectionState.Connected) continue;
            try { await _transport.SendAsync(peer.DeviceId, kind, JsonSerializer.Serialize(new { taskId }), cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException) { }
        }
    }

    private void OnPeerObserved(MeshTransportPeer peer)
    {
        var known = _state.TrustedPeers.FirstOrDefault(item => item.DeviceId == peer.DeviceId);
        if (known is null || known.TrustState != MeshPeerTrustState.Trusted) return;
        if (!string.Equals(known.PublicKeyFingerprint, peer.PublicKeyFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            _presence[peer.DeviceId] = new(peer.DeviceId, MeshPresenceState.Offline, MeshConnectionState.Failed, DateTimeOffset.UtcNow, null, [], Activity: "Identity fingerprint mismatch");
            StateChanged?.Invoke();
            return;
        }
        UpdatePresence(peer, MeshPresenceState.Available, MeshConnectionState.Connected, DateTimeOffset.UtcNow);
        StateChanged?.Invoke();
    }

    private void OnPairingCompleted(MeshTransportPeer peer)
    {
        _ = AcceptInboundPairingAsync(peer);
    }

    private async Task AcceptInboundPairingAsync(MeshTransportPeer observed)
    {
        if (!_initialised || observed.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(observed.PublicKeyFingerprint)) return;
        var existing = _state.TrustedPeers.FirstOrDefault(peer => peer.DeviceId == observed.DeviceId);
        if (existing is { TrustState: MeshPeerTrustState.Revoked }) return;
        if (existing is not null && !string.Equals(existing.PublicKeyFingerprint, observed.PublicKeyFingerprint, StringComparison.OrdinalIgnoreCase)) return;
        var now = DateTimeOffset.UtcNow;
        var peer = new MeshPeerRecord(observed.DeviceId, observed.DisplayName, observed.DeviceClass, observed.Platform, observed.PublicKeyFingerprint, MeshPeerTrustState.Trusted, existing?.FirstSeenAt ?? now, now, null, observed.Endpoint);
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try { await ReplacePeerAsync(peer, CancellationToken.None).ConfigureAwait(false); }
            finally { _gate.Release(); }
            UpdatePresence(observed, MeshPresenceState.Available, MeshConnectionState.Connected, now);
            StateChanged?.Invoke();
        }
        catch (Exception)
        {
            try { await _transport.DisconnectAsync(observed.DeviceId, CancellationToken.None).ConfigureAwait(false); } catch { }
        }
    }

    private void OnConnectionChanged(Guid deviceId, MeshConnectionState connection)
    {
        var known = _state.TrustedPeers.FirstOrDefault(item => item.DeviceId == deviceId);
        if (known is null || known.TrustState != MeshPeerTrustState.Trusted) return;
        var now = DateTimeOffset.UtcNow;
        var existing = GetPresence(known);
        _presence[deviceId] = existing with
        {
            Connection = connection,
            Presence = connection == MeshConnectionState.Connected ? MeshPresenceState.Available : connection == MeshConnectionState.Reconnecting ? MeshPresenceState.Stale : MeshPresenceState.Offline,
            ObservedAt = now,
            LastSeenAt = connection == MeshConnectionState.Connected ? now : existing.LastSeenAt
        };
        StateChanged?.Invoke();
    }

    private void OnMessageReceived(MeshTransportMessage message)
    {
        var peer = _state.TrustedPeers.FirstOrDefault(item => item.DeviceId == message.SourceDeviceId);
        if (peer is null || peer.TrustState != MeshPeerTrustState.Trusted) return;
        if (TryHandleDeviceMessage(message)) return;
        if (TryHandleRuntimeMessage(message)) return;
        if (TryHandleTaskMessage(message)) return;
        if (message.Kind == "presence")
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<MeshPresenceSnapshot>(message.Payload);
                if (snapshot is not null && snapshot.DeviceId == message.SourceDeviceId)
                    _presence[message.SourceDeviceId] = snapshot with { ObservedAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow };
            }
            catch (JsonException) { }
        }
        StateChanged?.Invoke();
    }

    private void UpdatePresence(MeshTransportPeer peer, MeshPresenceState presence, MeshConnectionState connection, DateTimeOffset now)
    {
        _presence[peer.DeviceId] = new(peer.DeviceId, presence, connection, now, now, peer.Capabilities);
    }

    public async ValueTask DisposeAsync()
    {
        _transport.PeerObserved -= OnPeerObserved;
        _transport.PairingCompleted -= OnPairingCompleted;
        _transport.ConnectionChanged -= OnConnectionChanged;
        _transport.MessageReceived -= OnMessageReceived;
        await StopDiscoveryAsync().ConfigureAwait(false);
        if (_transport.IsRunning) await _transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
        await _transport.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
