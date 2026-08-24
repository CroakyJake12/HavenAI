# Mandatory Haven Architecture Rules

- `Haven.Core` owns stable entities, enums, value objects and contracts. Never renumber persisted enum values or recycle stable IDs.
- `Haven.Application` owns use cases, orchestration, routing policy and capability contracts without Windows, Android or SQLite details.
- `Haven.Infrastructure` owns persistence, filesystem, model/provider, OS and external integration implementations.
- `Haven.Desktop` owns shared Avalonia presentation; `Haven.Android` owns Android lifetime/host/provider glue while reusing shared semantic UI.
- Apps are the major visible product architecture. `HavenMode` remains only the compatible persisted conversation/storage capability. Do not reintroduce a parallel Mode product architecture under another name.
- Built-in Apps keep the stable IDs in `BuiltInModeSeed`. Startup reconciles by stable ID; never recycle one.
- `MainView.LaunchAppAsync` and `HavenAppRoutePolicy` are the single launch-routing contract. Every enabled App must open a real working surface or an honest setup-required state.
- App-owned business state is authoritative. GenUI, templates and Floating Activities reflect/share it rather than creating disconnected copies.
- Persistence changes are forward-only. Back up and integrity-check before migration, keep fixtures for every prior schema, and preserve conversations, messages, projects, lessons, tasks, calls, Apps, agents, instructions, capabilities, settings and run history.
- Cancellation flows through asynchronous I/O; rapidly superseded UI results must be rejected.
- Do not create versioned source copies, handoff directories, duplicate page trees or ZIP deliverables unless the user asks.
