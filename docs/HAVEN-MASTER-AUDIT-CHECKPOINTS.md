# Haven — Master Full-Product Audit and Checkpoint Ledger

**Ledger date:** 16 July 2026  
**Repository:** `CroakyJake12/HavenAI`  
**Branch:** `haven-continuation`  
**Purpose:** Replace the earlier Chat/Models-only checkpoint plan with a complete whole-product audit queue.

## Status legend

- **VERIFIED** — implemented and required automated/platform validation passed.
- **SOURCE-COMPLETE** — full source path and meaningful tests exist, but all required build/platform validation has not passed.
- **PARTIAL** — meaningful work exists, but the vertical slice is incomplete.
- **BASELINE-PRESENT** — original audit found the capability, but it has not been revalidated on the continuation head.
- **MISSING** — no meaningful implementation.
- **BLOCKED** — depends on an unresolved external API/platform/hardware decision.
- **RE-AUDIT** — evidence is stale, contradictory or too indirect.

Because the current continuation head has no complete green Windows build/test run, recent continuation work is generally **SOURCE-COMPLETE**, not **VERIFIED**.

Each checkpoint is complete only when its Definition of Done is satisfied through the real user/runtime path.

---

# 0. Build, validation and release control

- **VAL-001 — Clean restore and dependency resolution — MISSING validation.** Done when `dotnet restore Haven.sln` succeeds from a clean clone and advisories/package conflicts are reviewed.
- **VAL-002 — Debug solution build — MISSING validation.** Done when Debug builds with zero errors and zero warnings-as-errors.
- **VAL-003 — Debug test suite — MISSING validation.** Done when all tests run and pass; skipped tests are reviewed.
- **VAL-004 — Release solution build — MISSING validation.** Done when Release builds deterministically with no hidden Debug-only dependency.
- **VAL-005 — Release test suite — MISSING validation.** Done when all tests pass against Release binaries.
- **VAL-006 — Automation Worker build and launch — MISSING validation.** Done when the worker builds, obtains a lease, runs one safe automation and exits cleanly.
- **VAL-007 — Windows desktop startup — MISSING validation.** Done when Haven launches on a clean supported Windows x64 environment without XAML, DI, SQLite or WebView crashes.
- **VAL-008 — Critical-path smoke suite — MISSING.** Done when Chat, Ollama, persistence, temporary chat, attachment, tool cancellation, Browse and automation journeys pass.
- **VAL-009 — Manual-only CI — BASELINE-PRESENT.** Done when workflows remain manual-only, can be dispatched, preserve logs/artifacts and avoid repeated email noise.
- **VAL-010 — Pass evidence discipline — PARTIAL.** Done when every pass updates this ledger and records commits, commands, results, primary docs and unresolved failures.
- **VAL-011 — Release candidate gate — MISSING.** Done when all P0/P1 checkpoints are VERIFIED and a release checklist is signed off.
- **VAL-012 — Merge gate — MISSING.** Done when the actual continuation head is in a reviewed PR, validation is green and explicit merge approval is given.

# 1. Chat, conversations and message lifecycle

- **CHAT-001 — Persistent standard conversations — BASELINE-PRESENT.** Create, rename, reopen, restart and archive/delete pass.
- **CHAT-002 — Temporary chat isolation — BASELINE-PRESENT.** No conversation, message, memory, attachment or retrieval residue after close/restart.
- **CHAT-003 — Conversation branches — SOURCE-COMPLETE.** Persist/switch independent ancestry without duplication.
- **CHAT-004 — Edit-and-branch — SOURCE-COMPLETE.** Editing an earlier user turn preserves original and regenerates only the new branch.
- **CHAT-005 — Overwrite with recoverable versions — SOURCE-COMPLETE.** Prior versions restore and retrieval/usage references remain consistent.
- **CHAT-006 — Assistant response versions — SOURCE-COMPLETE.** Regeneration creates navigable versions with correct active persistence.
- **CHAT-007 — Duplicate-free regeneration — SOURCE-COMPLETE.** Restarts from the correct preceding turn and never duplicates the user message.
- **CHAT-008 — Bookmarks/message tools — SOURCE-COMPLETE.** Bookmark, copy, version, regenerate and branch tools operate from real message UI.
- **CHAT-009 — Draft persistence — SOURCE-COMPLETE.** Composer and draft attachments recover after restart and clear only after successful send/discard.
- **CHAT-010 — Search — SOURCE-COMPLETE.** Accurate, cancellable and bounded; opens correct conversation/message.
- **CHAT-011 — Export — SOURCE-COMPLETE.** Markdown/JSON/text preserve order, metadata and citations without leaking secrets.
- **CHAT-012 — Temporary LAN sharing — SOURCE-COMPLETE.** Tokens expire, server stops, access is read-only and binding is explicit.
- **CHAT-013 — Native message renderer — SOURCE-COMPLETE.** Streaming Markdown, tables, code, lists and maths pass copy/accessibility tests.
- **CHAT-014 — Safe code-block actions — SOURCE-COMPLETE.** Copy is immediate; Run/Apply route through permission-aware tools.
- **CHAT-015 — Context compaction/summaries — BASELINE-PRESENT / RE-AUDIT.** Token-aware, citation-preserving and branch-safe.
- **CHAT-016 — Conversation import — MISSING.** Versioned schema, conflict review and attachment handling.
- **CHAT-017 — Durable sharing/collaboration — MISSING.** Shared conversations, comments, reviews and roles or explicit post-1.0 deferral.
- **CHAT-018 — Message lifecycle stress suite — MISSING validation.** Long chats, failures, restarts, branches, attachments and usage survive.

