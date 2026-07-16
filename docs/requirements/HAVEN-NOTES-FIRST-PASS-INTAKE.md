# Haven Notes first-pass production requirements

Branch: `haven-continuation`

Source package: `Haven_Notes_First_Pass_Prompt.zip`

Package generated: 2026-07-16T21:17:12.884520+00:00

## Authority

The uploaded package is the authoritative implementation contract for Haven Notes. Its manifest hashes were verified before this file was written.

- `00_MASTER_BUILD_PROMPT.md`: `a17444fcce68ba6f9bdf4dc92ec8a89d8c492224d74fb1d0f15f08b314a5f4f9`
- `01_FULL_FEATURE_REQUIREMENTS.md`: `1be78b24a2cfd6b1f5f48acadcde9f6bc6af9d6cf983198a8ca8d9da870d7567`
- `02_ALLOWED_TEACH_MODE_DEFERRALS.md`: `0ab580812abe8dad1471f22ffbf20a74ad0742b61ef85eca1d7338b81d878249`
- `03_FIRST_PASS_ACCEPTANCE_GATES.md`: `ff894184d6c6234d690f2c4edee7de23d37cae4b5bdb23134d0ea2f5a8c50033`
- `04_REQUIREMENT_TRACEABILITY.md`: `def940ade6352ec5346b6d757a9c14c2d078e1cec66f169eb2661978b3cdd586`
- `source/Original_AI_Ink_Education_Features.txt`: `14800d38046bce7a6aebba35ed23f895b9b8d6f97c3ef004bdb405a91c0f4535`
- `source/Original_Word_Processor_Features.md`: `63f9376dd1a7c20facdd0bb3b71fffb9d4f3a295843a7b269e04b0d79e22971a`

The source package remains the detailed line-by-line specification. This intake document prevents the broad scope and delivery rules from being lost between implementation passes.

## Absolute delivery rule

Haven Notes is a production implementation, not a prototype, design mock-up or broad UI scaffold. Every requirement in the authoritative requirements file is first-pass work except the exact items recorded in `docs/backlog/HAVEN-TEACH-DEFERRED-NOTES.md`.

The old labels “essential”, “second-stage” and “future” do not authorise additional deferral.

A control may not be shown unless its labelled operation works end to end. Do not ship mock success messages, fake AI output, decorative editor commands, sample-only persistence, empty dialogs, untruthful import/export, or hidden “coming soon” behaviour.

## Required product routes

### Haven Notes

Haven Notes is the complete primary mode. Its single durable document model must preserve and coordinate:

- paginated and continuous rich-text documents;
- freeform pages and infinite canvas objects;
- editable vector ink and Ghost Pen reveal layers;
- visual, source and split LaTeX editing;
- sandboxed visual, source and split HTML/CSS/JavaScript embeds;
- tables, media, citations, comments, references and revisions;
- flashcards, image occlusion, scheduling, review history and source links;
- AI provenance, permissions and review-before-apply changes;
- autosave, recovery, versions, conflicts and collaboration metadata.

### Intentionally blank routed modes

The following are real, accessible routes but intentionally contain no fake product UI in this build:

- Haven Present — slides.
- Haven Data — spreadsheets.
- Haven Tasks — tasks.
- Haven Imagine — image, video and audio generation.

Each route must retain the normal Haven shell, have a unique accessible title, support keyboard and pointer navigation, preserve selection state, survive route restoration/deep linking supported by the native application, and return cleanly to Haven Notes. They must not contain invented editor controls.

## Requirement groups

The authoritative requirements include all of the following non-deferred product groups. Completion requires behaviour, persistence, error states, accessibility and tests rather than a similarly named button.

1. Document creation and file management, including the complete required format list.
2. Main editor interface.
3. Text editing.
4. Character formatting.
5. Paragraph formatting.
6. Lists.
7. Styles and themes.
8. Page layout.
9. Headers and footers.
10. Tables.
11. Images and media.
12. Visual LaTeX and equation editing:
   - visual, source and split editing;
   - live rendering and errors;
   - visual construction and placeholders;
   - symbols, intelligent input and navigation;
   - formatting, numbering, labels and references;
   - supported LaTeX, macros and equation library;
   - handwriting conversion review;
   - import/export, accessibility and stress behaviour.
