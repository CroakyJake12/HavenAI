# Editing Haven

This repository contains Haven's local-first .NET 10 and Avalonia application. Make changes in this source tree in place. Do not create versioned copies, handoff folders, or ZIP files unless the user explicitly requests one.

## Source of truth

- `src/Haven.Core` owns stable entities, enums, value objects, and contracts. Existing numeric enum values and persisted IDs must never be renumbered.
- `src/Haven.Application` owns use cases, mode registration, routing policy, chat orchestration, planner and call coordination. It must depend on contracts rather than Windows or SQLite details.
- `src/Haven.Infrastructure` owns SQLite, Ollama, Windows, filesystem, calendar, audio, and other external integrations.
- `src/Haven.Desktop` owns Avalonia controls, pages, themes, shell navigation, accessibility, and Windows presentation.
- All full-page UI belongs under `src/Haven.Desktop/Views/Pages`. Shell controls belong under `Views/Shell`; reusable controls belong under `Controls`. Do not recreate a parallel `Interface/Pages` tree.

## Haven apps and surfaces

- Persisted conversation capability is `HavenMode`: Chat, Study, Tasks, and Studio. The old Teach and Do names are compatibility aliases only and must not appear in new code or UI.
- Visible navigation is `HavenSurface`. A surface may use Chat or Tasks storage without pretending to be the Chat or Tasks UI.
- Built-in Apps are defined by stable IDs in `src/Haven.Application/Modes/BuiltInModeSeed.cs`. Startup reconciles these records into existing profiles, so change metadata by stable ID and never recycle one.
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