# 2. Models, providers, routing, usage and cost

- **MODEL-001 — Ollama first-class provider — SOURCE-COMPLETE/baseline dependency.** Discovery, streaming, tools, pull/delete and health pass against real Ollama.
- **MODEL-002 — OpenAI — SOURCE-COMPLETE.** Discovery/selection, streaming, tools, errors and usage pass current official API behaviour.
- **MODEL-003 — Generic OpenAI-compatible — SOURCE-COMPLETE.** Custom URL, model IDs, auth, stream/tool differences and errors are honest.
- **MODEL-004 — OpenRouter — SOURCE-COMPLETE.** Headers, IDs, usage and errors pass official docs.
- **MODEL-005 — Anthropic — SOURCE-COMPLETE.** Messages, tools, streaming, system prompts and usage pass official docs.
- **MODEL-006 — Gemini — SOURCE-COMPLETE.** Native payloads, tools, streaming, safety/errors and usage pass official docs.
- **MODEL-007 — Provider secrets — SOURCE-COMPLETE.** Credential Manager, no JSON/log leakage, delete/update/recovery pass.
- **MODEL-008 — Provider configuration UI — SOURCE-COMPLETE.** Connect/test/edit/disable/remove and validation errors are exposed.
- **MODEL-009 — Provider health/model refresh — SOURCE-COMPLETE.** Timeouts/cancellation/stale models/saved selections pass.
- **MODEL-010 — Capability-aware routing — SOURCE-COMPLETE.** Text, vision, tools, streaming and context requirements filter models.
- **MODEL-011 — Pre-output fallback — SOURCE-COMPLETE.** No fallback after output or tool state.
- **MODEL-012 — Local-to-cloud consent — SOURCE-COMPLETE.** No cloud crossing without explicit policy and visible indication.
- **MODEL-013 — Usage persistence — SOURCE-COMPLETE.** Input/output/cached/reasoning/latency attach to correct response/version/provider.
- **MODEL-014 — Confirmed versus estimated usage — SOURCE-COMPLETE.** Estimates labelled and never replace confirmed values.
- **MODEL-015 — Multi-call aggregation — SOURCE-COMPLETE.** Correct sums across tools, cancellation and fallback.
- **MODEL-016 — Cost calculation — SOURCE-COMPLETE.** Only configured prices/currency; never fabricated local cost.
- **MODEL-017 — Context-window enforcement — PARTIAL / RE-AUDIT.** Provider limits, attachments, retrieval and tools budget before dispatch.
- **MODEL-018 — Model aliases/canonical identity — PARTIAL.** Human names with unambiguous routing keys and migration.
- **MODEL-019 — Provider test matrix — MISSING validation.** Contract tests and optional live tests without CI secrets.
- **MODEL-020 — Model import/training — PARTIAL baseline.** Real runtimes, provenance and safety or explicit deferral.

# 3. Attachments, documents, retrieval and knowledge

