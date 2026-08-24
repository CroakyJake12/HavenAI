# Editing Haven

These rules are mandatory for every agent, subagent, generated-code agent and automated tool modifying Haven. A change is not valid or complete if it violates an applicable Haven rule, even if it compiles, tests pass or the immediate feature appears to work.

This repository contains Haven's local-first .NET 10 and Avalonia application. Make changes in this source tree in place. Do not create versioned copies, handoff folders or ZIP files unless the user explicitly requests one.

## Required read-before-edit rules

Read this file first, determine the affected subsystem, then read every applicable authoritative rule file before editing:

- `HAVEN_UI_RULES.md` — mandatory before creating, editing, refactoring, reviewing, debugging, migrating or generating any Haven-owned UI; includes the Theme Modification Lock, Montserrat typography and Floating Activities.
- `docs/ARCHITECTURE_RULES.md` — layers, Apps, stable IDs, routing, persistence and state ownership.
- `docs/SECURITY_RULES.md` — privacy, credentials, approvals and sandbox boundaries.
- `docs/PLATFORM_RULES.md` — Windows/Android parity, platform providers, Haven Home, model residency and runtime hosts.
- `docs/GENUI_RULES.md` — GenUI events/state/actions, Apps, templates and attachment routing.
- `docs/BACKGROUND_LEARNING_RULES.md` — Background Learning, Haven Library, API Bank and scheduling.
- `docs/AGENT_TOOL_EXECUTION_RULES.md` — capabilities, agent loops, file/command/vision execution and observed results.
- `docs/VALIDATION_RULES.md` — mandatory build, runtime, visual, device, lifecycle and evidence requirements.

The Generative UI release is currently incomplete. Before continuing release work, read `docs/releases/generative-ui-2026-08-08/GENUI_RELEASE_SUCCESS_RUBRIC.md`, verify the requirement-index count/hash metadata, load the active unresolved work package, and retrieve detailed indexed records/original source ranges as needed. Do not replace unresolved requirements with a context summary. This release ledger is temporary execution state and does not override permanent Haven rules.

Rule precedence:

1. Explicit current user instruction that expressly changes an applicable Haven rule.
2. Mandatory repository rules in this file and the authoritative files above.
3. Subsystem-specific instructions.
4. Existing implementation conventions.
5. Agent defaults/preferences.

An ordinary feature request does not silently override a rule. Existing legacy code does not authorise repeating a violation.

Delegation and generated code do not relax these requirements. The parent agent must ensure every subagent reads the applicable rules. Before completion, identify all applicable rules, inspect for bypasses, run the required validation, fix violations and repeat. If any applicable mandatory rule is knowingly broken or any required runtime remains unvalidated, the task/pass is incomplete.

## Source of truth

- `src/Haven.Core` owns stable entities, enums, value objects, and contracts. Existing numeric enum values and persisted IDs must never be renumbered.
- `src/Haven.Application` owns use cases, mode registration, routing policy, chat orchestration, planner and call coordination. It must depend on contracts rather than Windows or SQLite details.
- `src/Haven.Infrastructure` owns SQLite, Ollama, Windows, filesystem, calendar, audio, and other external integrations.
- `src/Haven.Desktop` owns Avalonia controls, pages, themes, shell navigation, accessibility, and Windows presentation.
- All full-page UI belongs under `src/Haven.Desktop/Views/Pages`. Shell controls belong under `Views/Shell`; reusable controls belong under `Controls`. Do not recreate a parallel `Interface/Pages` tree.

## Haven apps and surfaces

- Persisted conversation capability is `HavenMode`: Chat, Study, Tasks, and Studio.
- Visible navigation is `HavenSurface`. A surface may use Chat or Tasks storage without pretending to be the Chat or Tasks UI.
- Built-in Apps are defined by stable IDs in `src/Haven.Application/Modes/BuiltInModeSeed.cs` and cover Chat, Study, Tasks, Studio, Browse, Plan, Training, Imagine and many more. Startup reconciles these records into existing profiles, so change metadata by stable ID and never recycle one.
- Launch routing lives in `MainView.LaunchAppAsync`. Every enabled App entry must lead to a working surface or an honest setup-required state.
- App-specific chat workspaces use `NewChatPage.ConfigureMode`, which carries their instructions into the model context while preserving the compatible base mode.
- The window background follows `HavenSurface`, not `HavenMode`, through `TidalBackground`.

