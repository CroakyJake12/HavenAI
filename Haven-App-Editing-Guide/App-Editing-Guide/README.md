# Haven App Editing Guide

This guide maps the current Avalonia application as of 15 July 2026. It is for editing Haven itself, not for using Haven as an end user.

## Start here

The solution root is `Haven-Native-Pass9` and the startup project is `src/Haven.Desktop/Haven.Desktop.csproj`.

| What you want to change | Start here |
|---|---|
| Global colours, controls, button variants, cards and flyouts | `src/Haven.Desktop/App.axaml` and `UserPreferencesService.cs` |
| Reusable live acrylic | `src/Haven.Desktop/Controls/AcrylicSurface.cs` and the acrylic themes in `App.axaml` |
| Product and catalog icons | `src/Haven.Desktop/Controls/HavenIcon.cs` |
| Product surfaces, horizontal tabs, menus and sidebars | `src/Haven.Desktop/MainWindow.axaml`, `ViewModels/MainWindowViewModel.cs` and `src/Haven.Core/HavenSurface.cs` |
| Home dashboard, aggregate data and tile layout | `Views/HomeView.axaml`, `ViewModels/HomePageViewModel.cs`, `DashboardRepository.cs` and `DashboardLayoutRepository.cs` |
| Chat and Teach messages, composer, subjects and lessons | `Views/ChatView.axaml`, `ViewModels/ChatPageViewModel.cs`, `ConversationScope` and the conversation/container repositories |
| Chat Group home, references and group lifecycle | `Views/ChatGroupView.axaml`, `ViewModels/ChatGroupPageViewModel.cs` and `ContainerResourceRepository.cs` |
| Markdown and common LaTeX rendering | `src/Haven.Desktop/Controls/MarkdownView.cs` |
| Local Call UI and state machine | `Views/CallView.axaml`, `ViewModels/CallPageViewModel.cs`, `CallCoordinator.cs` and the call service contracts |
| Plan UI, planner domain and AI proposals | `Views/PlanView.axaml`, `ViewModels/PlanPageViewModel.cs`, `PlannerRepository.cs` and `PlannerProposalService.cs` |
| Google/Microsoft calendar connection and sync | `PlannerAbstractions.cs`, `CalendarSyncProviders.cs`, `OAuthCalendarTransportBase.cs` and provider transports |
| Studio Home and project cards | `Views/WorkspaceHomeView.axaml` and `ViewModels/WorkspaceSurfacesViewModels.cs` |
| Project creation / NuGet / existing-folder flow | `Views/ProjectCreatorView.axaml`, `ViewModels/ProjectCreatorPageViewModel.cs` and `Services/ProjectCreationService.cs` |
| Project files, Git, build and tests | `Views/StudioProjectView.axaml`, `ViewModels/WorkspaceSurfacesViewModels.cs` and `ProjectIntelligenceService.cs` |
| Built-in browser | `Views/BrowserView.axaml`, `ViewModels/BrowserPageViewModel.cs` and the `Haven.Browser` project |
| Which model tools are available in each context | `src/Haven.Application/ToolAvailability.cs` |
| SQLite migrations, including migration 7 | `src/Haven.Infrastructure/SqliteDatabase.cs`, `PlannerMigration.cs` and `FeatureMigration.cs` |
| Tests | `tests/Haven.Core.Tests`, `tests/Haven.Infrastructure.Tests` and `tests/Haven.Desktop.Tests` (Avalonia headless UI coverage) |

Read the focused files in this folder before changing persistence, scoped chat behavior, local media, provider synchronization, project tooling, browser behavior, or model-facing tools.

## Normal edit loop

```powershell
dotnet restore Haven.sln
dotnet build Haven.sln -c Debug
dotnet test Haven.sln -c Debug
dotnet run --project .\src\Haven.Desktop\Haven.Desktop.csproj
```

Do not edit generated `bin`, `obj`, or `artifacts` files. If Visual Studio or a running Haven process locks normal outputs, use an absolute isolated `ArtifactsPath`, then remove that verification directory afterwards.

```powershell
dotnet build .\src\Haven.Desktop\Haven.Desktop.csproj -c Debug `
  -p:ArtifactsPath="C:\absolute\path\to\Haven-Native-Pass9\artifacts\verify"
```

Use `HAVEN_DATA_DIR` for migration and destructive-operation testing. Never point a test run at the normal `%APPDATA%\Haven` profile.

## Important invariants

- `HavenSurface` is transient shell navigation. Never persist it or renumber the existing `HavenMode` or `ConversationKind` values.
- General Chat, each Chat Group, Teach Quick Chats, and each exact Teach lesson are separate `ConversationScope` values. Query them through the repository.
- Keep all existing container IDs and conversation links when migrating. Archive is non-destructive; deleting a Chat Group or lesson detaches its conversations rather than deleting messages.
- Chat Groups may provide shared instructions and references, but never gain local-folder, Git, build, test, or arbitrary command tools. Those are Studio capabilities.
- Store call transcripts and call metadata only. Raw microphone buffers and captured frames must never enter SQLite or logs.
- Planner mutations proposed by AI are not applied until the user chooses Apply.
- Calendar OAuth tokens belong only in the DPAPI-protected token store; SQLite stores account and synchronization metadata.
- Add a new SQLite migration instead of rewriting any migration that a user may already have applied.
- Keep user project operations confined to the selected project root.
- Do not advertise a tool if its runtime, surface, permissions, platform, or project root cannot support it.
- Do not claim hidden chain-of-thought. Haven exposes streamed replies, explicit activity, command output, timings and edit counts.
- Keep temporary build outputs and deliverables separate, then remove task-created verification files.

## Guide contents

- `ARCHITECTURE.md` — project boundaries, startup and runtime flows.
- `UI-MAP.md` — shell ownership, pages, controls, icons and acrylic.
- `SURFACES-AND-VISUAL-SYSTEM.md` — `HavenSurface`, singleton tabs, global themes, reusable acrylic and `HavenIcon`.
- `TEACH-AND-CHAT-GROUPS.md` — exact conversation scopes, subjects, lessons, group homes and durable references.
- `HOME-CALL-AND-PLAN.md` — dashboard, local call pipeline, planner, AI proposals and calendar providers.
- `DATA-STORAGE.md` — persistent locations, migration 7, secrets and backup safety.
- `CHAT-PROJECTS-AND-TOOLS.md` — chat/project context, tool policy, activity, Markdown and LaTeX.
- `BROWSER.md` — WebView lifecycle, tabs, bookmarks, history, logins and extensions.
- `BUILD-TEST-AND-RECIPES.md` — configuration, verification and safe change recipes.

The Markdown files in `Haven-App-Editing-Guide/App-Editing-Guide` are the source of truth. Do not create numbered guide folders or app packages when editing them.
