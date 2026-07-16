# Haven Notes — exclusive Teach-mode deferrals

This backlog is the complete list of requirements that may be postponed from the Haven Notes first-pass build. Nothing outside this file is authorised for deferral.

Deferred features must not appear as disabled controls, decorative panels or “coming soon” UI inside Haven Notes.

## 27.9 — Formal AI feedback and marking support

### Intended user outcome

A later Haven Teach identity/class/assignment model can apply formal rubrics and mark schemes, report indicative grades, map assessment objectives and support teacher-facing marking workflows.

### Expected model dependencies

- teacher, learner, class and assignment identities;
- rubric and mark-scheme definitions;
- assessment-objective mappings;
- submissions and immutable grading attempts;
- moderation, appeal and change history;
- grade export metadata.

### Integration points

- Haven Teach;
- general Notes comments, suggestions and revisions;
- provider routing and AI provenance;
- assignment/submission storage;
- permissions and reporting.

### Privacy and access control

Teacher access must be explicit and scoped to the relevant class or assignment. Student documents, provider prompts and grades require auditable access, retention and export controls.

### Later acceptance criteria

- rubric/mark-scheme application is evidence-linked;
- assessment-objective feedback is traceable to source text;
- indicative marks are labelled honestly;
- teacher edits and moderation are versioned;
- exports preserve provenance and permissions;
- provider failures never fabricate marks.

General writing feedback, document review and non-formal assistance remain first-pass Notes requirements.

## 27.11 — Adaptive AI Tutor Mode

### Intended user outcome

A later Haven Teach tutor conducts adaptive, diagnostic and Socratic learning sessions with prerequisite planning, misconception remediation and explicit answer-reveal governance.

### Expected model dependencies

- learner profile and mastery state;
- curriculum/prerequisite graph;
- misconceptions and evidence;
- lesson/session state;
- answer-reveal policy;
- intervention and progress history.

### Integration points

- Haven Teach conversation runtime;
- lesson and curriculum planning;
- Notes resources and source links;
- flashcards and retrieval systems;
- provider routing and safety policy.

### Privacy and access control

Learner profiles and inferred weaknesses must remain private, exportable and deletable. Teacher visibility requires explicit class permissions. The tutor must not expose hidden answers against active lesson policy.

### Later acceptance criteria

- diagnostic sequencing changes from demonstrated evidence;
- prerequisite gaps are explained;
- misconceptions are tracked without inventing learner state;
- Socratic and answer-reveal rules are enforced;
- sessions resume safely and remain auditable;
- provider failure does not corrupt mastery state.

General AI document assistance and revision generation remain first-pass Notes requirements.

## 28.7 — Teacher-led/classroom HTML widgets only

### Intended user outcome

A later Haven Teach classroom session can orchestrate audience response widgets, teacher-controlled live activities and class-level results.

### Expected model dependencies

- classroom session and participants;
- response/event stream;
- teacher control state;
- aggregation and reporting;
- anonymous/pseudonymous participation policy.

### Integration points

- secure Notes HTML embed runtime;
- Haven Teach live session orchestration;
- presentation and response systems.

### Privacy and access control

Participant identity and responses must be scoped to the session. Anonymous mode and retention must be explicit. Scripts cannot bypass the Notes HTML sandbox.

### Later acceptance criteria

- teacher can start, pause and end the widget session;
- responses are isolated per classroom;
- reconnect and duplicate submission behaviour is deterministic;
- reporting respects anonymous mode;
- the HTML sandbox remains enforced.

The underlying HTML editor/sandbox and standalone Notes flashcard, reveal, quiz-like, graphing, equation and media widgets are not deferred.

## Section 32 — Dedicated Education Workspace

Deferred sub-requirements:

- 32.1 Courses, Subjects, and Topics.
- 32.2 Specification and Syllabus Mapping.
- 32.3 Learning Objectives.
- 32.4 Retrieval Practice session orchestration.
- 32.5 Dedicated Blank-Page Recall workflow.
- 32.6 Dedicated Cornell Notes workflow/template automation.
- 32.7 Knowledge Organiser generation/export workflow.
- 32.8 Full Quiz Builder and question-bank management.
- 32.9 Practice Paper assembly and timed-paper system.
- 32.10 Dedicated Essay Practice workflow.
- 32.11 Restricted Exam Mode.
- 32.12 Revision Planner.
- 32.13 Education Progress Tracking dashboards.
- 32.14 Mistake Journal.
- 32.15 Education Confidence Tracking dashboards.
- 32.16 Guided Study Sessions.

