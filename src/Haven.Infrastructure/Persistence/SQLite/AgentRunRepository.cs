using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Persists durable Agent execution history independently of chat history.</summary>
public sealed class AgentRunRepository(ISqliteConnectionFactory factory) : IAgentRunRepository
{
    public async Task UpsertAsync(AgentRun run, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO agent_runs(
                id,agent_id,agent_name,task,status,model_name,result,error,capabilities_json,activity_json,
                created_at,started_at,completed_at,retry_of_run_id,resource_reference,progress_percent)
            VALUES(
                $id,$agentId,$agentName,$task,$status,$modelName,$result,$error,$capabilitiesJson,$activityJson,
                $createdAt,$startedAt,$completedAt,$retryOfRunId,$resourceReference,$progressPercent)
            ON CONFLICT(id) DO UPDATE SET
                agent_id=excluded.agent_id,
                agent_name=excluded.agent_name,
                task=excluded.task,
                status=excluded.status,
                model_name=excluded.model_name,
                result=excluded.result,
                error=excluded.error,
                capabilities_json=excluded.capabilities_json,
                activity_json=excluded.activity_json,
                started_at=excluded.started_at,
                completed_at=excluded.completed_at,
                retry_of_run_id=excluded.retry_of_run_id,
                resource_reference=excluded.resource_reference,
                progress_percent=excluded.progress_percent;
            """;
        Bind(command, run);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AgentRun?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM agent_runs WHERE id=$id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<AgentRun>> GetRecentAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM agent_runs ORDER BY created_at DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 200));
        return await ReadManyAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentRun>> GetByAgentAsync(Guid agentId, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM agent_runs WHERE agent_id=$agentId ORDER BY created_at DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$agentId", agentId.ToString());
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 200));
        return await ReadManyAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<AgentRun>> ReadManyAsync(
        Microsoft.Data.Sqlite.SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var result = new List<AgentRun>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(Read(reader));
        return result;
    }

    private static AgentRun Read(Microsoft.Data.Sqlite.SqliteDataReader reader) => new(
        reader.Guid("id"),
        reader.Guid("agent_id"),
        reader.String("agent_name"),
        reader.String("task"),
        (AgentRunStatus)reader.Int32("status"),
        reader.String("model_name"),
        reader.String("result"),
        reader.String("error"),
        reader.String("capabilities_json"),
        reader.String("activity_json"),
        reader.DateTimeOffset("created_at"),
        reader.NullableDateTimeOffset("started_at"),
        reader.NullableDateTimeOffset("completed_at"),
        reader.NullableGuid("retry_of_run_id"),
        reader.IsDBNull(reader.GetOrdinal("resource_reference")) ? null : reader.GetString(reader.GetOrdinal("resource_reference")),
        reader.Int32("progress_percent"));

    private static void Bind(Microsoft.Data.Sqlite.SqliteCommand command, AgentRun run)
    {
        command.Parameters.AddWithValue("$id", run.Id.ToString());
        command.Parameters.AddWithValue("$agentId", run.AgentId.ToString());
        command.Parameters.AddWithValue("$agentName", run.AgentName);
        command.Parameters.AddWithValue("$task", run.Task);
        command.Parameters.AddWithValue("$status", (int)run.Status);
        command.Parameters.AddWithValue("$modelName", run.ModelName);
        command.Parameters.AddWithValue("$result", run.Result);
        command.Parameters.AddWithValue("$error", run.Error);
        command.Parameters.AddWithValue("$capabilitiesJson", run.CapabilitiesJson);
        command.Parameters.AddWithValue("$activityJson", run.ActivityJson);
        command.Parameters.AddWithValue("$createdAt", run.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$startedAt", (object?)run.StartedAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$completedAt", (object?)run.CompletedAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$retryOfRunId", (object?)run.RetryOfRunId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$resourceReference", (object?)run.ResourceReference ?? DBNull.Value);
        command.Parameters.AddWithValue("$progressPercent", Math.Clamp(run.ProgressPercent, 0, 100));
    }
}
