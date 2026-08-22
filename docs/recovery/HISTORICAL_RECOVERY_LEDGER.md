# Haven Historical Recovery / Supersession Ledger

**Status:** Active convergence ledger  
**Authoritative integration branch:** `use-this-branch`

Rule: never restore a historical branch wholesale. Recover only unique still-valid code or requirements after comparison with current Haven. Never delete/reset/stash/clean/retire historical worktrees during this audit. Invalid/missing Sandbox registration does not prove a branch or commit is valueless.

## Classifications

- **SUPERSEDED / PRESERVED** — equal or greater current capability exists.
- **PARTIALLY SUPERSEDED** — meaningful current implementation exists, but valid depth remains missing.
- **STILL RELEVANT — REQUIREMENT** — requirement remains valid; unique historical code has not been proven.
- **STILL RELEVANT — CODE** — unique historical implementation is a recovery candidate.
- **NEEDS VERIFICATION** — evidence is insufficient for a safe conclusion.

## P0 — Worker 8 / Imagine

Historical committed baseline: `e73f2be` (`int(UI-A): INT-003 dedicated Imagine and Vision production workspaces`) on `results-day-ui-migration-a` (`80c96c85-4923-4b3a-813f-d7d529ab3da4`). Current comparison: Worker 14 (`c44c4471-cb35-4af7-a5ca-3f58ccd562dc`). The previously attributed `results-day-phase2-worker8-haven-ui` worktree is system-UI/TopRail work, not Imagine. `results-day-imagine-premium-audit` (`4f7ddbdf-8547-4427-8bb9-9f190eafbc16`) is a preserved dirty-only evidence source but is currently unavailable to safe worktree-aware reads because its registered task branch no longer matches; do not infer its dirty contents are empty.

| ID | Capability | Classification | Verified evidence / action |
|---|---|---|---|
| IM-1 | Shared editable project model, history, persistence | PARTIALLY SUPERSEDED | Current `ImagineProjectSession` has immutable project boundary, undo/redo, real objects/assets/selections/tracks and atomic operations. `ImagineProjectRepository` has managed source copies, SHA-256 verification, atomic JSON manifests and bundle export. Keep current substrate; do not wholesale-restore old model. |
| IM-2 | Semantic decomposition with real masks/segmentation/matting | PARTIALLY SUPERSEDED | Current semantic service creates editable hierarchy/bounds but explicitly does not invent masks and writes `MaskPath = null`. Recover real segmentation/matting/component-mask requirement on current architecture. |
| IM-4 | Generative editing/reconstruction | PARTIALLY SUPERSEDED | Current provider adapter performs real text-to-image and reference-image editing and requires real image bytes. Selection-aware inpainting, exposed-region reconstruction, component-scoped edits and alternatives remain unproven. |
| IM-5 | Native animated-image keyframes, rigging/bones/IK/handles | STILL RELEVANT — REQUIREMENT | The committed historical Imagine baseline contains zero rigging, bone or keyframe implementation hits. Current Imagine has a much deeper generic media timeline, but still no Imagine-native rig/keyframe model. Global Haven.UI/Present animation is not a substitute. Dirty-only premium-audit evidence remains unavailable and preserved. |
| IM-6 | Dedicated versioned animated-semantic native image format | STILL RELEVANT — REQUIREMENT | Current persistence is JSON manifests + managed assets + ZIP bundle. No dedicated animated-semantic native format evidenced. |
| IM-7 | Audio editor / DAW depth | PARTIALLY SUPERSEDED | Current tracks/clips have waveform plumbing and real move/trim/split/reorder/mute/gain operations. Full mixed-timeline playback remains incomplete. |
| IM-8 | TTS Studio + custom censorship workflow | STILL RELEVANT — REQUIREMENT | The committed historical Imagine baseline contains zero text-to-speech/TTS or censorship implementation hits, and current Imagine still has no such workflow. Treat this as a surviving requirement, not lost committed code; preserve the unreadable dirty premium-audit copy for later forensic recovery. |
| IM-9 | Video editing | PARTIALLY SUPERSEDED | Current Imagine imports video, has video tracks/clips and video-frame preview tests; generic clip mutation applies. Full effects/compositing/export depth remains unverified. |

## P0 — Historical Worker 6 / shared system UI

Historical target: `uiux-recovery-worker6-system-ui-release` (`088e48b3-4069-4ee3-a00c-0031b7d5d533`).