- **DATA-001 — Managed attachment storage — SOURCE-COMPLETE.** Copy, limits, hashes and survival after original deletion.
- **DATA-002 — Picker/drag-drop/clipboard — SOURCE-COMPLETE.** One pipeline, Windows UI tests.
- **DATA-003 — Text/source extraction — SOURCE-COMPLETE.** Encoding, binary detection, size and code metadata.
- **DATA-004 — Office extraction — SOURCE-COMPLETE.** DOCX/PPTX/XLSX/ODS representative and malformed files.
- **DATA-005 — PDF extraction — SOURCE-COMPLETE.** Direct text, optional tool discovery and honest OCR/unsupported state.
- **DATA-006 — Image understanding — SOURCE-COMPLETE/provider-dependent.** Type/resizing/privacy/capability checks.
- **DATA-007 — Audio transcription — SOURCE-COMPLETE optional path.** Discovery, cancellation, limits, provenance and unsupported state.
- **DATA-008 — Video understanding — SOURCE-COMPLETE optional path.** Sampling, limits, cleanup, timestamps and honest partial state.
- **DATA-009 — Attachment cleanup — SOURCE-COMPLETE.** Source/derived files/frames/DB/index removed.
- **DATA-010 — Incremental indexing — SOURCE-COMPLETE.** Skip unchanged, remove stale, preserve migrations.
- **DATA-011 — Local embeddings — SOURCE-COMPLETE.** Versioned deterministic fallback with honest quality.
- **DATA-012 — Hybrid search/reranking — SOURCE-COMPLETE.** Lexical/vector, diversity, scope and token budgets.
- **DATA-013 — Citation model — SOURCE-COMPLETE.** Stable/navigable IDs, titles, locations, excerpts and ranges.
- **DATA-014 — Conversation/attachment isolation — SOURCE-COMPLETE.** No scope leakage.
- **DATA-015 — Studio project retrieval — SOURCE-COMPLETE.** Confinement, caps, reparse exclusion and stale cleanup.
- **DATA-016 — Teach subject retrieval — SOURCE-COMPLETE.** Context/instructions/lessons/resources indexed and isolated.
- **DATA-017 — Collections/knowledge base — PARTIAL.** Durable source management, refresh, provenance and deletion.
- **DATA-018 — Website crawling/indexing — MISSING.** Scope, robots/limits, provenance, refresh and deletion.
- **DATA-019 — Citation verification — MISSING.** Generated claims checked against source spans.
- **DATA-020 — Document creation/editing — MISSING.** Word/PowerPoint/spreadsheet/PDF real libraries and round trips.
- **DATA-021 — Python/dataframe/notebook — MISSING.** Sandboxed runtime, packages, cancellation, resource limits, tables/charts.
- **DATA-022 — Data export/deletion — PARTIAL.** Complete export/delete of attachments/indexes/knowledge.

# 4. Do mode, agentic tools, permissions and Duo

- **DO-001 — Multi-turn tool loop — BASELINE-PRESENT.** Inspect, act, observe, continue; final claims consistent.
- **DO-002 — Action/time limits — BASELINE-PRESENT.** Limits, timeouts, cancellation and truncation stress tests.
- **DO-003 — Workspace read tools — BASELINE-PRESENT.** Confinement, encoding, ignores and cancellation.
- **DO-004 — Workspace mutation path — SOURCE-COMPLETE improvement.** All mutations use preview/change-set/transactions.
- **DO-005 — Multi-file transactions — SOURCE-COMPLETE.** Staging, backup, rollback, history and recovery end-to-end.
- **DO-006 — Diff review/accept/reject — SOURCE-COMPLETE / UI revalidation.** Material edits reviewable; rejects never mutate.
- **DO-007 — Selective hunks — MISSING.** Accept/reject hunks/files with atomic apply.
- **DO-008 — Command execution — BASELINE-PRESENT.** Output/exit/process-tree/workdir/encoding pass.
- **DO-009 — Test discovery/execution — BASELINE-PRESENT.** Supported stacks, targeting, timeout and result parsing.
- **DO-010 — Fine-grained command policy — MISSING.** Executable/args/path/network classifications and persistent decisions.
- **DO-011 — File glob policy — MISSING.** Per-workspace read/write glob enforcement and UI.
- **DO-012 — Worktree/sandbox isolation — MISSING.** Risky changes isolated with controlled apply.
- **DO-013 — Containerised execution — MISSING/optional.** Images, mounts, limits, cleanup or explicit deferral.
- **DO-014 — Background agent jobs — MISSING.** Persist/pause/cancel/resume/checkpoint; no mutation replay.
- **DO-015 — Structured task plan — PARTIAL prompt-level.** Persisted steps/dependencies/states/evidence/criteria.
- **DO-016 — Chat invoking Do — MISSING.** Same conversation, visible execution profile, no silent escalation.
- **DO-017 — Studio recommendation — MISSING.** Reviewable move proposal for broad coding requests.
- **DO-018 — Computer Use target safety — PARTIAL.** UI Automation, exact target, inspect-act-verify and hard Stop.
- **DO-019 — Computer Use permissions — PARTIAL.** Classification, approvals, scoped decisions and high-risk blocks.
- **DO-020 — Duo independent sessions — MISSING.** Separate model contexts and explicit turn ownership.
- **DO-021 — Reviewer agent — MISSING.** Independent patch/test validation.
- **DO-022 — Parallel tasks — MISSING.** Isolated jobs, conflicts, cancellation and merge review.
- **DO-023 — Conflict resolution — MISSING.** Compare/merge for simultaneous/external edits.
- **DO-024 — Full agentic journey — MISSING validation.** Inspect→plan→preview→approve→edit→test→repair→evidence.

