# Data Storage

## Base directory

Haven stores its normal local profile under:

```text
%APPDATA%\Haven
```

Set `HAVEN_DATA_DIR` before starting Haven to use a disposable profile. This is mandatory for migration tests and the safest way to exercise permanent-delete flows without touching normal user data.

`AppPaths.cs` owns the core paths; feature repositories create their own child directories when first used:

```text
%APPDATA%\Haven\haven.db
%APPDATA%\Haven\preferences.json
%APPDATA%\Haven\browser-data.json
%APPDATA%\Haven\BrowserProfile\
%APPDATA%\Haven\Attachments\
%APPDATA%\Haven\Logs\
%APPDATA%\Haven\container-resources\<container-id>\
%APPDATA%\Haven\SpeechModels\
%APPDATA%\Haven\CalendarTokens\
```

The legacy import source is `%APPDATA%\LocalCode\state.json`. `LegacyStateMigrator` records completion so it is not imported repeatedly.

## SQLite and migration ownership

`haven.db` uses foreign keys, WAL journaling and a five-second busy timeout. `SqliteDatabase.cs` owns the ordered migration list. Migration 7 is registered exactly once as:

```text
PlannerMigration.Sql + FeatureMigration.Sql
```

Keep it as one migration version. Do not register two separate version-7 rows and do not edit an earlier migration to make a new field appear.

Migration 7 preserves existing enum values, container IDs and conversation links. It appends `ConversationKind.Call = 7`, adds scoped indexes, normalizes legacy Teach chats with no lesson into Quick Chats, and creates a General lesson only for a subject that has no lesson.

## Main tables

| Table/group | Contents |
|---|---|
| `conversations`, `messages` | mode/kind/scope links, titles and ordered transcript/tool content |
| `containers`, `lessons` | Chat Groups, Teach Subjects/lessons, task groups and Studio projects |
| `container_resources` | group reference metadata, generated stored name, type, byte size and SHA-256 |
| `agents`, `plugins`, `prompts` | built-in/custom catalog data; plugins include declarative dashboard tile JSON |
| `conversation_context` | registered context, compact summaries and evidence-backed handoffs |
| `call_sessions` | one call's conversation link, model/devices/voice, sharing flag, status and times; never raw media |
| `planner_collections` | stable Personal/College/Work defaults plus user collections |
| `planner_tasks`, `planner_task_completions` | hierarchical task state, recurrence/reminders and completion history |
| `planner_calendars`, `planner_events` | local and linked provider calendars/events |
| `calendar_accounts`, `calendar_event_links` | provider account metadata, remote IDs and ETags |
| `calendar_sync_state`, `calendar_outbox` | sync/delta cursors, foreground/offline work and retry metadata |
| `calendar_conflicts` | local/provider snapshots awaiting explicit resolution |
| `planner_reminder_deliveries` | deduplication of delivered reminders |
| `automations`, `automation_runs` | scheduled AI action definitions, leases and history; separate from Plan |
| `workspace_versions`, `decisions`, `macros` | Studio edit history, project decisions and local macros |
| `settings` | versioned dashboard layout and other general key/value settings |
| `schema_migrations`, `migration_log` | database and one-time import history |

Repositories are the normal access path. The UI must not open or mutate `haven.db` directly.

## Conversation scope safety

The migration-7 `ix_conversations_scope_updated` index supports exact mode/kind/container/lesson queries. Use `IConversationRepository.GetRecentInScopeAsync` so General Chat, each Chat Group, Teach Quick Chats and each lesson remain isolated.

Archiving does not alter conversation links. Permanent Chat Group deletion transactionally detaches its conversations to General Chat before deleting the container. Lesson deletion converts linked conversations to Teach Quick Chats. Neither flow deletes messages.

## Chat Group resources

`ContainerResourceRepository` copies accepted files under `container-resources`. SQLite stores only metadata and the generated stored filename. Content is hash-deduplicated per group. Deleting a resource removes the metadata and the managed copy; it never deletes the user's original file.

Back up the database and `container-resources` together. Restoring only one side can leave missing files or orphaned managed copies.

## Call media and speech models

`SpeechModels` contains downloaded `ggml-tiny.bin`, `ggml-base.bin` or `ggml-small.bin` files. An in-progress download uses a `.download` suffix and is promoted atomically. These large model files can be re-downloaded; they are not conversation data.

Call transcripts live in `messages`, and metadata lives in `call_sessions`. Microphone PCM, VAD frames, captured screen/window frames and preview JPEG bytes are memory-only. Adding any raw-media column or log output violates the privacy boundary.

## Planner and calendar secrets

Planner tasks/events and provider synchronization metadata live in SQLite. OAuth access/refresh tokens do not.

`WindowsCalendarTokenStore` serializes a `CalendarTokenEnvelope`, encrypts it with Windows DPAPI for the current user, and atomically writes:

```text
CalendarTokens\<account-id-N>.token
```

The token cannot normally be decrypted under another Windows account. Disconnect removes the token file and marks account metadata disconnected. Never log token content, place it in `preferences.json`, or add a client secret to this directory.

## Dashboard layout and preferences

Dashboard order, visibility and size are versioned JSON in the `settings` row `dashboard.layout.v1`. Tile definitions and live values are code/data-provider concerns; do not serialize arbitrary provider instances.

`preferences.json` is managed by `UserPreferencesService` with atomic replacement. It stores themes, model defaults, generation/action limits, browser assistant state and permission choices. The old `VerticalTabs` property remains readable for compatibility, but workspace tabs are horizontal. Browser vertical tabs remain a separate `BrowserSettings.VerticalTabs` value in `browser-data.json`.

## Browser data

`browser-data.json` stores bookmark metadata/groups, non-private history, restored non-private tabs, saved-login metadata, limited Haven extension definitions and browser settings. Windows Credential Manager stores browser passwords; JSON stores only origin/username metadata.

`BrowserProfile` is WebView-owned state. Do not parse or edit its internal files. Private visits are excluded from restored tabs and history.

## Project files and edit history

Haven edits the selected project in place; it does not copy a project into AppData. Model and editor file paths pass through workspace-root validation. Atomic writes use a sibling temporary file followed by replacement. Haven-authored versions in `workspace_versions` support undo/redo/rollback/rollforward.

## Backup, test and reset

With Haven closed, backing up the complete data directory preserves the local profile. Before a schema edit:

1. copy an old profile to a disposable location;
2. point `HAVEN_DATA_DIR` to it;
3. start Haven once and inspect `schema_migrations`;
4. verify old conversations, group IDs and project roots;
5. test a new clean profile separately;
6. never delete or rewrite the normal profile from a migration.
