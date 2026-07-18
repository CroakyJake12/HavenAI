/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/ConversationProductionDatabase.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns ConversationProductionDatabase, ConversationProductionSchema. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>
/// Represents conversation production database and keeps its related state and behavior together.
/// </summary>
public sealed class ConversationProductionDatabase : IAppDatabase
{
    /// <summary>
    /// Stores database locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SqliteDatabase _database;
    /// <summary>
    /// Stores maintenance locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IDatabaseMaintenance? _maintenance;
    /// <summary>
    /// Stores restore locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IDatabaseRestoreService? _restore;

    // Focused repository tests historically construct this wrapper around an isolated
    // temporary database. Production DI selects the longest constructor and applies the
    // complete restore, backup and integrity gate.
    public ConversationProductionDatabase(SqliteDatabase database)
    {
        _database = database;
    }

    public ConversationProductionDatabase(
        SqliteDatabase database,
        IDatabaseMaintenance maintenance)
    {
        _database = database;
        _maintenance = maintenance;
    }

    public ConversationProductionDatabase(
        SqliteDatabase database,
        IDatabaseMaintenance maintenance,
        IDatabaseRestoreService restore)
    {
        _database = database;
        _maintenance = maintenance;
        _restore = restore;
    }

    /// <summary>
    /// Performs initialize async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        // A restore is intentionally applied before SqliteDatabase opens its first
        // connection. The restore service re-verifies the backup, creates an emergency
        // copy of the current data and performs an atomic swap or rollback.
        if (_restore is not null)
            await _restore.ApplyPendingRestoreAsync(cancellationToken).ConfigureAwait(false);

        if (_maintenance is not null)
        {
            // Version 10 is the highest additive continuation schema currently used.
            // One verified backup protects the base, conversation and retrieval migrations.
            await _maintenance.PrepareForMigrationAsync(10, cancellationToken).ConfigureAwait(false);
        }

        await _database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await ConversationProductionSchema.EnsureAsync(_database, cancellationToken).ConfigureAwait(false);

        if (_maintenance is not null)
        {
            var health = await _maintenance.VerifyIntegrityAsync(cancellationToken).ConfigureAwait(false);
            if (!health.IsHealthy)
                throw new InvalidDataException("The Haven database failed its post-migration integrity check. Startup was stopped to protect the data.");
        }
    }
}

