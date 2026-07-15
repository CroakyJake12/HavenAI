# Architecture

## Project boundaries

```text
Haven.Core
  Domain records, persisted enums, transient HavenSurface, recurrence,
  built-in agents/plugins/prompts
        ↓
Haven.Application
  Service contracts, chat/tool orchestration, dashboard/call/planner contracts,
  availability policy, call coordinator and planner proposal validation
        ↓
Haven.Infrastructure
  SQLite, Ollama HTTP, filesystem/process/Git services, repositories,
  local speech, calendar OAuth/sync and Windows Computer Use

Haven.Browser                 Haven.Automations
  WebView session/data          schedules, leases, worker registration
        ↘                     ↙
             Haven.Desktop
          Avalonia shell, views, view-models, Windows capture picker

Haven.AutomationWorker
  Closed-app scheduled-action entry point
```

References point inward. `Core` must not reference UI or Infrastructure. `Application` defines contracts and deterministic orchestration. Operating-system, HTTP and storage behavior belongs in Infrastructure, Browser or Automations; Avalonia/native-window presentation belongs in Desktop.

## Startup and shutdown

1. `src/Haven.Desktop/Program.cs` builds Avalonia.
2. `App.axaml.cs` registers Infrastructure, Planner, Automations, Browser and Desktop-only services.
3. The window opens and `IAppDatabase.InitializeAsync` applies ordered migrations.
4. `ILegacyStateMigrator` imports old LocalCode state once when present.
5. `MainWindowViewModel.InitializeAsync` loads initial Chat state and checks Ollama.
6. Selecting an `IActivatablePage` invokes its activation hook; leaving it invokes `Deactivate`.
7. Application shutdown ends an active call and disposes native media/browser resources.

Infrastructure registrations are in `Haven.Infrastructure/ServiceCollectionExtensions.cs`. Planner registrations are exposed by `AddHavenPlannerInfrastructure`. Desktop-only Windows screen capture replaces the infrastructure fallback in `App.axaml.cs`.

## MVVM pattern

- `.axaml` owns layout and bindings.
- `.axaml.cs` is limited to view-specific behavior such as StorageProvider pickers, pointer/keyboard gestures, clipboard and native window handles.
- View-models expose observable state and `RelayCommand` / `AsyncRelayCommand` actions.
- Repositories and service contracts own persistence and operating-system boundaries.
- `App.axaml` maps page view-model types to views in `Application.DataTemplates`.

Open pages through `MainWindowViewModel.AddOrSelectTab`; each tab carries an explicit `HavenSurface`. See `SURFACES-AND-VISUAL-SYSTEM.md` before changing routing.

## Conversation and scope flow

`Conversation` persists `HavenMode` and `ConversationKind`. `ConversationScope` expresses one exact history list independently from the current page:

- General Chat;
- one exact Chat Group;
- Teach Quick Chats;
- one exact subject/lesson.

The repository performs scoped SQL. `ChatPageViewModel` selects a scope, prepares the message and attachments, and builds group/subject/lesson context. It must never infer the first container as the active scope.

The model request flow is:

1. `ChatPageViewModel` prepares the conversation, original user input, attachments, selected catalog items, permissions and optional container context.
2. `CapabilityPreflightService` checks image/tool requirements against the selected model.
3. `ChatSessionService` builds system context and asks `ToolAvailabilityPlanner` for the exact allowed definitions and execution routes.
4. `OllamaClient` streams text or structured tool calls.
5. Tool calls execute through their planned workspace, computer, browser or automation runtime.
6. Explicit activity and final messages are persisted through repositories.

Never add a tool only to a prompt. Its typed execution, exact route, permission/platform checks and tests belong in the same change.

## Surface-specific application flows

### Home

Home is not a chat mode. `DashboardRepository` performs aggregate SQLite queries and returns a stable snapshot. Tile providers transform that snapshot; `DashboardLayoutRepository` stores only versioned layout JSON. Plugin dashboard entries remain declarative and allow-listed.

### Call

`CallCoordinator` is the deterministic application layer between local speech input, Ollama streaming, speech output, screen capture and repositories. It owns cancellation, one-call enforcement, barge-in, state changes and cleanup. Raw media never crosses the persistence contracts.

### Plan

Planner entities are separate from `AutomationDefinition`. `PlannerRepository` owns local task/event transactions. `PlannerProposalService` validates structured AI proposals and applies them only after explicit UI approval. Calendar providers use a registry around system-browser OAuth, encrypted token storage, incremental synchronization, outbox and conflicts.

## Project context and tool boundary

`MainWindowViewModel.ActiveProject` is persistent shell context across project home, project chat, editor and settings tabs. Project chat view-models are kept per project. `Conversation.ContainerId` links the chat to the Studio container; `ContainerDefinition.RootPath` supplies its workspace root.

Chat Groups also use containers but have no `RootPath` workspace capability. Never treat any non-null `ContainerId` as sufficient for workspace tools. Tool availability requires a Do/Studio mode, an existing selected root, the appropriate permission and the matching runtime.

## Local model boundary

`OllamaClient` connects to `http://127.0.0.1:11434` unless `OLLAMA_HOST` overrides it. Haven does not bundle an Ollama model. Discovery, pull/delete, streamed chat, structured tools and compatibility fallback all go through `IOllamaClient`.

Call speech recognition models are separate Whisper model files under Haven's data directory. They never replace or configure the Ollama response model.
