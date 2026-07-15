# Build, Test and Editing Recipes

## Prerequisites

- Windows 10/11 x64
- .NET 10 SDK
- Ollama for model-backed Chat, Call and AI Plan proposals
- Microsoft Edge WebView2 Runtime for Browse
- Git for Studio Git features
- microphone and Windows speech components for the full local Call smoke test
- internet access only when downloading a Whisper model or using calendar providers

## Optional environment configuration

Use a disposable data profile while developing persistence:

```powershell
$env:HAVEN_DATA_DIR = "C:\temp\haven-dev-profile"
```

Ollama and provider configuration:

```powershell
$env:OLLAMA_HOST = "http://127.0.0.1:11434"
$env:HAVEN_GOOGLE_CALENDAR_CLIENT_ID = "your-installed-app-public-client-id"
$env:HAVEN_MICROSOFT_CALENDAR_CLIENT_ID = "your-desktop-public-client-id"
```

Calendar client IDs are public configuration values. Do not add client secrets. Missing IDs must remain a tested Not Configured state.

Register the exact loopback redirects `http://127.0.0.1:53682/oauth/google/` and `http://localhost:53683/oauth/microsoft/` in the corresponding provider consoles.

## Build and test

```powershell
dotnet restore Haven.sln
dotnet build Haven.sln -c Debug
dotnet test Haven.sln -c Debug
dotnet build Haven.sln -c Release
dotnet test Haven.sln -c Release
dotnet build .\src\Haven.AutomationWorker\Haven.AutomationWorker.csproj -c Release
```

Verified baseline on 15 July 2026: Release build 0 warnings / 0 errors; 55 Core + 29 Infrastructure + 13 Avalonia headless Desktop tests = 97 passing.

Use `scripts/build-windows.ps1` only when deliberately publishing the application. Routine edits should not create app zips, numbered handoff folders or persistent verification directories.

The source guide lives in `Haven-App-Editing-Guide/App-Editing-Guide`. Edit it in place. Do not create a new application archive as part of a guide-only edit. If the existing guide-only archive is deliberately refreshed later, replace that one file rather than creating another version.

## Add a surface or page

1. Add the view and view-model; use code-behind only for view/native gestures.
2. Add a DataTemplate in `App.axaml`.
3. For a top-level destination, add a non-persisted `HavenSurface` member and shell command.
4. Open it through `AddOrSelectTab` with a stable key and explicit surface.
5. Implement `IActivatablePage` if visibility controls timers or resource work.
6. Verify title, menu state, sidebar, keyboard navigation, high DPI and repeated navigation (singleton behavior where intended).

Do not add a persisted conversation mode merely to represent a shell page.

## Add or change a global control

1. Change the global theme in `App.axaml`.
2. Add/update theme brush values in `UserPreferencesService`.
3. Use a semantic class such as `primary`, `secondary`, `subtle`, `icon`, `danger`, `send` or `chip`.
4. Test pointer-over, pressed, disabled and keyboard focus.
5. Remove any page-local duplicate template made obsolete by the change.

For blur/elevated content, use `AcrylicSurface` or the shared acrylic flyout/menu presenter. Test Windows transparency disabled.

## Add an icon

Add a closed, filled path and any compatibility aliases to `HavenIcon.CreateIcons`. Use the control by `IconKey`; do not load an SVG into an `Image` for a UI glyph. Verify small sizes and the unknown-key fallback.

## Add a dashboard tile

1. Give it a permanent unique key.
2. Add shared aggregation to `DashboardRepository` if necessary.
3. Implement `IDashboardTileProvider` for a built-in tile, or a declarative allow-listed plugin manifest for imported content.
4. Add an action key that resolves through Home's navigation map.
5. Test refresh cancellation, error/empty data, ordering, hiding and restart persistence.

Never allow a manifest to instantiate a type or execute plugin .NET code.

## Change Teach or Chat Groups

1. Start from `ConversationScope`; do not add view-model-only filtering.
2. Update repository SQL and scoped indexes together.
3. Preserve exact group/subject/lesson IDs.
4. Keep subject + default General lesson creation transactional.
5. Thread cancellation through lesson loads.
6. Keep Chat Group references inside `ContainerResourceRepository` and group data storage.
7. Verify archive is reversible and permanent delete detaches conversations without deleting messages.
8. Confirm groups still have no Studio workspace tools.

## Add a call service

1. Define or extend an interface in `CallAbstractions.cs`.
2. Keep the implementation's raw media buffers private.
3. Integrate it through `CallCoordinator` state/cancellation; do not drive services directly from the view.
4. Dispose/stop it on interrupt, end, source closure, fatal error and app shutdown.
5. Use fake services in `CallCoordinatorTests` and assert no media appears in repository calls.
6. Perform a manual x64 Windows microphone, VAD/Whisper, TTS and system-picker smoke test.

