using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Generic authenticated external-agent task service; no client brand is hard-coded.</summary>
public sealed class ExternalAgentTaskService(IExternalAgentTaskRepository repository, IExecutionEventSink events)
{
    public static readonly TimeSpan DefaultLease = TimeSpan.FromMinutes(5);

    public async Task<ExternalAgentTask> CreateAsync(
        ExternalAgentPrincipal principal,
        string agentPluginId,
        string title,
        string instruction,
        string contextReferenceJson,
        string expectedOutput,
        Guid? workspaceId,
        Guid? projectId,
        Guid? sourceTabId,
        Guid? executionId,
        TimeSpan? lifetime,
        CancellationToken cancellationToken)
    {
        ValidateAccess(principal, workspaceId, projectId);
        var now = DateTimeOffset.UtcNow;
        var task = new ExternalAgentTask(
            Guid.NewGuid(), CreateLocator(), principal.UserId, workspaceId, projectId, sourceTabId, executionId,
            agentPluginId, SensitiveTextRedactor.Redact(title, 240),
            SensitiveTextRedactor.Redact(instruction, 12_000), SanitizeContextReferenceJson(contextReferenceJson),
            SensitiveTextRedactor.Redact(expectedOutput, 2_000), HavenTaskStatus.AwaitingAgent,
            null, null, null, null, null, null, null, now, now, now + (lifetime ?? TimeSpan.FromDays(7)));
        await repository.CreateAsync(task, cancellationToken).ConfigureAwait(false);
        Publish(task, ExecutionActionStatus.Queued, "External-agent task created");
        return task;
    }

    public async Task<ExternalAgentTask?> GetAuthorisedAsync(
        HavenTaskLocator locator,
        ExternalAgentPrincipal principal,
        CancellationToken cancellationToken)
    {
        var task = await repository.GetByLocatorAsync(locator, cancellationToken).ConfigureAwait(false);
        if (task is null) return null;
        EnsureAuthorised(task, principal);
        return task.ExpiresAt <= DateTimeOffset.UtcNow ? task with { Status = HavenTaskStatus.Expired } : task;
    }

    public async Task<IReadOnlyList<ExternalAgentTask>> GetRecentAuthorisedAsync(
        ExternalAgentPrincipal principal,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        var tasks = await repository.GetRecentAsync(limit, cancellationToken).ConfigureAwait(false);
        return tasks.Where(task => task.OwnerUserId == principal.UserId
                                   && (task.WorkspaceId is null || principal.WorkspaceIds.Contains(task.WorkspaceId.Value))
                                   && (task.ProjectId is null || principal.ProjectIds.Contains(task.ProjectId.Value)))
            .Select(task => task.ExpiresAt <= DateTimeOffset.UtcNow && task.Status is not (HavenTaskStatus.Completed or HavenTaskStatus.Cancelled)
                ? task with { Status = HavenTaskStatus.Expired }
                : task)
            .ToArray();
    }