# 5. Studio and OpenCode-style development platform

- **STUDIO-001 — Project import/discovery — BASELINE-PRESENT.**
- **STUDIO-002 — Project creation — BASELINE-PRESENT.**
- **STUDIO-003 — File tree/editor — BASELINE-PRESENT.** Large trees, external changes, encoding and lifecycle.
- **STUDIO-004 — Editor history/undo/redo — BASELINE-PRESENT.**
- **STUDIO-005 — Unified diff editor — PARTIAL/SOURCE-COMPLETE components.** Syntax-aware hunks/navigation/whitespace.
- **STUDIO-006 — Embedded terminal — MISSING.** Real PTY, input, resize, cancellation and ownership.
- **STUDIO-007 — Persistent shell sessions — MISSING.**
- **STUDIO-008 — Environment manager — MISSING.** Scoped/masked variables and secrets.
- **STUDIO-009 — Build/test/server workflows — BASELINE-PRESENT.**
- **STUDIO-010 — LSP transport/lifecycle — SOURCE-COMPLETE.**
- **STUDIO-011 — Diagnostics — SOURCE-COMPLETE.**
- **STUDIO-012 — Formatting — SOURCE-COMPLETE.**
- **STUDIO-013 — Workspace symbols — SOURCE-COMPLETE.**
- **STUDIO-014 — Go to definition — MISSING.**
- **STUDIO-015 — Find references — MISSING.**
- **STUDIO-016 — Rename symbol — MISSING.**
- **STUDIO-017 — Completion/signature help — MISSING.**
- **STUDIO-018 — Code actions/refactorings — MISSING.**
- **STUDIO-019 — Semantic tokens/highlighting — MISSING.**
- **STUDIO-020 — Repository map/context selection — PARTIAL retrieval.**
- **STUDIO-021 — Fuzzy `@file` references — MISSING.**
- **STUDIO-022 — Project rules/`AGENTS.md` — MISSING.**
- **STUDIO-023 — Agent Skills/`SKILL.md` — MISSING.**
- **STUDIO-024 — Git status/diff/log — BASELINE-PRESENT.**
- **STUDIO-025 — Git commit — MISSING.**
- **STUDIO-026 — Git branches — MISSING.**
- **STUDIO-027 — Git pull/push/fetch — MISSING.**
- **STUDIO-028 — Git merge/rebase/stash — MISSING.**
- **STUDIO-029 — GitHub/GitLab integration — MISSING.**
- **STUDIO-030 — CI status/logs/artifacts — MISSING.**
- **STUDIO-031 — Worktrees/snapshots — MISSING.**
- **STUDIO-032 — MCP client — MISSING.**
- **STUDIO-033 — MCP server — MISSING.**
- **STUDIO-034 — ACP — MISSING.**
- **STUDIO-035 — OpenCode-compatible commands — MISSING.**
- **STUDIO-036 — Custom tool SDK — MISSING.**
- **STUDIO-037 — CLI/TUI/headless API — MISSING.**
- **STUDIO-038 — IDE extension — MISSING.**
- **STUDIO-039 — Session sharing — PARTIAL temporary share.**
- **STUDIO-040 — OpenCode parity acceptance — MISSING.** Explicit current-doc parity matrix and all claimed items VERIFIED.

