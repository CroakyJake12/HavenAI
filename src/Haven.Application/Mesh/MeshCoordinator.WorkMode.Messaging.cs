using System.Text;
using Haven.Core;

namespace Haven.Application;

public sealed partial class MeshCoordinator
{
    public async Task<MeshWorkMessage> SendWorkMessageAsync(Guid workerId, string content, CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        var member = RequireWorkMember(workerId);
        return await InvokeWorkMemberAsync(member, content, MeshWorkChannelKind.Direct, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MeshWorkMessage>> PostSharedPoolAsync(string content, IReadOnlyCollection<Guid>? workerIds, CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("A shared-pool message is required.", nameof(content));
        var targets = WorkMembers().Where(member => member.IsEnabled && (workerIds is null || workerIds.Contains(member.WorkerId))).ToArray();
        if (targets.Length == 0) throw new InvalidOperationException("The shared pool has no enabled members.");

        var parent = new MeshWorkMessage(Guid.NewGuid(), MeshWorkChannelKind.SharedPool, MeshWorkMessageRole.User, content.Trim(), DateTimeOffset.UtcNow, Status: MeshWorkMessageStatus.Running);
        await RecordWorkMessageAsync(parent, cancellationToken).ConfigureAwait(false);
        var replies = new List<MeshWorkMessage>();
        foreach (var member in targets)
            replies.Add(await InvokeWorkMemberAsync(member, content, MeshWorkChannelKind.SharedPool, parent.MessageId, cancellationToken).ConfigureAwait(false));
        await ReplaceWorkMessageAsync(parent with
        {
            Status = replies.All(reply => reply.Status == MeshWorkMessageStatus.Succeeded) ? MeshWorkMessageStatus.Succeeded : MeshWorkMessageStatus.Failed
        }, cancellationToken).ConfigureAwait(false);
        return replies;
    }

    private async Task<MeshWorkMessage> InvokeWorkMemberAsync(
        MeshWorkMember member, string content, MeshWorkChannelKind channel, Guid? parentMessageId, CancellationToken cancellationToken)
    {
        if (!member.IsEnabled) throw new InvalidOperationException($"{member.Name} is disabled in Work Mode.");
        if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("A Work Mode message is required.", nameof(content));
        var dashboard = await GetDashboardAsync(cancellationToken).ConfigureAwait(false);
        var peer = dashboard.TrustedPeers.FirstOrDefault(item => item.Peer.DeviceId == member.DeviceId);
        if (peer is null || peer.Presence.Connection != MeshConnectionState.Connected)
        {
            var failed = new MeshWorkMessage(Guid.NewGuid(), channel, MeshWorkMessageRole.System,
                $"{member.Name} is offline; the message was not silently sent elsewhere.", DateTimeOffset.UtcNow,
                TargetWorkerId: member.WorkerId, ParentMessageId: parentMessageId, Status: MeshWorkMessageStatus.Failed, Error: "mesh-worker-offline");
            await RecordWorkMessageAsync(failed, cancellationToken).ConfigureAwait(false);
            return failed;
        }

        var request = new MeshWorkMessage(Guid.NewGuid(), channel, MeshWorkMessageRole.User, content.Trim(), DateTimeOffset.UtcNow,
            TargetWorkerId: member.WorkerId, ParentMessageId: parentMessageId, Status: MeshWorkMessageStatus.Running);
        await RecordWorkMessageAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            var prompt = BuildWorkerPrompt(member, channel, content.Trim(), request.MessageId);
            var response = member.RuntimeKind switch
            {
                MeshWorkRuntimeKind.Model => await InvokeWorkModelAsync(member, prompt, cancellationToken).ConfigureAwait(false),
                MeshWorkRuntimeKind.Agent => await ExecuteRemoteAgentAsync(member.DeviceId,
                    member.AgentId ?? throw new InvalidDataException($"{member.Name} has no assigned agent ID."), prompt, cancellationToken).ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(member.RuntimeKind))
            };
            await ReplaceWorkMessageAsync(request with { Status = MeshWorkMessageStatus.Succeeded }, cancellationToken).ConfigureAwait(false);
            var reply = new MeshWorkMessage(Guid.NewGuid(), channel, member.IsCoordinator ? MeshWorkMessageRole.Coordinator : MeshWorkMessageRole.Worker,
                response, DateTimeOffset.UtcNow, SenderWorkerId: member.WorkerId, ParentMessageId: request.MessageId, Status: MeshWorkMessageStatus.Succeeded);
            await RecordWorkMessageAsync(reply, cancellationToken).ConfigureAwait(false);
            return reply;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ReplaceWorkMessageAsync(request with { Status = MeshWorkMessageStatus.Failed, Error = "Cancelled" }, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await ReplaceWorkMessageAsync(request with { Status = MeshWorkMessageStatus.Failed, Error = ex.Message }, CancellationToken.None).ConfigureAwait(false);
            var failed = new MeshWorkMessage(Guid.NewGuid(), channel, MeshWorkMessageRole.System, $"{member.Name} could not answer: {ex.Message}",
                DateTimeOffset.UtcNow, TargetWorkerId: member.WorkerId, ParentMessageId: request.MessageId, Status: MeshWorkMessageStatus.Failed, Error: ex.Message);
            await RecordWorkMessageAsync(failed, CancellationToken.None).ConfigureAwait(false);
            return failed;
        }
    }

    private Task<string> InvokeWorkModelAsync(MeshWorkMember member, string prompt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(member.ProviderId) || string.IsNullOrWhiteSpace(member.ModelName))
            throw new InvalidDataException($"{member.Name} has an incomplete model assignment.");
        var route = MeshRemoteModelProvider.EncodeRoute(member.DeviceId, member.ProviderId, member.ModelName);
        var system = $"You are {member.Name}, a named AI team member in Haven Mesh Work Mode. Role: {member.Role ?? "general team member"}. Work with the shared team context provided in the prompt. Be explicit about what you actually completed and never claim access to capabilities you were not given.";
        return CompleteRemoteModelAsync(route, new OllamaChatRequest(route, [new OllamaMessage("user", prompt)], EffortLevel.Medium, system), cancellationToken);
    }