/// <summary>
/// Represents conversation production schema and keeps its related state and behavior together.
/// </summary>
internal static class ConversationProductionSchema
{
    /// <summary>
    /// Stores gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>
    /// Performs ensure async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public static async Task EnsureAsync(ISqliteConnectionFactory factory, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The SQL is deliberately idempotent. Running it for every connection factory
            // avoids a process-global cache causing a second development/test database to
            // be treated as migrated when only the first database was initialized.
            await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = Sql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await using var migration = connection.CreateCommand();
            migration.Transaction = transaction;
            migration.CommandText = "INSERT OR IGNORE INTO schema_migrations(version, applied_at) VALUES(9, $appliedAt);";
            migration.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
            await migration.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Stores sql locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    internal const string Sql = """
        CREATE TABLE IF NOT EXISTS conversation_branches(
            id TEXT PRIMARY KEY,
            conversation_id TEXT NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
            parent_branch_id TEXT NULL REFERENCES conversation_branches(id) ON DELETE SET NULL,
            forked_from_message_id TEXT NULL REFERENCES messages(id) ON DELETE SET NULL,
            name TEXT NOT NULL,
            reason INTEGER NOT NULL DEFAULT 0,
            is_current INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_conversation_branches_conversation ON conversation_branches(conversation_id, created_at);
        CREATE UNIQUE INDEX IF NOT EXISTS ix_conversation_branches_current ON conversation_branches(conversation_id) WHERE is_current=1;

        CREATE TABLE IF NOT EXISTS conversation_branch_messages(
            branch_id TEXT NOT NULL REFERENCES conversation_branches(id) ON DELETE CASCADE,
            message_id TEXT NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
            sequence INTEGER NOT NULL,
            PRIMARY KEY(branch_id, message_id)
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ix_branch_messages_sequence ON conversation_branch_messages(branch_id, sequence);

        CREATE TABLE IF NOT EXISTS conversation_turns(
            id TEXT PRIMARY KEY,
            conversation_id TEXT NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
            branch_id TEXT NOT NULL REFERENCES conversation_branches(id) ON DELETE CASCADE,
            sequence INTEGER NOT NULL,
            user_message_id TEXT NULL REFERENCES messages(id) ON DELETE SET NULL,
            assistant_message_id TEXT NULL REFERENCES messages(id) ON DELETE SET NULL,
            created_at TEXT NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ix_conversation_turns_sequence ON conversation_turns(branch_id, sequence);

        CREATE TABLE IF NOT EXISTS message_versions(
            id TEXT PRIMARY KEY,
            message_id TEXT NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
            branch_id TEXT NOT NULL REFERENCES conversation_branches(id) ON DELETE CASCADE,
            version_number INTEGER NOT NULL,
            kind INTEGER NOT NULL,
            content TEXT NOT NULL,
            metadata_json TEXT NULL,
            is_current INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ix_message_versions_number ON message_versions(message_id, branch_id, version_number);
        CREATE UNIQUE INDEX IF NOT EXISTS ix_message_versions_current ON message_versions(message_id, branch_id) WHERE is_current=1;

        CREATE TABLE IF NOT EXISTS message_attachments(
            id TEXT PRIMARY KEY,
            conversation_id TEXT NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
            message_id TEXT NULL REFERENCES messages(id) ON DELETE CASCADE,
            branch_id TEXT NULL REFERENCES conversation_branches(id) ON DELETE CASCADE,
            original_name TEXT NOT NULL,
            stored_name TEXT NOT NULL,
            media_type TEXT NOT NULL,
            kind INTEGER NOT NULL,
            size_bytes INTEGER NOT NULL,
            sha256 TEXT NOT NULL,
            processing_state INTEGER NOT NULL DEFAULT 0,
            analysis_method INTEGER NOT NULL DEFAULT 0,
            extracted_text TEXT NOT NULL DEFAULT '',
            metadata_json TEXT NOT NULL DEFAULT '{}',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_message_attachments_conversation ON message_attachments(conversation_id, created_at);
        CREATE INDEX IF NOT EXISTS ix_message_attachments_hash ON message_attachments(sha256);

        CREATE TABLE IF NOT EXISTS response_usage(
            id TEXT PRIMARY KEY,
            conversation_id TEXT NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
            message_id TEXT NULL REFERENCES messages(id) ON DELETE SET NULL,
            provider_id TEXT NOT NULL,
            model_name TEXT NOT NULL,
            input_tokens INTEGER NULL,
            output_tokens INTEGER NULL,
            cached_tokens INTEGER NULL,
            reasoning_tokens INTEGER NULL,
            cost TEXT NULL,
            currency TEXT NULL,
            measurement INTEGER NOT NULL,
            latency_ms INTEGER NOT NULL,
            recorded_at TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_response_usage_conversation ON response_usage(conversation_id, recorded_at);

        CREATE TABLE IF NOT EXISTS conversation_drafts(
            conversation_id TEXT NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
            branch_id TEXT NULL REFERENCES conversation_branches(id) ON DELETE CASCADE,
            content TEXT NOT NULL,
            attachment_ids_json TEXT NOT NULL DEFAULT '[]',
            updated_at TEXT NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ix_conversation_drafts_scope
            ON conversation_drafts(conversation_id, COALESCE(branch_id, ''));

        CREATE TABLE IF NOT EXISTS message_bookmarks(
            id TEXT PRIMARY KEY,
            conversation_id TEXT NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
            message_id TEXT NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
            label TEXT NOT NULL DEFAULT '',
            note TEXT NOT NULL DEFAULT '',
            created_at TEXT NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ix_message_bookmarks_message ON message_bookmarks(message_id);

        CREATE TABLE IF NOT EXISTS shared_sessions(
            id TEXT PRIMARY KEY,
            conversation_id TEXT NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
            token_hash TEXT NOT NULL UNIQUE,
            bind_address TEXT NOT NULL,
            port INTEGER NOT NULL,
            state INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            expires_at TEXT NOT NULL,
            stopped_at TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_shared_sessions_active ON shared_sessions(conversation_id, state, expires_at);
        """;
}