# 6. Haven Browse

- **BROWSE-001 — Native WebView host — BASELINE-PRESENT.** Windows host/profile/cleanup validation.
- **BROWSE-002 — Navigation controls — BASELINE-PRESENT.**
- **BROWSE-003 — Structured snapshots — SOURCE-COMPLETE.**
- **BROWSE-004 — Reference interaction — SOURCE-COMPLETE.**
- **BROWSE-005 — Model network policy — SOURCE-COMPLETE.**
- **BROWSE-006 — Visible navigation interception — BLOCKED/PARTIAL.** Adapter-specific cancellable navigation-start policy.
- **BROWSE-007 — Form approvals — SOURCE-COMPLETE.**
- **BROWSE-008 — Approved downloads — SOURCE-COMPLETE.**
- **BROWSE-009 — Download manager — PARTIAL.** Progress/cancel/retry/reveal/history/errors.
- **BROWSE-010 — Independent tabs — MISSING.**
- **BROWSE-011 — Background tab lifecycle — PARTIAL.**
- **BROWSE-012 — Tab groups — PARTIAL.** Colours/rename/reorder/collapse/persist.
- **BROWSE-013 — Private profiles — MISSING.** Isolated disposable storage and verified cleanup.
- **BROWSE-014 — Bookmarks/history — BASELINE-PRESENT.**
- **BROWSE-015 — Saved-login prompts — PARTIAL.**
- **BROWSE-016 — Site permissions — MISSING.**
- **BROWSE-017 — Certificate/security UI — MISSING.**
- **BROWSE-018 — Popups/new windows — MISSING.**
- **BROWSE-019 — Find/zoom/print/devtools — PARTIAL.**
- **BROWSE-020 — Screenshots — MISSING.**
- **BROWSE-021 — Safe file upload — BLOCKED/MISSING.**
- **BROWSE-022 — Extension runtime — PARTIAL.**
- **BROWSE-023 — Persistent shell browser — MISSING.**
- **BROWSE-024 — Browser crash recovery — MISSING.**
- **BROWSE-025 — Browse acceptance — MISSING validation.**

# 7. Teach mode

- **TEACH-001 — Subject/lesson hierarchy — BASELINE-PRESENT.**
- **TEACH-002 — Quick Chats — BASELINE-PRESENT.**
- **TEACH-003 — Resources/retrieval — SOURCE-COMPLETE partial vertical slice.**
- **TEACH-004 — Teaching session types — PARTIAL prompt-driven.**
- **TEACH-005 — Quiz engine — MISSING.**
- **TEACH-006 — Answer scoring — MISSING.**
- **TEACH-007 — Question bank — MISSING.**
- **TEACH-008 — Flashcards — MISSING.**
- **TEACH-009 — Spaced repetition — MISSING.**
- **TEACH-010 — Progress/mastery — MISSING.**
- **TEACH-011 — Revision timetable — PARTIAL prompt-level.**
- **TEACH-012 — Curriculum/exam board — MISSING.**
- **TEACH-013 — Marking rubrics — MISSING.**
- **TEACH-014 — Research/citations — PARTIAL retrieval/browser.**
- **TEACH-015 — Whiteboard/canvas — MISSING.**
- **TEACH-016 — Notes/resource export — MISSING.**
- **TEACH-017 — Classroom/collaboration — MISSING/optional.**
- **TEACH-018 — Teach acceptance — MISSING validation.**

# 8. Plan mode

- **PLAN-001 — Collections/tasks/events — BASELINE-PRESENT.**
- **PLAN-002 — Subtasks/recurrence — BASELINE-PRESENT.**
- **PLAN-003 — Core views — BASELINE-PRESENT/PARTIAL.**
- **PLAN-004 — Structured AI proposals — BASELINE-PRESENT.**
- **PLAN-005 — Natural-language capture — PARTIAL.**
- **PLAN-006 — Drag/drop scheduling — MISSING.**
- **PLAN-007 — Dependencies — MISSING.**
- **PLAN-008 — Attachments/comments — MISSING.**
- **PLAN-009 — Gantt/timeline — MISSING/optional.**
- **PLAN-010 — Local reminders — PARTIAL.**
- **PLAN-011 — Google Calendar — PARTIAL scaffold.**
- **PLAN-012 — Microsoft Calendar — PARTIAL scaffold.**
- **PLAN-013 — Conflict resolution — PARTIAL.**
- **PLAN-014 — Shared calendars — MISSING.**
- **PLAN-015 — Location/travel time — MISSING.**
- **PLAN-016 — Email-to-task — MISSING.**
- **PLAN-017 — Plan companion — PARTIAL.**
- **PLAN-018 — Plan acceptance — MISSING validation.**

