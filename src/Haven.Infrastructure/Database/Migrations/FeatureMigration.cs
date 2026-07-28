/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/FeatureMigration.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns FeatureMigration. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

namespace Haven.Infrastructure;

/// <summary>Non-planner schema introduced with the integrated surfaces in migration 7.</summary>
public static class FeatureMigration
{
    /// <summary>
    /// Stores sql locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public const string Sql = """
        CREATE INDEX ix_conversations_scope_updated
            ON conversations(mode,kind,container_id,lesson_id,is_archived,is_temporary,updated_at DESC);

        CREATE TABLE container_resources(
            id TEXT PRIMARY KEY,
            container_id TEXT NOT NULL REFERENCES containers(id) ON DELETE CASCADE,
            name TEXT NOT NULL,
            stored_name TEXT NOT NULL,
            media_type TEXT NOT NULL,
            kind INTEGER NOT NULL,
            size_bytes INTEGER NOT NULL,
            sha256 TEXT NOT NULL,
            created_at TEXT NOT NULL,
            CHECK(size_bytes >= 0)
        );
        CREATE UNIQUE INDEX ux_container_resources_hash ON container_resources(container_id,sha256);
        CREATE INDEX ix_container_resources_created ON container_resources(container_id,created_at DESC);

        CREATE TABLE call_sessions(
            id TEXT PRIMARY KEY,
            conversation_id TEXT NOT NULL UNIQUE REFERENCES conversations(id) ON DELETE CASCADE,
            model_name TEXT NOT NULL,
            input_device_id TEXT NULL,
            output_device_id TEXT NULL,
            voice_name TEXT NULL,
            input_mode INTEGER NOT NULL DEFAULT 0 CHECK(input_mode IN (0,1)),
            used_screen_share INTEGER NOT NULL DEFAULT 0 CHECK(used_screen_share IN (0,1)),
            status INTEGER NOT NULL DEFAULT 0 CHECK(status IN (0,1,2,3)),
            started_at TEXT NOT NULL,
            ended_at TEXT NULL,
            error TEXT NULL
        );
        CREATE INDEX ix_call_sessions_started_at ON call_sessions(started_at DESC);
        CREATE INDEX ix_call_sessions_status ON call_sessions(status,started_at DESC);

        ALTER TABLE plugins ADD COLUMN dashboard_tiles_json TEXT NOT NULL DEFAULT '[]';

        -- Quick Study chats are intentionally outside subjects and lessons.
        UPDATE conversations
           SET container_id=NULL,kind=1
         WHERE mode=1 AND lesson_id IS NULL;

        -- Existing subjects also receive a usable default lesson without changing IDs.
        INSERT INTO lessons(id,subject_id,topic_group,name,structure_json,sort_order,created_at,updated_at)
        SELECT lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4' ||
               substr(lower(hex(randomblob(2))),2) || '-' ||
               substr('89ab',abs(random()) % 4 + 1,1) || substr(lower(hex(randomblob(2))),2) || '-' ||
               lower(hex(randomblob(6))),
               containers.id,'','General','{}',0,
               strftime('%Y-%m-%dT%H:%M:%fZ','now'),strftime('%Y-%m-%dT%H:%M:%fZ','now')
          FROM containers
         WHERE mode=1
           AND NOT EXISTS(SELECT 1 FROM lessons WHERE lessons.subject_id=containers.id);
        """;
}
