# State and Persistence

Haven keeps data local-first. Categories, owners and lifecycles:

## 1. Durable domain data — SQLite (`Haven.Infrastructure`)

Conversations, messages, groups, projects, lessons, planner items, calls,
Apps/modes (by stable ID), agents, instructions, capabilities, run history.
Accessed through repositories defined in `Application`; schemas change via
**forward-only numbered migrations** with fixtures for every prior schema and
a backup/integrity check before migrating. Never renumber persisted enums or
recycle stable IDs. Representative: `src/Haven.Infrastructure` SQLite
services; fixtures in `tests/Haven.Infrastructure.Tests`.

Agentic recovery data is also durable SQLite (migration 23):

- `workspace_versions.haven_sequence` — nullable sequence column backfilled
  from `rowid`, kept populated by the `trg_workspace_versions_sequence`
  insert trigger and indexed per workspace root.
- `agent_checkpoints` — one row per recorded checkpoint (id, optional
  conversation/container ids, `workspace_root`, label, `CheckpointMode`,
  `start_sequence`, `created_at`). Restores replay every recorded mutation
  after `start_sequence`, taking the latest before-content per path, so
  recovery works in non-Git directories. Owned by `CheckpointRepository`.

## 2. User preferences — JSON files

| Store | File | Contents |
|---|---|---|
| `UserPreferencesService` | `{DataDir}/preferences.json` | Appearance + theme + accent override + font + avatar flags, model defaults, permissions, voice profiles. Atomic tmp+move writes; malformed JSON falls back to defaults. |
| `MotionPreferencesService` | `%LocalAppData%/Haven/ui-preferences.json` | Reduced-motion toggle. |

Personalisation fields are tolerant: unknown theme names resolve to Glow,
unknown accent palettes disable override, unknown fonts fall back to bundled
Montserrat.

### 2.1 Versioned settings — `{DataDir}/settings.json`

`VersionedAtomicSettingsStore`
(`src/Haven.Application/VersionedAtomicSettingsStore.cs`) persists named JSON
documents as one exportable manifest (`{version, exportedAt, settings}`) with
atomic tmp+move writes, a `.bak` fallback on corruption and export/import for
settings transfer. Keys defined so far (implementations in
`Haven.Infrastructure/Persistence` unless noted):

| Key | Implementing store | Contents |
|---|---|---|
| `models.fallback-order.v1` | `VersionedModelFallbackOrderStore` | Ordered model fallback keys, most preferred first. |
| `models.personalisation.v1` | `VersionedModelPersonalisationStore` | Shared personality defaults plus per-model entries; null personality members mean "use Haven defaults" and round-trip as explicit nulls; blank nicknames persist as null (= inherit). |
| `models.permissions.v1` | `VersionedModelPermissionStore` | Deny-rule model permission policy evaluated by `ModelPermissionEvaluator`. |
| `actions.default-providers.v1` | `VersionedDefaultProviderStore` (`Persistence/DefaultProviderStore.cs`) | Per-category default provider App key or `"ask"` (Always Ask). |
| `updates.preferences.v1` | `VersionedUpdatePreferenceStore` (`Updates/UpdateOrchestrator.cs`) | Background-check toggle and preferred release channel. |

The checkpoint policy (`CheckpointMode`) is engine-owned state on
`CheckpointService.Mode` (default `BeforeFileChanges`) and is not yet exposed
as a settings key — PARTIAL.

Spaces are also persisted through `IVersionedSettingsStore` by
`SpaceRegistry`; they are user content, not preferences. See
`docs/ARCHITECTURE_RULES.md` for state ownership rules.

## 3. Avatar assets — processed local files

`AvatarStore` stores centre-cropped images at `{DataDir}/avatars/user.png`
and `haven.png`. Preferences persist only the enabled flag; the path is a
stable well-known reference. Originals are never uploaded anywhere.

## 4. UI/session state — in-memory or session stores

Tab sessions, split-view geometry, workspace windows: owned by Desktop
services (e.g., `MainView.TabSessions.cs`, workspace session persistence).
Not a settings concern; may be rebuilt safely after a crash.

## 5. Caches and derived state

Model lists, retrieval indexes, generated-UI instances (`GenUiInstanceStore`)
are rebuildable caches or explicitly persisted stores with their own
contracts. Treat as disposable unless the owning service documents
persistence.

## Rules of thumb

- New durable product data → repository + migration in Infrastructure.
- New user setting → field on the existing preferences record + safe default;
  never a second settings store.
- New shared cross-surface policy or default (model routing, permissions,
  provider defaults, update behaviour) → contract in Application +
  `IVersionedSettingsStore` key named `<area>.<name>.v1`; never a bespoke
  settings file.
- New binary asset → process into a stable file under the data directory;
  never embed blobs in preferences.json.