# 9. Call mode

- **CALL-001 — Session lifecycle — BASELINE-PRESENT.**
- **CALL-002 — Persistent transcript — BASELINE-PRESENT.**
- **CALL-003 — Microphone capture — PARTIAL platform validation.**
- **CALL-004 — Speech-to-text — PARTIAL.**
- **CALL-005 — Text-to-speech — PARTIAL.**
- **CALL-006 — Push-to-talk/mute — BASELINE-PRESENT.**
- **CALL-007 — Barge-in — BASELINE-PRESENT.**
- **CALL-008 — Screen sharing — PARTIAL.**
- **CALL-009 — Parent-child chat link — MISSING.**
- **CALL-010 — Summary hand-back — MISSING.**
- **CALL-011 — In-chat widget/singleton — PARTIAL.**
- **CALL-012 — Camera/video — MISSING/optional.**
- **CALL-013 — Recording/export — MISSING.**
- **CALL-014 — Noise suppression — MISSING.**
- **CALL-015 — Call acceptance — MISSING validation.**

# 10. Scheduled Actions and automations

- **AUTO-001 — Schedule CRUD — BASELINE-PRESENT.**
- **AUTO-002 — Next-run calculation — BASELINE-PRESENT.**
- **AUTO-003 — Run now/history — BASELINE-PRESENT.**
- **AUTO-004 — Duplicate-run leases — BASELINE-PRESENT.**
- **AUTO-005 — Closed-app worker — BASELINE-PRESENT/unvalidated.**
- **AUTO-006 — Friendly schedule builder — MISSING.**
- **AUTO-007 — Condition framework — PARTIAL enum/scaffold.**
- **AUTO-008 — Retry/backoff — MISSING.**
- **AUTO-009 — Failure notifications — PARTIAL.**
- **AUTO-010 — Multi-step workflows — MISSING.**
- **AUTO-011 — Connector actions — MISSING.**
- **AUTO-012 — Per-automation permissions — PARTIAL.**
- **AUTO-013 — Concurrency management — PARTIAL.**
- **AUTO-014 — Templates — MISSING.**
- **AUTO-015 — Import/export — MISSING.**
- **AUTO-016 — Full audit/replay — MISSING.**
- **AUTO-017 — Automation acceptance — MISSING validation.**

# 11. Modes, agents, prompts, plugins and standards

- **EXT-001 — Catalogue persistence — BASELINE-PRESENT.**
- **EXT-002 — Agent application — BASELINE-PRESENT.**
- **EXT-003 — Prompt library — BASELINE-PRESENT.**
- **EXT-004 — Declarative plugins — PARTIAL.**
- **EXT-005 — Executable plugin runtime — MISSING.**
- **EXT-006 — Dependencies/versioning — MISSING.**
- **EXT-007 — Signatures/trust — MISSING.**
- **EXT-008 — Marketplace — MISSING/defer until trust.**
- **EXT-009 — Mode Library — BASELINE-PRESENT/PARTIAL.**
- **EXT-010 — Adaptive ranking — PARTIAL.**
- **EXT-011 — Mode-specific settings — MISSING.**
- **EXT-012 — Custom-mode staging — MISSING.**
- **EXT-013 — Workflow/card execution — MISSING.**
- **EXT-014 — Package tests/dry run — MISSING.**
- **EXT-015 — Rollback/uninstall — PARTIAL.**
- **EXT-016 — Capability quotas — MISSING.**
- **EXT-017 — MCP client — MISSING.**
- **EXT-018 — MCP server — MISSING.**
- **EXT-019 — ACP — MISSING.**
- **EXT-020 — Agent Skills — MISSING.**
- **EXT-021 — Custom commands — MISSING.**
- **EXT-022 — Plugin/tool SDK — MISSING.**
- **EXT-023 — Headless/server API — MISSING.**
- **EXT-024 — Extensibility acceptance — MISSING validation.**

# 12. Shell, Settings and keybindings