    public async Task CancelAsync(
        Guid taskId,
        ExternalAgentPrincipal principal,
        CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Task was not found.");
        EnsureAuthorised(task, principal);
        if (!await repository.TryCancelAsync(taskId, principal.UserId, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Completed or already-cancelled tasks cannot be cancelled.");
        var cancelled = await repository.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (cancelled is not null) Publish(cancelled, ExecutionActionStatus.Cancelled, "External-agent task cancelled");
    }

    public async Task<ExternalTaskClaim> ClaimAsync(
        HavenTaskLocator locator,
        ExternalAgentPrincipal principal,
        string claimant,
        CancellationToken cancellationToken)
    {
        var task = await GetAuthorisedAsync(locator, principal, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Task was not found.");
        if (task.Status is not (HavenTaskStatus.AwaitingAgent or HavenTaskStatus.Claimed))
            throw new InvalidOperationException($"Task cannot be claimed while {task.Status}.");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var hash = HashToken(token);
        var now = DateTimeOffset.UtcNow;
        var claim = await repository.TryClaimAsync(task.Id, SensitiveTextRedactor.Redact(claimant, 200), hash, token,
            now, now + DefaultLease, cancellationToken).ConfigureAwait(false);
        if (claim is null) throw new InvalidOperationException("Task is already claimed by another active agent.");
        Publish(claim.Task, ExecutionActionStatus.Running, "External-agent task claimed");
        return claim;
    }

    public async Task UpdateAsync(
        Guid taskId,
        string leaseToken,
        HavenTaskStatus status,
        string? safeProgress,
        string? safeResult,
        string? safeError,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (status is HavenTaskStatus.Draft or HavenTaskStatus.AwaitingAgent or HavenTaskStatus.Claimed)
            throw new ArgumentOutOfRangeException(nameof(status));
        var updated = await repository.TryUpdateClaimedAsync(
            taskId, HashToken(leaseToken), status, SensitiveTextRedactor.Redact(safeProgress, 2_000),
            SensitiveTextRedactor.Redact(safeResult, 12_000), SensitiveTextRedactor.Redact(safeError, 4_000),
            idempotencyKey, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        if (!updated) throw new InvalidOperationException("The task lease is invalid, expired, or the update was already completed with a different idempotency key.");
        var task = await repository.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task is not null) Publish(task, MapStatus(status), $"External-agent task {status.ToString().ToLowerInvariant()}");
    }

    private void Publish(ExternalAgentTask task, ExecutionActionStatus status, string name)
    {
        var executionId = task.ExecutionId ?? task.Id;
        events.TryPublish(new ExecutionEvent(
            Guid.NewGuid(), executionId, task.Id, null, ExecutionOrigin.ExternalAgent,
            ExecutionActionType.ExternalAgent, status, name, task.SafeProgress, task.SafeError,
            task.AgentPluginId, DateTimeOffset.UtcNow, TaskId: task.Id, TabId: task.SourceTabId,
            ProjectId: task.ProjectId));
    }

    private static void ValidateAccess(ExternalAgentPrincipal principal, Guid? workspaceId, Guid? projectId)
    {
        if (workspaceId is { } workspace && !principal.WorkspaceIds.Contains(workspace)) throw new UnauthorizedAccessException("Workspace access is required.");
        if (projectId is { } project && !principal.ProjectIds.Contains(project)) throw new UnauthorizedAccessException("Project access is required.");
    }

    private static void EnsureAuthorised(ExternalAgentTask task, ExternalAgentPrincipal principal)
    {
        if (task.OwnerUserId != principal.UserId) throw new UnauthorizedAccessException("Task ownership could not be verified.");
        ValidateAccess(principal, task.WorkspaceId, task.ProjectId);
    }

    private static HavenTaskLocator CreateLocator()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<byte> bytes = stackalloc byte[12];
        RandomNumberGenerator.Fill(bytes);
        Span<char> chars = stackalloc char[12];
        for (var index = 0; index < chars.Length; index++) chars[index] = alphabet[bytes[index] % alphabet.Length];
        return new HavenTaskLocator($"HAV-{new string(chars[..4])}-{new string(chars[4..8])}-{new string(chars[8..])}");
    }

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string SanitizeContextReferenceJson(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 12_000) throw new ArgumentOutOfRangeException(nameof(value), "Task context references are limited to 12,000 characters.");
        var safe = SensitiveTextRedactor.Redact(value, 12_000);
        using var _ = JsonDocument.Parse(safe);
        return safe;
    }

    private static ExecutionActionStatus MapStatus(HavenTaskStatus status) => status switch
    {
        HavenTaskStatus.Completed => ExecutionActionStatus.Completed,
        HavenTaskStatus.Failed => ExecutionActionStatus.Failed,
        HavenTaskStatus.Cancelled or HavenTaskStatus.Expired => ExecutionActionStatus.Cancelled,
        HavenTaskStatus.Blocked or HavenTaskStatus.AwaitingUser => ExecutionActionStatus.UserActionRequired,
        _ => ExecutionActionStatus.Running
    };
}
