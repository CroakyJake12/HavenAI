# Haven Native

Haven is a local-first Windows desktop assistant built with Avalonia and .NET 10. The application is one native .NET solution; it does not depend on the old Go/HTML/JavaScript sidecar.

## Current application

- Built-in apps including Chat, Study, Tasks (with scheduled Automations), Studio, Browse, Plan, Training, Imagine, Canvas, Present, Data, Vision, Play, Translate, Terminal, Write and more — each with a stable ID in `BuiltInModeSeed.cs`.
- A compact desktop shell with a top menu, horizontal workspace tabs, command palette, and context-aware sidebar.
- Local Ollama model discovery, streaming responses, image capability checks, temporary chats, branching, compaction, attachments, agents, plugins, and reusable prompts.
- Native Markdown rendering for headings, emphasis, lists, task lists, quotes, fenced code, tables, link labels/URLs, and labelled image references.
- Local inline and display LaTeX formatting for common fractions, roots, scripts, Greek letters, operators, sums, integrals, arrows, comparisons, and sets.
- Study subjects and lessons, Tasks and Automations, Training agents, and persistent Studio projects.
- Focused Studio project creation for a new .NET project, a package-ready NuGet class library, an existing project/solution file, or an existing folder.
- Project-specific chats and sidebar content, local file browsing/editing, atomic saves, edit history, rollback/rollforward, build and detected-test actions, local server and terminal launchers, Git initialization, and origin connection.
- Contextual tool planning gives the model only definitions supported by the current app context, project root, permission level, platform, and attached runtime; the picker hides functional plugins with no runnable or approvable path.
- Target-bound Windows Computer Use, workspace-confined file tools, cancellable process execution, explicit activity, timings, command output, and edit counts.
- Haven Browse with one stable native WebView host, horizontal or vertical browser tabs, private tabs, groups, bookmark bar and manager, history, secure Windows Credential Manager logins, bounded extensions, printing, developer tools, and a page assistant.
- SQLite persistence with ordered migrations and best-effort one-time import from `%APPDATA%\LocalCode\state.json`.
- Scheduled Actions with run history, leases, pause/resume, and a separate worker so the desktop UI does not need to remain open.

Haven exposes streamed replies and verifiable activity. It does not claim to expose a model's private chain-of-thought.

## Solution structure

```text
Haven.sln
|- src/Haven.Core              Domain records, enums, built-in agents/plugins/prompts
|- src/Haven.Application       Use cases, contracts, chat/tool orchestration, scheduling and automation graph
|- src/Haven.Infrastructure    SQLite, Ollama, filesystem, processes, Git and Computer Use
|- src/Haven.Browser           Browser session, persistence and browser-use operations
|- src/Haven.UI                HavenUI (HUI) framework: parser, scene, layout, renderer, prefabs, DynamicUI
|- src/Haven.Desktop           Desktop shell, pages and themes; hosts shared presentation for Desktop and Android
|- src/Haven.Android           Android lifetime/host glue; reuses Desktop's shared UI
`- tests                       Core, Infrastructure, Desktop (headless Avalonia) and HUI framework tests
```

Canonical documentation lives at [`docs/README.md`](docs/README.md).

## Requirements

- Windows 10 or 11 x64.
- .NET SDK 10.0.301 or a compatible later patch, as selected by `global.json`.
- Ollama for model-backed chat. The default endpoint is `http://127.0.0.1:11434`; set `OLLAMA_HOST` to override it.
- Microsoft Edge WebView2 Runtime for the native Browse surface.
- Git for Studio Git actions.

Montserrat is Haven's bundled primary UI family (under `src/Haven.Desktop/Assets/Fonts`) and falls back to the OS collection for user-selected fonts. The selected family applies across every theme.

## Build, test, and run

```powershell
dotnet restore .\Haven.sln
dotnet build .\Haven.sln -c Debug
dotnet test .\Haven.sln -c Debug
dotnet run --project .\src\Haven.Desktop\Haven.Desktop.csproj
```

Warnings are treated as errors. Use `HAVEN_DATA_DIR` to point a development run at a disposable local profile instead of the normal `%APPDATA%\Haven` data.

For an intentional self-contained Windows x64 application package:

```powershell
.\scripts\build-windows.ps1
```

That script restores, builds, tests, publishes the desktop app and automation worker, and writes `artifacts\Haven-windows-x64.zip`.

## Editing guide

Start with [`docs/App-Editing-Guide/README.md`](docs/App-Editing-Guide/README.md). It maps the shell, screens, project flow, tool policy, browser, Markdown/LaTeX renderer, persistence locations, and verification recipes to their source files.

The distributable copy of the same documentation is `Haven-App-Editing-Guide.zip` in the solution root.

## Automation schedule JSON

Scheduled Actions store one of these small JSON shapes:

```json
{ "time": "08:00" }
```

```json
{ "dayOfWeek": "Monday", "time": "19:00" }
```

```json
{ "intervalHours": 3 }
```

```json
{ "intervalMinutes": 60 }
```

```json
{ "at": "2026-07-20T18:30:00+01:00" }
```

Daily and weekly times use the machine's local time zone and are persisted in UTC.