- **SHELL-001 — Native shell/navigation — BASELINE-PRESENT.**
- **SHELL-002 — Workspace tabs — BASELINE-PRESENT.**
- **SHELL-003 — Sidebar/recent/pinned — BASELINE-PRESENT.**
- **SHELL-004 — Home/dashboard — BASELINE-PRESENT.**
- **SHELL-005 — App-wide search — PARTIAL.**
- **SHELL-006 — Adaptive mode switcher — PARTIAL.**
- **SHELL-007 — Companion dock — PARTIAL.**
- **SHELL-008 — Companion layout — MISSING.**
- **SHELL-009 — Conversation move/drag-drop — PARTIAL.**
- **SHELL-010 — Multiple windows — MISSING.**
- **SHELL-011 — System tray — MISSING.**
- **SHELL-012 — Global quick overlay — MISSING.**
- **SETTINGS-001 — Searchable settings architecture — MISSING.**
- **SETTINGS-002 — Themes — BASELINE-PRESENT; preview/import/export incomplete.**
- **SETTINGS-003 — Density/text scale — MISSING.**
- **SETTINGS-004 — Reduced motion/high contrast — MISSING application/UI.**
- **SETTINGS-005 — System theme/accent — PARTIAL.**
- **SETTINGS-006 — Notifications — MISSING.**
- **SETTINGS-007 — Data and Backup — MISSING.**
- **SETTINGS-008 — Advanced diagnostics — PARTIAL.**
- **SETTINGS-009 — Per-mode settings — MISSING.**
- **KEYS-001 — Central command registry — PARTIAL.**
- **KEYS-002 — Global custom bindings — MISSING.**
- **KEYS-003 — Chords/scopes — MISSING.**
- **KEYS-004 — Conflict/reserved detection — MISSING.**
- **KEYS-005 — Reset/import/export — MISSING.**
- **KEYS-006 — AI proposals — MISSING/optional.**
- **SHELL-013 — Shell/settings acceptance — MISSING validation.**

# 13. Persistence, reliability and production hardening

- **REL-001 — Ordered SQLite migrations — BASELINE-PRESENT.**
- **REL-002 — Real pre-migration backup — MISSING.**
- **REL-003 — Real SQLite integrity check — MISSING.**
- **REL-004 — Atomic settings storage — PARTIAL; improved in new stores.**
- **REL-005 — Corruption quarantine — PARTIAL.**
- **REL-006 — Settings migrations — PARTIAL.**
- **REL-007 — Rolling diagnostics — MISSING.**
- **REL-008 — Redacted diagnostics export — PARTIAL.**
- **REL-009 — Crash markers/safe mode — BASELINE-PRESENT/PARTIAL.**
- **REL-010 — Retryable initialisation — MISSING.**
- **REL-011 — Deterministic disposal — PARTIAL.**
- **REL-012 — Browser crash cleanup — PARTIAL.**
- **REL-013 — Worker crash cleanup — PARTIAL.**
- **REL-014 — External-file compare/merge — PARTIAL stale checks.**
- **REL-015 — Save-copy flow — MISSING.**
- **REL-016 — Single instance/activation — BASELINE-PRESENT.**
- **REL-017 — Security review — PARTIAL.**
- **REL-018 — Dependency vulnerability review — PARTIAL historical.**
- **REL-019 — Auto-update — MISSING.**
- **REL-020 — Installer/code signing — MISSING.**
- **REL-021 — Long-run soak — MISSING.**
- **REL-022 — Backup/restore acceptance — MISSING.**

# 14. Accessibility

- **A11Y-001 — Accessible names — MISSING broadly.**
- **A11Y-002 — Full keyboard navigation — PARTIAL.**
- **A11Y-003 — Focus management/restoration — PARTIAL.**
- **A11Y-004 — Headings/live regions — MISSING.**
- **A11Y-005 — 150/200% text scale — PARTIAL model only.**
- **A11Y-006 — High contrast — PARTIAL model only.**
- **A11Y-007 — Reduced motion — PARTIAL model only.**
- **A11Y-008 — Screen-reader-optimised mode — PARTIAL model only.**
- **A11Y-009 — Accessible charts/canvas/media — MISSING.**
- **A11Y-010 — Accessibility test matrix — MISSING.**

# 15. Everything AI

## Connectors/accounts