### Intended user outcome

A later Haven Teach workspace connects curriculum structure, learning objectives, practice, assessment and progress into one dedicated education environment.

### Expected model dependencies

- course/subject/topic hierarchy;
- specification versions and mappings;
- objectives and mastery evidence;
- question banks and attempts;
- practice papers and timed sessions;
- mistakes, confidence and study-session records;
- planner and progress summaries.

### Integration points

- Haven Teach navigation;
- Notes documents and source-linked cards;
- flashcards and spaced repetition;
- Plan and Automations;
- general search and AI provider services.

### Privacy and access control

Education analytics and inferred mastery must be private by default. Sharing with teachers or guardians requires explicit role and scope controls. Exam restrictions must never silently lock or destroy personal Notes data.

### Later acceptance criteria

Each numbered requirement receives a separate traceability row, durable model, migration, accessibility coverage and end-to-end workflow test before the Education Workspace is declared complete.

This deferral does not defer ordinary Notes pages, blank freeform pages, Ghost Pen, flashcards, spaced repetition, AI revision generation, general templates, ordinary metadata or timers used elsewhere.

## Section 33 — Teacher and Classroom Features

Deferred:

- all of Section 33;
- 33.1 teacher feedback layers as classroom/assignment workflows;
- 33.2 assignment modes and submission history.

### Intended user outcome

A later Haven Teach classroom layer supports teacher feedback, assignments, submission history and class orchestration without weakening general Notes collaboration.

### Expected model dependencies

- teacher/student/class roles;
- assignments, submissions and deadlines;
- feedback-layer ownership;
- return/resubmit history;
- class policy and reporting.

### Integration points

- general Notes comments, tracked changes and versions;
- sharing and collaboration;
- formal marking support;
- Haven Teach navigation.

### Privacy and access control

Class and assignment membership must be explicit. Teachers cannot gain general access to unrelated personal Notes. Submission snapshots and feedback retention must be visible to the learner.

### Later acceptance criteria

- role and class boundaries are tested;
- submission versions are immutable and recoverable;
- feedback ownership and visibility are clear;
- offline/reconnect conflict behaviour is deterministic;
- general personal Notes collaboration remains independent.

Ordinary comments, review tools, tracked changes, audio/ink annotations, collaboration, document sharing and version history remain first-pass requirements.

## Section 39 — Classroom presentation orchestration only

Deferred:

- audience/student response collection;
- live classroom quizzes and polls;
- teacher-led timed activities;
- Teach-specific presenter/student orchestration.

### Intended user outcome

A later Haven Teach session coordinates a teacher presenter and participating learners in real time.

### Expected model dependencies

- presentation session;
- presenter/participant roles;
- activity timing;
- responses and connection state;
- session report.

### Integration points

- Notes presentation/fullscreen reading;
- secure HTML widgets;
- classroom identity and response systems.

### Privacy and access control

Participation, response visibility and retention must be explicit. Joining a session must not grant access to unrelated Notes content.

### Later acceptance criteria

- role changes and reconnects work;
- timed activities use one authoritative clock;
- responses cannot cross sessions;
- teacher controls are auditable;
- participant access ends with the session.

General fullscreen reading, section navigation, zoom, reveal animation, Ghost-layer reveal, laser pointer, live annotation, presenter notes, HTML interaction and freeform-canvas presentation remain first-pass Notes requirements.

## Teacher-specific controls elsewhere

Deferred only when they depend on the dedicated Teach class/assignment identity model:

- teacher-configurable AI permissions;
- assignment-specific AI restrictions;
- class-level reporting;
- formal grade export;
- teacher access to student work.

Later acceptance requires explicit identities, role scopes, audit history, revocation, data export/deletion and tests proving teachers cannot access unrelated personal Notes.
