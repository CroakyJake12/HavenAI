using Haven.Core;

namespace Haven.Application;

public sealed partial class MeshCoordinator
{
    public async Task<MeshWorkMember> ConfigureModelWorkerAsync(
        string name, Guid deviceId, string providerId, string modelName, string? runtimeDisplayName, string? role,
        IReadOnlyList<string>? specialties, bool isCoordinator, CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        _ = RequireTrustedPeer(deviceId);
        if (string.IsNullOrWhiteSpace(providerId) || string.Equals(providerId.Trim(), MeshRemoteModelProvider.MeshProviderId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A non-Mesh target model provider is required.", nameof(providerId));
        if (string.IsNullOrWhiteSpace(modelName)) throw new ArgumentException("A target model name is required.", nameof(modelName));
        return await UpsertWorkMemberAsync(name, deviceId, MeshWorkRuntimeKind.Model, providerId.Trim(), modelName.Trim(), null,
            runtimeDisplayName ?? modelName.Trim(), role, specialties, isCoordinator, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MeshWorkMember> ConfigureAgentWorkerAsync(
        string name, Guid deviceId, Guid agentId, string runtimeDisplayName, string? role, IReadOnlyList<string>? specialties,
        bool isCoordinator, CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        _ = RequireTrustedPeer(deviceId);
        if (agentId == Guid.Empty) throw new ArgumentException("A target agent ID is required.", nameof(agentId));
        return await UpsertWorkMemberAsync(name, deviceId, MeshWorkRuntimeKind.Agent, null, null, agentId, runtimeDisplayName,
            role, specialties, isCoordinator, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetWorkCoordinatorAsync(Guid workerId, CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        if (!WorkMembers().Any(member => member.WorkerId == workerId && member.IsEnabled))
            throw new KeyNotFoundException("The selected Work Mode member is missing or disabled.");
        var now = DateTimeOffset.UtcNow;
        await UpdateWorkStateAsync(state => state with
        {
            WorkMembers = WorkMembers(state).Select(member => member with { IsCoordinator = member.WorkerId == workerId, UpdatedAt = now }).ToArray()
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveWorkMemberAsync(Guid workerId, CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        await UpdateWorkStateAsync(state => state with
        {
            WorkMembers = WorkMembers(state).Where(member => member.WorkerId != workerId).ToArray()
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MeshWorkModeSnapshot> GetWorkModeAsync(CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        var dashboard = await GetDashboardAsync(cancellationToken).ConfigureAwait(false);
        var peerPresence = dashboard.TrustedPeers.ToDictionary(peer => peer.Peer.DeviceId);
        var messages = WorkMessages();
        var work = WorkItems();
        var members = WorkMembers().Where(member => member.IsEnabled)
            .OrderByDescending(member => member.IsCoordinator)
            .ThenBy(member => member.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var statuses = members.Select(member =>
        {
            var peer = peerPresence.TryGetValue(member.DeviceId, out var snapshot) ? snapshot : null;
            var presence = peer?.Presence.Presence ?? MeshPresenceState.Offline;
            var connection = peer?.Presence.Connection ?? MeshConnectionState.Disconnected;
            var active = work.Where(item => item.AssignedWorkerId == member.WorkerId &&
                    item.Status is MeshWorkItemStatus.Planned or MeshWorkItemStatus.Delegated or MeshWorkItemStatus.Running or MeshWorkItemStatus.AwaitingReview)
                .OrderByDescending(item => item.UpdatedAt).ToArray();
            var lastMessage = messages.Where(message => message.SenderWorkerId == member.WorkerId || message.TargetWorkerId == member.WorkerId)
                .OrderByDescending(message => message.CreatedAt).FirstOrDefault()?.CreatedAt;
            var summary = active.Length > 0
                ? $"{member.Name} is {presence.ToString().ToLowerInvariant()} and working on {active[0].Goal}."
                : $"{member.Name} is {presence.ToString().ToLowerInvariant()} with no active Work Mode task.";
            return new MeshWorkMemberStatus(member, presence, connection, peer?.Presence.LastSeenAt, active, lastMessage, summary);
        }).ToArray();
        return new(statuses, members.FirstOrDefault(member => member.IsCoordinator),
            messages.OrderByDescending(message => message.CreatedAt).Take(200).Reverse().ToArray(),
            work.OrderByDescending(item => item.UpdatedAt).Take(100).ToArray());
    }

    public async Task<MeshWorkMemberStatus> CheckUpAsync(string workerName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workerName)) throw new ArgumentException("A worker name is required.", nameof(workerName));
        var snapshot = await GetWorkModeAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Members.FirstOrDefault(member => member.Member.Name.Equals(workerName.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"No Work Mode member named '{workerName.Trim()}' exists.");
    }

    private async Task<MeshWorkMember> UpsertWorkMemberAsync(
        string name, Guid deviceId, MeshWorkRuntimeKind kind, string? providerId, string? modelName, Guid? agentId, string runtimeDisplayName,
        string? role, IReadOnlyList<string>? specialties, bool isCoordinator, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A friendly Work Mode name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(runtimeDisplayName)) throw new ArgumentException("A runtime display name is required.", nameof(runtimeDisplayName));
        var trimmed = name.Trim();
        var existing = WorkMembers().FirstOrDefault(member => member.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        var now = DateTimeOffset.UtcNow;
        var worker = new MeshWorkMember(existing?.WorkerId ?? Guid.NewGuid(), trimmed, deviceId, kind, providerId, modelName, agentId, runtimeDisplayName.Trim(),
            string.IsNullOrWhiteSpace(role) ? null : role.Trim(), NormalizeSpecialties(specialties), isCoordinator, true, existing?.CreatedAt ?? now, now);
        await UpdateWorkStateAsync(state =>
        {
            var members = WorkMembers(state).Where(member => member.WorkerId != worker.WorkerId)
                .Select(member => isCoordinator ? member with { IsCoordinator = false } : member)
                .Append(worker).TakeLast(64).ToArray();
            return state with { WorkMembers = members };
        }, cancellationToken).ConfigureAwait(false);
        return worker;
    }

    private MeshWorkMember ResolveWorkMember(string name) => WorkMembers().FirstOrDefault(member => member.IsEnabled && member.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"No enabled Work Mode member named '{name.Trim()}' exists.");

    private MeshWorkMember ResolveWorkMember(Guid workerId) => WorkMembers().FirstOrDefault(member => member.WorkerId == workerId)
        ?? throw new KeyNotFoundException("The Work Mode member does not exist.");

    private MeshWorkMember RequireWorkMember(Guid workerId)
    {
        var member = ResolveWorkMember(workerId);
        if (!member.IsEnabled) throw new InvalidOperationException($"{member.Name} is disabled in Work Mode.");
        _ = RequireTrustedPeer(member.DeviceId);
        return member;
    }

    private IReadOnlyList<MeshWorkMember> WorkMembers() => WorkMembers(_state);
    private static IReadOnlyList<MeshWorkMember> WorkMembers(MeshPersistentState state) => state.WorkMembers ?? [];
    private IReadOnlyList<MeshWorkMessage> WorkMessages() => _state.WorkMessages ?? [];
    private IReadOnlyList<MeshWorkItem> WorkItems() => _state.WorkItems ?? [];

    private async Task RecordWorkMessageAsync(MeshWorkMessage message, CancellationToken cancellationToken) =>
        await UpdateWorkStateAsync(state => state with { WorkMessages = (state.WorkMessages ?? []).Append(message).OrderBy(item => item.CreatedAt).TakeLast(2048).ToArray() }, cancellationToken).ConfigureAwait(false);

    private async Task ReplaceWorkMessageAsync(MeshWorkMessage message, CancellationToken cancellationToken) =>
        await UpdateWorkStateAsync(state => state with { WorkMessages = (state.WorkMessages ?? []).Where(item => item.MessageId != message.MessageId).Append(message).OrderBy(item => item.CreatedAt).TakeLast(2048).ToArray() }, cancellationToken).ConfigureAwait(false);

    private async Task RecordWorkItemAsync(MeshWorkItem item, CancellationToken cancellationToken) =>
        await UpdateWorkStateAsync(state => state with { WorkItems = (state.WorkItems ?? []).Append(item).OrderBy(entry => entry.CreatedAt).TakeLast(512).ToArray() }, cancellationToken).ConfigureAwait(false);

    private async Task ReplaceWorkItemAsync(MeshWorkItem item, CancellationToken cancellationToken) =>
        await UpdateWorkStateAsync(state => state with { WorkItems = (state.WorkItems ?? []).Where(entry => entry.WorkItemId != item.WorkItemId).Append(item).OrderBy(entry => entry.CreatedAt).TakeLast(512).ToArray() }, cancellationToken).ConfigureAwait(false);

    private async Task UpdateWorkStateAsync(Func<MeshPersistentState, MeshPersistentState> update, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _state = update(_state) with { Version = MeshPersistentState.CurrentVersion };
            await _stateStore.SaveAsync(_state, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
        StateChanged?.Invoke();
    }

    private static IReadOnlyList<string> NormalizeSpecialties(IReadOnlyList<string>? specialties) =>
        (specialties ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToArray();
}
