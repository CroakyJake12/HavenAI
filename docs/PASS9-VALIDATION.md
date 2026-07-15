# Haven Native Pass 9 — Validation Report

Date: 13 July 2026

## Completed in source

- One `.sln` with native Avalonia desktop, application, core, infrastructure, browser, automations and worker projects.
- No Go, HTML or JavaScript application files.
- Editable XAML shell and page views; the main UI is not generated from C#.
- Dependency injection graph and cancellation-aware service contracts.
- SQLite schema migrations for conversations, containers/lessons, agents/plugins and automations/run history.
- LocalCode state migration path.
- Ollama discovery, streaming and one-shot requests.
- Chat/Teach/Do/Code navigation, mode terminology, temporary chats, Subject/Lesson and Workspace selectors.
- Model/effort picker, `@` plugin picker, conditional Agent bar, Duo selection, file/image attachments and stop command.
- Embedded browser-only service boundary and dedicated profile path.
- Workspace path confinement, atomic writes, hidden process execution, output limits and cancellation.
- Closed-app automation worker, Task Scheduler registration, UTC persistence and lease-based duplicate protection.
- Windows icon, app identity and source packaging scripts.
- Unit-test source for schedule, preflight, workspace and SQLite behaviour.

## Static checks performed here

- XML parsing of every `.axaml`, `.csproj`, `Directory.Build.props`, `Directory.Packages.props` and `global.json`.
- Required project/reference/file checks.
- Search for forbidden hidden-sidecar source extensions.
- C# delimiter and file-scoped namespace sanity checks.
- ZIP integrity and source inventory checks.

## Not physically validated in this environment

The execution environment did not have `dotnet`, `msbuild`, `csc`, Mono or Wine. Normal network access and SDK/package downloads were blocked. As a result, the following are deliberately **not claimed**:

- `dotnet restore` success
- C# compilation success
- Avalonia XAML compilation success
- unit-test execution success
- Windows x64 publish success
- WebView2 creation, profile isolation and DOM script behaviour on Windows
- Windows Task Scheduler registration/execution
- taskbar icon/AppUserModelID visual behaviour
- physical cancellation behaviour against real Ollama/process workloads

## Known functional limitations

- The embedded WebView adapter uses API discovery to tolerate package API differences. It must be replaced with direct strongly typed calls after the first successful Windows restore/build confirms the exact Avalonia WebView 12 API.
- Browser screenshots/download interception are not yet implemented.
- Real target-bound Computer Use is intentionally not implemented or advertised as working.
- The code/workspace service exists, but the full autonomous inspect → edit → run → repair tool-call loop is not yet connected to Ollama tool-call responses.
- Agent and Plugin pages currently show the persisted catalogue; full visual studios, import/export and version history are future passes.
- Model Import Studio is not implemented.
- Duo exposes its modes and shared workspace foundation; live conflict resolution, file watching and inline prompt comments are not yet implemented.
- Automation desktop notifications and retry backoff are not yet implemented.

## Required first Windows validation

Run `scripts\build-windows.ps1` from PowerShell with .NET 10.0.301 installed. Fix any compile-time API differences before calling the package runnable. Then test:

1. launch identity and icon;
2. SQLite first-run and restart;
3. Ollama discovery/stream/stop;
4. text and image attachment preflight;
5. each mode and hierarchy;
6. native WebView navigation/profile/DOM actions;
7. automation creation, run-now, scheduler registration and closed-app run;
8. migration with a copy of representative legacy state;
9. timeout/cancellation and workspace traversal tests.