    private string BuildWorkerPrompt(MeshWorkMember member, MeshWorkChannelKind channel, string content, Guid currentRequestId)
    {
        // Coordinator orchestration prompts are self-contained control turns. Replaying prior control
        // instructions here can make a later synthesis obey an earlier planning-format request.
        if (channel == MeshWorkChannelKind.Coordinator) return content;
        IEnumerable<MeshWorkMessage> history = WorkMessages().Where(message => message.MessageId != currentRequestId && message.Status == MeshWorkMessageStatus.Succeeded);
        history = channel switch
        {
            MeshWorkChannelKind.SharedPool => history.Where(message => message.Channel == MeshWorkChannelKind.SharedPool),
            MeshWorkChannelKind.Direct => history.Where(message => message.Channel == MeshWorkChannelKind.Direct &&
                (message.SenderWorkerId == member.WorkerId || message.TargetWorkerId == member.WorkerId)),
            MeshWorkChannelKind.Coordinator => history.Where(message => message.Channel == MeshWorkChannelKind.Coordinator),
            _ => history
        };
        var recent = history.OrderByDescending(message => message.CreatedAt).Take(channel == MeshWorkChannelKind.SharedPool ? 24 : 12).Reverse().ToArray();
        if (recent.Length == 0) return content;
        var transcript = string.Join(Environment.NewLine, recent.Select(FormatWorkMessage));
        return $"Recent Haven Work Mode {channel} context:\n{transcript}\n\nNew message for {member.Name}:\n{content}";
    }

    private string FormatWorkMessage(MeshWorkMessage message)
    {
        var speaker = message.Role == MeshWorkMessageRole.User ? "You"
            : message.SenderWorkerId is { } workerId ? WorkMembers().FirstOrDefault(member => member.WorkerId == workerId)?.Name ?? message.Role.ToString()
            : message.Role.ToString();
        return $"{speaker}: {message.Content}";
    }
}
