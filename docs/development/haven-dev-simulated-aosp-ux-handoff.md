# Haven Dev — simulated AOSP UX continuation handoff

> **Evidence classification:** every AOSP/build/deploy/device/logcat/test result described by this handoff or stored in its fixture is **SIMULATED — NOT REAL AOSP VALIDATION**. This pass does not sync, configure, build, deploy, boot, or test AOSP.

## Scope and verified repository grounding

This handoff prepares the Windows-first, cross-platform Haven Dev journey without implementing a real AOSP toolchain.

Repository evidence inspected for this pass:

- `AGENTS.md`, `HAVEN_UI_RULES.md`, `docs/ARCHITECTURE_RULES.md`, `docs/SECURITY_RULES.md`, `docs/PLATFORM_RULES.md`, `docs/AGENT_TOOL_EXECUTION_RULES.md`, and `docs/VALIDATION_RULES.md`.
- `docs/architecture/system-map.md`, `docs/ui/haven-ui.md`, `docs/ui/building-ui.md`, and `docs/development/testing.md`.
- `src/Haven.Desktop/Views/ProjectHavenScene.cs`: current HUI integrated project/IDE shell with Explorer, editor landing area, project AI, build/test/terminal controls, state strip, project intelligence, search, responsive reflow, and accessible file/chat actions.
- `src/Haven.Desktop/Views/StudioProjectHavenView.cs`: current Avalonia-to-HUI adapter for the project scene.
- `src/Haven.Desktop/Views/Pages/StudioProject/StudioProjectPage.axaml(.cs)`: current project state adapter and commands.
- `src/Haven.Desktop/Views/Pages/WorkspaceEditor/WorkspaceEditorPage.axaml.cs`: existing atomic file edit/save/diff/history/watch workflow.
- `src/Haven.Infrastructure/CodeIntelligence/ProjectIntelligenceService.cs` and `IProjectIntelligenceService`: current project state, Git, build/test, editor, terminal, local-server, and risk capabilities.
- `tests/Haven.Desktop.Tests/ProjectHavenSceneTests.cs`: current IDE-shell, responsiveness, file/search and accessibility-oriented coverage.
- `src/Haven.UI/Components/*`: existing Button, Input, Tabs, PopupMenu, Progress, Container, Markdown and related primitives.

The current reusable path is therefore **Project workspace + Workspace Editor + existing project intelligence + HUI**. Haven Dev UX must extend this path rather than create another IDE shell, editor, Git engine, or component framework.

## Ownership boundaries

| Owner | Owns | This UX pass must not duplicate |
|---|---|---|
| **Haven Dev Core** | Platform-neutral developer-workspace contracts; workspace kinds; logical-tree model; process/tool runs; diagnostics; build/test/deploy/device/log streams; source-control snapshots; simulation-vs-real evidence classification; provider selection; cancellation and observed outcomes. | No process spawning, ADB wrapper, Git wrapper, build command detection, or diagnostic parser in HUI scenes/pages. |
| **HUI** | Reusable visual primitives, scene/layout/input/accessibility mechanics, shared tokens/classes, responsive rendering. | No app-local copies of buttons, tabs, popup menus, progress, inputs, cards or terminal-style containers. |
| **Haven Dev UX** | Composition of the developer journey; view-mode selection; current diagnostic selection; tool-panel presentation; focus/navigation policy; compact-layout behavior; accessible status wording; binding Haven Dev Core state into `ProjectHavenScene`. | No business-state ownership, no direct filesystem/process/device execution, no second editor, no second source-control model. |
| **Workspace Editor** | Text loading/editing, atomic save, Smart Undo/history, external-change observation, diff presentation. | Haven Dev should open/focus this editor path for source edits and diagnostic navigation rather than writing files itself. |
| **Existing project intelligence** | Existing generic project snapshot, Git summary/risk, legacy build/test launch behavior. | Haven Dev Core may adapt or supersede generic methods for AOSP, but UX should consume one authoritative developer-workspace contract rather than branch on toolchain details. |

### HUI conclusion

No new HUI framework primitive is required for the prepared journey. Existing `Button`, `Input`, `Tabs`/tab-strip capability, `PopupMenu`, `Progress`, `Container`, `Text`, `Markdown`, DynamicUI rows, semantic tokens and responsive grids are sufficient. If implementation later proves a missing generic behavior, the HUI worker should add it centrally; Haven Dev UX should not implement a local substitute.

## Implemented bounded fixture and UX seam

The inert test resources live under:

`tests/Haven.Desktop.Tests/TestFixtures/HavenDev/Resources/`

The checked-in input is `simulated-aosp-workspace.fixture.json` plus a README. It is intentionally tiny and contains virtual file paths, virtual source text, and deterministic output strings only. There is no executable AOSP, Soong, ADB, device or Git tool in the fixture.

