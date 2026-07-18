/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/AutomationRepository.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns AutomationRepository. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents automation repository and keeps its related state and behavior together.
/// </summary>
public sealed class AutomationRepository(ISqliteConnectionFactory factory) : IAutomationRepository
{
    /// <summary>
    /// Retrieves all async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<AutomationDefinition>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM automations ORDER BY updated_at DESC;";
        return await ReadAutomationsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves due async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<AutomationDefinition>> GetDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM automations
            WHERE is_enabled=1 AND next_run_at IS NOT NULL AND next_run_at <= $now
              AND (lease_until IS NULL OR lease_until < $now)
            ORDER BY next_run_at;
            """;
        command.Parameters.AddWithValue("$now", now.ToUniversalTime().ToString("O"));
        return await ReadAutomationsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs upsert async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task UpsertAsync(AutomationDefinition automation, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO automations(id,name,mode,instruction,schedule_kind,schedule_json,next_run_at,container_id,is_enabled,created_at,updated_at)
            VALUES($id,$name,$mode,$instruction,$scheduleKind,$scheduleJson,$nextRunAt,$containerId,$isEnabled,$createdAt,$updatedAt)
            ON CONFLICT(id) DO UPDATE SET name=excluded.name,mode=excluded.mode,instruction=excluded.instruction,
              schedule_kind=excluded.schedule_kind,schedule_json=excluded.schedule_json,next_run_at=excluded.next_run_at,
              container_id=excluded.container_id,is_enabled=excluded.is_enabled,updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", automation.Id.ToString());
        command.Parameters.AddWithValue("$name", automation.Name);
        command.Parameters.AddWithValue("$mode", (int)automation.Mode);
        command.Parameters.AddWithValue("$instruction", automation.Instruction);
        command.Parameters.AddWithValue("$scheduleKind", (int)automation.ScheduleKind);
        command.Parameters.AddWithValue("$scheduleJson", automation.ScheduleJson);
        command.Parameters.AddWithValue("$nextRunAt", (object?)automation.NextRunAt?.ToUniversalTime().ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$containerId", (object?)automation.ContainerId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$isEnabled", automation.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", automation.CreatedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", automation.UpdatedAt.ToUniversalTime().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }


    /// <summary>
    /// Performs delete async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM automations WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Attempts to acquire lease async and reports the result without using failure for normal control flow.
    /// </summary>
    public async Task<bool> TryAcquireLeaseAsync(Guid automationId, string leaseToken, DateTimeOffset leaseUntil, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE automations SET lease_token=$leaseToken, lease_until=$leaseUntil
            WHERE id=$id AND (lease_until IS NULL OR lease_until < $now);
            """;
        command.Parameters.AddWithValue("$leaseToken", leaseToken);
        command.Parameters.AddWithValue("$leaseUntil", leaseUntil.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$id", automationId.ToString());
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    /// <summary>
    /// Performs complete run async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task CompleteRunAsync(AutomationRun run, DateTimeOffset? nextRunAt, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO automation_runs(id,automation_id,status,scheduled_for,started_at,completed_at,result,error,lease_token)
                VALUES($id,$automationId,$status,$scheduledFor,$startedAt,$completedAt,$result,$error,$leaseToken);
                """;
            insert.Parameters.AddWithValue("$id", run.Id.ToString());
            insert.Parameters.AddWithValue("$automationId", run.AutomationId.ToString());
            insert.Parameters.AddWithValue("$status", (int)run.Status);
            insert.Parameters.AddWithValue("$scheduledFor", run.ScheduledFor.ToUniversalTime().ToString("O"));
            insert.Parameters.AddWithValue("$startedAt", (object?)run.StartedAt?.ToUniversalTime().ToString("O") ?? DBNull.Value);
            insert.Parameters.AddWithValue("$completedAt", (object?)run.CompletedAt?.ToUniversalTime().ToString("O") ?? DBNull.Value);
            insert.Parameters.AddWithValue("$result", (object?)run.Result ?? DBNull.Value);
            insert.Parameters.AddWithValue("$error", (object?)run.Error ?? DBNull.Value);
            insert.Parameters.AddWithValue("$leaseToken", (object?)run.LeaseToken ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE automations SET next_run_at=$nextRunAt, lease_token=NULL, lease_until=NULL, updated_at=$updatedAt WHERE id=$id AND lease_token=$leaseToken;";
            update.Parameters.AddWithValue("$nextRunAt", (object?)nextRunAt?.ToUniversalTime().ToString("O") ?? DBNull.Value);
            update.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            update.Parameters.AddWithValue("$id", run.AutomationId.ToString());
            update.Parameters.AddWithValue("$leaseToken", (object?)run.LeaseToken ?? DBNull.Value);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves runs async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<AutomationRun>> GetRunsAsync(Guid automationId, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM automation_runs WHERE automation_id=$automationId ORDER BY scheduled_for DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$automationId", automationId.ToString());
        command.Parameters.AddWithValue("$limit", limit);
        var result = new List<AutomationRun>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new AutomationRun(reader.Guid("id"), reader.Guid("automation_id"), (AutomationRunStatus)reader.Int32("status"),
                reader.DateTimeOffset("scheduled_for"), reader.NullableDateTimeOffset("started_at"), reader.NullableDateTimeOffset("completed_at"),
                reader.NullableString("result"), reader.NullableString("error"), reader.NullableString("lease_token")));
        }
        return result;
    }

    /// <summary>
    /// Performs read automations async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<IReadOnlyList<AutomationDefinition>> ReadAutomationsAsync(Microsoft.Data.Sqlite.SqliteCommand command, CancellationToken cancellationToken)
    {
        var result = new List<AutomationDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new AutomationDefinition(reader.Guid("id"), reader.String("name"), (HavenMode)reader.Int32("mode"), reader.String("instruction"),
                (AutomationScheduleKind)reader.Int32("schedule_kind"), reader.String("schedule_json"), reader.NullableDateTimeOffset("next_run_at"),
                reader.NullableGuid("container_id"), reader.Boolean("is_enabled"), reader.DateTimeOffset("created_at"), reader.DateTimeOffset("updated_at")));
        }
        return result;
    }
}
