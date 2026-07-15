# Haven App Editing Guide

This guide maps the current Avalonia application as of 14 July 2026. It is intended for editing Haven itself, not for using Haven as an end user.

## Start here

The solution root is `Haven-Native-Pass9` and the startup project is `src/Haven.Desktop/Haven.Desktop.csproj`.

The fastest way to locate an edit is:

| What you want to change | Start here |
|---|---|
| Global colours, buttons, dropdowns, cards, typography | `src/Haven.Desktop/App.axaml` |
| Top menu, horizontal workspace tabs, sidebar, command palette | `src/Haven.Desktop/MainWindow.axaml` and `ViewModels/MainWindowViewModel.cs` |
| Chat messages, composer, plugin/prompt pickers | `src/Haven.Desktop/Views/ChatView.axaml` and `src/Haven.Desktop/ViewModels/ChatPageViewModel.cs` |
| Markdown and common LaTeX rendering | `src/Haven.Desktop/Controls/MarkdownView.cs` |
| Studio Home and project cards | `src/Haven.Desktop/Views/WorkspaceHomeView.axaml` and `src/Haven.Desktop/ViewModels/WorkspaceSurfacesViewModels.cs` |
| New project / NuGet / existing-folder flow | `src/Haven.Desktop/Views/ProjectCreatorView.axaml`, `src/Haven.Desktop/ViewModels/ProjectCreatorPageViewModel.cs`, `src/Haven.Desktop/Services/ProjectCreationService.cs` |
| Project home, files, Git, build, tests | `src/Haven.Desktop/Views/StudioProjectView.axaml`, `src/Haven.Desktop/ViewModels/WorkspaceSurfacesViewModels.cs`, `src/Haven.Infrastructure/ProjectIntelligenceService.cs` |
| Built-in browser UI | `src/Haven.Desktop/Views/BrowserView.axaml` and `src/Haven.Desktop/ViewModels/BrowserPageViewModel.cs` |
| Browser storage, bookmarks, history, logins, extensions | `src/Haven.Browser/BrowserDataService.cs` |
| Agents, plugins, prompts and their built-ins | `src/Haven.Core/PluginCatalog.cs` |
| Which tools exist in which context | `src/Haven.Application/ToolAvailability.cs` |
| Model tool loop and system instructions | `src/Haven.Application/ChatSessionService.cs` |
| SQLite tables and migrations | `src/Haven.Infrastructure/SqliteDatabase.cs` |
| Tests | `tests/Haven.Core.Tests` and `tests/Haven.Infrastructure.Tests` |

Read the other files in this folder before changing persistence, project tooling, browser behavior, or model-facing tools.

## Normal edit loop

```powershell
dotnet restore Haven.sln
dotnet build Haven.sln -c Debug
dotnet test Haven.sln -c Debug
dotnet run --project .\src\Haven.Desktop\Haven.Desktop.csproj
```

Do not edit generated `bin`, `obj`, or `artifacts` files. If Visual Studio or a running Haven process locks normal outputs, use an absolute isolated `ArtifactsPath`, then remove that verification folder afterwards.

```powershell
dotnet build .\src\Haven.Desktop\Haven.Desktop.csproj -c Debug `
  -p:ArtifactsPath="C:\absolute\path\to\Haven-Native-Pass9\artifacts\verify"
```

## Important design rules

- Keep user project operations confined to the selected project root.
- Store chats and catalog data through repositories; do not write directly to `haven.db` from the UI.
- Keep tool availability and tool execution aligned through `ToolAvailabilityPlanner`.
- Do not advertise a tool in the picker if its runtime, mode, permissions, platform, or project root cannot support it.
- Do not claim hidden chain-of-thought. Haven exposes streamed replies, explicit live activity, command output, timings, and edit counts.
- Add a SQLite migration instead of rewriting an old migration that users may already have applied.
- Native browser actions must stay bound to the attached Haven WebView and its dedicated profile.
- Keep deliverables and temporary build outputs separate; remove temporary verification artifacts after use.

## Guide contents

- `ARCHITECTURE.md` — project boundaries and runtime flow.
- `UI-MAP.md` — shell and screen ownership.
- `DATA-STORAGE.md` — every persistent data location and safety note.
- `CHAT-PROJECTS-AND-TOOLS.md` — project chats, local editing, tool policy, activity, Markdown and LaTeX.
- `BROWSER.md` — WebView lifecycle, tabs, bookmarks, history, logins and extensions.
- `BUILD-TEST-AND-RECIPES.md` — verification and common change recipes.
