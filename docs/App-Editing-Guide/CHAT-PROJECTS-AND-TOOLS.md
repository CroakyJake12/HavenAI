# Chat, Projects and Tools

## Project chats

A Studio project is a `ContainerDefinition` with `Mode = Studio` and a real `RootPath`. Project chats store that project ID in `Conversation.ContainerId`.

The active project is shell state, not page-type state. This matters because the sidebar must remain project-specific while the user is in a chat, file editor or project settings page.

Creating or connecting a project refreshes Studio containers, selects the project and creates a clean project chat. The project sidebar's New project chat command creates another conversation while retaining the selected project.

## What a project chat can do

When mode is Do/Studio and the selected root exists, the planned workspace tools can:

- list, read and search project files;
- write a file atomically or replace exact text;
- record before/after versions and line counts;
- run the typed test command;
- run an arbitrary command only when Full Access allows it.

The project context also includes Git branch/worktree/commit/build/error state and saved decisions. Studio system instructions require inspect-before-edit, real validation, concise impact estimates, changelogs, plain-English errors, decision warnings, risk-aware testing and honest evidence.

Project home supplies direct user actions for editor, terminal, local server, build, detected tests, Git initialization and origin connection. Test detection chooses `npm test` for `package.json`, `cargo test` for `Cargo.toml`, `python -m pytest` for Python test metadata, and otherwise `dotnet test`. Origin connection accepts validated HTTP(S), SSH, `git://`, or `git@host:path` URLs and adds or replaces `origin`; it never commits or pushes automatically.

## Tool availability matrix

`ToolAvailabilityPlanner` is the source of truth. The plugin picker uses `ChatSessionService.CanActivatePlugin`, so it does not show functional plugins that cannot operate in the current context.

| Capability | Required context |
|---|---|
| Read/list/search files | Do or Studio plus an existing selected root |
| Write/replace files | Same, plus Auto Safe or Full Access file permission |
| Typed tests | Same, plus Auto Safe or Full Access command permission |
| Arbitrary command | Same, plus Full Access command permission |
| `@WebSearch` | local browser runtime and browser permission; background navigate/read only |
| `@BrowserUse` | attached native Browse host; interactive actions require Full Access |
| `@ComputerUse` | Windows and explicit per-pass enablement |
| `@Automate` / `@Macro` | Do or Studio plus local automation storage |
| `@Test` | Do or Studio, existing project root and command permission |

Tool dispatch is by the exact route returned by the plan. Do not restore broad `StartsWith` routing; it can accidentally send a newly named tool to the wrong runtime.

## Permissions

Permissions are `Ask`, `AutoSafe` and `FullAccess`. Ask can be approved for one message through the inline permission banner. Computer Use also has an explicit enable banner. Keep mutations bounded even under Full Access: workspace confinement, target-bound desktop actions, browser host checks and action limits still apply.

## Live activity and “reasoning”

Haven does not reveal or store private chain-of-thought. It does provide user-verifiable work signals:

- streamed assistant text;
- a current-message activity area;
- tool name, result detail, success, duration and file line counts;
- a floating edit widget with step and aggregate `+/-` lines;
- real build/test/command output;
- Stop at the next safe boundary.

When adding a new long-running operation, surface an explicit activity state and evidence. Do not label invented prose as internal reasoning.

## Markdown and LaTeX

Chat stores the original message unchanged. `Controls/MarkdownView.cs` renders it into local Avalonia controls.

Supported Markdown includes headings, paragraphs, bold, italics, inline code, ordered/unordered/task lists, blockquotes, rules, fenced code and tables. Links render as their label plus URL rather than launching an external browser, and images render as labelled references rather than fetching remote content.

Inline `$…$` and display `$$…$$` math use a local formatter and math fonts. Common fractions, square roots, scripts, Greek letters, sums, integrals, arrows, comparison and set operators are rendered without a WebView or remote MathJax dependency. Add commands in `LatexFormatter.Commands`; extend parsing with tests when adding more complex notation.

The context-menu copy action always copies the original Markdown/LaTeX source, not the visual approximation.

## Built-in catalog

Functional plugins are defined by `PluginCatalog` in `Haven.Core/PluginCatalog.cs`. Reusable instruction-only behavior belongs in `PromptCatalog` and is invoked with `>`. Built-ins are seeded by `CatalogRepository`; custom items live in the same database tables.

If you add a functional plugin:

1. define its catalog entry and allowed modes;
2. implement a typed runtime and definitions;
3. add availability policy and an exact execution route;
4. add permission/preflight behavior;
5. add matrix and tool-loop tests;
6. only then expose it in the picker.
