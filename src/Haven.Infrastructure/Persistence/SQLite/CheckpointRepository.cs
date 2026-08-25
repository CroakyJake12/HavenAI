/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/Persistence/SQLite/CheckpointRepository.cs, in the Infrastructure layer.
 * What: Owns SqliteCheckpointRepository, WorkspaceCheckpointRestorer and ProjectInstructionSource —
 *       SQLite-backed checkpoint storage, confined restore writes and filesystem discovery of
 *       agent.md / AGENTS.md instruction files.
 * How: Checkpoints reference workspace_versions rowid sequences; restores replay recorded
 *      before-content per path with root confinement; discovery caps depth at 6.
 * Why: Recovery and project rules must work in non-Git directories without trusting model memory.
 * Maintenance: Preserve forward-only schema (migration 23) and traversal rejection in every path use.
 */

using System.Security.Cryptography;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class SqliteCheckpointRepository(ISqliteConnectionFactory factory) : ICheckpointRepository
{
    public async Task SaveAsync(CheckpointInfo checkpoint, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO agent_checkpoints(id, conversation_id, container_id, workspace_root, label, mode, start_sequence, created_at)
            VALUES($id, $conversationId, $containerId, $root, $label, $mode, $startSequence, $createdAt)
            """;
        command.Parameters.AddWithValue("$id", checkpoint.Id.ToString());
        command.Parameters.AddWithValue("$conversationId", (object?)checkpoint.ConversationId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$containerId", (object?)checkpoint.ContainerId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$root", checkpoint.WorkspaceRoot);
        command.Parameters.AddWithValue("$label", checkpoint.Label);
        command.Parameters.AddWithValue("$mode", (int)checkpoint.Mode);
        command.Parameters.AddWithValue("$startSequence", checkpoint.StartSequence);
        command.Parameters.AddWithValue("$createdAt", checkpoint.CreatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CheckpointInfo?> GetLatestAsync(Guid? conversationId, string workspaceRoot, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, conversation_id, container_id, workspace_root, label, mode, start_sequence, created_at
            FROM agent_checkpoints
            WHERE workspace_root = $root
            ORDER BY created_at DESC LIMIT 1
            """;
        command.Parameters.AddWithValue("$root", workspaceRoot);
        return await ReadSingleAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CheckpointInfo?> GetAsync(Guid checkpointId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, conversation_id, container_id, workspace_root, label, mode, start_sequence, created_at
            FROM agent_checkpoints WHERE id = $id LIMIT 1
            """;
        command.Parameters.AddWithValue("$id", checkpointId.ToString());
        return await ReadSingleAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> GetLatestVersionSequenceAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(COALESCE(haven_sequence, rowid)), 0) FROM workspace_versions WHERE workspace_root = $root";
        command.Parameters.AddWithValue("$root", workspaceRoot);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is long value ? value : Convert.ToInt64(result ?? 0L);
    }

    public async Task<IReadOnlyList<WorkspaceRestoreEntry>> GetVersionsSinceAsync(string workspaceRoot, long sequence, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(haven_sequence, rowid) AS sequence, relative_path, kind, before_content, after_content
            FROM workspace_versions
            WHERE workspace_root = $root AND COALESCE(haven_sequence, rowid) > $sequence
            ORDER BY sequence ASC
            """;
        command.Parameters.AddWithValue("$root", workspaceRoot);
        command.Parameters.AddWithValue("$sequence", sequence);
        var entries = new List<WorkspaceRestoreEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new WorkspaceRestoreEntry(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4)));
        }
        return entries;
    }

    public async Task<WorkspaceRestoreEntry?> GetLatestVersionAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(haven_sequence, rowid) AS sequence, relative_path, kind, before_content, after_content
            FROM workspace_versions WHERE workspace_root = $root
            ORDER BY sequence DESC LIMIT 1
            """;
        command.Parameters.AddWithValue("$root", workspaceRoot);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new WorkspaceRestoreEntry(
            reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4));
    }

    private static async Task<CheckpointInfo?> ReadSingleAsync(System.Data.Common.DbCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new CheckpointInfo(
            Guid.Parse(reader.GetString(0)),
            reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
            reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
            reader.GetString(3),
            reader.GetString(4),
            (CheckpointMode)reader.GetInt32(5),
            reader.GetInt64(6),
            DateTimeOffset.Parse(reader.GetString(7)));
    }
}
