# Generative UI PowerPoint Annotation Index

Source: `Haven_AI_Generative_UI_Update_REPAIRED_LIGHTWEIGHT.pptx`  
SHA-256: `CA36B513EAE9C53AB59F49632D5E2BDE3205DF96A42208567A896C7D5EE144B2`  
Slides inspected at full rendered size: 21/21  
Speaker-note relationships inspected: 8/8

Pink Aptos text on a faint square-cornered background is annotation text, not UI. This index distinguishes explicit annotation requirements from visual observations. The written brief wins where it expressly changes names or architecture.

Typography clarification: the rendered slide images substituted fonts because Montserrat is not installed as a system font in the inspection environment. Per direct user instruction `USER-UI-0001`, all Haven-owned UI uses the bundled Montserrat family and prefers Medium/SemiBold/Bold/ExtraBold weights. The render fallback is not a design reference.

| Slide | Surface | Explicit annotations / requirements | Visual observations |
| --- | --- | --- | --- |
| 1 | Title/legend | Pink Aptos text with faint non-rounded background is design commentary, not product UI. | Haven mascot; dark blue/black background. |
| 2 | Section divider | None. | `Key Principles/Components` divider. |
| 3 | Buttons | Hover visibly morphs; click bounces and becomes slightly brighter; colours follow accent. Speaker note: destructive controls become `Hold to Delete`, require a five-second hold, intensify brightness/vibration toward completion, and visibly wind down over the same accumulated hold duration when released/left. | Pill controls; Primary, Secondary, Tertiary, Negative and Text styles with distinct default/hover states. |
| 4 | Inputs and states | Colours follow accent. Speaker note repeats that destructive controls change to a hold interaction. | Placeholder/input/selected text boxes; selected outline; slider/progress; on/off switch; selected tab underline. |
| 5 | Dropdowns/cards | Optional title; external/nested destinations show right arrow; background blur; item icons match text colour; three actionable styles (Important/Main/Negative); fourth invisible non-interactive container style; section titles; sticky rows such as search; accent-aware colours. | Rounded dropdown; model picker example with effort slider. |
| 6 | Pop-ups | Content region sizes to content; large content scrolls vertically only; Close is mandatory, primary action optional; clicking outside closes; accent-aware colours. | Blurred modal composition with actions at lower right. |
| 7 | Section divider | None. | `Design Philosophies` divider. |
| 8 | Background | Animated darker-accent-to-black gradient described as moving like a tide; accent changes morph colour; background itself is also described as a static element that does not translate with layout animation; colours follow accent. | Multiple dark accent examples. Treat gradient animation and layout-position stability as compatible; written brief governs if implementation detail conflicts. |
| 9 | Background blur | A translucent element over content blurs the content behind it. | Modal blur example. |
| 10 | Section divider | None. | `Apps/Screens — The Header`. |
| 11 | Desktop header | Haven icon launches Go; Back/Forward appear only when available; tab area flexes; full tab bar scrolls horizontally with edge arrows; model button colour encodes effort (20% yellow, 100% orange); notification colour progresses yellow to bright red up to 30 unread; blue is replaced by current accent. | Mascot, history, tabs, add, Apps, Actions, model, notification, search. |
| 12 | Mobile header | Desktop rules carry over; notification/search/tab view merge into `You`; You adopts yellow/orange/red unread state; blue becomes accent. | Compact top controls with tabs below. |
| 13 | Desktop Apps panel | Populate Haven and user Apps by real categories; no duplicate appearance except pins/recommendations; every App appears at least once; icon background is App accent; panel capped at 50% of screen; overflow must scroll; blue becomes accent. | Search, pinned Chat/Tasks, large rounded flyout. Speaker notes contain no extra design prose. |
| 14 | Mobile Apps menu | Same content rules as desktop; large clickable icons in a four-column grid; blue becomes accent. | Bottom sheet with drag handle, Manage, search and pins. |
| 15 | Desktop Actions panel | Category population from available items; item accent follows owning App, general items follow page accent; panel capped at 50%; overflow scrolls; Manage opens settings; blue becomes accent. | This visual/interaction pattern is retained, but the written brief supersedes `Actions`, `Run Macro`, and `Manage Actions` with the Capabilities architecture and removes stale Macro systems. |
| 16 | Section divider | None. | `Apps/Screens — Haven Apps`. |
| 17 | Go | Suggested pills inherit a mode/App accent where applicable. | Central suggestions and persistent bottom composer. |
| 18 | Tasks | No pink annotation prose. | Running/history/macros-era tabs, two active tasks and Stop controls. The brief supersedes Macro UI while preserving real Tasks functionality. |
| 19 | Chat/App workspace | Manage Attachments opens a pop-up showing all attachments. | Sidebar, attached App/file chips, persistent composer. Written brief additionally requires attachment actions to remain in the active thread/App/task rather than navigating to Chat. |
| 20 | Study/Lesson | No pink annotation prose. | Lesson conversation, horizontally presented flashcards and sidebar. |
| 21 | Study whiteboard | No pink annotation prose. | Worked-time disclosure, structured whiteboard content, fullscreen action, sidebar and composer. |

## Recorded source conflicts and resolutions

| Conflict | Resolution |
| --- | --- |
| PowerPoint uses `Actions`, `Run Macro`, `Create Macro`, and `My Macros`; written brief removes stale Actions/Macro/Plugin systems and requires Capabilities. | Preserve the panel/layout intent while implementing the authoritative Capabilities terminology, registry and editor. No compatibility label may remain visible after migration. |
| PowerPoint refers to Apps overflow as horizontal scrolling when content is too far down. | Implement accessible overflow appropriate to the panel geometry; the brief's responsive/accessibility requirements and the deck's 50% cap are authoritative. A vertical list/grid overflow is expected unless a horizontal category rail is intentionally present. |
| Slide 8 calls the background both animated and static. | Animate gradient colour/flow and accent morphing while keeping the background anchored during layout/shared-element motion. |
| PowerPoint shows an earlier limited App inventory. | Use the real built-in inventory from `BuiltInModeSeed`: Chat, Study, Tasks, Studio, Browse, Plan, Training, Imagine, Present, Data, Vision, Play, Translate, Launcher, Go and Dashboard, plus user Apps. |

## Implementation evidence rule

Visual similarity alone is insufficient. Each slide requirement must be linked to real routing/state/services, exercised at desktop and mobile sizes, and validated in all four HavenUI brightness themes before its related workstream may be marked `Passed`.
