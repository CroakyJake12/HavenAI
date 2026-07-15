# Architecture

## Project boundaries

```text
Haven.Core
  Domain records, enums, built-in agents/plugins/prompts
        ↓
Haven.Application
  Service contracts, chat/tool orchestration, availability policy
        ↓
Haven.Infrastructure
  SQLite, Ollama HTTP, filesystem, processes, Git/project intelligence,
  Windows Computer Use

Haven.Browser                 Haven.Automations
  WebView session/data          schedules, leases, worker registration
        ↘                     ↙
             Haven.Desktop
          Avalonia views and view-models

Haven.AutomationWorker
  Closed-app scheduled-action entry point
```

References should continue to point inward. `Core` must not reference UI or infrastructure. `Application` defines the contracts; concrete operating-system and storage behavior belongs in `Infrastructure`, `Browser`, or `Automations`.

## Startup

1. `src/Haven.Desktop/Program.cs` builds the Avalonia application.
2. `App.axaml.cs` registers services and creates `MainWindow` plus `MainWindowViewModel`.
3. When the window opens, `IAppDatabase.InitializeAsync` applies ordered SQLite migrations.
4. `ILegacyStateMigrator` imports the old LocalCode state once when present.
5. `MainWindowViewModel.InitializeAsync` starts the initial chat, loads catalog data and checks Ollama.

Infrastructure registrations live in `Haven.Infrastructure/ServiceCollectionExtensions.cs`. Automation registrations live in `Haven.Automations/ServiceCollectionExtensions.cs`. Desktop-only services such as `ProjectCreationService` are registered in `App.axaml.cs`.

## MVVM pattern

- `.axaml` owns layout and bindings.
- `.axaml.cs` is limited to genuinely view-specific work such as StorageProvider pickers, keyboard events, clipboard access and attaching the native WebView.
- View-models expose state and `RelayCommand` / `AsyncRelayCommand` actions.
- `ObservableObject` implements property notification.
- `App.axaml` maps view-model types to views in `Application.DataTemplates`.

When adding a page, add the view, view-model, code-behind only if needed, and one DataTemplate. Open it through `MainWindowViewModel.AddOrSelectTab` so it participates in the horizontal workspace strip.

## Chat request flow

1. `ChatPageViewModel` prepares the message, attachments, selected agent, prompts, plugins, permissions and project context.
2. `CapabilityPreflightService` checks whether the chosen local model supports the requested image/tool capabilities.
3. `ChatSessionService` builds the system prompt and asks `ToolAvailabilityPlanner` for the exact allowed definitions and execution routes.
4. `OllamaClient` streams normal text or returns structured tool calls.
5. Tool calls execute through the planned runtime: workspace, computer, browser or automation.
6. Results become explicit activity entries with success, timing and file change counts; the final assistant message is stored through `IConversationRepository`.

Never add a tool definition only to the model prompt. Add its execution behavior and availability policy in the same change.

## Project context

`MainWindowViewModel.ActiveProject` is persistent shell context. It remains set when the current tab changes from project home to a project chat, project file, or project settings. The sidebar therefore continues to show project chats and files.

Project chat view-models are kept per project rather than sharing one mutable Studio chat across all projects. `Conversation.ContainerId` is the persistence link between a chat and a project. The project root comes from `ContainerDefinition.RootPath`.

## Local model boundary

`OllamaClient` connects to `http://127.0.0.1:11434` unless `OLLAMA_HOST` overrides it. Haven does not bundle a model. Model discovery, pull/delete operations, streamed chat, structured tool calls and compatibility fallback all go through `IOllamaClient`.
