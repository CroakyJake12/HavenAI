# Haven Generative UI Release Success Rubric

Release: Generative UI overhaul, 8 August 2026  
Overall status: **INCOMPLETE**  
Canonical ledger: this file  
Active branch: `codex/genui-release-overhaul-20260808`

This is the compact execution and validation ledger required by the release brief. It is derived from the authoritative written brief and complementary PowerPoint; it does not replace either source or Haven's permanent repository rules. A workstream is complete only when every linked substantive requirement is `Passed` with evidence. `Blocked`, `Unvalidated`, `Failed`, `Not started`, and `In progress` all keep the release `INCOMPLETE`.

## Source integrity

| Source | Integrity | Indexed evidence |
| --- | --- | --- |
| `Haven_Generative_UI_Release_Overhaul(20260808-172008)(1).md` | SHA-256 `8DF4F382BF483E0C69EDBCE8B718EAAE04E0F45B207AE27C4B7A7A9091A4E8A5`; 541,387 bytes; 13,678 lines; 9,450 indexed non-empty records; 8,581 initially classified substantive records | `GENUI_REQUIREMENT_SOURCE_INDEX.meta.json` and `GENUI_REQUIREMENT_SOURCE_INDEX.jsonl` (index SHA-256 `FB8770C7B2675B7349ADF48AF7857099017E109984473D5892CC126649D51784`) |
| `Haven_AI_Generative_UI_Update_REPAIRED_LIGHTWEIGHT.pptx` | SHA-256 `CA36B513EAE9C53AB59F49632D5E2BDE3205DF96A42208567A896C7D5EE144B2`; 1,462,107 bytes; 21 slides; speaker notes on slides 1, 3-6, 13-15 | `GENUI_MOCKUP_REQUIREMENT_INDEX.md` |
| Direct user clarifications in this task | User authority; append-only stable IDs | `GENUI_SUPPLEMENTAL_USER_REQUIREMENTS.md` |

Authority and conflict handling:

1. The written brief is authoritative where it explicitly supersedes earlier terminology or architecture.
2. The PowerPoint is authoritative for complementary visual and interaction detail that does not conflict with the brief.
3. Existing working product behaviour is preserved until its replacement is implemented, migrated, and verified through `GENUI_FEATURE_MIGRATION_MATRIX.md`.
4. No summary, implementation note, or generated index may erase an unresolved source requirement.

## Status vocabulary

| Status | Meaning |
| --- | --- |
| Passed | Implemented and all required automated/runtime/visual evidence is recorded. |
| In progress | Active work exists, but at least one acceptance condition is outstanding. |
| Not started | No accepted implementation evidence exists. |
| Unvalidated | Source appears implemented, but mandatory validation has not been completed. |
| Blocked | A named external dependency prevents progress; this is not success. |
| Failed | Evidence shows a requirement is not satisfied. |

## Workstream ledger