- **CONN-001 — OAuth/account centre — MISSING.**
- **CONN-002 — Connector permission manager — MISSING.**
- **CONN-003 — Gmail — MISSING in Haven.**
- **CONN-004 — Outlook/Microsoft mail — MISSING.**
- **CONN-005 — Google Drive — MISSING.**
- **CONN-006 — OneDrive/SharePoint — MISSING.**
- **CONN-007 — GitHub product connector — MISSING.**
- **CONN-008 — GitLab — MISSING.**
- **CONN-009 — Slack/Discord/Teams — MISSING.**
- **CONN-010 — Contacts — MISSING.**
- **CONN-011 — Cross-service search — MISSING.**
- **CONN-012 — Connector automation actions — MISSING.**

## Research/knowledge

- **RESEARCH-001 — Search provider abstraction — PARTIAL browser navigation.**
- **RESEARCH-002 — Multi-source research agent — PARTIAL prompt-level.**
- **RESEARCH-003 — Provenance — PARTIAL retrieval citations.**
- **RESEARCH-004 — Citation verification — MISSING.**
- **RESEARCH-005 — Deep research reports — MISSING.**
- **RESEARCH-006 — Personal knowledge base — PARTIAL retrieval infrastructure.**

## Media/creative

- **MEDIA-001 — Image generation — MISSING.**
- **MEDIA-002 — Image editing — MISSING.**
- **MEDIA-003 — OCR — MISSING.**
- **MEDIA-004 — Dedicated audio transcription — PARTIAL attachments.**
- **MEDIA-005 — Audio/music generation — MISSING/optional.**
- **MEDIA-006 — Rich video understanding — PARTIAL sampled attachments.**
- **MEDIA-007 — Video generation/editing — MISSING/optional.**
- **MEDIA-008 — Camera input — MISSING.**
- **MEDIA-009 — Diagram generation — MISSING.**
- **MEDIA-010 — Canvas editor — MISSING.**

## Document/data creation

- **DOC-001 — Word creation/editing — MISSING.**
- **DOC-002 — PowerPoint creation/editing — MISSING.**
- **DOC-003 — Spreadsheet analysis/editing — MISSING.**
- **DOC-004 — PDF creation/conversion — MISSING.**
- **DOC-005 — Charts/visualisations — MISSING.**
- **DOC-006 — Template library — MISSING.**

## Platforms/collaboration

- **PLATFORM-001 — Windows desktop release — BASELINE-PRESENT/unvalidated continuation.**
- **PLATFORM-002 — CLI — MISSING.**
- **PLATFORM-003 — TUI — MISSING/optional.**
- **PLATFORM-004 — Web client/server — MISSING.**
- **PLATFORM-005 — Mobile companion — MISSING.**
- **PLATFORM-006 — Local/remote-control API — MISSING.**
- **PLATFORM-007 — Account/cloud sync — MISSING.**
- **PLATFORM-008 — Multiple profiles — PARTIAL.**
- **PLATFORM-009 — Team workspaces — MISSING.**
- **PLATFORM-010 — Shared conversations/comments/reviews — MISSING.**
- **PLATFORM-011 — Role-based access — MISSING.**
- **PLATFORM-012 — Platform acceptance — MISSING validation.**

# 16. Training and evaluation

- **TRAIN-001 — Training runtime uses real agent/tools — BASELINE-PRESENT/RE-AUDIT.**
- **TRAIN-002 — Complete action logging — BASELINE-PRESENT.**
- **TRAIN-003 — Grounded scoring — PARTIAL heuristic.**
- **TRAIN-004 — Reproducible evaluations — MISSING.**
- **TRAIN-005 — Comparative model/provider evaluation — MISSING.**
- **TRAIN-006 — Reward/fine-tuning export — MISSING/optional.**
- **TRAIN-007 — Evaluation acceptance — MISSING validation.**

# 17. Release completion rule

Production-ready requires:

1. every P0/P1 checkpoint VERIFIED;
2. every remaining PARTIAL/MISSING item explicitly classified post-1.0 and removed from misleading UI/marketing;
3. Debug and Release matrices green;
4. clean install, upgrade, rollback and uninstall green;
5. migration, backup and restore green;
6. security review of browser, Computer Use, providers, plugins and connectors;
7. secret-leak tests green;
8. accessibility matrix green;
9. long-run soak green;
10. documentation matching actual runtime;
11. a reviewed PR containing the real continuation head;
12. explicit user approval to merge.
