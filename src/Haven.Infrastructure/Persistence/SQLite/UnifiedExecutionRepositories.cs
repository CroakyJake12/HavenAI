using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

internal static class UnifiedPersistenceJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    internal static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    internal static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Options)
        ?? throw new InvalidDataException($"Persisted {typeof(T).Name} payload was empty.");
}

public sealed class ExecutionEventRepository(ISqliteConnectionFactory factory) : IExecutionEventRepository
{
    public async Task AppendAsync(IReadOnlyList<ExecutionEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0) return;
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var item in events)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO execution_events(event_id,execution_id,action_id,parent_action_id,origin,action_type,status,name,component_id,timestamp,payload_json)
                VALUES($eventId,$executionId,$actionId,$parentActionId,$origin,$actionType,$status,$name,$componentId,$timestamp,$payload);
                """;
            command.Parameters.AddWithValue("$eventId", item.EventId.ToString());
            command.Parameters.AddWithValue("$executionId", item.ExecutionId.ToString());
            command.Parameters.AddWithValue("$actionId", item.ActionId.ToString());
            command.Parameters.AddWithValue("$parentActionId", (object?)item.ParentActionId?.ToString() ?? DBNull.Value);
            command.Parameters.AddWithValue("$origin", (int)item.Origin);
            command.Parameters.AddWithValue("$actionType", (int)item.ActionType);
            command.Parameters.AddWithValue("$status", (int)item.Status);
            command.Parameters.AddWithValue("$name", item.Name);
            command.Parameters.AddWithValue("$componentId", (object?)item.ComponentId ?? DBNull.Value);
            command.Parameters.AddWithValue("$timestamp", item.Timestamp.ToString("O"));
            command.Parameters.AddWithValue("$payload", UnifiedPersistenceJson.Write(item));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ExecutionEvent>> GetExecutionAsync(Guid executionId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM execution_events WHERE execution_id=$id ORDER BY sequence;";
        command.Parameters.AddWithValue("$id", executionId.ToString());
        var result = new List<ExecutionEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(UnifiedPersistenceJson.Read<ExecutionEvent>(reader.GetString(0)));
        return result;
    }

    public async Task<IReadOnlyList<ExecutionSummary>> SearchExecutionsAsync(string? query, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH executions AS (
              SELECT execution_id,MIN(timestamp) started_at,MAX(timestamp) updated_at,COUNT(DISTINCT action_id) action_count
              FROM execution_events
              WHERE ($query='' OR name LIKE $pattern)
              GROUP BY execution_id ORDER BY MAX(sequence) DESC LIMIT $limit
            )
            SELECT x.execution_id,x.started_at,x.updated_at,x.action_count,
              COALESCE((SELECT name FROM execution_events p WHERE p.execution_id=x.execution_id AND p.action_type=$prompt ORDER BY sequence LIMIT 1),'Execution'),
              COALESCE((SELECT origin FROM execution_events p WHERE p.execution_id=x.execution_id ORDER BY sequence LIMIT 1),0),
              COALESCE((SELECT status FROM execution_events p WHERE p.execution_id=x.execution_id ORDER BY sequence DESC LIMIT 1),0),
              (SELECT payload_json FROM execution_events p WHERE p.execution_id=x.execution_id ORDER BY sequence DESC LIMIT 1)
            FROM executions x ORDER BY x.updated_at DESC;
            """;
        var normalized = query?.Trim() ?? string.Empty;
        command.Parameters.AddWithValue("$query", normalized);
        command.Parameters.AddWithValue("$pattern", $"%{normalized.Replace("%", "[%]", StringComparison.Ordinal)}%");
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$prompt", (int)ExecutionActionType.UserPrompt);
        var result = new List<ExecutionSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var last = UnifiedPersistenceJson.Read<ExecutionEvent>(reader.GetString(7));
            var started = DateTimeOffset.Parse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture);
            var updated = DateTimeOffset.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture);
            result.Add(new ExecutionSummary(Guid.Parse(reader.GetString(0)), reader.GetString(4),
                (ExecutionOrigin)reader.GetInt32(5), (ExecutionActionStatus)reader.GetInt32(6), started, updated,
                reader.GetInt32(3), updated - started, last.TabId, last.TaskId));
        }
        return result;
    }
}

