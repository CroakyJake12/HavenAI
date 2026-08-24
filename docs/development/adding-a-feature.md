# Development Workflows

## Adding or changing a feature

```text
1. Identify the owning system        → docs/architecture/system-map.md
2. Inspect current implementation    → read the start-reading file, search consumers
3. Read relevant canonical docs      → only the ones your change touches
4. Search for reusable infrastructure→ components/prefabs/services before new ones
5. Choose the correct layer          → docs/architecture/repository-structure.md
6. Implement                          → preserve behaviour; cancellation through async I/O
7. Validate                           → docs/development/testing.md (build → focused tests → affected suites)
8. Update docs                        → if the documented contract changed, same pass
```

## Adding an app

Apps are registered by stable ID in `src/Haven.Application/Modes/BuiltInModeSeed.cs`
and reconciled at startup — never recycle IDs. To make one launchable:

1. Add/extend the mode record with stable metadata.
2. Route it in `MainView.LaunchAppAsync` via `HavenAppRoutePolicy` to a real
   surface or an honest setup-required state.
3. Build the surface as a HUI scene + thin AXAML host (`docs/ui/building-ui.md`).
4. Persist app-owned state in its own repository (forward-only migration).
5. Add headless route tests proving the launch actually renders.

## Changing UI

Follow `docs/ui/building-ui.md`. Non-negotiables: shared component system,
semantic tokens, accessibility names, all-state coverage, desktop+compact
validation in every changed appearance, honest states.

## Changing personalisation/theme plumbing

Read `docs/ui/theming-and-personalisation.md`, extend the catalogs/applier,
and keep `PersonalisationTests` green — especially the Glow baseline test.

## Before creating a shared system

Search for an existing one first (system map + `grep`). A new shared system
needs: a named owner project, a documented contract page here, tests, and a
reason no existing system can host it.