13. Academic writing tools.
14. Navigation and document structure.
15. Comments and review tools.
16. General collaboration.
17. Spellchecking and language tools.
18. Statistics and document information.
19. Code editing.
20. Templates.
21. Printing and PDF export.
22. Accessibility.
23. Keyboard shortcuts.
24. Privacy and security.
25. Performance and reliability.
26. Extensibility.
27. AI-assisted writing and learning, except the explicitly deferred formal marking and adaptive Tutor workflows:
   - writing assistance;
   - context-aware assistance;
   - document planning;
   - research;
   - citations;
   - source-grounded writing;
   - fact and consistency checks;
   - mathematics;
   - revision tools.
28. HTML and interactive content embedding:
   - embed types;
   - visual, source and split editing;
   - HTML/CSS/JavaScript editing;
   - sandbox security;
   - sizing;
   - export and fallbacks;
   - standalone Notes widgets.
29. Freeform canvas and digital ink:
   - freeform pages;
   - pen tools with pressure and tilt;
   - ink editing;
   - mixed typed and handwritten notes;
   - infinite canvas.
30. Ghost Ink and revealable Flashcard Pen:
   - ghost behaviour and layers;
   - reveal interactions;
   - ghosting existing content;
   - masks and scratch reveal;
   - self-marking;
   - export choices.
31. Flashcards and spaced repetition:
   - creation and source links;
   - image/diagram occlusion;
   - scheduling;
   - study interface;
   - bidirectional document links.
34. AI-assisted multimedia.
35. Voice and conversation features.
36. Education accessibility features.
37. Academic integrity and responsible AI.
38. Workspace search and knowledge retrieval.
39. General document presentation paths, excluding the explicitly deferred classroom orchestration.
40. Suggested AI interface.
41. AI command palette.
42. AI privacy controls.
43–45. Updated priority lists, interpreted as requirements rather than deferral permission.
46. Product principles: user ownership, visual/source parity, writing and learning together, first-class ink, active recall, safe interactivity, explainable AI, education without dependency, and one connected workspace.

## Durable native model minimum

The native format must be schema-versioned and migration-tested. It must preserve at least:

- documents, pages, sections, page setup, headers, footers, styles, fields, bookmarks and metadata;
- structured rich-text runs, lists and tables;
- media, attachments, anchors, crops, transforms, captions, alt text and wrapping;
- equations with exact source, visual structure, numbering, labels, macros and accessible alternatives;
- HTML source, sandbox permissions, fallbacks, sizing and snapshots;
- canvas objects, frames, connectors, grouping, z-order, dimensions and locks;
- vector strokes, pressure/tilt, presets, recognition results and source strokes;
- ghost states, masks, layers, hints, answer groups and reveal/export rules;
- flashcards, schedules, attempts, confidence, history, source links and occlusion masks;
- citations, bibliography records, source locations, access dates and evidence links;
- comments, revisions, collaboration and conflict metadata;
- AI provenance, approval and provider metadata permitted by privacy settings;
- autosave, backup, recovery, version and sync metadata.

Save, migration, import and destructive edits must be atomic or transactional. Corrupt current state must not destroy the last valid version.

## First-pass acceptance gates

Haven Notes is not complete until all sixteen gates pass:

1. Routes and shell.
2. No inert UI.
3. Persistence and recovery.
4. Mixed-content editing fidelity.
5. Formats, print and output truthfulness.
6. Complete visual/source/split LaTeX workflow.
7. Functional and security-tested HTML sandbox.
8. Ink and canvas editing.
9. Ghost Pen and flashcards.
10. Real-provider AI with consent, evidence, cancellation, errors and provenance.
11. General collaboration and review.
12. Search and language tooling.
13. Accessibility.
14. Security and privacy.
15. Repeatable performance stress gates.
16. Clean repository with build, formatting, type, unit, integration, end-to-end, security, accessibility and performance suites passing.

## Implementation discipline

Before broad editor work, maintain a requirement-to-code matrix that records for every requirement group:

- owning module/component/service;
- persistent representation;
- interaction surface;
- errors and empty states;
- keyboard and accessibility behaviour;
- automated tests;
- manual verification.

Implement vertical slices. A slice is complete only when its UI, state, persistence, undo/redo, relevant import/export, errors, accessibility and tests work together.

Do not weaken tests or claim build/test success without an actual current-head validation run. CI remains manual-only unless the user explicitly changes that policy.