public sealed class ActionFeedbackRepository(ISqliteConnectionFactory factory) : IActionFeedbackRepository
{
    public async Task UpsertAsync(ActionFeedback feedback, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO action_feedback(id,execution_id,action_id,rating,comment,payload_json,created_at,updated_at)
            VALUES($id,$executionId,$actionId,$rating,$comment,$payload,$createdAt,$updatedAt)
            ON CONFLICT(execution_id,action_id) DO UPDATE SET rating=excluded.rating,comment=excluded.comment,payload_json=excluded.payload_json,updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", feedback.Id.ToString());
        command.Parameters.AddWithValue("$executionId", feedback.ExecutionId.ToString());
        command.Parameters.AddWithValue("$actionId", feedback.ActionId.ToString());
        command.Parameters.AddWithValue("$rating", (object?)(feedback.Rating is null ? null : (int)feedback.Rating) ?? DBNull.Value);
        command.Parameters.AddWithValue("$comment", (object?)feedback.Comment ?? DBNull.Value);
        command.Parameters.AddWithValue("$payload", UnifiedPersistenceJson.Write(feedback));
        command.Parameters.AddWithValue("$createdAt", feedback.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", feedback.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ActionFeedback?> GetAsync(Guid executionId, Guid actionId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM action_feedback WHERE execution_id=$executionId AND action_id=$actionId;";
        command.Parameters.AddWithValue("$executionId", executionId.ToString());
        command.Parameters.AddWithValue("$actionId", actionId.ToString());
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return value is null ? null : UnifiedPersistenceJson.Read<ActionFeedback>(value);
    }

    public async Task DeleteAsync(Guid feedbackId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM action_feedback WHERE id=$id;";
        command.Parameters.AddWithValue("$id", feedbackId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class RemediationRepository(ISqliteConnectionFactory factory) : IRemediationRepository
{
    public async Task UpsertAsync(RemediationRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO remediations(id,execution_id,action_id,state,expires_at,payload_json,updated_at)
            VALUES($id,$executionId,$actionId,$state,$expiresAt,$payload,$updatedAt)
            ON CONFLICT(id) DO UPDATE SET state=excluded.state,expires_at=excluded.expires_at,payload_json=excluded.payload_json,updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", request.Id.ToString());
        command.Parameters.AddWithValue("$executionId", request.ExecutionId.ToString());
        command.Parameters.AddWithValue("$actionId", request.ActionId.ToString());
        command.Parameters.AddWithValue("$state", (int)request.State);
        command.Parameters.AddWithValue("$expiresAt", (object?)request.ExpiresAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$payload", UnifiedPersistenceJson.Write(request));
        command.Parameters.AddWithValue("$updatedAt", request.LastActivityAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<RemediationRequest?> GetAsync(Guid remediationId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM remediations WHERE id=$id;";
        command.Parameters.AddWithValue("$id", remediationId.ToString());
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return value is null ? null : UnifiedPersistenceJson.Read<RemediationRequest>(value);
    }

    public async Task<IReadOnlyList<RemediationRequest>> GetWaitingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM remediations WHERE state IN($waiting,$progress,$suspended) ORDER BY updated_at DESC;";
        command.Parameters.AddWithValue("$waiting", (int)RemediationState.Waiting);
        command.Parameters.AddWithValue("$progress", (int)RemediationState.InProgress);
        command.Parameters.AddWithValue("$suspended", (int)RemediationState.Suspended);
        var result = new List<RemediationRequest>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(UnifiedPersistenceJson.Read<RemediationRequest>(reader.GetString(0)));
        return result;
    }
}

public sealed class ExternalAgentTaskRepository(ISqliteConnectionFactory factory) : IExternalAgentTaskRepository
{
    public Task CreateAsync(ExternalAgentTask task, CancellationToken cancellationToken) => UpsertAsync(task, insertOnly: true, cancellationToken);

    public Task<ExternalAgentTask?> GetByLocatorAsync(HavenTaskLocator locator, CancellationToken cancellationToken) =>
        ReadOneAsync("locator", locator.Value, cancellationToken);

    public Task<ExternalAgentTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        ReadOneAsync("id", id.ToString(), cancellationToken);

    public async Task<IReadOnlyList<ExternalAgentTask>> GetRecentAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM external_agent_tasks ORDER BY updated_at DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);
        var result = new List<ExternalAgentTask>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(UnifiedPersistenceJson.Read<ExternalAgentTask>(reader.GetString(0)));
        return result;
    }

    public async Task<ExternalTaskClaim?> TryClaimAsync(Guid taskId, string claimant, string leaseTokenHash, string returnedLeaseToken,
        DateTimeOffset now, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var existing = await ReadOneAsync(connection, transaction, "id", taskId.ToString(), cancellationToken).ConfigureAwait(false);
        if (existing is null || existing.Status is not (HavenTaskStatus.AwaitingAgent or HavenTaskStatus.Claimed) ||
            existing.ExpiresAt <= now || (existing.LeaseExpiresAt > now && !string.Equals(existing.ClaimedBy, claimant, StringComparison.Ordinal)))
            return null;
        var claimed = existing with
        {
            Status = HavenTaskStatus.Claimed,
            ClaimedBy = claimant,
            LeaseTokenHash = leaseTokenHash,
            LeaseExpiresAt = leaseExpiresAt,
            UpdatedAt = now
        };
        await UpsertAsync(connection, transaction, claimed, false, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new ExternalTaskClaim(claimed, returnedLeaseToken, leaseExpiresAt);
    }

    public async Task<bool> TryUpdateClaimedAsync(Guid taskId, string leaseTokenHash, HavenTaskStatus status, string? safeProgress,
        string? safeResult, string? safeError, string? idempotencyKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var existing = await ReadOneAsync(connection, transaction, "id", taskId.ToString(), cancellationToken).ConfigureAwait(false);
        if (existing is null || existing.LeaseTokenHash != leaseTokenHash || existing.LeaseExpiresAt < now) return false;
        if (existing.Status is HavenTaskStatus.Completed or HavenTaskStatus.Failed or HavenTaskStatus.Cancelled)
            return !string.IsNullOrWhiteSpace(idempotencyKey) && existing.IdempotencyKey == idempotencyKey && existing.Status == status;
        var updated = existing with
        {
            Status = status, SafeProgress = safeProgress, SafeResult = safeResult, SafeError = safeError,
            IdempotencyKey = idempotencyKey ?? existing.IdempotencyKey,
            LeaseExpiresAt = status is HavenTaskStatus.InProgress ? now + ExternalAgentTaskService.DefaultLease : existing.LeaseExpiresAt,
            UpdatedAt = now
        };
        await UpsertAsync(connection, transaction, updated, false, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> TryCancelAsync(Guid taskId, Guid ownerUserId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (existing is null || existing.OwnerUserId != ownerUserId || existing.Status is HavenTaskStatus.Completed or HavenTaskStatus.Cancelled) return false;
        await UpsertAsync(existing with { Status = HavenTaskStatus.Cancelled, UpdatedAt = now }, false, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<ExternalAgentTask?> ReadOneAsync(string column, string value, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadOneAsync(connection, null, column, value, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ExternalAgentTask?> ReadOneAsync(SqliteConnection connection, System.Data.Common.DbTransaction? transaction,
        string column, string value, CancellationToken cancellationToken)
    {
        if (column is not ("id" or "locator")) throw new ArgumentOutOfRangeException(nameof(column));
        await using var command = connection.CreateCommand();
        command.Transaction = transaction as SqliteTransaction;
        command.CommandText = $"SELECT payload_json FROM external_agent_tasks WHERE {column}=$value;";
        command.Parameters.AddWithValue("$value", value);
        var payload = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return payload is null ? null : UnifiedPersistenceJson.Read<ExternalAgentTask>(payload);
    }

    private async Task UpsertAsync(ExternalAgentTask task, bool insertOnly, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await UpsertAsync(connection, null, task, insertOnly, cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertAsync(SqliteConnection connection, System.Data.Common.DbTransaction? transaction,
        ExternalAgentTask task, bool insertOnly, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction as SqliteTransaction;
        command.CommandText = insertOnly
            ? """
              INSERT INTO external_agent_tasks(id,locator,owner_user_id,workspace_id,project_id,status,claimed_by,lease_token_hash,lease_expires_at,idempotency_key,payload_json,created_at,updated_at,expires_at)
              VALUES($id,$locator,$owner,$workspace,$project,$status,$claimedBy,$leaseHash,$leaseExpires,$idempotency,$payload,$createdAt,$updatedAt,$expiresAt);
              """
            : """
              INSERT INTO external_agent_tasks(id,locator,owner_user_id,workspace_id,project_id,status,claimed_by,lease_token_hash,lease_expires_at,idempotency_key,payload_json,created_at,updated_at,expires_at)
              VALUES($id,$locator,$owner,$workspace,$project,$status,$claimedBy,$leaseHash,$leaseExpires,$idempotency,$payload,$createdAt,$updatedAt,$expiresAt)
              ON CONFLICT(id) DO UPDATE SET status=excluded.status,claimed_by=excluded.claimed_by,lease_token_hash=excluded.lease_token_hash,
                lease_expires_at=excluded.lease_expires_at,idempotency_key=excluded.idempotency_key,payload_json=excluded.payload_json,updated_at=excluded.updated_at;
              """;
        BindTask(command, task);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void BindTask(SqliteCommand command, ExternalAgentTask task)
    {
        command.Parameters.AddWithValue("$id", task.Id.ToString()); command.Parameters.AddWithValue("$locator", task.Locator.Value);
        command.Parameters.AddWithValue("$owner", task.OwnerUserId.ToString()); command.Parameters.AddWithValue("$workspace", (object?)task.WorkspaceId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$project", (object?)task.ProjectId?.ToString() ?? DBNull.Value); command.Parameters.AddWithValue("$status", (int)task.Status);
        command.Parameters.AddWithValue("$claimedBy", (object?)task.ClaimedBy ?? DBNull.Value); command.Parameters.AddWithValue("$leaseHash", (object?)task.LeaseTokenHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$leaseExpires", (object?)task.LeaseExpiresAt?.ToString("O") ?? DBNull.Value); command.Parameters.AddWithValue("$idempotency", (object?)task.IdempotencyKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$payload", UnifiedPersistenceJson.Write(task)); command.Parameters.AddWithValue("$createdAt", task.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", task.UpdatedAt.ToString("O")); command.Parameters.AddWithValue("$expiresAt", (object?)task.ExpiresAt?.ToString("O") ?? DBNull.Value);
    }
}
