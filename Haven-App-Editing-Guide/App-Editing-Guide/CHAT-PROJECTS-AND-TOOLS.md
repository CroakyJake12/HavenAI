# Chat, Projects and Tools

## Container types are not interchangeable

Haven reuses `ContainerDefinition` for several organizational concepts, but capability follows mode and root—not merely the presence of a container ID.

| Container | Mode | Local root | Workspace/Git/build/test tools |
|---|---|---:|---:|
| Chat Group | Chat | none | no |
| Teach Subject | Teach | none | no |
| Task Group | Do | optional/contextual | only with a valid allowed root |
| Studio Project | Studio | required | yes, subject to permission/runtime |

Group reference files are bounded shared context. They do not turn a group into a project or grant filesystem access.

## Project chats

A Studio project is a container with `Mode = Studio` and a real `RootPath`. Project chats store the project ID in `Conversation.ContainerId`.

`MainWindowViewModel.ActiveProject` is shell state, so the sidebar remains project-specific while the user moves between project home, chat, editor and settings. Project chat view-models are retained per project rather than sharing one mutable Studio conversation.

Creating or connecting a project refreshes Studio containers, selects the project and creates a clean project chat. New project chat creates another conversation while retaining the selected project.

## What a project chat can do

When the selected Do/Studio root exists and permissions allow it, the planned workspace tools can:

- list, read and search project files;
- write a file atomically or replace exact text;
- record before/after versions and line counts;
- run a typed test command;
- run an arbitrary command only under Full Access.

Project context includes Git branch/worktree/commit/build/error state and saved decisions. Studio instructions require inspect-before-edit, real validation, concise impact estimates, changelogs, plain-English errors, decision warnings and honest evidence.

Project home supplies editor, terminal, local server, build, detected tests, Git initialization and origin connection. Test detection chooses `npm test`, `cargo test`, `python -m pytest`, or otherwise `dotnet test`. Origin URLs are validated; Haven never commits or pushes automatically.

## Tool availability matrix

`ToolAvailabilityPlanner` is the source of truth. The picker and `ChatSessionService` consume the same plan.

| Capability | Required context |
|---|---|
| Read/list/search project files | Do or Studio plus an existing selected root |
| Write/replace project files | same, plus Auto Safe or Full Access file permission |
| Typed tests | same, plus Auto Safe or Full Access command permission |
| Arbitrary command | same, plus Full Access command permission |
| `@WebSearch` | browser runtime and browser permission; background navigate/read only |
| `@BrowserUse` | attached native Browse host; interactive actions require Full Access |
| `@ComputerUse` | Windows and explicit per-pass enablement |
| `@Automate` / `@Macro` | Do or Studio plus local automation storage |
| `@Test` | Do or Studio, existing project root and command permission |
| Chat Group shared references | exact group scope; context/vision attachment only, never workspace tools |

Dispatch uses the exact route returned by the plan. Do not use broad prefix matching; a newly named tool could otherwise reach the wrong runtime.

## Planner tool boundary

Plan uses the structured `planner_propose_changes` path. It returns a proposal for review, not an immediate mutation. Planner entities are separate from scheduled AI Automations, even if both appear on Home statistics or a calendar-like screen.

Do not expose planner repository mutation methods as ordinary chat tools without retaining validation, human-readable preview and explicit Apply.

## Permissions

Permissions are Ask, Auto Safe and Full Access. Ask can be approved for one message through the inline banner. Computer Use also has an explicit enable banner. Full Access does not remove confinement, platform checks, target binding, browser host checks or action limits.

## Live activity and “reasoning”

Haven does not reveal or store private chain-of-thought. It provides user-verifiable work signals:

- streamed assistant text;
- current-message activity;
- tool name, result, success, duration and changed-line counts;
- edit-step and aggregate `+/-` counts;
- real build/test/command output;
- Stop at the next safe boundary.

Call exposes its own deterministic state (Listening, Transcribing, Thinking, Speaking, Paused/Error), not fabricated internal reasoning. When adding any long-running operation, expose a cancellable state and real evidence.

## Markdown and LaTeX

Chat stores original message source unchanged. `Controls/MarkdownView.cs` renders it into local Avalonia controls.

Supported Markdown includes headings, paragraphs, bold, italics, inline code, ordered/unordered/task lists, blockquotes, rules, fenced code and tables. Links render as label plus URL rather than launching an external browser, and images render as labelled references rather than fetching remote content.

Inline `$…$` and display `$$…$$` math use the local formatter and math fonts. Common fractions, roots, scripts, Greek letters, sums, integrals, arrows, comparison and set operators render without WebView/remote MathJax. Add simple command mappings to `LatexFormatter.Commands`; extend parsing and tests for more complex notation.

The context-menu Copy action copies the original Markdown/LaTeX source, not the visual approximation. Malformed or partial streaming input must fall back to readable text rather than crash layout.

## Built-in catalog and dashboard manifests

Functional plugins are defined by `PluginCatalog` in `Haven.Core/PluginCatalog.cs`. Reusable instruction-only behavior belongs in `PromptCatalog`. Built-ins are seeded by `CatalogRepository`; custom items use the same tables.

If you add a functional plugin:

1. define its catalog entry and allowed modes;
2. implement a typed runtime and definitions;
3. add availability policy and an exact route;
4. add permission/preflight behavior;
5. add matrix and tool-loop tests;
6. only then expose it in the picker.

`PluginDefinition.DashboardTilesJson` is declarative metadata. Home allow-lists provider and navigation keys. Never place type names, assemblies, scripts or executable expressions in a dashboard tile manifest.
