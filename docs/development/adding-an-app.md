# Adding an App

An App is a user-facing product surface with its own identity, backed by
compatible conversation/storage capability.

## Contract

- **Stable IDs**: built-in Apps live in `BuiltInModeSeed` with stable keys and
  metadata. Startup reconciles records into existing profiles. Never renumber
  or recycle a persisted ID.
- **Capability vs surface**: `HavenMode` (Chat/Study/Tasks/Studio) remains the
  persisted storage capability; visible navigation uses `HavenSurface`. A new
  app may use Chat or Tasks storage without pretending to be the Chat/Tasks UI.
- **Routing**: `MainView.LaunchAppAsync` + `HavenAppRoutePolicy` are the single
  launch contract. Every enabled app opens a working surface or an honest
  setup-required state — never a dead tab.
- **Mode instructions**: app-specific chat workspaces configure their context
  via `NewChatPage.ConfigureMode`, preserving the compatible base mode.

## Steps

1. Seed the app (stable ID, name, description, icons) in `BuiltInModeSeed`.
2. Decide storage capability and repositories; add forward-only migrations if
   new durable data is needed (`docs/architecture/state-and-persistence.md`).
3. Implement backend services in `Haven.Application` (+ Infrastructure for
   concrete integrations).
4. Build the page: HUI scene + thin AXAML host; register in launch routing;
   wire contextual actions via the existing Actions system rather than a new
   toolbar.
5. Follow the window-background rule: the tidal background follows the
   app's `HavenSurface`.
6. Validate: headless route test for launch rendering, focused service tests,
   desktop + compact dimensions across changed appearances
   (`docs/VALIDATION_RULES.md`).
