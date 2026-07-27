# Haven — Full Project Continuation Handover

**Handover date:** 16 July 2026  
**Repository:** `CroakyJake12/HavenAI`  
**Active development branch:** `haven-continuation`  
**Protected base branch:** `main`  
**Base commit:** `a720516064676a40cc1c73d210f00dcb587d1fd0`  
**Last implementation head before this handover:** `d49b94c64c02ce6730b09656c6dcfe3fcf814354`  
**Branch distance before this handover:** 210 commits ahead of `main`, 0 behind  
**Merge status:** Do not merge. The continuation branch has not passed a complete Windows build/test/smoke gate.  
**CI policy:** Keep GitHub Actions manual-only. Do not enable push/PR workflows that generate repeated failure emails.

---

## 1. Immediate instruction to the next agent

Continue the real Haven implementation from `haven-continuation`.

Do not:

- restart from the old Pass 8 Go/browser-hosted implementation;
- restart from `main`;
- assume draft PR #1 contains the latest continuation work;
- create placeholder interfaces, fake cards, disconnected services, or prompt-only simulations and call them complete;
- mark a checkpoint complete because source files exist;
- merge the branch;
- silently enable cloud providers, language servers, browser mutations, Computer Use, external workers, or executable plugins;
- claim a build, test, platform API, or Windows behaviour succeeded unless it was actually run and the result was inspected.

The next agent must read, in order:

1. `docs/START-HERE-CONTINUATION.md`
2. `docs/HAVEN-FULL-PROJECT-HANDOVER.md`
3. `docs/HAVEN-MASTER-AUDIT-CHECKPOINTS.md`
4. `docs/HAVEN-CONTINUATION-VALIDATION.md`
5. `docs/CODE-INTELLIGENCE.md`
6. `docs/BROWSER-AUTOMATION-SAFETY.md`
7. `docs/PASS-REPORT-WORKSPACE-CHANGE-SETS.md`
8. `docs/PASS-REPORT-BROWSER-DOWNLOAD-FINALIZATION.md`
9. the latest commits and changed files on `haven-continuation`

The complete audit is now represented by the master checkpoint ledger in the repository. A future agent should not require the original uploaded audit text to understand the remaining scope.

---

## 2. How to access the repository

### Normal Git clone

The repository is private, so the GitHub account must have access.

```powershell
git clone https://github.com/CroakyJake12/HavenAI.git
cd HavenAI
git fetch --all --prune
git switch --track origin/haven-continuation
```

When the branch already exists locally:

```powershell
cd HavenAI
git fetch origin --prune
git switch haven-continuation
git pull --ff-only origin haven-continuation
```

If `git switch --track` reports that the local branch already exists:

```powershell
git switch haven-continuation
git branch --set-upstream-to=origin/haven-continuation haven-continuation
git pull --ff-only
```

### GitHub CLI authentication

Do not paste a personal access token into a chat or source file.

```powershell
gh auth login
gh repo view CroakyJake12/HavenAI
```

### Connected-agent access

A GitHub-enabled agent should use:

```text
Repository: CroakyJake12/HavenAI
Branch: haven-continuation
```

It must pass `ref: haven-continuation` when reading files and `branch: haven-continuation` when writing files.

Some connector branch-search calls have incorrectly returned an empty result even while direct branch reads and writes worked. Verify the branch through a direct file fetch, commit fetch, or `compare_commits`, rather than deciding the branch is missing from a failed branch-search result.

### Important PR warning

Draft PR #1 is:

```text
codex/haven-chat-model-production-pass -> main
```

It represents the earlier Chat/Models phase and does **not** automatically contain the later `haven-continuation` commits. Do not review or merge PR #1 as though it is the complete continuation branch.

Create a new draft PR from `haven-continuation` only after the branch builds and tests, or deliberately retarget/supersede the old PR after reviewing its effect. Never merge merely to make the branch easier to find.

---

## 3. Solution and runtime target

```text
Haven.sln
├─ src/Haven.Core
├─ src/Haven.Application
├─ src/Haven.Infrastructure
├─ src/Haven.Browser
├─ src/Haven.Automations
├─ src/Haven.AutomationWorker
├─ src/Haven.Desktop
├─ tests/Haven.Core.Tests
├─ tests/Haven.Infrastructure.Tests
└─ tests/Haven.Desktop.Tests
```

Primary target:

- Windows 10/11 x64
- .NET 10
- Avalonia 12
- native desktop lifetime
- local-first operation
- local Ollama as a first-class provider
- editable `.axaml` UI
- SQLite persistence
- no hidden Go/JavaScript application sidecar
- no browser-hosted shell pretending to be native

Main startup project:

```text
src/Haven.Desktop/Haven.Desktop.csproj
```

Background automation executable:

