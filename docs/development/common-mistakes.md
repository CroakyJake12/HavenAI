# Common Mistakes and Anti-Patterns

How a correct Haven change differs from a plausible-looking shortcut. For
each, the preferred approach is the one in canonical docs.

## Creating a parallel system instead of extending the existing one

**Problem.** A new settings store, theme engine, or navigation helper that
duplicates an already-owned system. Two sources of truth diverge; bugs are
fixed in one and reappear in the other.

**Why it hurts.** Persistence, DI and tests all split; migrations miss the
second store; users lose data on upgrade.

**Prefer.** Reuse the owner: preferences → `UserPreferencesService` + the
shared `HavenPersonalisation` funnel; navigation → `MainView.LaunchAppAsync`
and `HavenAppRoutePolicy`; storage → existing repositories.

## Putting backend logic in the UI

**Problem.** Business rules in `*.axaml.cs` or inside a HUI scene.

**Why it hurts.** Logic becomes untestable headlessly; platform hosts cannot
share it; the scene cannot be exercised without a window.

**Prefer.** Scenes own layout and wiring; use cases live in
`Haven.Application` contracts tested by Core/Desktop suites. The page adapter
passes services into the scene and handles events back out.

## Bypassing HUI for normal Haven UI

**Problem.** Constructing ad-hoc Avalonia controls for a product surface that
should be a HUI scene + thin host.

**Why it hurts.** Bypasses Haven's token system, responsive patterns, and
future theme/typography rollouts; breaks Android sharing.

**Prefer.** `docs/ui/building-ui.md`: scene + host, reuse `Haven.UI`
components and `Prefabs`/`DynamicUI` templates, consume semantic tokens.

## Hard-coding colours, radii, or fonts

**Problem.** Hex literals or numeric radii in product pages instead of tokens.

**Why it hurts.** Personalisation cannot reach them; Glow diverges from
production; accessibility regressions (contrast/focus) hide.

**Prefer.** Semantic brushes (`HavenTextPrimaryBrush`, `HavenAccentSoftBrush`,
etc.), radius tokens (`HavenControlRadius`/`HavenCardRadius`/`HavenPopupRadius`)
and `HavenFontFamily`. The canonical palette definition is the one exception.

## App-local personalisation

**Problem.** An app reads appearance or the override-appearance and rolls its
own `if Theme == Bubble` branches.

**Why it hurts.** Inconsistent behaviour across surfaces; generated UI misses
updates; tests cannot exercise the live pipeline in one place.

**Prefer.** Consume resources resolved by `HavenAvaloniaThemeResolver` /
`SurfacePaletteCatalog`. Theme-scoped work belongs in
`HavenThemeCatalog` + `HavenUiResourceApplier`.

## Duplicating screens across Desktop and Android

**Problem.** Copying a page into `Haven.Android` for visual tweaks.

**Why it hurts.** Two implementations of the same surface drift; shared fixes
apply to only one.

**Prefer.** Shared content stays in `Haven.Desktop`; Android owns only the
host/lifetime glue. Use HUI render conditions (`Platform`,
`RequiredScreenWidth`) for adaptations that are genuinely platform-specific.

## Platform dependencies in shared backend code

**Problem.** `Haven.Core` or `Haven.Application` referencing `Avalonia`,
`SQLite` details or `Microsoft.Win32.Registry` directly.

**Why it hurts.** Violates dependency direction; breaks Android and headless
tests; leaks OS assumptions into business rules.

**Prefer.** Keep Core contract-only; put OS/integration details behind
`Haven.Infrastructure` (or Desktop/Android) behind interfaces defined in Core.

## Swallowing exceptions or using fake service returns to compile

**Problem.** `catch {}` over a migration or a `return new List<T>()` stub so
the solution builds.

**Why it hurts.** Failures hide; user data appears missing rather than
reported; tests pass vacuously.

**Prefer.** Report the real result through the existing approval/error surface;
carry cancellation forward; keep the real dependency graph.