## Header and commands

- `Views/Shell/TopRail/TopRail` owns the header capsule, history buttons, text-only tabs, Apps, Actions, universal model/voice picker, notifications, and universal search.
- `AppLauncherControl` is the reusable Apps launcher. Its order is pins followed by local decayed usage; do not re-sort it alphabetically in the view.
- `ActionsFlyoutControl` renders the searchable three-column action library. Context is supplied by `MainView.RefreshContextualActions`; do not add another dynamic toolbar to the header.
- Reuse registered commands and domain services. Never make an action look available if it has no implementation.

## Visual system and icons

- Use global classes and dynamic resources from `Styles`; avoid window-local duplicates.
- Use `HavenIcon` and a stable icon key for line icons. Unknown keys must keep the visible fallback.
- Haven's supplied brand master is `Assets/haven-1024.png`. Regenerate the PNG and ICO scales with `tools/update-haven-icon.py <master.png>`; do not redraw or independently edit the small scales.
- Preserve keyboard access, automation names, focus restoration, scalable text, reduced motion, theme switching, and high-contrast fallbacks.

## Persistence and safety

- Add schema changes as forward-only migrations with fixtures from every prior schema. Back up before migrations and preserve all conversations, messages, groups, projects, lessons, planner items, calls, and mode IDs.
- Keep project, file, Git, build, and test tools restricted to Studio or an explicitly accepted workspace. Chat Groups and Study references are context, not code workspaces.
- Destructive, external, privileged, and ambiguous tool actions require the existing approval path. Never report an operation as successful without an observed service result.
- Carry cancellation through async I/O and reject stale UI results after rapid navigation.

## Validation and cleanup

- Use `AVALONIA_TELEMETRY_OPTOUT=1` for local validation if Avalonia's build service locks its log.
- After meaningful edits run the closest tests, then a clean Release restore/build/full test pass before production handoff.
- A green build proves compilation, not that a route rendered. Add or run Avalonia headless route tests and perform relevant Windows smoke tests for browser, microphone, screen capture, shell, and icon changes.
- Preserve unrelated user changes. Clean task-created staging, temporary files, and stale `bin`/`obj`; do not delete user archives or downloads.

## Canonical documentation

- The single entry point is `docs/README.md`. Sub-indices live at `docs/architecture/README.md`, `docs/ui/README.md`, and `docs/development/README.md`.
- Architecture is documented in `docs/architecture/` (repository structure, system map, state and persistence).
- UI is documented in `docs/ui/` (HavenUI framework, building UI, design/philosophy embedded via `HAVEN_UI_RULES.md`, theming and personalisation).
- Development workflows are documented in `docs/development/` (adding a feature/app, testing, common mistakes).
- Before any substantial change that touches documented architecture, UI, shared systems, state/persistence, navigation, integrations or platform behaviour, read the relevant canonical page. A trivial unrelated edit does not require reading the entire tree — relevant docs only.

## Before substantial work

For changes affecting the areas above, follow this contract and treat it as blocking:

```text
Understand request
      ↓
Find the owning system          (docs/architecture/system-map.md + current code)
      ↓
Inspect the current implementation
      ↓
Read the relevant canonical doc page(s)
      ↓
Search for reusable existing infrastructure (system map + grep)
      ↓
Check architecture / UI fit      (ownership, dependency direction, HAVEN_UI_RULES)
      ↓
Implement
      ↓
Validate                         (docs/development/testing.md + VALIDATION_RULES)
      ↓
If the documented contract changed, update the affected doc in the same pass
```

Do not create a parallel shared subsystem when the map already names an owner. Cite the doc page you consulted in the PR or pass report when the change is non-trivial.

## When code and docs disagree

1. Inspect context and history; decide whether docs are stale, code is transitional, or your task intentionally changes the contract.
2. Preserve working behaviour unless your task requires a change.
3. Correct stale canonical docs in the same pass — do not silently pick whichever is convenient.
4. When intentionally changing documented architecture, update the relevant doc in the same pass so the next agent sees one obvious current truth. Docs guide without fossilising mistakes.
