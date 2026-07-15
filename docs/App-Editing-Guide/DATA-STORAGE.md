# Data Storage

## Base directory

By default Haven stores data in:

```text
%APPDATA%\Haven
```

Set `HAVEN_DATA_DIR` before starting Haven to use a separate test/profile directory. This is the safest way to test migrations and destructive catalog changes without touching normal user data.

`AppPaths.cs` defines the paths:

```text
%APPDATA%\Haven\haven.db
%APPDATA%\Haven\preferences.json
%APPDATA%\Haven\browser-data.json
%APPDATA%\Haven\BrowserProfile\
%APPDATA%\Haven\Attachments\
%APPDATA%\Haven\Logs\
```

The legacy import source is `%APPDATA%\LocalCode\state.json`. `LegacyStateMigrator` records completion so it is not repeatedly imported.

## SQLite

`haven.db` uses foreign keys, WAL journaling and a 5-second busy timeout. Schema ownership is `Haven.Infrastructure/SqliteDatabase.cs`.

Key tables:

| Table | Contents |
|---|---|
| `conversations` | mode, kind, title, project/group link, pin/archive/temp/branch/compaction state |
| `messages` | ordered user/assistant/tool content, model/agent metadata, compacted flag |
| `containers` | Chat Groups, Subjects, Task Groups and Studio Projects, including root/context/instructions |
| `lessons` | Teach lessons under Subjects |
| `agents`, `plugins`, `prompts` | built-in and custom catalog entries |
| `conversation_context` | registered context, compact summaries and evidence-backed handoffs |
| `automations`, `automation_runs` | schedule definitions, leases and run history |
| `macros` | click-to-run local instructions, optionally project-scoped |
| `workspace_versions` | before/after text, changelog summary and line counts for undo/rollback |
| `decisions` | project decision, alternatives, reasoning, evidence and consequences |
| `settings` | reserved general key/value settings |
| `schema_migrations`, `migration_log` | applied database and one-time import state |

Repositories in `Haven.Infrastructure` are the only normal access path. Add a new migration with a larger version number. Never edit an already-shipped migration and expect existing databases to rerun it.

## Preferences

`preferences.json` is managed by `UserPreferencesService` with atomic temporary-file replacement. It stores theme/custom themes, model defaults, temperature/context/action limits, compact/confidence options, browser assistant setting and file/command/browser/computer permissions.

The old `VerticalTabs` JSON field remains readable for compatibility, but workspace tabs are now always presented horizontally. Browser vertical tabs are a separate `BrowserSettings.VerticalTabs` value in `browser-data.json`.

## Browser data

`browser-data.json` stores bookmark metadata/groups, non-private history, restored non-private tabs, saved-login metadata, limited Haven extension definitions and browser settings.

Passwords are not stored in JSON or SQLite. On Windows, `BrowserDataService` writes password secrets to Windows Credential Manager and stores only origin/username metadata in JSON. Private tabs are excluded from restored tab state and history.

The native WebView profile is under `BrowserProfile`. Treat it as browser-owned state; do not parse or edit its internal files.

## Project files and edit history

Haven edits the user's selected project in place. The project itself is not copied into AppData. Model-facing and editor file paths pass through `WorkspaceToolService.ResolveWorkspacePath`, which rejects traversal outside the selected root. Direct project actions such as build, test, Git, terminal, editor, and local-server launch validate the project root and invoke fixed executables through `ProjectIntelligenceService`.

Atomic writes use a temporary file next to the target followed by replacement. Haven-authored versions are recorded in `workspace_versions`, enabling smart undo, redo, rollback and rollforward in the editor.

## Backup and reset

With Haven closed, backing up `%APPDATA%\Haven` preserves the local profile. To create a clean test profile without deleting anything, set `HAVEN_DATA_DIR` to a new folder. Do not delete the normal data folder from an installer or migration.
