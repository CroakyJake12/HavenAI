/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/SqliteDatabase.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns ISqliteConnectionFactory, SqliteDatabase, Migration, Migrations. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>
/// Defines the sqlite connection factory contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface ISqliteConnectionFactory
{
    Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Represents sqlite database and keeps its related state and behavior together.
/// </summary>
public sealed class SqliteDatabase : IAppDatabase, ISqliteConnectionFactory
{
    /// <summary>
    /// Stores connection string locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _connectionString;

    public SqliteDatabase(IAppPaths paths)
    {
        SqliteProviderBootstrap.EnsureInitialized();
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            ForeignKeys = true
        }.ToString();
    }

    /// <summary>
    /// Performs open asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    /// <summary>
    /// Performs initialize asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, "CREATE TABLE IF NOT EXISTS schema_migrations(version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);", cancellationToken).ConfigureAwait(false);

        var current = await GetCurrentVersionAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        foreach (var migration in Migrations.All.Where(x => x.Version > current).OrderBy(x => x.Version))
        {
            await ExecuteAsync(connection, transaction, migration.Sql, cancellationToken).ConfigureAwait(false);
            await using var record = connection.CreateCommand();
            record.Transaction = transaction;
            record.CommandText = "INSERT INTO schema_migrations(version, applied_at) VALUES($version, $appliedAt);";
            record.Parameters.AddWithValue("$version", migration.Version);
            record.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
            await record.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves current version async for the current operation.
    /// </summary>
    private static async Task<int> GetCurrentVersionAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Runs execute async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Represents migration and keeps its related state and behavior together.
/// </summary>
internal sealed record Migration(int Version, string Sql);

/// <summary>
/// Represents migrations and keeps its related state and behavior together.
/// </summary>
internal static class Migrations
{
    /// <summary>
    /// Stores all locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public static readonly IReadOnlyList<Migration> All =
    [
        new(1, """
            CREATE TABLE conversations(
                id TEXT PRIMARY KEY,
                mode INTEGER NOT NULL,
                kind INTEGER NOT NULL,
                title TEXT NOT NULL,
                container_id TEXT NULL,
                lesson_id TEXT NULL,
                is_pinned INTEGER NOT NULL DEFAULT 0,
                is_temporary INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX ix_conversations_mode_updated ON conversations(mode, updated_at DESC);

            CREATE TABLE messages(
                id TEXT PRIMARY KEY,
                conversation_id TEXT NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
                role INTEGER NOT NULL,
                content TEXT NOT NULL,
                agent_name TEXT NULL,
                model_name TEXT NULL,
                metadata_json TEXT NULL,
                created_at TEXT NOT NULL
            );
            CREATE INDEX ix_messages_conversation_created ON messages(conversation_id, created_at);

            CREATE TABLE containers(
                id TEXT PRIMARY KEY,
                mode INTEGER NOT NULL,
                name TEXT NOT NULL,
                root_path TEXT NULL,
                context TEXT NOT NULL DEFAULT '',
                instructions TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX ix_containers_mode_name ON containers(mode, name);

            CREATE TABLE lessons(
                id TEXT PRIMARY KEY,
                subject_id TEXT NOT NULL REFERENCES containers(id) ON DELETE CASCADE,
                topic_group TEXT NOT NULL DEFAULT '',
                name TEXT NOT NULL,
                structure_json TEXT NOT NULL DEFAULT '{}',
                sort_order INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX ix_lessons_subject_sort ON lessons(subject_id, sort_order, name);
        """),
        new(2, """
            CREATE TABLE agents(
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                description TEXT NOT NULL,
                instructions TEXT NOT NULL,
                icon_key TEXT NOT NULL,
                preferred_model TEXT NOT NULL DEFAULT '',
                fallback_model TEXT NULL,
                detection_rules TEXT NOT NULL DEFAULT '',
                permissions_json TEXT NOT NULL DEFAULT '{}',
                is_built_in INTEGER NOT NULL DEFAULT 0,
                is_enabled INTEGER NOT NULL DEFAULT 1,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE plugins(
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                description TEXT NOT NULL,
                icon_key TEXT NOT NULL,
                instructions TEXT NOT NULL,
                capabilities_json TEXT NOT NULL DEFAULT '[]',
                conflicts_json TEXT NOT NULL DEFAULT '[]',
                persists INTEGER NOT NULL DEFAULT 0,
                is_built_in INTEGER NOT NULL DEFAULT 0,
                is_enabled INTEGER NOT NULL DEFAULT 1,
                updated_at TEXT NOT NULL
            );
        """),
        new(3, """
            CREATE TABLE automations(
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                mode INTEGER NOT NULL,
                instruction TEXT NOT NULL,
                schedule_kind INTEGER NOT NULL,
                schedule_json TEXT NOT NULL,
                next_run_at TEXT NULL,
                container_id TEXT NULL,
                is_enabled INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                lease_token TEXT NULL,
                lease_until TEXT NULL
            );
            CREATE INDEX ix_automations_due ON automations(is_enabled, next_run_at);

            CREATE TABLE automation_runs(
                id TEXT PRIMARY KEY,
                automation_id TEXT NOT NULL REFERENCES automations(id) ON DELETE CASCADE,
                status INTEGER NOT NULL,
                scheduled_for TEXT NOT NULL,
                started_at TEXT NULL,
                completed_at TEXT NULL,
                result TEXT NULL,
                error TEXT NULL,
                lease_token TEXT NULL
            );
            CREATE INDEX ix_automation_runs_recent ON automation_runs(automation_id, scheduled_for DESC);

            CREATE TABLE settings(
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE migration_log(
                key TEXT PRIMARY KEY,
                completed_at TEXT NOT NULL,
                note TEXT NULL
            );
        """),
        new(4, """
            ALTER TABLE conversations ADD COLUMN is_archived INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE conversations ADD COLUMN parent_conversation_id TEXT NULL;
            ALTER TABLE conversations ADD COLUMN compacted_at TEXT NULL;
            ALTER TABLE messages ADD COLUMN is_compacted INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE containers ADD COLUMN is_archived INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE plugins ADD COLUMN is_agentic INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE plugins ADD COLUMN allowed_modes_json TEXT NOT NULL DEFAULT '[]';

            CREATE INDEX ix_conversations_archive_mode_updated ON conversations(is_archived, mode, updated_at DESC);
            CREATE INDEX ix_messages_context ON messages(conversation_id, is_compacted, created_at);

            CREATE TABLE prompts(
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                description TEXT NOT NULL,
                icon_key TEXT NOT NULL,
                instructions TEXT NOT NULL,
                persists INTEGER NOT NULL DEFAULT 0,
                is_built_in INTEGER NOT NULL DEFAULT 0,
                is_enabled INTEGER NOT NULL DEFAULT 1,
                updated_at TEXT NOT NULL,
                is_agentic INTEGER NOT NULL DEFAULT 0,
                allowed_modes_json TEXT NOT NULL DEFAULT '[]'
            );

            CREATE TABLE conversation_context(
                id TEXT PRIMARY KEY,
                conversation_id TEXT NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
                kind INTEGER NOT NULL,
                title TEXT NOT NULL,
                content TEXT NOT NULL,
                evidence TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL
            );
            CREATE INDEX ix_conversation_context_created ON conversation_context(conversation_id, created_at);

            CREATE TABLE macros(
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                description TEXT NOT NULL,
                instruction TEXT NOT NULL,
                container_id TEXT NULL REFERENCES containers(id) ON DELETE SET NULL,
                is_enabled INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX ix_macros_container_name ON macros(container_id, name);

            CREATE TABLE workspace_versions(
                id TEXT PRIMARY KEY,
                conversation_id TEXT NULL REFERENCES conversations(id) ON DELETE SET NULL,
                container_id TEXT NULL REFERENCES containers(id) ON DELETE SET NULL,
                workspace_root TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                kind INTEGER NOT NULL,
                before_content TEXT NOT NULL,
                after_content TEXT NOT NULL,
                summary TEXT NOT NULL,
                lines_added INTEGER NOT NULL DEFAULT 0,
                lines_removed INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL
            );
            CREATE INDEX ix_workspace_versions_file ON workspace_versions(container_id, relative_path, created_at DESC);

            CREATE TABLE decisions(
                id TEXT PRIMARY KEY,
                container_id TEXT NOT NULL REFERENCES containers(id) ON DELETE CASCADE,
                title TEXT NOT NULL,
                decision_text TEXT NOT NULL,
                alternatives TEXT NOT NULL,
                reasoning TEXT NOT NULL,
                evidence TEXT NOT NULL,
                consequences TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX ix_decisions_container_updated ON decisions(container_id, updated_at DESC);

            UPDATE plugins
               SET is_enabled=0
             WHERE is_built_in=1
               AND name NOT IN ('Agent','Goal','BrowserUse','ComputerUse','WebSearch','DuoMode','Automate','Test','Macro');
        """),
        new(6, """
            CREATE TABLE training_runs(
                id TEXT PRIMARY KEY,
                task_prompt TEXT NOT NULL,
                workspace_path TEXT NOT NULL,
                snapshot_path TEXT NOT NULL,
                model_name TEXT NOT NULL,
                max_attempts INTEGER NOT NULL DEFAULT 5,
                duration_minutes INTEGER NOT NULL DEFAULT 10,
                file_permission INTEGER NOT NULL DEFAULT 0,
                command_permission INTEGER NOT NULL DEFAULT 0,
                browser_permission INTEGER NOT NULL DEFAULT 0,
                allow_desktop_tools INTEGER NOT NULL DEFAULT 0,
                allow_file_system_writes INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL,
                completed_at TEXT NULL
            );

            CREATE TABLE training_attempts(
                id TEXT PRIMARY KEY,
                training_run_id TEXT NOT NULL REFERENCES training_runs(id) ON DELETE CASCADE,
                attempt_number INTEGER NOT NULL,
                report_markdown TEXT NOT NULL,
                feedback TEXT NULL,
                action_log TEXT NOT NULL,
                succeeded INTEGER NOT NULL DEFAULT 0,
                duration_ms INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL
            );
            CREATE INDEX ix_training_attempts_run ON training_attempts(training_run_id, attempt_number);
        """),
        new(7, PlannerMigration.Sql + FeatureMigration.Sql),
        new(8, """
            CREATE TABLE mode_definitions(
                id TEXT PRIMARY KEY,
                key TEXT NOT NULL UNIQUE,
                name TEXT NOT NULL,
                description TEXT NOT NULL DEFAULT '',
                icon_key TEXT NOT NULL DEFAULT '',
                base_mode INTEGER NOT NULL DEFAULT 0,
                surfaces_json TEXT NOT NULL DEFAULT '[]',
                tool_allowlist_json TEXT NOT NULL DEFAULT '[]',
                tool_denylist_json TEXT NOT NULL DEFAULT '[]',
                plugins_json TEXT NOT NULL DEFAULT '[]',
                system_prompt_suffix TEXT NOT NULL DEFAULT '',
                source INTEGER NOT NULL DEFAULT 0,
                install_state INTEGER NOT NULL DEFAULT 0,
                author TEXT NOT NULL DEFAULT '',
                version TEXT NOT NULL DEFAULT '1.0.0',
                tags_json TEXT NOT NULL DEFAULT '[]',
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                is_enabled INTEGER NOT NULL DEFAULT 1
            );
            CREATE INDEX ix_mode_definitions_key ON mode_definitions(key);
            CREATE INDEX ix_mode_definitions_source ON mode_definitions(source);

            CREATE TABLE mode_versions(
                id TEXT PRIMARY KEY,
                mode_id TEXT NOT NULL REFERENCES mode_definitions(id) ON DELETE CASCADE,
                major INTEGER NOT NULL DEFAULT 1,
                minor INTEGER NOT NULL DEFAULT 0,
                patch INTEGER NOT NULL DEFAULT 0,
                manifest_json TEXT NOT NULL DEFAULT '{}',
                changelog TEXT NOT NULL DEFAULT '',
                published_at TEXT NOT NULL
            );
            CREATE INDEX ix_mode_versions_mode ON mode_versions(mode_id, major DESC, minor DESC, patch DESC);

            CREATE TABLE mode_permission_grants(
                id TEXT PRIMARY KEY,
                mode_id TEXT NOT NULL REFERENCES mode_definitions(id) ON DELETE CASCADE,
                file_permission INTEGER NOT NULL DEFAULT 0,
                command_permission INTEGER NOT NULL DEFAULT 0,
                browser_permission INTEGER NOT NULL DEFAULT 0,
                allow_desktop_tools INTEGER NOT NULL DEFAULT 0,
                allow_file_system_writes INTEGER NOT NULL DEFAULT 1,
                granted_at TEXT NOT NULL
            );
            CREATE INDEX ix_mode_grants_mode ON mode_permission_grants(mode_id);

            CREATE TABLE mode_pins(
                id TEXT PRIMARY KEY,
                mode_id TEXT NOT NULL REFERENCES mode_definitions(id) ON DELETE CASCADE,
                sort_order INTEGER NOT NULL DEFAULT 0,
                pinned_at TEXT NOT NULL
            );
            CREATE UNIQUE INDEX ix_mode_pins_mode ON mode_pins(mode_id);

            CREATE TABLE mode_usage(
                id TEXT PRIMARY KEY,
                mode_id TEXT NOT NULL REFERENCES mode_definitions(id) ON DELETE CASCADE,
                date TEXT NOT NULL,
                turn_count INTEGER NOT NULL DEFAULT 0,
                completion_count INTEGER NOT NULL DEFAULT 0,
                total_duration_ms INTEGER NOT NULL DEFAULT 0
            );
            CREATE UNIQUE INDEX ix_mode_usage_mode_date ON mode_usage(mode_id, date);

            CREATE TABLE surface_runs(
                id TEXT PRIMARY KEY,
                conversation_id TEXT NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
                surface INTEGER NOT NULL,
                surface_key TEXT NOT NULL DEFAULT '',
                target_mode_key TEXT NULL,
                started_at TEXT NOT NULL,
                completed_at TEXT NULL,
                succeeded INTEGER NOT NULL DEFAULT 1
            );
            CREATE INDEX ix_surface_runs_conversation ON surface_runs(conversation_id, started_at DESC);

            CREATE TABLE activity_events(
                id TEXT PRIMARY KEY,
                kind INTEGER NOT NULL,
                conversation_id TEXT NULL REFERENCES conversations(id) ON DELETE SET NULL,
                mode_id TEXT NULL REFERENCES mode_definitions(id) ON DELETE SET NULL,
                summary TEXT NOT NULL DEFAULT '',
                detail_json TEXT NOT NULL DEFAULT '{}',
                timestamp TEXT NOT NULL
            );
            CREATE INDEX ix_activity_events_timestamp ON activity_events(timestamp DESC);
            CREATE INDEX ix_activity_events_kind ON activity_events(kind, timestamp DESC);

            CREATE TABLE conversation_moves(
                id TEXT PRIMARY KEY,
                conversation_id TEXT NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
                from_mode_id TEXT NULL REFERENCES mode_definitions(id) ON DELETE SET NULL,
                to_mode_id TEXT NULL REFERENCES mode_definitions(id) ON DELETE SET NULL,
                from_placement INTEGER NOT NULL DEFAULT 0,
                to_placement INTEGER NOT NULL DEFAULT 0,
                reason TEXT NOT NULL DEFAULT '',
                moved_at TEXT NOT NULL
            );
            CREATE INDEX ix_conversation_moves_conversation ON conversation_moves(conversation_id, moved_at DESC);
        """),
        new(11, """
            CREATE TABLE capabilities(
                id TEXT PRIMARY KEY,
                key TEXT NOT NULL UNIQUE,
                name TEXT NOT NULL,
                description TEXT NOT NULL,
                owner_app_key TEXT NOT NULL,
                icon_key TEXT NOT NULL,
                instructions TEXT NOT NULL,
                implementation_key TEXT NOT NULL,
                semantic_actions_json TEXT NOT NULL DEFAULT '[]',
                platforms INTEGER NOT NULL DEFAULT 0,
                risk_class INTEGER NOT NULL DEFAULT 0,
                availability INTEGER NOT NULL DEFAULT 0,
                dependencies_json TEXT NOT NULL DEFAULT '[]',
                provider_id TEXT NOT NULL,
                is_attachable INTEGER NOT NULL DEFAULT 1,
                is_agent_usable INTEGER NOT NULL DEFAULT 1,
                is_built_in INTEGER NOT NULL DEFAULT 0,
                is_enabled INTEGER NOT NULL DEFAULT 1,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX ix_capabilities_owner_name ON capabilities(owner_app_key,name);
            CREATE INDEX ix_capabilities_platform_enabled ON capabilities(platforms,is_enabled);
        """),
        new(12, """
            CREATE TABLE genui_templates(
                id TEXT PRIMARY KEY,
                key TEXT NOT NULL UNIQUE,
                version TEXT NOT NULL,
                name TEXT NOT NULL,
                description TEXT NOT NULL,
                category TEXT NOT NULL,
                tags_json TEXT NOT NULL DEFAULT '[]',
                canonical_implementation TEXT NOT NULL,
                scale INTEGER NOT NULL,
                recommended_apps_json TEXT NOT NULL DEFAULT '[]',
                compatible_apps_json TEXT NOT NULL DEFAULT '[]',
                inputs_json TEXT NOT NULL DEFAULT '[]',
                outputs_json TEXT NOT NULL DEFAULT '[]',
                emitted_events_json TEXT NOT NULL DEFAULT '[]',
                configurable_properties_json TEXT NOT NULL DEFAULT '[]',
                data_requirements_json TEXT NOT NULL DEFAULT '[]',
                supported_interactions_json TEXT NOT NULL DEFAULT '[]',
                havenui_primitives_json TEXT NOT NULL DEFAULT '[]',
                app_services_json TEXT NOT NULL DEFAULT '[]',
                capabilities_json TEXT NOT NULL DEFAULT '[]',
                model_capabilities_json TEXT NOT NULL DEFAULT '[]',
                requires_network INTEGER NOT NULL DEFAULT 0,
                supports_offline INTEGER NOT NULL DEFAULT 1,
                platforms INTEGER NOT NULL,
                accessibility_summary TEXT NOT NULL,
                supports_persistence INTEGER NOT NULL DEFAULT 1,
                supports_thread_scope INTEGER NOT NULL DEFAULT 1,
                supports_user_apps INTEGER NOT NULL DEFAULT 1,
                supports_mini_apps INTEGER NOT NULL DEFAULT 0,
                supports_embedding INTEGER NOT NULL DEFAULT 1,
                agent_interaction INTEGER NOT NULL,
                deterministic_without_model INTEGER NOT NULL DEFAULT 0,
                state_ownership INTEGER NOT NULL,
                maturity INTEGER NOT NULL DEFAULT 0,
                is_built_in INTEGER NOT NULL DEFAULT 0,
                is_enabled INTEGER NOT NULL DEFAULT 1,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX ix_genui_templates_category_name ON genui_templates(category,name);
            CREATE INDEX ix_genui_templates_platform_enabled ON genui_templates(platforms,is_enabled);
        """),
        new(13, """
            CREATE TABLE reusable_tasks(
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                description TEXT NOT NULL,
                instruction TEXT NOT NULL,
                container_id TEXT NULL REFERENCES containers(id) ON DELETE SET NULL,
                is_enabled INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            INSERT OR IGNORE INTO reusable_tasks(id,name,description,instruction,container_id,is_enabled,created_at,updated_at)
            SELECT id,name,description,instruction,container_id,is_enabled,created_at,updated_at FROM macros;
            DROP TABLE macros;
            CREATE INDEX ix_reusable_tasks_container_name ON reusable_tasks(container_id,name);
        """),
        new(14, """
            INSERT OR IGNORE INTO capabilities(
                id,key,name,description,owner_app_key,icon_key,instructions,implementation_key,
                semantic_actions_json,platforms,risk_class,availability,dependencies_json,provider_id,
                is_attachable,is_agent_usable,is_built_in,is_enabled,updated_at)
            SELECT
                id,
                'imported-plugin-' || lower(substr(replace(id,'-',''),1,12)),
                name,
                description,
                'general',
                icon_key,
                instructions,
                'retired-plugin-metadata',
                '[]',
                3,
                0,
                4,
                '[]',
                'legacy-plugin',
                0,
                0,
                0,
                0,
                updated_at
            FROM plugins
            WHERE is_built_in=0;
            ALTER TABLE mode_definitions RENAME COLUMN plugins_json TO capabilities_json;
            DROP TABLE plugins;
        """),
        new(15, """
            ALTER TABLE reusable_tasks ADD COLUMN graph_json TEXT NULL;
        """),
        new(16, """
            CREATE TABLE conversation_safety_flags(
                conversation_id TEXT NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
                event_id TEXT NOT NULL,
                source TEXT NOT NULL,
                category TEXT NOT NULL,
                evidence_hash TEXT NOT NULL,
                confirmed_at TEXT NOT NULL,
                PRIMARY KEY(conversation_id,event_id)
            );
            CREATE INDEX ix_conversation_safety_flags_confirmed
                ON conversation_safety_flags(conversation_id,confirmed_at);

            CREATE TABLE conversation_safety_state(
                conversation_id TEXT PRIMARY KEY REFERENCES conversations(id) ON DELETE CASCADE,
                confirmed_count INTEGER NOT NULL DEFAULT 0 CHECK(confirmed_count>=0),
                state INTEGER NOT NULL DEFAULT 0 CHECK(state IN(0,1)),
                locked_at TEXT NULL,
                version INTEGER NOT NULL DEFAULT 0 CHECK(version>=0),
                updated_at TEXT NOT NULL
            );

            CREATE TRIGGER conversation_safety_block_conversation_update
            BEFORE UPDATE ON conversations
            WHEN EXISTS(SELECT 1 FROM conversation_safety_state WHERE conversation_id=OLD.id AND state=1)
            BEGIN SELECT RAISE(ABORT,'CONVERSATION_SAFETY_LOCKED'); END;
            CREATE TRIGGER conversation_safety_block_message_insert
            BEFORE INSERT ON messages
            WHEN EXISTS(SELECT 1 FROM conversation_safety_state WHERE conversation_id=NEW.conversation_id AND state=1)
            BEGIN SELECT RAISE(ABORT,'CONVERSATION_SAFETY_LOCKED'); END;
            CREATE TRIGGER conversation_safety_block_message_update
            BEFORE UPDATE ON messages
            WHEN EXISTS(SELECT 1 FROM conversation_safety_state WHERE conversation_id=OLD.conversation_id AND state=1)
            BEGIN SELECT RAISE(ABORT,'CONVERSATION_SAFETY_LOCKED'); END;
            CREATE TRIGGER conversation_safety_block_context_insert
            BEFORE INSERT ON conversation_context
            WHEN EXISTS(SELECT 1 FROM conversation_safety_state WHERE conversation_id=NEW.conversation_id AND state=1)
            BEGIN SELECT RAISE(ABORT,'CONVERSATION_SAFETY_LOCKED'); END;
            CREATE TRIGGER conversation_safety_block_context_update
            BEFORE UPDATE ON conversation_context
            WHEN EXISTS(SELECT 1 FROM conversation_safety_state WHERE conversation_id=OLD.conversation_id AND state=1)
            BEGIN SELECT RAISE(ABORT,'CONVERSATION_SAFETY_LOCKED'); END;
        """),
        new(17, """
            CREATE TABLE IF NOT EXISTS knowledge_records(
                id TEXT PRIMARY KEY,
                category INTEGER NOT NULL,
                topic TEXT NOT NULL,
                title TEXT NOT NULL,
                summary TEXT NOT NULL,
                privacy_class INTEGER NOT NULL,
                confidence REAL NOT NULL,
                is_pinned INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                expires_at TEXT NULL,
                learned_because TEXT NOT NULL,
                sources_json TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_knowledge_records_category ON knowledge_records(category,updated_at);

            CREATE TABLE IF NOT EXISTS knowledge_record_details(
                id TEXT PRIMARY KEY REFERENCES knowledge_records(id) ON DELETE CASCADE,
                freshness INTEGER NOT NULL DEFAULT 0,
                last_confirmed_at TEXT NULL,
                scope TEXT NOT NULL DEFAULT 'global',
                status INTEGER NOT NULL DEFAULT 0,
                origin INTEGER NOT NULL DEFAULT 0,
                user_correction TEXT NULL,
                supersedes_id TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_knowledge_details_status ON knowledge_record_details(status,last_confirmed_at);

            CREATE TABLE IF NOT EXISTS knowledge_rejections(
                fingerprint TEXT PRIMARY KEY,
                record_id TEXT NULL,
                reason TEXT NULL,
                rejected_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS api_bank_records(
                id TEXT PRIMARY KEY,
                application TEXT NOT NULL,
                api_name TEXT NOT NULL,
                version TEXT NOT NULL,
                documentation_url TEXT NOT NULL,
                actions_json TEXT NOT NULL,
                authentication TEXT NOT NULL,
                requires_internet INTEGER NOT NULL,
                requires_credentials INTEGER NOT NULL,
                cost_per_request TEXT NULL,
                alternatives_json TEXT NOT NULL,
                deprecation TEXT NULL,
                last_checked_at TEXT NOT NULL,
                documentation_hash TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_api_bank_name ON api_bank_records(application,api_name);

            CREATE TABLE IF NOT EXISTS api_bank_details(
                id TEXT PRIMARY KEY REFERENCES api_bank_records(id) ON DELETE CASCADE,
                inputs_json TEXT NOT NULL DEFAULT '[]',
                outputs_json TEXT NOT NULL DEFAULT '[]',
                scopes_json TEXT NOT NULL DEFAULT '[]',
                rate_limits TEXT NOT NULL DEFAULT '',
                pricing TEXT NOT NULL DEFAULT '',
                capability_notes TEXT NOT NULL DEFAULT '',
                limitations TEXT NOT NULL DEFAULT '',
                offline_queue_policy TEXT NOT NULL DEFAULT '',
                is_pinned INTEGER NOT NULL DEFAULT 0,
                source_url TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS background_learning_settings(
                id INTEGER PRIMARY KEY CHECK(id=1),
                global_enabled INTEGER NOT NULL DEFAULT 1,
                mode INTEGER NOT NULL DEFAULT 1,
                disabled_categories_json TEXT NOT NULL DEFAULT '[]',
                updated_at TEXT NOT NULL
            );
            INSERT OR IGNORE INTO background_learning_settings(id,global_enabled,mode,disabled_categories_json,updated_at)
            VALUES(1,1,1,'[]','2026-08-21T00:00:00+00:00');

            CREATE TABLE IF NOT EXISTS background_learning_tasks(
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                category INTEGER NOT NULL,
                priority INTEGER NOT NULL,
                status INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                source TEXT NOT NULL,
                started_at TEXT NULL,
                last_run_at TEXT NULL,
                completed_at TEXT NULL,
                result TEXT NULL,
                error TEXT NULL,
                requires_network INTEGER NOT NULL DEFAULT 0,
                requires_model INTEGER NOT NULL DEFAULT 1
            );
            CREATE INDEX IF NOT EXISTS ix_background_learning_tasks_status
                ON background_learning_tasks(status,created_at DESC);
        """),
        new(18, """
            CREATE TABLE genui_apps(instance_id TEXT PRIMARY KEY, app_id TEXT NOT NULL, thread_id TEXT NOT NULL, definition_json TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE INDEX ix_genui_apps_updated ON genui_apps(updated_at DESC);
            CREATE INDEX ix_genui_apps_thread ON genui_apps(thread_id,updated_at DESC);
        """),
        new(19, """
            ALTER TABLE genui_apps ADD COLUMN is_pinned INTEGER NOT NULL DEFAULT 0;
            CREATE INDEX ix_genui_apps_pinned_updated ON genui_apps(is_pinned,updated_at DESC);
        """),
        new(20, """
             CREATE TABLE external_connections(
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                provider_key TEXT NOT NULL,
                kind INTEGER NOT NULL,
                preset_key TEXT NOT NULL DEFAULT '',
                is_enabled INTEGER NOT NULL DEFAULT 1,
                state INTEGER NOT NULL DEFAULT 0,
                status TEXT NOT NULL DEFAULT '',
                configuration_json TEXT NOT NULL DEFAULT '{}',
                server_name TEXT NULL,
                server_version TEXT NULL,
                protocol_version TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
             CREATE INDEX ix_external_connections_provider ON external_connections(provider_key,is_enabled);
             CREATE INDEX ix_external_connections_preset ON external_connections(preset_key,is_enabled);
        """)
    ];
}
