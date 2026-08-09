# Generative UI Template Coverage Matrix

This matrix uses the actual built-in App names found in `BuiltInModeSeed`: Chat, Study, Tasks, Studio, Browse, Plan, Training, Imagine, Present, Data, Vision, Play, Translate, Launcher, Go and Dashboard. A row is not covered by a registry name, empty renderer, screenshot or generic card. `Passed` requires the configured user experience, structured state/events, mobile behaviour and Clause 85.47 feature-completeness evidence.

Initial status for every row is `Not started`; existing partial screens may be reused only after they satisfy the same contract.

| Requested concept | Reusable foundation | Recommended/owning App | Agent events | Mobile | Status |
| --- | --- | --- | --- | --- | --- |
| Interactive Lesson | Learning Activity | Study | Required | Required | Not started |
| Guided Lesson | Learning Activity | Study | Required | Required | Not started |
| Worked Example | Step Sequence | Study | Required | Required | Not started |
| Step-by-Step Reveal | Step Sequence | Study | Meaningful completion only | Required | Not started |
| Guided Practice | Learning Activity | Study | Required | Required | Not started |
| Independent Practice | Learning Activity | Study | Required | Required | Not started |
| Retrieval Practice | Learning Activity | Study | Required | Required | Not started |
| Whiteboard / Scratchboard | Structured Whiteboard | Study | Required, stable item/question IDs | Required | Not started |
| Quiz | Assessment | Study | Required | Required | Not started |
| Multiple-Choice Quiz | Assessment | Study | Required | Required | Not started |
| Typed-Answer Quiz | Assessment | Study | Required | Required | Not started |
| Revision Test | Assessment | Study | Required | Required | Not started |
| Timed Test | Assessment + Timer | Study | Required | Required | Not started |
| Flashcards | Card Deck | Study | Required | Required | Not started |
| Confidence-rated Flashcards | Card Deck | Study | Required | Required | Not started |
| Matching Activity | Item Board | Study | Required | Required | Not started |
| Sorting Activity | Item Board | Study | Required | Required | Not started |
| Categorisation Activity | Item Board | Study | Required | Required | Not started |
| Fill-in-the-Blanks | Structured Form | Study | Required | Required | Not started |
| Label-the-Diagram | Diagram + Item Board | Study | Required | Required | Not started |
| Concept Map | Diagram | Study | Required | Required | Not started |
| Mind Map | Diagram | Study | Required | Required | Not started |
| Timeline (education) | Timeline | Study | Required | Required | Not started |
| Interactive Diagram | Diagram | Study | Required | Required | Not started |
| Process Viewer | Step/Diagram | Study | Required | Required | Not started |
| Decision Tree | Diagram/Decision | Study | Required | Required | Not started |
| Reference Sheet | Document | Study | Optional | Required | Not started |
| Glossary | Structured Reference | Study | Optional | Required | Not started |
| Formula Sheet | Structured Reference | Study | Optional | Required | Not started |
| Maths/Science visualisers and simulators | Simulation | Study | Required where semantic | Required | Not started |
| Planner | Planner | Plan | Required | Required | Not started |
| Calendar | Calendar | Plan | Required | Required | Not started |
| Itinerary | Planner | Plan | Required | Required | Not started |
| Checklist | Task List | Plan/Tasks | Required | Required | Not started |
| Kanban Board | Board | Plan/Tasks | Required | Required | Not started |
| Goal Tracker | Tracker | Plan/Tasks | Required | Required | Not started |
| Budget Planner | Grid/Planner | Plan/Data | Required | Required | Not started |
| Decision Matrix | Grid/Decision | Plan/Data | Required | Required | Not started |
| Scenario Explorer | Simulation/Decision | Plan | Required | Required | Not started |
| Habit Tracker | Tracker | Plan/Tasks | Required | Required | Not started |
| Resource Planner | Planner/Grid | Plan | Required | Required | Not started |
| Document Editor | Document | Present/Study | Required where semantic | Required | Not started |
| Notes | Document | Study | Required where semantic | Required | Not started |
| Outline | Document Tree | Present/Study | Required | Required | Not started |
| Essay/Report Workspace | Document Workspace | Present/Study | Required | Required | Not started |
| Wiki Page | Document | Study | Required where semantic | Required | Not started |
| Research Notes | Document + Sources | Browse/Study | Required | Required | Not started |
| Citation Explorer | Sources | Browse/Present | Required | Required | Not started |
| Comparison Table | Grid | Data/Browse | Required | Required | Not started |
| Spreadsheet/Grid | Grid | Data | Required | Required | Not started |
| Presentation | Presentation | Present | Required | Required | Not started |
| Code Editor | Code Workspace | Studio | Required | Required | Not started |
| Repository Explorer | Tree/Code Workspace | Studio | Required | Required | Not started |
| Diff Viewer | Diff | Studio | Required | Required | Not started |
| Build/Test Results | Run Results | Studio | Required | Required | Not started |
| Logs | Log Viewer | Studio | Required | Required | Not started |
| Terminal | Terminal | Studio | Required | Required | Not started |
| API Tools | API Workbench | Studio | Required | Required | Not started |
| Debugging Dashboard | Dashboard | Studio | Required | Required | Not started |
| Capabilities editor/list | Shared Resource Editor | Studio | Required | Required | In progress - real SQLite registry/editor and Windows route pass; Android runtime/complete editor proof remains |
| Instructions editor/list | Shared Resource Editor | Studio | Required | Required | Not started |
| Agents editor/list | Shared Resource Editor | Studio | Required | Required | Not started |
| Search Results | Research Results | Browse | Required | Required | Not started |
| Article Reader | Reader | Browse | Required where semantic | Required | Not started |
| Source Comparison | Sources/Grid | Browse | Required | Required | Not started |
| Research Board | Board/Sources | Browse | Required | Required | Not started |
| Map/Timeline views | Map/Timeline | Browse | Required | Required | Not started |
| Research Data Dashboard | Dashboard | Browse/Data | Required | Required | Not started |
| Task List | Task List | Tasks | Required | Required | Not started |
| Workflow Builder | Workflow | Tasks | Required | Required | Not started |
| Approval Queue | Approval Queue | Tasks | Required | Required | Not started |
| System Monitor | Monitor/Dashboard | Tasks | Required | Required | Not started |
| Automation Builder | Workflow | Tasks | Required | Required | Not started |
| Device Control Panels | Control Panel | Tasks | Required | Required | Not started |
| Canvas | Canvas | Imagine | Required where semantic | Required | Not started |
| Image Gallery | Gallery | Imagine | Required | Required | Not started |
| Moodboard | Board/Gallery | Imagine | Required | Required | Not started |
| Storyboard | Board/Timeline | Imagine | Required | Required | Not started |
| Image Controls | Control Panel | Imagine | Required | Required | Not started |
| Calculator | Deterministic Utility | Chat | Completed result only | Required | In progress - trusted renderer, local event/patch loop, Preview Lab and safe Chat request pass on Windows; Android runtime/mobile interaction remains |
| Converter | Deterministic Utility | Chat | Completed result only | Required | Not started |
| Timer | Deterministic Utility | Chat/Tasks | Completed result only | Required | Not started |
| Forms (Chat utility use) | Form | Chat | Required | Required | Not started |
| Polls | Form | Chat | Required | Required | Not started |
| Wizards | Wizard | Chat | Required | Required | Not started |
| Decision tools | Decision | Chat/Plan | Required | Required | Not started |
| Dashboards | Dashboard | Dashboard/Data | Required | Required | Not started |
| Tables | Grid | Data | Required | Required | Not started |
| KPI cards | Dashboard | Data/Dashboard | Required | Required | Not started |
| Data explorers | Data Explorer | Data | Required | Required | Not started |
| Tree views | Tree | Various | Required where semantic | Required | Not started |
| Tabs | Canonical HavenUI Tabs | Various | Local unless semantic | Required | Not started |
| Split panes | Canonical HavenUI Layout | Various | Local unless semantic | Required | Not started |
| Command palette | Command Palette | Go/Studio | Required | Required | Not started |
| Settings panels | Settings Panel | Various | Required where semantic | Required | Not started |
| Graphs | Graph | Data/Study | Required | Required | Not started |
| Charts | Chart | Data/Study | Required | Required | Not started |
| Maps | Map | Browse/Plan | Required | Required | Not started |
| Diagrams | Diagram | Various | Required | Required | Not started |
| Simulations | Simulation | Study/Play | Required | Required | Not started |
| Converters (utility family) | Deterministic Utility | Chat/Data | Completed result only | Required | Not started |
| Timers (utility family) | Deterministic Utility | Chat/Tasks | Completed result only | Required | Not started |
| Generators | Utility | Chat/Studio | Required when generative | Required | Not started |
| Diagnostic tools | Diagnostic | Studio/Tasks | Required | Required | Not started |
| Boards | Board | Various | Required | Required | Not started |
| Grids | Grid | Various | Required | Required | Not started |
| Games-like structures | Interactive Activity | Play/Study | Required | Required | Not started |
| Simulations (interactive foundation) | Simulation | Play/Study | Required | Required | Not started |
| Drag/drop foundations | Interaction Foundation | Various | Completed operation only | Required | Not started |
| Selectable/categorisable items | Item Board | Various | Meaningful selection only | Required | Not started |
| Forms (input family) | Form | Various | Required | Required | Not started |
| Wizards (input family) | Wizard | Various | Required | Required | Not started |
| Surveys | Form | Various | Required | Required | Not started |
| Rule builders | Rule Builder | Tasks/Studio | Required | Required | Not started |
| Graphing mini-App | Graph + Formula Engine | Study/Data | Required | Required | Not started |
| Research Report composition | Workspace Composition | Browse/Present/Tasks | Required | Required | Not started |

## Foundation acceptance rules

Every foundation must use canonical HavenUI primitives, structured values/state, stable IDs, validation and field-level errors where relevant, incremental updates, permission-aware actions, offline-safe deterministic behaviour, version/dependency/trust metadata, lazy resource cleanup, mobile responsiveness, and the shared GenUI event/action router. App-owned business state remains authoritative.