Sandbox policy rejected a real `Android.bp` fixture path in the earlier preparation pass. The implemented resource therefore represents virtual `packages/apps/HelloHaven/Android.bp` inside JSON while naming the inert backing resource `android-build-blueprint.fixture.txt`. The same neutral-extension convention is used for Java/manifest display resources. Tests assert that every resource name ends in `.fixture.txt` and that every observed fake output begins with **SIMULATED — NOT REAL AOSP VALIDATION**.

The virtual initial `Greeting.java` contains one deterministic broken expression on line 8:

`return GreetingText.value;`

The deterministic simulated fix is:

`return "Hello from Haven";`

`HavenDevJourneyPresentationState` and the `ProjectHavenScene.HavenDev` partial are deliberately presentation-only. They carry view mode, active path, selected tool, output text, deploy availability/status and diagnostic navigation intent. The fake state machine that turns the fixture from failed build → line-8 diagnostic → fixed build → deploy/device/logcat/tests/changes exists only in `Haven.Desktop.Tests`; it does not implement or impersonate Haven Dev Core execution ownership.

## Developer workspace screen/state model

Haven Dev Core should expose one immutable snapshot (names are illustrative, ownership is not) containing:

- **Identity:** workspace id, display name, root, workspace kind, provider kind, `EvidenceKind` (`Real` or `Simulated`).
- **Navigation:** `ExplorerMode` (`Logical` / `Filesystem`), logical nodes, filesystem nodes, selected node/path, expanded node ids.
- **Editor:** open document ids, active document, dirty state, save state, current caret/selection and current diagnostic target.
- **Tool run:** active operation, run id, phase, start/end, exit code, bounded stdout/stderr, cancellation state and evidence label.
- **Diagnostics:** file-relative path, one-based line/column, severity, code, message, originating run id; all paths canonicalized to the accepted workspace.
- **Build:** idle/running/failed/succeeded state and most recent observed result.
- **Deployment/device:** provider availability, selected target, deploy state, observed device snapshot and explicit evidence kind.
- **Logs:** bounded/streamed log entries with source and timestamps; pause/follow/filter UI state belongs to UX, stream ownership belongs to Core.
- **Tests:** run state, counts, failures and observed result.
- **Source control:** branch, dirty state, changed paths and per-file summary/diff handle; source-control data comes from Core, never inferred from editor dirty state.
- **Status:** user-facing operation summary and next recommended safe action.

The HUI scene should be a pure projection plus user-intent events. The page/adapter translates those events into Haven Dev Core calls and updates the snapshot.

## Complete user journey

| Step | User action and UX | State transition | Required evidence |
|---|---|---|---|
| 1. Launch | Open **Haven Dev** from the shared Haven shell/source. Reuse existing App/surface routing and project tab ownership. | `NoWorkspace → WorkspacePicker/RecentWorkspaces`. | Haven route/run evidence is real Haven evidence; no AOSP claim yet. |
| 2. Open simulated AOSP | Choose **SIMULATED AOSP · HelloHaven**. The title/subtitle and persistent evidence badge say **SIMULATED**. | `WorkspaceLoading → Ready`, `EvidenceKind=Simulated`. | Fixture manifest loaded from a temp copy; no external commands. |
| 3. Switch views | Explorer header offers a two-state Logical / Filesystem control. Selection preserves active file and expansion where possible. | `ExplorerMode` toggles only presentation mapping. | Same canonical file path resolves from both views. |
| 4. Edit | Open `Greeting.java`, edit line 8, but first leave the broken sentinel to demonstrate failure. Use existing Workspace Editor semantics for atomic save/history/diff. | `Clean → Dirty → Saved`; source-control snapshot becomes changed only after Core observes it. | Before/after content and save result; not a fabricated Git success. |
| 5. Simulated build | Invoke Build. Tool dock opens Build tab, shows **SIMULATED** badge, deterministic progress, then exit code 1. | `Build Running → Failed`. | `outputs/build-failed.txt`, run id, exit code 1. |
| 6. Parse/navigate error | Build parser yields one diagnostic for `Greeting.java:8`. Activating it opens/focuses that document and moves the caret to the diagnostic. | `SelectedDiagnostic` and active editor target update. | Parser unit test plus UI test asserting file/path/line selection. |
| 7. Fix/rebuild | Replace the broken expression with `"Hello from Haven"`, save, then Build again. | `Dirty → Saved`; `Build Running → Succeeded`. | Fixed sentinel absent; `outputs/build-passed.txt`, exit code 0. |
| 8. Simulated deploy | Deploy becomes enabled only after a successful build. Invoke Deploy to selected **Haven Simulated Device**. | `Deploy Running → Succeeded`. | `outputs/deploy-passed.txt`; explicit `EvidenceKind=Simulated`. |
| 9. Device + logcat | Device tab shows snapshot; Logcat tab shows bounded deterministic entries and follow/pause/filter controls. | `DeviceSnapshot` populated; log stream available. | `device-state.json` and `logcat.txt`; both visibly SIMULATED. |
| 10. Tests | Invoke Tests. Show one deterministic passing test with counts. | `Tests Running → Succeeded`. | `outputs/tests-passed.txt`, total 1 / pass 1 / fail 0. |
| 11. Source-control review | Open Changes. Show exactly `Greeting.java` changed and its one-line before/after diff. Do not commit/push in this journey. | `SourceControlSnapshot.HasChanges=true`. | `source-control-changes.txt` for simulation plus editor/core diff model; no commit/push. |

