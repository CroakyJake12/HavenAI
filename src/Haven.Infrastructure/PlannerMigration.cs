namespace Haven.Infrastructure;

/// <summary>Schema fragment to add to the application's version 7 SQLite migration.</summary>
public static class PlannerMigration
{
    public const int Version = 7;

    public const string Sql = """
        CREATE TABLE planner_collections(
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            sort_order INTEGER NOT NULL DEFAULT 0,
            is_archived INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );
        CREATE INDEX ix_planner_collections_active_sort ON planner_collections(is_archived, sort_order, name);

        CREATE TABLE planner_tasks(
            id TEXT PRIMARY KEY,
            collection_id TEXT NOT NULL REFERENCES planner_collections(id) ON DELETE CASCADE,
            parent_task_id TEXT NULL REFERENCES planner_tasks(id) ON DELETE SET NULL,
            title TEXT NOT NULL,
            notes TEXT NOT NULL DEFAULT '',
            priority INTEGER NOT NULL DEFAULT 0,
            status INTEGER NOT NULL DEFAULT 0,
            tags_json TEXT NOT NULL DEFAULT '[]',
            estimated_minutes INTEGER NULL,
            starts_at TEXT NULL,
            due_at TEXT NULL,
            recurrence_rule TEXT NULL,
            reminder_at TEXT NULL,
            completed_at TEXT NULL,
            sort_order INTEGER NOT NULL DEFAULT 0,
            time_zone_id TEXT NOT NULL DEFAULT 'UTC',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            CHECK(estimated_minutes IS NULL OR estimated_minutes >= 0)
        );
        CREATE INDEX ix_planner_tasks_collection_status_sort ON planner_tasks(collection_id, status, sort_order, due_at);
        CREATE INDEX ix_planner_tasks_due ON planner_tasks(status, due_at);
        CREATE INDEX ix_planner_tasks_parent ON planner_tasks(parent_task_id, sort_order);

        CREATE TABLE planner_task_completions(
            id TEXT PRIMARY KEY,
            task_id TEXT NOT NULL REFERENCES planner_tasks(id) ON DELETE CASCADE,
            completed_at TEXT NOT NULL,
            occurrence_due_at TEXT NULL
        );
        CREATE INDEX ix_planner_task_completions_task ON planner_task_completions(task_id, completed_at DESC);

        CREATE TABLE calendar_accounts(
            id TEXT PRIMARY KEY,
            provider INTEGER NOT NULL,
            display_name TEXT NOT NULL,
            account_identifier TEXT NOT NULL,
            status INTEGER NOT NULL DEFAULT 0,
            status_message TEXT NULL,
            last_synced_at TEXT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            UNIQUE(provider, account_identifier)
        );

        CREATE TABLE planner_calendars(
            id TEXT PRIMARY KEY,
            account_id TEXT NULL REFERENCES calendar_accounts(id) ON DELETE CASCADE,
            provider INTEGER NOT NULL DEFAULT 0,
            provider_calendar_id TEXT NOT NULL DEFAULT '',
            name TEXT NOT NULL,
            color TEXT NOT NULL DEFAULT '#74E5C1',
            permission INTEGER NOT NULL DEFAULT 0,
            is_visible INTEGER NOT NULL DEFAULT 1,
            updated_at TEXT NOT NULL,
            UNIQUE(account_id, provider_calendar_id)
        );
        CREATE INDEX ix_planner_calendars_account ON planner_calendars(account_id, is_visible);

        CREATE TABLE planner_events(
            id TEXT PRIMARY KEY,
            calendar_id TEXT NOT NULL REFERENCES planner_calendars(id) ON DELETE CASCADE,
            title TEXT NOT NULL,
            notes TEXT NOT NULL DEFAULT '',
            location TEXT NOT NULL DEFAULT '',
            starts_at TEXT NOT NULL,
            ends_at TEXT NOT NULL,
            is_all_day INTEGER NOT NULL DEFAULT 0,
            recurrence_rule TEXT NULL,
            reminder_at TEXT NULL,
            is_read_only INTEGER NOT NULL DEFAULT 0,
            provider_event_id TEXT NULL,
            provider_etag TEXT NULL,
            time_zone_id TEXT NOT NULL DEFAULT 'UTC',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            deleted_at TEXT NULL,
            CHECK(ends_at > starts_at)
        );
        CREATE INDEX ix_planner_events_range ON planner_events(calendar_id, starts_at, ends_at) WHERE deleted_at IS NULL;
        CREATE UNIQUE INDEX ux_planner_events_provider ON planner_events(calendar_id, provider_event_id) WHERE provider_event_id IS NOT NULL;

        CREATE TABLE calendar_event_links(
            event_id TEXT PRIMARY KEY REFERENCES planner_events(id) ON DELETE CASCADE,
            account_id TEXT NOT NULL REFERENCES calendar_accounts(id) ON DELETE CASCADE,
            remote_id TEXT NOT NULL,
            remote_etag TEXT NULL,
            last_haven_revision TEXT NOT NULL,
            last_provider_revision TEXT NOT NULL,
            UNIQUE(account_id, remote_id)
        );

        CREATE TABLE calendar_sync_state(
            account_id TEXT NOT NULL REFERENCES calendar_accounts(id) ON DELETE CASCADE,
            calendar_id TEXT NOT NULL REFERENCES planner_calendars(id) ON DELETE CASCADE,
            sync_cursor TEXT NULL,
            delta_link TEXT NULL,
            window_start TEXT NULL,
            window_end TEXT NULL,
            last_synced_at TEXT NULL,
            PRIMARY KEY(account_id, calendar_id)
        );

        CREATE TABLE calendar_outbox(
            id TEXT PRIMARY KEY,
            account_id TEXT NOT NULL REFERENCES calendar_accounts(id) ON DELETE CASCADE,
            event_id TEXT NULL REFERENCES planner_events(id) ON DELETE CASCADE,
            operation TEXT NOT NULL,
            payload_json TEXT NOT NULL,
            attempt_count INTEGER NOT NULL DEFAULT 0,
            next_attempt_at TEXT NOT NULL,
            last_error TEXT NULL,
            created_at TEXT NOT NULL
        );
        CREATE INDEX ix_calendar_outbox_due ON calendar_outbox(next_attempt_at, attempt_count);

        CREATE TABLE calendar_conflicts(
            id TEXT PRIMARY KEY,
            event_id TEXT NOT NULL REFERENCES planner_events(id) ON DELETE CASCADE,
            account_id TEXT NOT NULL REFERENCES calendar_accounts(id) ON DELETE CASCADE,
            haven_snapshot_json TEXT NOT NULL,
            provider_snapshot_json TEXT NOT NULL,
            detected_at TEXT NOT NULL,
            resolved_at TEXT NULL,
            resolution INTEGER NULL
        );
        CREATE INDEX ix_calendar_conflicts_open ON calendar_conflicts(resolved_at, detected_at DESC);

        CREATE TABLE planner_reminder_deliveries(
            entity_kind INTEGER NOT NULL,
            entity_id TEXT NOT NULL,
            occurrence_at TEXT NOT NULL,
            delivered_at TEXT NOT NULL,
            PRIMARY KEY(entity_kind, entity_id, occurrence_at)
        );
        CREATE INDEX ix_planner_reminder_deliveries_recent ON planner_reminder_deliveries(delivered_at DESC);

        INSERT INTO planner_collections(id,name,sort_order,is_archived,created_at,updated_at) VALUES
            ('8f51f72f-3c1f-4a5f-a101-010000000001','Personal',0,0,strftime('%Y-%m-%dT%H:%M:%fZ','now'),strftime('%Y-%m-%dT%H:%M:%fZ','now')),
            ('8f51f72f-3c1f-4a5f-a101-010000000002','College',1,0,strftime('%Y-%m-%dT%H:%M:%fZ','now'),strftime('%Y-%m-%dT%H:%M:%fZ','now')),
            ('8f51f72f-3c1f-4a5f-a101-010000000003','Work',2,0,strftime('%Y-%m-%dT%H:%M:%fZ','now'),strftime('%Y-%m-%dT%H:%M:%fZ','now'));

        INSERT INTO planner_calendars(id,account_id,provider,provider_calendar_id,name,color,permission,is_visible,updated_at)
        VALUES('8f51f72f-3c1f-4a5f-a101-020000000001',NULL,0,'local','Haven','#74E5C1',0,1,strftime('%Y-%m-%dT%H:%M:%fZ','now'));
        """;
}
