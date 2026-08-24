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

## 2. User preferences — JSON files

| Store | File | Contents |
|---|---|---|
| `UserPreferencesService` | `{DataDir}/preferences.json` | Appearance + theme + accent override + font + avatar flags, model defaults, permissions, voice profiles. Atomic tmp+move writes; malformed JSON falls back to defaults. |
| `MotionPreferencesService` | `%LocalAppData%/Haven/ui-preferences.json` | Reduced-motion toggle. |

Personalisation fields are tolerant: unknown theme names resolve to Glow,
unknown accent palettes disable override, unknown fonts fall back to bundled
Montserrat.

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
- New binary asset → process into a stable file under the data directory;
  never embed blobs in preferences.json.