## Change planner or calendar sync

1. Add Core records/enums without renumbering persisted values.
2. Extend `IPlannerRepository` / `ICalendarSyncStore` before infrastructure code.
3. Put schema changes in a new migration.
4. Keep AI proposal parsing/validation separate from atomic Apply.
5. Preserve time zone and recurrence behavior; test a DST boundary.
6. Keep local tasks out of provider task APIs.
7. Store tokens only through `ICalendarTokenStore`.
8. Mock OAuth/provider HTTP for cursor expiry, incremental changes, outbox retries, remote deletes, conflicts and read-only permission.

## Add a project action

1. Put process behavior on an Application contract.
2. Implement it in Infrastructure using a fixed executable and bounded arguments/timeouts.
3. Validate paths, names and URLs.
4. Expose a direct project command or typed model tool.
5. Report real output and failure details.
6. Add validation and cleanup tests.

Never build a shell command from unvalidated text when direct process invocation works.

## Add a database field or table

1. Choose a version larger than the latest shipped migration.
2. Update domain record, repository reads/writes and SQL parameters together.
3. Use compatible defaults for old rows.
4. Test both a clean database and an upgrade copied from the previous schema.
5. Assert existing conversations, seven Chat Groups, links and enum numeric values are unchanged.
6. Inspect `schema_migrations` to prove the migration ran once.

Migration 7 is already the combined Planner + integrated-feature migration. Do not append more SQL to it after release; create migration 8.

## Add Markdown or math syntax

Block parsing and control creation are in `MarkdownView`; inline tokens are handled by `InlinePattern` and `AddInlines`; math conversion is in `LatexFormatter`. Preserve source and change only rendering.

Test streaming partial input, unclosed delimiters, nested emphasis, long code, Unicode, formula scripts and malformed text. Rendering must fail as readable text, not crash Chat.

## Full verification checklist

- Debug and Release solution builds have zero errors and preferably zero warnings.
- All Core, Infrastructure and Avalonia headless Desktop tests pass; Automation Worker builds.
- General, every Chat Group, Teach Quick Chats and exact lessons show only their scoped histories.
- Subject/default lesson, rapid subject switch and restart behavior work.
- Deleting a group/lesson preserves messages in the documented detached scope.
- Home aggregates refresh on activation/manual/timer and tile layout persists.
- Home/Call/Plan remain singleton tabs with correct menu/sidebar after leaving a chat.
- Call rejects a second active call, supports typed fallback, cleans all media and persists no raw media.
- Plan CRUD, hierarchy, recurrence/reminder, views and proposal Apply/Dismiss work.
- Missing calendar client IDs show Not Configured; mocked provider sync covers incremental/offline/conflict paths.
- Acrylic moves with the backdrop and remains readable in opaque fallback mode.
- Buttons and icons render at 100/150/200% DPI in dark/light/system themes.
- Project file tools remain Studio/Do-root-only; Chat Groups cannot use them.
- Markdown, lists, code, tables and `$` / `$$` math render without raw markers.
- Browse starts, navigates, switches tab layout and persists bookmarks.
- Temporary verification outputs and task-created downloads are removed.

## Troubleshooting

### Metadata DLL could not be found

Fix the first project compilation error. A missing reference assembly is usually downstream noise.

### Acrylic is transparent but not blurred

Use the shared presenter/control theme, confirm Digger acrylic is active, and test the Windows transparency setting. A custom semitransparent `Background` alone is not live backdrop blur.

### Unknown or missing icon

Check the `IconKey` and aliases in `HavenIcon`. Unknown keys should show the `info` fallback. Do not work around a missing key with a font glyph or SVG bitmap.

### Teach list vanished

Check `CurrentSurface`, the Teach sidebar visibility bindings and `HasNoSubjects`/Quick Chats empty states. Teach must not depend on selecting a subject.

### Calendar provider says Not Configured

Set the correct public client-ID environment variable before launching Haven, then confirm the provider's redirect URI is registered in its console. Do not add a client secret.

### Call has no microphone transcription

Confirm x64 build, a selected installed Whisper model, microphone permission/device, and the capability reason shown in Call. Typed transcript mode should remain available.

### Avalonia native child-window failure

Confirm `app.manifest` is assigned, WebView2 is installed, and only one native WebView host is mounted.

### Build output is locked

Close the running Haven instance or use an absolute isolated `ArtifactsPath`. Verify the resolved path is inside the workspace before removing it.

### Data looks stale or a migration is uncertain

Copy the profile, point `HAVEN_DATA_DIR` to the copy, and inspect `schema_migrations`. Never delete the normal profile while diagnosing.