```text
src/Haven.AutomationWorker/Haven.AutomationWorker.csproj
```

---

## 4. Non-negotiable product and engineering rules

### Product

- Haven Chat, Teach, Do, Studio, Browse, Plan, Call and Training remain first-class surfaces.
- Ollama remains a complete local provider rather than a compatibility fallback.
- Cloud providers are optional and require explicit configuration.
- Temporary chats must not leak into persistent history or memory.
- Existing user data and LocalCode migrations must be preserved.
- The user-facing UI must be native and editable.
- Features must be discoverable through the real UI and execution paths.
- “Everything AI” means the complete product, not merely Chat plus model providers.

### Engineering

- Use dependency injection and existing repository/service boundaries.
- Add ordered schema migrations for persistent changes.
- Use unique temporary files and atomic replacement.
- Validate cancellation, cleanup, crash recovery, and stale-state behaviour.
- Inspect every affected call site, constructor, DI registration and XAML binding.
- Remove superseded implementations rather than leaving two active paths.
- Prefer integration/end-to-end tests through public runtime entry points.
- External APIs, SDKs, protocols and framework behaviour must be checked against current primary documentation.
- Record consulted primary documentation in pass reports.
- Never convert an unavailable capability into a misleading “success” message.
- Avoid reflection/bootstrap injection where a stable direct integration can be implemented safely; existing bootstrap controls should be replaced during the relevant subsystem cleanup when practical.

### Safety

- No workspace access outside the selected root.
- No generic unrestricted file-system mutation.
- No browser model access to raw CSS selectors.
- No model filling of password, payment, file, hidden or one-time-code fields.
- No unapproved form submission or download.
- No Computer Use action without exact target binding and post-action verification.
- No executable plugin or language-server command sourced automatically from a repository.
- No secrets in JSON settings, logs, audit records, prompts or Git.
- No automatic replay of tool actions after a crash.
- No fallback to another model after output or tool side effects have begun.

---

## 5. Trust boundary and current validation state

The continuation branch contains substantial implementation and tests, but there is still no confirmed complete build/test result for its current head.

The following must be treated as **unrun**, not passed:

```powershell
dotnet restore Haven.sln
dotnet build Haven.sln -c Debug --no-restore
dotnet test Haven.sln -c Debug --no-build
dotnet build Haven.sln -c Release --no-restore
dotnet test Haven.sln -c Release --no-build
dotnet build src\Haven.AutomationWorker\Haven.AutomationWorker.csproj -c Release --no-restore
```

The current branch also needs Windows smoke validation for:

- Avalonia startup and XAML loading;
- WebView host and navigation events;
- Windows Credential Manager;
- Windows Task Scheduler;
- microphone, speech and screen capture;
- language-server process lifecycle;
- browser approval UI;
- local Ollama streaming and tool calls;
- provider secrets and cloud-provider requests;
- file pickers, clipboard and drag/drop;
- download completion and cleanup;
- installer/publish output.

A source-complete checkpoint remains source-complete but unvalidated until these gates pass.

---

## 6. Work completed on `haven-continuation`

### 6.1 Conversation, attachments, retrieval and rendering

Implemented in source:

- persistent conversation branches;
- message edit branches and overwrite recovery versions;
- assistant response versions;
- regeneration without duplicating the user turn;
- bookmarks;
- conversation search;
- persistent composer drafts;
- persistent attachment drafts and restart recovery;
- Markdown, JSON and text export;
- token-protected temporary LAN sharing;
- persistent managed attachment storage;
- text/source, DOCX, PPTX, XLSX, ODS, PDF, image, audio and video processing paths;
- honest direct/extracted/transcribed/sampled/metadata-only/unsupported states;
- native Markdown rendering;
- headings, lists, tasks, quotes, tables, fenced code and bounded LaTeX;
- explicit Copy / Ask to run / Ask to apply code actions;
- local hybrid retrieval;
- deterministic local embeddings;
- conversation, attachment, Studio project, Teach subject and collection scopes;
- citations and composer insertion;
- bounded Studio project indexing and stale-source cleanup.

These are substantial source implementations, not yet a green Windows release.

### 6.2 Model providers, routing, metering and cost

Implemented in source:

- provider-neutral registry and routing;
- Ollama;
- OpenAI;
- generic OpenAI-compatible;
- OpenRouter;
- Anthropic;
- Gemini;
- Windows Credential Manager secret storage;
- provider settings UI;
- provider health and model discovery;
- explicit fallback chains;
- explicit local-to-cloud fallback permission;
- no fallback after streamed output or tool state;
- usage persistence;
- provider-confirmed and clearly estimated usage;
- input/output/cached/reasoning token accounting;
- multi-call aggregation;
- user-configured pricing and currencies;
- local models do not receive fabricated cloud costs.

