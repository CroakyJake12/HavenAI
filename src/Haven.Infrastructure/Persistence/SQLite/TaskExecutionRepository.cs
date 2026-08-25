using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class TaskExecutionRepository(ISqliteConnectionFactory factory) : ITaskExecutionRepository
{
    public async Task UpsertAsync(TaskExecutionSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO task_execution_state(task_id,context_id,execution_id,state,durability,plan_version,payload_json,created_at,updated_at)
            VALUES($taskId,$contextId,$executionId,$state,$durability,$planVersion,$payload,$createdAt,$updatedAt)
            ON CONFLICT(task_id) DO UPDATE SET context_id=excluded.context_id,execution_id=excluded.execution_id,state=excluded.state,
              durability=excluded.durability,plan_version=excluded.plan_version,payload_json=excluded.payload_json,updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$taskId", snapshot.TaskId.ToString());
        command.Parameters.AddWithValue("$contextId", snapshot.ContextId.ToString());
        command.Parameters.AddWithValue("$executionId", snapshot.ExecutionId.ToString());
        command.Parameters.AddWithValue("$state", (int)snapshot.State);
        command.Parameters.AddWithValue("$durability", (int)snapshot.Durability);
        command.Parameters.AddWithValue("$planVersion", snapshot.PlanVersion);
        command.Parameters.AddWithValue("$payload", UnifiedPersistenceJson.Write(snapshot));
        command.Parameters.AddWithValue("$createdAt", snapshot.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", snapshot.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<TaskExecutionSnapshot?> GetAsync(Guid taskId, CancellationToken cancellationToken) =>
        ReadOneAsync("task_id", taskId.ToString(), cancellationToken);

    public Task<TaskExecutionSnapshot?> GetByContextAsync(Guid contextId, CancellationToken cancellationToken) =>
        ReadOneAsync("context_id", contextId.ToString(), cancellationToken);

    public async Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM task_execution_state WHERE state IN($running,$waiting,$blocked,$suspended) ORDER BY updated_at DESC;";
        command.Parameters.AddWithValue("$running", (int)TaskExecutionLifecycle.Running);
        command.Parameters.AddWithValue("$waiting", (int)TaskExecutionLifecycle.WaitingSafeBoundary);
        command.Parameters.AddWithValue("$blocked", (int)TaskExecutionLifecycle.Blocked);
        command.Parameters.AddWithValue("$suspended", (int)TaskExecutionLifecycle.Suspended);
        var result = new List<TaskExecutionSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(UnifiedPersistenceJson.Read<TaskExecutionSnapshot>(reader.GetString(0)));
        return result;
    }

    private async Task<TaskExecutionSnapshot?> ReadOneAsync(string column, string value, CancellationToken cancellationToken)
    {
        if (column is not ("task_id" or "context_id")) throw new ArgumentOutOfRangeException(nameof(column));
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT payload_json FROM task_execution_state WHERE {column}=$value ORDER BY updated_at DESC LIMIT 1;";
        command.Parameters.AddWithValue("$value", value);
        var payload = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return payload is null ? null : UnifiedPersistenceJson.Read<TaskExecutionSnapshot>(payload);
    }
}
