# Haven Native Architecture

## Design goals

Haven uses one typed .NET codebase and one durable state model. The Avalonia project owns views and desktop lifetime only; business rules live outside the UI.

## Dependency direction

```text
Haven.Core
   ↑
Haven.Application
   ↑          ↑
Infrastructure Browser Automations
          \     |     /
             Desktop

Infrastructure + Automations → AutomationWorker
```

`Haven.Core` has no platform dependencies. `Haven.Application` defines repository/runtime interfaces and orchestration. Infrastructure implements those interfaces. Desktop composes the graph through Microsoft.Extensions.DependencyInjection.

## Persistence

`SqliteDatabase` applies numbered migrations inside a transaction. The database stores:

- conversations and messages
- mode containers and lessons
- agents and plugins
- automation definitions, leases and run history
- settings and migration markers

Temporary chats bypass repository writes. Uploaded file bytes remain on disk; only app state belongs in SQLite.

## Ollama

`OllamaClient` uses `/api/tags` for model discovery and `/api/chat` for streamed or one-shot generation. Cancellation tokens flow into HTTP reads. A capability preflight runs before a request so image or tool requirements can stop early and suggest another installed model.

## Workspace tools

`WorkspaceToolService` canonicalises every path against the selected root. Traversal outside that root throws before I/O. Writes use a temporary file followed by an atomic replace. Process execution avoids a shell by default, redirects output, limits captured text, supports timeout/cancellation and only terminates the process tree started by Haven.

## Browser isolation

`BrowserSessionService` exposes typed navigation and DOM operations and only attaches to the WebView hosted inside Haven. Desktop creates the native control and attempts to set a dedicated profile directory. Browser Use never targets the user's ordinary browser instance.

The native WebView package API and profile event sequence still require a physical Windows test before this is called production-complete.

## Automations

The desktop and worker share the same SQLite repository and domain records. A worker pass:

1. reads due definitions;
2. atomically acquires a lease;
3. runs the local model;
4. writes the result or failure;
5. calculates the next UTC run;
6. clears the lease.

Windows Task Scheduler starts only the small worker executable every five minutes. Duplicate workers cannot execute the same automation while a valid lease exists.

## Safety boundary

Computer Use is not represented as complete. The UI and prompts explicitly avoid claiming actions without tool results. Workspace file/process tools are implemented; target-bound Windows UI Automation and inspect → act → verify still need a dedicated, physically tested project before the `@ComputerUse` plugin should be enabled for real actions.