### 6.3 Workspace change sets and coding core

Implemented in source:

- multi-file transactional changes;
- root confinement;
- duplicate-target rejection;
- staging before mutation;
- backups;
- rollback after partial failure;
- transaction IDs and changed-path reporting;
- structured change sets;
- diff generation;
- preview and explicit accept/reject paths;
- transaction-backed apply;
- stale-file protection;
- edit history and rollback integration;
- workspace tool runtime wiring;
- tests for traversal, duplicate targets, rollback and public tool paths.

### 6.4 Code intelligence

Implemented in source:

- configurable trusted LSP definitions;
- disabled-by-default language-server suggestions;
- LSP 3.18 stdio framing and lifecycle;
- diagnostics;
- workspace symbols;
- formatting requests;
- UTF-16 text positions;
- cancellation and process-tree cleanup;
- bounded stderr and protocol messages;
- `.NET` build diagnostic fallback without restore;
- bounded lexical symbol fallback;
- formatting preview;
- SHA-256 stale-file check;
- transaction-backed formatting application;
- Studio UI and Settings UI;
- public-service integration tests.

Not yet implemented as a complete IDE:

- go to definition;
- references;
- rename;
- completion;
- code actions/refactorings;
- semantic tokens;
- full symbol outline;
- multi-server workspace orchestration.

### 6.5 Browser automation and safety

Implemented in source:

- bounded structured page snapshots;
- temporary `haven-N` references;
- removal of raw-selector model tools;
- sensitive-field refusal;
- fingerprint-bound approval for form submission;
- approval-gated downloads;
- Browser safety UI;
- pending, approved, rejected, executed, failed and expired states;
- action serialisation;
- interrupted-action recovery without replay;
- persistent audit and download records;
- secret-redacted signed URL handling;
- public-network navigation policy;
- loopback/private/reserved address blocking;
- redirect-by-redirect checks;
- DNS-pinned `SocketsHttpHandler.ConnectCallback`;
- cookie/proxy-disabled pinned requests;
- bounded headless page extraction;
- streamed download limits;
- atomic download completion;
- SHA-256 records;
- tests using public runtime paths and local TCP servers.

Known browser limitations remain:

- visible WebView page-script/redirect navigation is not yet intercepted through a cancellable adapter-specific navigation-start event;
- tabs still require a true independent-host implementation;
- private tabs need isolated disposable profiles;
- model-controlled file uploads remain unavailable;
- popup/new-window handling, site permissions, certificate UI, screenshots, find/zoom and crash recovery remain incomplete.

---

## 7. Current repository evidence

Before this handover, `haven-continuation` was 210 commits ahead of `main`.

Important implementation documents already in the branch:

```text
docs/CODE-INTELLIGENCE.md
docs/BROWSER-AUTOMATION-SAFETY.md
docs/HAVEN-CONTINUATION-VALIDATION.md
docs/PASS-REPORT-WORKSPACE-CHANGE-SETS.md
docs/PASS-REPORT-BROWSER-DOWNLOAD-FINALIZATION.md
scripts/validate-continuation.ps1
```

The branch contains major new source areas under:

```text
src/Haven.Application
src/Haven.Browser
src/Haven.Core
src/Haven.Desktop
src/Haven.Infrastructure
tests/Haven.Core.Tests
tests/Haven.Desktop.Tests
tests/Haven.Infrastructure.Tests
```

The master checkpoint ledger is the authoritative whole-product queue after this handover.

---

## 8. How to execute future major passes

A major pass must complete at least three substantial, dependency-linked checkpoints or one complete subsystem tranche.

A valid pass includes:

1. reading the handover, master audit and latest branch state;
2. choosing a coherent tranche;
3. inspecting all affected code and call sites;
4. checking primary external documentation;
5. implementing domain, persistence, runtime, UI, permissions, cancellation, recovery and diagnostics;
6. replacing conflicting placeholder paths;
7. adding integration tests through the real entry point;
8. running available validation;
9. fixing failures;
10. committing logical changes;
11. updating checkpoint statuses and evidence;
12. leaving a precise pass report.

A pass is not acceptable when it only adds:

- an interface;
- a record type;
- a repository with no caller;
- a settings card with no runtime effect;
- an isolated helper;
- a prompt pretending to be a feature;
- TODO comments;
- disabled placeholder buttons;
- tests that never exercise the real runtime.

---

## 9. Required status language

Use these exact status classes in the master audit:

- **VERIFIED** — implemented and the required automated plus platform gates passed.
- **SOURCE-COMPLETE** — fully wired source and tests exist, but required build/platform gates have not all run.
- **PARTIAL** — meaningful implementation exists, but one or more required vertical layers are missing.
- **BASELINE-PRESENT** — present in the original app and audit, but not revalidated on the continuation head.
- **MISSING** — no meaningful implementation.
- **BLOCKED** — cannot be completed until an external platform/API/hardware decision is resolved.
- **RE-AUDIT** — evidence is stale or contradictory and the code must be inspected before choosing another status.