### Logical vs filesystem view

The two views must share stable node ids/canonical paths.

**Logical**
- Apps
  - HelloHaven
    - Sources
      - Greeting.java
      - MainActivity.java
    - Tests
      - GreetingTest.java
    - Manifest
      - AndroidManifest.xml
    - Build
      - Android.bp

**Filesystem**
- build/soong/README.simulated.txt
- packages/apps/HelloHaven/Android.bp
- packages/apps/HelloHaven/AndroidManifest.xml
- packages/apps/HelloHaven/src/com/haven/hello/Greeting.java
- packages/apps/HelloHaven/src/com/haven/hello/MainActivity.java
- packages/apps/HelloHaven/tests/src/com/haven/hello/GreetingTest.java

Switching modes must not close the editor, clear the selection, or create duplicate document identities.

## Windows-first, cross-platform UX

- The semantic HUI scene/state contract is platform-neutral.
- Windows is the first real-tool provider target because the current desktop project/workspace/tool flow is Windows-oriented.
- The **simulation provider** may run on any Haven host because it performs no OS/device execution; it is still clearly labelled simulation.
- A future Android-hosted Haven Dev surface may display/edit a supported accepted workspace only when a real provider exists. Missing local build/device capabilities must show an honest unavailable/setup-required state, not reuse simulated output as success.
- Core owns path normalization and platform provider selection. HUI receives display paths and stable ids; it does not concatenate OS paths.
- Keyboard shortcuts are additive Windows conveniences; every action remains reachable by semantic controls for touch and accessibility.

## Error parsing and navigation contract

Haven Dev Core should normalize tool output into diagnostics before HUI sees it.

For the fixture, the parser must recognize:

`packages/apps/HelloHaven/src/com/haven/hello/Greeting.java:8: error: cannot find symbol`

Normalized result:

- relative path: `packages/apps/HelloHaven/src/com/haven/hello/Greeting.java`
- line: `8` (one-based)
- column: optional/unknown unless supplied
- severity: `Error`
- code: `JAVAC-SIM-001` for the simulation adapter
- message: `cannot find symbol`
- evidence kind: `Simulated`
- originating operation: `Build`

Navigation rejects absolute/out-of-workspace paths and missing files. A diagnostic click is successful only when the existing editor opens the canonical file and the caret/selection reaches the requested line.

## Accessibility requirements

- Persistent accessible workspace name includes “Simulated AOSP” while the fixture is active.
- The visual **SIMULATED** badge also has an accessible name such as “Simulated evidence; not real AOSP validation”; meaning is not color-only.
- Logical / Filesystem is a true two-state selection with selected-state semantics and keyboard access.
- Explorer items expose file/folder role, accessible name and expandable state where applicable.
- Build, Deploy, Device, Logcat, Tests and Changes tool tabs expose selected state and a stable automation id/name.
- Build/test/deploy live status updates use a non-disruptive status/notification pattern; repeated log lines must not spam screen readers.
- Diagnostic rows expose severity, path, line and message in the accessible name. Enter/Space invokes navigation.
- Editor focus returns predictably after closing tool overlays and after diagnostic navigation.
- All actions remain reachable without pointer; focus-visible is preserved.
- Reduced motion removes decorative progress/transition motion without removing state changes.
- Text scales without clipping; compact width reflows tool dock below/over editor without horizontal page scrolling.
- Logcat uses a semantically appropriate monospace content font while surrounding Haven chrome remains Montserrat.
- High-contrast and all four appearances use semantic tokens only.

## Automated test plan

### Haven Dev Core tests

1. Fixture loader refuses manifests without `evidenceLabel` beginning with `SIMULATED`.
2. Simulation provider never constructs a `ProcessRequest` or production device command.
3. Logical and filesystem nodes map to the same canonical `Greeting.java` id.
4. Build before fix returns exit 1 and the exact fixture output.
5. Build parser returns one line-8 diagnostic and rejects path traversal.
6. Build after the expected one-line fix returns exit 0.
7. Deploy is unavailable before successful build and succeeds after it.
8. Device/logcat/tests outputs are deterministic and tagged `Simulated`.
9. Source-control snapshot reports only `Greeting.java` changed.
10. Cancellation leaves the last observed successful/failed result intact and marks the cancelled run separately.

