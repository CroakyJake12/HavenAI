/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/CallRepository.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns CallRepository. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>
/// Represents call repository and keeps its related state and behavior together.
/// </summary>
public sealed class CallRepository(ISqliteConnectionFactory factory) : ICallRepository
{
    /// <summary>
    /// Performs upsert asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task UpsertAsync(CallSession session, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO call_sessions(
                id,conversation_id,model_name,input_device_id,output_device_id,voice_name,
                input_mode,used_screen_share,status,started_at,ended_at,error)
            VALUES(
                $id,$conversationId,$modelName,$inputDeviceId,$outputDeviceId,$voiceName,
                $inputMode,$usedScreenShare,$status,$startedAt,$endedAt,$error)
            ON CONFLICT(id) DO UPDATE SET
                conversation_id=excluded.conversation_id,
                model_name=excluded.model_name,
                input_device_id=excluded.input_device_id,
                output_device_id=excluded.output_device_id,
                voice_name=excluded.voice_name,
                input_mode=excluded.input_mode,
                used_screen_share=excluded.used_screen_share,
                status=excluded.status,
                started_at=excluded.started_at,
                ended_at=excluded.ended_at,
                error=excluded.error;
            """;
        command.Parameters.AddWithValue("$id", session.Id.ToString());
        command.Parameters.AddWithValue("$conversationId", session.ConversationId.ToString());
        command.Parameters.AddWithValue("$modelName", session.ModelName);
        command.Parameters.AddWithValue("$inputDeviceId", (object?)session.InputDeviceId ?? DBNull.Value);
        command.Parameters.AddWithValue("$outputDeviceId", (object?)session.OutputDeviceId ?? DBNull.Value);
        command.Parameters.AddWithValue("$voiceName", (object?)session.VoiceName ?? DBNull.Value);
        command.Parameters.AddWithValue("$inputMode", (int)session.InputMode);
        command.Parameters.AddWithValue("$usedScreenShare", session.UsedScreenShare ? 1 : 0);
        command.Parameters.AddWithValue("$status", (int)session.Status);
        command.Parameters.AddWithValue("$startedAt", session.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$endedAt", (object?)session.EndedAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)session.Error ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves async for the current operation.
    /// </summary>
    public async Task<CallSession?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM call_sessions WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        var sessions = await ReadAsync(command, cancellationToken).ConfigureAwait(false);
        return sessions.FirstOrDefault();
    }

    /// <summary>
    /// Retrieves recent async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<CallSession>> GetRecentAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM call_sessions ORDER BY started_at DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        return await ReadAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs read asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<IReadOnlyList<CallSession>> ReadAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var result = new List<CallSession>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new CallSession(
                reader.Guid("id"),
                reader.Guid("conversation_id"),
                reader.String("model_name"),
                reader.NullableString("input_device_id"),
                reader.NullableString("output_device_id"),
                reader.NullableString("voice_name"),
                (CallInputMode)reader.Int32("input_mode"),
                reader.Boolean("used_screen_share"),
                (CallSessionStatus)reader.Int32("status"),
                reader.DateTimeOffset("started_at"),
                reader.NullableDateTimeOffset("ended_at"),
                reader.NullableString("error")));
        }
        return result;
    }
}