| ID | Capability | Classification | Verified evidence / action |
|---|---|---|---|
| W6-POPUP | Haven-owned detached `PopupMenu` | SUPERSEDED / PRESERVED | Current Haven contains the detached popup architecture and extends disabled-item semantics. Do not restore old primitive. |
| W6-TOPRAIL | Direct TopRail action ownership | SUPERSEDED / PRESERVED | Current `TopRail.SemanticActions.cs` directly raises Home/Back/Forward/Apps/Actions/Model/Search/tab events; semantic tokens remain compatibility/telemetry. |
| W6-PROXY | Eliminate hidden `ElementProxy` runtime forwarding | SUPERSEDED / PRESERVED | `ElementProxy.cs` remains as a large legacy token catalog, but no production `Subscribe.To(...)` or `EventToken` consumption exists outside the Events subsystem. The live `HavenEventBus` is injected directly and is separate from the dormant proxy catalog. Do not restore old forwarding; defer physical deletion until final cleanup. |
| W6-CONTEXT | Correct Haven-owned popup/context-menu surface | SUPERSEDED / PRESERVED | Current production `HavenContextMenu` use is limited to three active shell/chrome sites: pinned-mode actions plus tab and tab-group actions. They already use Haven-owned menu/item classes. No legacy browser/chat/automations spread remains in production. |

## P0 — Project / Studio

Historical target: `uiux-recovery-worker4-project-creative-release` (`908824a4-0515-4ea9-a4f1-828fd887f837`), especially `docs/HAVEN-MASTER-AUDIT-CHECKPOINTS.md`.

| ID | Capability | Classification | Verified evidence / action |
|---|---|---|---|
| STUDIO-005–008 | File explorer, indent detection, version/diff/restore, project-chat AI/web | SUPERSEDED / PRESERVED | Historical audit source-complete; current Project/Workspace Editor retains versioned editor and project state. No wholesale restore. |
| STUDIO-009 | Integrated terminal | STILL RELEVANT — REQUIREMENT | Historical audit: not implemented. Current launcher buttons are not proof of integrated IDE terminal/session capability. |
| STUDIO-010–013 | LSP transport/configuration, diagnostics, formatting, workspace symbols | SUPERSEDED / PRESERVED | Current root retains `ILanguageServerHost`, `ICodeIntelligenceService`, process LSP host, diagnostics/formatting/workspace-symbol plumbing and tests. |
| STUDIO-014 | Definition/references | STILL RELEVANT — REQUIREMENT | Historical audit: not implemented. Current inspected abstraction exposes diagnostics/formatting/symbols, not definition/references. |
| STUDIO-015 | Rename | STILL RELEVANT — REQUIREMENT | Historical audit: not implemented; absent from inspected current abstraction. |
| STUDIO-016 | Completion, code actions, semantic tokens | STILL RELEVANT — REQUIREMENT | Historical audit: not implemented; absent from inspected current abstraction. |
| STUDIO-017 | Terminal sessions/environment management | STILL RELEVANT — REQUIREMENT | Historical audit: not implemented. Coordinate Project + Terminal. |
| STUDIO-018–019 | Branch/path status; Git status/diff/commit/push | SUPERSEDED / PRESERVED | Historical audit source-complete; current Project has real Git/project state. Runtime verify later. |
| STUDIO-020 | Stage/unstage, stashes, worktrees, checkout | STILL RELEVANT — REQUIREMENT | Historical audit: not implemented. Implement on current source-control architecture with safety gates. |
| STUDIO-021 | GitHub PR/actions basics | SUPERSEDED / PRESERVED - RUNTIME VERIFY | Historical audit source-complete. Verify connected/runtime path later. |
| STUDIO-022 | Extended GitHub operations | STILL RELEVANT — REQUIREMENT | Historical audit: not implemented. |
| STUDIO-023 | CI integration | STILL RELEVANT — REQUIREMENT | Historical audit: not implemented. |
| STUDIO-024 | OpenCode/MCP/ACP parity | STILL RELEVANT — REQUIREMENT | Historical audit: not implemented; old validation also had environment/API blockers. Re-evaluate against current MCP/external-app architecture. |
| STUDIO-025–026 | Projects/OpenCode UI + visible editor history | SUPERSEDED / PRESERVED - RUNTIME VERIFY | Historical audit source-complete; current surfaces retain relevant concepts. |

## Current convergence checkpoints

- Chat production File Library and grouping semantics recovered/focused-tested.
- Dead hidden Chat mode callback removed and checkpointed.
- Hidden mode-selector surface removed; search is icon-triggered/on-demand and clears on close.
- All affected Chat/Study sidebar tests pass after synchronizing constructor callers.
- No historical worktree was retired to satisfy stale Sandbox approved-branch metadata; actual root remains `use-this-branch`.

## Next slices


1. Finish Worker-8 Imagine archaeology and route surviving gaps.
2. Complete Worker-6 consumer-by-consumer popup/proxy classification.
3. Finish Project comparison and route genuinely never-built IDE requirements.
4. Audit Workers 1–5 and stranded Results Day/migration worktrees.
5. Return to Canvas recovery + focused interaction QA.
6. Full build/test/runtime matrix; checkpoint; merge to `main`; validate `main`; only then final uniqueness/cleanup audit.