| ID | Workstream | Depends on | Status | Primary evidence/index |
| --- | --- | --- | --- | --- |
| GENUI-00 | Source integrity, requirement indexing, annotations, parity and coverage ledgers | None | Passed | 9,450/9,450 records indexed with workstream/dependency fields; hash verified; all 21 slides/notes indexed; migration/template matrices created |
| GENUI-01 | Preservation audit, data fixtures, migration safety and old/new parity | GENUI-00 | In progress | `GENUI_FEATURE_MIGRATION_MATRIX.md` |
| GENUI-02 | HavenUI canonical primitives, four brightness themes, accent scope and accessibility | GENUI-00, GENUI-01 | In progress | Four semantic appearances, bundled Montserrat/thick hierarchy, typed component family, live gradient accents and earlier focused tests exist; user runtime screenshots exposed remaining popup, slider, startup and legacy-screen failures now under repair. Complete breadth plus desktop/Android visual and accessibility proof remain |
| GENUI-03 | HavenUI motion, morphing layout, responsive desktop/mobile and context actions | GENUI-02 | In progress | Compact/high-DPI Go now has a fluid composer plus centred, compact-safe startup; broader morph/motion/device coverage remains |
| GENUI-04 | Shell/header/navigation, Apps panel and direct Haven launch | GENUI-01, GENUI-02 | In progress | Direct-start routing, Go, unique Apps coverage and header controls exist, but the current fresh Release runtime fails before shell creation because `Haven.Desktop.App` is not found as precompiled XAML; direct-launch runtime proof therefore remains failed while the startup repair is in progress. Selected-tab, Add popup and destination-layout defects also remain under repair; complete route/device/cold-start validation is still required |
| GENUI-05 | Settings IA/search/AI, model browser, model residency and lifecycle | GENUI-02, GENUI-04 | Not started | Persistence tests; tray/notification/reboot evidence |
| GENUI-06 | Capability, Instruction and Agent registries/editors; remove stale Actions/Plugins/Macros systems | GENUI-01, GENUI-02 | In progress | SQLite v11 Capability Registry, built-ins, App ownership, editor, safety metadata, route and tests pass; Instruction/Agent consolidation and legacy Plugin/Macro deletion remain |
| GENUI-07 | Attachment routing and shared active App/thread/task context | GENUI-04, GENUI-06 | In progress | File/App/Capability relevance attachment and Go-to-Chat snapshot ownership tests pass; full persistence/mobile matrix remains |
| GENUI-08 | Bidirectional GenUI event/state/action runtime and renderer contracts | GENUI-02, GENUI-06 | In progress | Typed contracts, validator, instance store, destination router, bounded semantic audit, trusted renderer, safe Chat request and observed calculator patch loop pass; capability-status questions now route from deterministic host state but final validation and non-local destination adapters remain |
| GENUI-09 | App ownership, App Builder/Studio editors and reusable generated UI | GENUI-06, GENUI-08 | Not started | App registry/Studio tests; persisted object evidence |
| GENUI-10 | Template registry, built-in breadth, feature completeness and Graphing | GENUI-08, GENUI-09 | In progress | SQLite v12 searchable registry, 14 honest foundation records, Template Preview Lab and deterministic Calculator preview/Chat route exist; availability questions now open that live template as proof, while remaining complete templates and Graphing remain |
| GENUI-11 | Voice profiles, Lesson Voice, live whiteboard/notes/Browse routing and privacy | GENUI-08, GENUI-10 | Not started | 10-minute voice run; transcript/privacy evidence |
| GENUI-12 | Background Learning, Haven Library, API Bank and resource scheduling | GENUI-05, GENUI-06 | Not started | Service tests; maintenance/background-runtime evidence |
| GENUI-13 | Cross-platform Tasks, risk permissions and production agentic file/vision/command loop | GENUI-06, GENUI-08 | Not started | Windows/Android tool-loop and permission evidence |
| GENUI-14 | Floating Activity state model plus transparent Windows and Android hosts | GENUI-02, GENUI-03 | Not started | Independent host tests and real transparency evidence |
| GENUI-15 | Haven Home Android launcher and platform providers | GENUI-04, GENUI-05, GENUI-13 | Not started | Launcher selection/cold-start/device/reboot evidence |
| GENUI-16 | Legacy deletion: Magical UI, previous GenUI, Old Haven and chooser | GENUI-01 through GENUI-15 | Not started | Deletion scan; migration fixtures; route/build evidence |
| GENUI-17 | Repository AGENTS/rules, precedence, generated-code and continuation governance | GENUI-00, architecture workstreams | In progress | Rule files and path validator passed; fresh-agent audit remains |
| GENUI-18 | Full Release validation: restore/build/test, desktop/Android runtime, visuals, accessibility, performance, cold start and reboot | GENUI-01 through GENUI-17 | Not started | `GENUI_VALIDATION_EVIDENCE.md` and linked artifacts |

## Baseline evidence

Captured before product-code changes on 8 August 2026:

- Repository was clean at `39f8f3c8dcda75f3558a1bb5657c9d539235486f` (`main`) before switching to the dedicated release branch.
- `dotnet restore Haven.sln`: passed after granting access to the normal user NuGet configuration.
- `dotnet build Haven.sln -c Debug --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet test Haven.sln -c Debug --no-build`: passed 462/462 tests (139 Core, 189 Infrastructure, 134 Desktop).
- Baseline includes `Haven.OldHaven`, the launcher chooser, Magical-theme classes, the previous Generative UI theme runtime/schema, and Actions/Macros/Plugins terminology. Passing baseline tests therefore do not demonstrate release compliance.

## Active work package

Current package: **HavenUI/route/registry/GenUI foundation slice across GENUI-01, 02, 03, 04, 06, 07, 08 and 10**.

Exit criteria:

- retain the tested four-appearance, Montserrat, direct-start, App, Capability and attachment foundations;
- complete remaining GenUI destination adapters and feature-complete templates without promoting foundation records to Production;
- migrate Instructions/Agents and useful legacy Plugin/Macro data before deleting old stores or visible paths;
- execute remaining Windows/Android device, visual, accessibility, voice, floating-host, cold-start and reboot gates;
- keep the overall release `INCOMPLETE` until every dependent workstream is passed.

## Release gate

The release must remain **INCOMPLETE** until every substantive record in the source index has an implementation/evidence mapping and every required workstream is `Passed`. Environment-limited Android, device, voice-duration, reboot, transparency, cold-start, or visual checks remain `Unvalidated`; they must never be reported as successful by inference.