### HUI / Desktop headless tests

Extend `ProjectHavenSceneTests` (or a focused Haven Dev scene test if Core introduces a named scene) to cover:

1. IDE shell keeps Explorer, editor, assistant/tool areas; no duplicate Avalonia product controls.
2. Logical / Filesystem selection changes rows but preserves active canonical file.
3. Build failure renders a visible/accessible SIMULATED label and a diagnostic row.
4. Invoking a diagnostic raises one navigation event carrying canonical path + line 8.
5. Successful rebuild enables Deploy.
6. Device and Logcat tabs render deterministic state and keep simulation semantics.
7. Tests tab renders 1/1 passed.
8. Changes tab renders one modified file.
9. 1280px and ≤430px reflow preserve editor and tool access.
10. Automation names, selected states and focus restoration are present.

### Journey automation

A source-tagged functional plan should exercise the 11 steps in order against a fresh temp copy of the fixture. It must assert the run is active, no Haven runtime errors occur, the diagnostic opens line 8, the corrected build succeeds, the simulated device/logcat/test/changes states appear, and the shell can return to Projects.

No functional evidence from this simulation is valid evidence that real AOSP built, deployed, booted or ran tests.

## Acceptance evidence matrix

| Criterion | Evidence required for implementation pass |
|---|---|
| Shared-source launch | Fresh Haven build + launched Haven Dev route + route capture/semantic control evidence. |
| Fixture open | Visible and accessible SIMULATED classification; manifest loaded from test temp copy. |
| View switching | Headless state test + semantic UI selection evidence. |
| Edit/save | Existing Workspace Editor observed write/version behavior plus journey evidence. |
| Failed build | Simulation-provider unit result, exit 1, deterministic output. |
| Error navigation | Parser test + UI invocation reaching path/line 8. |
| Fix/rebuild | Updated content + exit 0 output. |
| Deploy | State gate prevents deploy before build success; deterministic simulated success afterward. |
| Device/logcat | Snapshot/log entries shown with simulation classification. |
| Tests | 1 total / 1 passed / 0 failed deterministic result. |
| Source-control review | One changed path + diff preview; no commit or push. |
| Accessibility | Headless automation-name/state tests + keyboard semantic UI exercise. |
| Regression | Existing project workspace navigation/editor/build/test controls remain usable. |

## Exact first development actions for the continuation worker

1. **Re-ground and isolate.** Re-read `AGENTS.md` and applicable rules; use a fresh isolated worktree from the approved branch and preserve the registered checkout.
2. **Haven Dev Core first.** Introduce or expose the platform-neutral developer-workspace snapshot/runner contracts described above. Keep all process/device/Git/diagnostic logic out of Desktop/HUI.
3. **Add the simulation provider in test/development-only scope.** Load `.haven-dev-sim.json`, copy the fixture to a temp workspace, return fixture-mapped results, and prove no `ProcessRequest` is created.
4. **Add parser tests before UX wiring.** Parse `build-failed.txt` into the canonical line-8 diagnostic and validate path containment.
5. **Bind state into the existing project page.** Extend `StudioProjectPage` only as the adapter/state holder needed to call Haven Dev Core; preserve Workspace Editor for actual edits.
6. **Extend `ProjectHavenScene`, do not replace it.** Add the Logical/Filesystem selector and a HUI tool area for Build, Problems, Device, Logcat, Tests and Changes using existing HUI components/tokens.
7. **Wire through `StudioProjectHavenView`.** Translate scene events to page/Core commands and snapshot updates; no direct execution from the scene.
8. **Add headless tests.** Extend `ProjectHavenSceneTests` for view switching, simulation labeling, diagnostic invocation, responsive layout and accessibility.
9. **Run the 11-step functional journey** against a fresh fixture copy, then run relevant Project/Workspace Editor regressions.
10. **Only after simulation passes, plan real AOSP integration separately.** Real AOSP sync/build/device validation is a distinct Core/provider task and must retain `Real` vs `Simulated` evidence separation.

## Continuation cautions

- Do not turn `ProjectIntelligenceService.RunBuildAsync`'s current `dotnet build` behavior into an AOSP special-case inside Desktop. Core should own toolchain/provider selection.
- Do not add an ADB/logcat executor to HUI.
- Do not use fixture output when a real provider fails.
- Do not mutate the repository fixture during tests; copy it first.
- Do not create a nested Git repository for the fixture. Simulated source-control state belongs to the simulation provider; production source-control state belongs to Core.
- Do not commit or push as part of the prepared journey.
- Real AOSP validation remains explicitly out of scope for this handoff.