Do not use a green checkmark for `SOURCE-COMPLETE`.

---

## 10. Recommended execution order

### Gate 0 — branch compilation and runtime stabilisation

This is mandatory before claiming any continuation checkpoint is verified.

- restore;
- Debug build;
- Debug tests;
- Release build;
- Release tests;
- AutomationWorker build;
- launch;
- inspect XAML/DI/runtime failures;
- fix all warnings-as-errors;
- rerun until green.

### Wave 1 — finish the coding platform

- resolve compile/integration faults in change sets and LSP;
- proper unified diff and selective hunks;
- embedded terminal and persistent sessions;
- full Git workflows;
- worktrees/sandboxes;
- project rules and `AGENTS.md`;
- MCP;
- Agent Skills;
- custom commands/tools;
- CLI/headless API;
- IDE integration.

### Wave 2 — complete Browse

- adapter-specific cancellable navigation interception;
- real independent tabs;
- isolated disposable private profiles;
- downloads UI finalisation;
- safe user-approved uploads;
- popup handling;
- permissions;
- certificate/security surfaces;
- screenshots;
- find/zoom;
- browser crash recovery;
- persistent shell browser host.

### Wave 3 — complete Teach, Plan and Call

- dedicated quiz/mastery/spaced-repetition systems;
- curriculum/import/export;
- real calendar provider sync;
- drag/drop and dependencies;
- child-call linking, summary hand-back and transcript links;
- device runtime validation and call export.

### Wave 4 — complete automations and cross-mode orchestration

- visual schedules;
- true condition evaluation;
- retries/backoff;
- multi-step workflows;
- connector actions;
- concurrency;
- replay/audit;
- Chat using Do;
- Studio recommendation and atomic move;
- persistent companion sessions.

### Wave 5 — complete modes, agents, plugins and open standards

- executable plugin runtime;
- package staging and rollback;
- dependency/version/signature model;
- MCP client/server;
- ACP;
- Skills;
- custom tool/plugin SDK;
- workflow/card execution;
- quotas and sandboxing;
- marketplace only after trust model is complete.

### Wave 6 — complete shell, settings, reliability and accessibility

- settings architecture and search;
- full keybinding registry;
- persistent dock/layout;
- multiple windows/tray/quick overlay;
- settings migrations and corruption recovery;
- rolling diagnostics;
- real SQLite integrity and pre-migration backup;
- deterministic shutdown;
- crash loops/safe mode;
- full accessibility application and validation.

### Wave 7 — Everything AI

- connectors and OAuth account centre;
- cross-service search;
- research provenance and citation verification;
- personal knowledge base;
- document creation/editing;
- Python/dataframe/notebook runtime;
- media generation/editing;
- CLI/TUI/web/mobile/server;
- sync, teams, shared conversations and reviews.

### Wave 8 — release

- security review;
- dependency/advisory review;
- installer;
- code signing;
- update channel;
- rollback;
- release notes;
- clean-machine install;
- long-running reliability;
- accessibility sign-off;
- privacy/data export/deletion sign-off.

---

## 11. Immediate next-agent opening message

> Continue Haven from the private GitHub repository `CroakyJake12/HavenAI`, branch `haven-continuation`. Read `docs/START-HERE-CONTINUATION.md`, `docs/HAVEN-FULL-PROJECT-HANDOVER.md`, and `docs/HAVEN-MASTER-AUDIT-CHECKPOINTS.md` before editing. Do not use `main` or assume draft PR #1 contains the continuation work. First run the complete restore/build/test matrix and fix all failures. Then complete a large dependency-linked tranche from the master checkpoint ledger, including real runtime/UI wiring, migrations, permissions, recovery, primary-documentation checks and integration tests. Never merge or mark source-only work verified.

---

## 12. Definition of project completion

Haven is not complete when every screen contains a card. It is complete only when:

- all mandatory master checkpoints are `VERIFIED`;
- no core checkpoint remains `SOURCE-COMPLETE`, `PARTIAL` or `RE-AUDIT`;
- optional deferred features are explicitly documented as post-1.0 rather than implied complete;
- full Debug and Release matrices pass;
- clean-machine Windows installation works;
- upgrade and rollback work;
- data migration and recovery work;
- security and secret handling pass review;
- accessibility passes keyboard, screen reader, scaling, contrast and reduced-motion testing;
- the user can perform the complete Chat, Do, Studio, Browse, Teach, Plan, Call and automation journeys without hidden manual repair;
- documentation reflects actual runtime behaviour;
- the branch receives a reviewed release PR and only then merges.
