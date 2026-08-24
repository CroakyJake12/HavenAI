# Building UI in Haven — practices

How ordinary Haven surfaces are built with today's architecture. Framework
mechanics live in [haven-ui.md](haven-ui.md); visual law lives in
`../HAVEN_UI_RULES.md`.

## Anatomy of a page

A current-generation page is:

```text
Views/Pages/<App>/<Name>Page.axaml          thin host:  <HavenSceneControl x:Name="Scene"/>
Views/Pages/<App>/<Name>Page.axaml.cs       adapter: resolves services, wires scene events
Views/Pages/<App>/<Name>HavenScene.cs       the real UI: Page root + typed components
DynamicUI/<Row>.hui                          list rows / cards as templates (when data-driven)
```

The AXAML host exists for platform wiring only. Do not build product layout
in it, and do not create a second page tree.

## Reuse before you build

Check in this order and reuse the first match:

1. **HUI components** (`src/Haven.UI/Components/*`): Button, Input, Toggle,
   Select, Slider, Progress, Tabs, Markdown, Media/Image, PopupMenu.
2. **Prefabs/DynamicUI templates** (`src/Haven.Desktop/Prefabs`,
   `DynamicUI`): chat transcript pieces, sidebar rows, catalog cards.
3. **Shared Desktop controls** (`src/Haven.Desktop/Controls`) for Avalonia-
   level chrome that predates a scene equivalent.
4. Only then create something new — app-local if used by one surface; promote
   to `Haven.UI` components or shared templates when reused.

## Composition rules

- **One component system**: never re-create buttons/cards/menus locally; use
  classes/tokens from `SystemClasses.hui`/`UserClasses.hui`.
- **Semantic tokens only** (`TextSecondary`, `SurfaceRaised`, accent tiers…).
  Hard-coded colours are forbidden outside canonical palette definitions.
- **State coverage**: every interactive element needs hover, press, selected,
  focus-visible and disabled treatments — they come from tokens automatically;
  verify them rather than restyling them.
- **Empty/loading/error states** are part of the surface, not an afterthought;
  honest unsupported states beat simulated success (`HAVEN_UI_RULES.md`).
- **Context actions** right-click on desktop / long-press on mobile via the
  shared popup menu component.

## Layout & responsiveness

- Keep the shell's structural rhythm (42px rail controls, card paddings);
  themes change expression, not geometry.
- Use render conditions (`RequiredScreenWidth`, `Platform`) or scene-level
  breakpoints for adaptive layouts; exercise desktop **and** compact widths.
- Scrolling belongs to designated scroll containers (`HavenOverflow.Scroll`);
  avoid nested scroll traps.

## Accessibility (part of correctness)

- Set `Accessibility.AccessibleName` on every meaningful control (see swatch
  buttons in `SettingsHavenScene` for the pattern).
- Keyboard: every action reachable and operable; focus visible via token
  brushes; restore focus after overlays close.
- Never encode meaning in colour alone (accent swatches pair colour with a
  check mark + status text).
- Respect reduced motion: gate decorative animation on
  `MotionPreferencesService.ReduceAnimations`; the HUI engine already does.

## Events, actions, safety

- Wire user intent through scene events → page adapter → application services.
  Business logic stays out of scenes.
- Destructive/external/ambiguous actions go through the existing approval path
  (`docs/SECURITY_RULES.md`, `docs/AGENT_TOOL_EXECUTION_RULES.md`).
- Cancellation flows through async I/O; stale results after fast navigation
  must be rejected (generation guards like `_launcherSearchGeneration`).

## Adding a prefab or template

1. Create `Foo.hui` (+ `Foo.hui.cs` subclassing `HavenPrefabDefinition` when
   code-behind is needed) under the consuming project's `Prefabs/`.
2. Ensure `<EmbeddedResource Include="Prefabs\**\*.hui" />` exists in the csproj.
3. Register nothing manually — `HavenPrefabCatalog.FromAssembly` pairs files
   1:1 and fails loudly on duplicates.
4. For row-like content prefer a DynamicUI template + `CreateItem` with typed
   variables; keep variable names SCREAMING_CASE (`ChatUserMessage.hui`).
