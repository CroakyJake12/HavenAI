using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public sealed partial class MeshCoordinator
{
    private static readonly JsonSerializerOptions WorkJson = new(JsonSerializerDefaults.Web);

    public async Task<MeshWorkRunResult> CoordinateWorkAsync(string goal, CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(goal)) throw new ArgumentException("A Work Mode goal is required.", nameof(goal));
        var snapshot = await GetWorkModeAsync(cancellationToken).ConfigureAwait(false);
        var available = snapshot.Members.Where(member => member.Member.IsEnabled && member.Connection == MeshConnectionState.Connected).ToArray();
        if (available.Length == 0) throw new InvalidOperationException("No enabled Work Mode member is currently connected.");

        var plan = await BuildWorkPlanAsync(goal.Trim(), snapshot, available, cancellationToken).ConfigureAwait(false);
        var executed = new List<MeshWorkItem>();
        foreach (var assignment in plan.Assignments)
        {
            var worker = ResolveWorkMember(assignment.WorkerName);
            var reviewer = string.IsNullOrWhiteSpace(assignment.ReviewerName) ? null : ResolveWorkMember(assignment.ReviewerName!);
            var now = DateTimeOffset.UtcNow;
            var item = new MeshWorkItem(Guid.NewGuid(), assignment.Task, worker.WorkerId, reviewer?.WorkerId, MeshWorkItemStatus.Planned, now, now);
            await RecordWorkItemAsync(item, cancellationToken).ConfigureAwait(false);
            item = item with { Status = MeshWorkItemStatus.Running, UpdatedAt = DateTimeOffset.UtcNow };
            await ReplaceWorkItemAsync(item, cancellationToken).ConfigureAwait(false);

            var result = await InvokeWorkMemberAsync(worker, assignment.Task, MeshWorkChannelKind.Coordinator, null, cancellationToken).ConfigureAwait(false);
            if (result.Status != MeshWorkMessageStatus.Succeeded)
            {
                item = item with { Status = MeshWorkItemStatus.Failed, UpdatedAt = DateTimeOffset.UtcNow, Error = result.Error ?? result.Content };
                await ReplaceWorkItemAsync(item, cancellationToken).ConfigureAwait(false);
                executed.Add(item);
                continue;
            }

            item = item with
            {
                Result = result.Content,
                Status = reviewer is null ? MeshWorkItemStatus.Succeeded : MeshWorkItemStatus.AwaitingReview,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await ReplaceWorkItemAsync(item, cancellationToken).ConfigureAwait(false);

            if (reviewer is not null)
            {
                var reviewPrompt = $"Review {worker.Name}'s result for this delegated task. Check correctness, omissions and whether the task is actually complete. Return concise actionable review feedback.\n\nTask: {assignment.Task}\n\nResult from {worker.Name}:\n{result.Content}";
                var review = await InvokeWorkMemberAsync(reviewer, reviewPrompt, MeshWorkChannelKind.Coordinator, result.MessageId, cancellationToken).ConfigureAwait(false);
                item = review.Status == MeshWorkMessageStatus.Succeeded
                    ? item with { Review = review.Content, Status = MeshWorkItemStatus.Succeeded, UpdatedAt = DateTimeOffset.UtcNow }
                    : item with { Status = MeshWorkItemStatus.Failed, Error = review.Error ?? review.Content, UpdatedAt = DateTimeOffset.UtcNow };
                await ReplaceWorkItemAsync(item, cancellationToken).ConfigureAwait(false);
            }
            executed.Add(item);
        }

        var finalSummary = await BuildCoordinatorReviewAsync(goal.Trim(), snapshot.Coordinator, executed, cancellationToken).ConfigureAwait(false);
        return new(finalSummary, plan, executed);
    }

    private async Task<MeshWorkPlan> BuildWorkPlanAsync(
        string goal, MeshWorkModeSnapshot snapshot, IReadOnlyList<MeshWorkMemberStatus> available, CancellationToken cancellationToken)
    {
        var coordinatorStatus = snapshot.Coordinator is null
            ? null
            : available.FirstOrDefault(status => status.Member.WorkerId == snapshot.Coordinator.WorkerId);
        if (coordinatorStatus is not null)
        {
            var roster = string.Join(Environment.NewLine, available
                .Where(item => item.Member.WorkerId != coordinatorStatus.Member.WorkerId)
                .Select(item => $"- {item.Member.Name}: role={item.Member.Role ?? "general"}; specialties={string.Join(", ", item.Member.Specialties)}; device={item.Member.DeviceId:N}; status={item.Presence}; activeWork={item.ActiveWork.Count}"));
            if (string.IsNullOrWhiteSpace(roster))
                roster = $"- {coordinatorStatus.Member.Name}: role={coordinatorStatus.Member.Role ?? "general"}; specialties={string.Join(", ", coordinatorStatus.Member.Specialties)}; status={coordinatorStatus.Presence}";
            var prompt = $"Act as the coordinator for this Haven Mesh AI team. Plan the goal across available named workers. Return ONLY JSON in this exact shape: {{\"summary\":\"short plan\",\"assignments\":[{{\"workerName\":\"Mike\",\"task\":\"concrete task\",\"reviewerName\":null}}]}}. Use only names in the roster. Keep assignments independent where possible and assign a reviewer only when useful.\n\nGoal: {goal}\n\nRoster:\n{roster}";
            var reply = await InvokeWorkMemberAsync(coordinatorStatus.Member, prompt, MeshWorkChannelKind.Coordinator, null, cancellationToken).ConfigureAwait(false);
            if (reply.Status == MeshWorkMessageStatus.Succeeded &&
                TryParseCoordinatorPlan(reply.Content, available.Select(item => item.Member).ToArray(), out var parsed))
                return parsed with { UsedCoordinator = true };
        }

        var nonCoordinator = available.Where(item => !item.Member.IsCoordinator).ToArray();
        var candidates = nonCoordinator.Length == 0 ? available : nonCoordinator;
        var selected = candidates.OrderByDescending(item => ScoreForGoal(item.Member, goal)).ThenBy(item => item.ActiveWork.Count).First();
        return new($"Fallback plan: assign the goal to {selected.Member.Name}, the best currently available match.",
            [new MeshWorkPlanAssignment(selected.Member.Name, goal)], false);
    }

    private async Task<string> BuildCoordinatorReviewAsync(
        string goal, MeshWorkMember? coordinator, IReadOnlyList<MeshWorkItem> items, CancellationToken cancellationToken)
    {
        var completed = items.Where(item => item.Status == MeshWorkItemStatus.Succeeded).ToArray();
        if (coordinator is null || !coordinator.IsEnabled || completed.Length == 0) return BuildDeterministicSummary(items);
        var dashboard = await GetDashboardAsync(cancellationToken).ConfigureAwait(false);
        if (dashboard.TrustedPeers.FirstOrDefault(peer => peer.Peer.DeviceId == coordinator.DeviceId)?.Presence.Connection != MeshConnectionState.Connected)
            return BuildDeterministicSummary(items);
        var body = string.Join("\n\n", completed.Select(item =>
        {
            var worker = ResolveWorkMember(item.AssignedWorkerId);
            var reviewer = item.ReviewerWorkerId is null ? null : ResolveWorkMember(item.ReviewerWorkerId.Value);
            return $"Task for {worker.Name}: {item.Goal}\nResult: {item.Result}\nReview from {reviewer?.Name ?? "none"}: {item.Review ?? "none"}";
        }));
        var prompt = $"Review the completed work for the original goal below. Reconcile disagreements, call out any failed or missing work, and give the user a concise final synthesis plus next actions. Do not pretend failed tasks succeeded.\n\nOriginal goal: {goal}\n\nTeam outputs:\n{body}";
        var reply = await InvokeWorkMemberAsync(coordinator, prompt, MeshWorkChannelKind.Coordinator, null, cancellationToken).ConfigureAwait(false);
        return reply.Status == MeshWorkMessageStatus.Succeeded ? reply.Content : BuildDeterministicSummary(items);
    }

    private static string BuildDeterministicSummary(IReadOnlyList<MeshWorkItem> items)
    {
        var succeeded = items.Count(item => item.Status == MeshWorkItemStatus.Succeeded);
        var failed = items.Count - succeeded;
        var detail = string.Join(Environment.NewLine, items.Select(item =>
            $"- {item.Goal}: {item.Status}{(string.IsNullOrWhiteSpace(item.Error) ? string.Empty : " — " + item.Error)}"));
        return $"Work Mode completed {succeeded} task(s); {failed} task(s) did not complete successfully.{Environment.NewLine}{detail}";
    }

    private static bool TryParseCoordinatorPlan(string text, IReadOnlyList<MeshWorkMember> allowed, out MeshWorkPlan plan)
    {
        plan = new MeshWorkPlan(string.Empty, [], true);
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return false;
        try
        {
            var payload = JsonSerializer.Deserialize<CoordinatorPlanPayload>(text[start..(end + 1)], WorkJson);
            if (payload is null || payload.Assignments is null || payload.Assignments.Length == 0) return false;
            var names = allowed.ToDictionary(member => member.Name, StringComparer.OrdinalIgnoreCase);
            var assignments = new List<MeshWorkPlanAssignment>();
            foreach (var item in payload.Assignments)
            {
                if (string.IsNullOrWhiteSpace(item.WorkerName) || string.IsNullOrWhiteSpace(item.Task) || !names.ContainsKey(item.WorkerName.Trim())) return false;
                if (!string.IsNullOrWhiteSpace(item.ReviewerName) && !names.ContainsKey(item.ReviewerName.Trim())) return false;
                assignments.Add(new MeshWorkPlanAssignment(item.WorkerName.Trim(), item.Task.Trim(),
                    string.IsNullOrWhiteSpace(item.ReviewerName) ? null : item.ReviewerName.Trim()));
            }
            plan = new MeshWorkPlan(string.IsNullOrWhiteSpace(payload.Summary) ? "Coordinator plan" : payload.Summary.Trim(), assignments, true);
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static int ScoreForGoal(MeshWorkMember member, string goal)
    {
        var score = 0;
        foreach (var specialty in member.Specialties.Where(value => !string.IsNullOrWhiteSpace(value)))
            if (goal.Contains(specialty, StringComparison.OrdinalIgnoreCase)) score += 4;
        if (!string.IsNullOrWhiteSpace(member.Role) && goal.Contains(member.Role, StringComparison.OrdinalIgnoreCase)) score += 2;
        return score;
    }

    private sealed record CoordinatorPlanPayload(string? Summary, CoordinatorAssignmentPayload[]? Assignments);
    private sealed record CoordinatorAssignmentPayload(string? WorkerName, string? Task, string? ReviewerName);
}
