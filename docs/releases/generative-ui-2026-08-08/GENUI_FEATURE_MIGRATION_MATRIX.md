# Existing Haven Feature Migration and Parity Matrix

This matrix exists before legacy deletion. `Preserve` means the current user-visible behaviour/data contract must remain until a verified replacement is active. `Retire after parity` means deletion is forbidden until every dependent row is passed.

| Area | Current implementation evidence | Release destination | Policy | Status / required proof |
| --- | --- | --- | --- | --- |
| Desktop composition | `src/Haven.Desktop` | Single Haven desktop host | Preserve and migrate | Baseline builds; new route/visual/runtime proof outstanding. |
| Android composition | `src/Haven.Android` links Desktop views/styles | Shared HavenUI plus platform providers | Preserve and migrate | Shared Interface XAML composition repaired; Debug APK build passes with 0 warnings/errors. Device/cold-start proof outstanding. |
| Old Haven executable | `src/Haven.OldHaven` | No separate old executable | Retire after parity | Not started; remove only after all rows pass. |
| Old/New launcher chooser | removed `Views/Shell/LauncherPicker.*`; startup policy routes directly to current Haven | Direct launch into current Haven | Retire after parity | Source removal and headless route policy tests passed; Windows/Android cold-start proof required. |
| Magical backdrop/control | `Controls/MagicalBackdrop.cs` | Canonical HavenUI tidal background | Retire after parity | Not started; theme/motion parity required. |
| Magical palette/theme bridge | `Services/MagicalPalette.cs`, `WorkspaceChromeHost.MagicalTheme.cs` | HavenUI theme/accent tokens | Retire after parity | Not started; all four themes and scoped accents required. |
| Previous Generative Theme schema/store/runtime | `schemas/haven-generative-theme.schema.json`, `GenerativeTheme*`, `GenerativeUiThemeRuntime` | New GenUI manifest/event/state/template system | Retire after migration | Not started; persisted data migration/rollback fixtures required. |
| Main shell/header | `Views/Shell/TopRail`, `MainView` | HavenUI shell matching deck/brief | Preserve and migrate | Direct Go launch, real model/effort, Apps, Capabilities, notifications, search, centred startup and high-DPI route screenshot pass; full visual/device matrix remains. |
| Apps launcher | `AppLauncherControl`, `BuiltInModeSeed` | Categorised Apps panel/menu | Preserve and extend | Enabled Apps are categorised and each renders exactly once in tests; usage/pin persistence and Android runtime proof remain. |
| Actions flyout | `ActionsFlyoutControl`, `RefreshContextualActions` | Capabilities catalogue/editor | Migrate then retire terminology | Visible Capabilities terminology, contextual real actions and corrected Edit Capabilities registry route pass; deeper legacy action-system retirement remains. |
| Add menu | `TopRail/AddMenu*` | Attach/Create flows in active context | Preserve and correct | File/App/Capability attachment, implicit App ownership, relevance-only safety and Go-to-Chat snapshot tests pass; mobile/runtime persistence remains. |
| Dynamic action toolbar | `DynamicActionToolbar.cs` | Canonical capabilities/context surfaces only | Retire if redundant | Inspect consumers and preserve reachable commands before deletion. |
| Plugins domain/catalogue | `PluginCatalog`, obsolete persisted plugin registrations and legacy UI | App-owned Capability Registry | Clean-remove obsolete subsystem; do not migrate stale Plugin objects | Current Capability Registry objects/data remain protected normally. Delete legacy Plugin registrations, menus, editor/discovery routes, adapters and execution paths so they cannot resurface. |
| Macros UI/data | obsolete Macro definitions, menus and parallel execution paths | Tasks-owned Create Macro/capabilities/workflows | Clean-remove obsolete subsystem; do not migrate stale Macro objects | Re-provide the required functionality class through current Tasks/Capability architecture; do not preserve a legacy Macro compatibility backend. |
| Automations worker | obsolete `Haven.Automations` / `Haven.AutomationWorker` parallel scheduler/execution paths | Cross-platform Tasks/background execution | Clean-remove redundant legacy backend after current Tasks functionality is present | Current Tasks data remains protected. Required closed-UI execution, permissions and Windows/Android providers belong to the Tasks architecture; do not retain a hidden parallel automation scheduler. |
| Chat | `NewChatPage`, chat orchestration/storage | Chat App with GenUI/attachments | Preserve and integrate | Safe registered-template requests and trusted Calculator rendering now join existing conversation/attachment/tool-loop behavior; full template/destination/mobile parity remains. |
| Study | Study/Notes/Lesson/Call surfaces | Study App plus templates and Lesson Voice | Preserve and extend | Lesson/whiteboard/voice/structured-event proof required. |
| Tasks | `TasksPage`, Tasks storage/tools | Cross-platform Tasks App | Preserve and extend | Running/history/stop/approvals/scripts/background proof required. |
| Studio | `StudioProjectPage`, project/file/Git/build/test services | Studio plus App/Template/Capability/Instruction/Agent editors | Preserve and extend | Real workspace/tool loop and shared registry proof required. |
| Browse | Browser project/controls/policy | Browse App plus research templates | Preserve and extend | Real tab/source/navigation/download/permission proof required. |
| Plan | Built-in App and planner/calendar services | Plan App plus planning templates | Preserve and extend | Authoritative business-state integration required. |
| Training | Built-in App and `TrainingRunner` | Training App | Preserve unless brief explicitly replaces | Runtime/scoring/data parity proof required. |
| Imagine | Built-in App | Imagine App plus visual templates/providers | Preserve and extend | Real provider output; no simulated generation. |
| Present | Built-in App | Present App plus Presentation template | Preserve and extend | Real artifact workflow integration required. |
| Data | Built-in App | Data App plus table/dashboard templates | Preserve and extend | Structured data binding/incremental update proof required. |
| Vision | Built-in App | Vision capability/App integration | Preserve and extend | Real image attachment/model capability proof required. |
| Play | Built-in App | Play App plus interactive templates | Preserve and extend | Real launch/state evidence required. |
| Translate | Built-in App | Translate App | Preserve | Context/routing/output tests required. |
| Launcher App | Built-in `Launcher` | App/command launcher surface; distinct from old/new chooser | Preserve and clarify | Must not be deleted with `LauncherPicker`; routing tests required. |
| Go | `GoPage`, mascot route | Universal contextual search/navigation | Preserve and extend | All entry points and mobile/desktop headers required. |
| Dashboard | Dashboard App/pages | Dashboard templates and authoritative state | Preserve and extend | No static mock dashboard coverage. |
| Notes | Notes document, ink, preview, dictation/read-aloud services | Write/Study-compatible canonical templates | Preserve and integrate | Persistent document/ink/media/accessibility proof required. |
| Call/Voice | `ICallCoordinator`, call widget, speech/VAD/TTS services | Voice profiles and Lesson Voice runtime | Preserve and extend | Singleton/interruption plus 10-minute live runtime/privacy proof. |
| Model picker/provider routing | `ModelConfigurationControl`, provider clients/preferences | Universal picker plus browser/residency lifecycle | Preserve and extend | Provider/local names, effort, settings, cold start/reboot proof. |
| Browser safety | `Haven.Browser` policy/download/profile code | Same safety boundary through Browse/templates/agents | Preserve | Private targets, redirects, approvals and observed outcomes required. |
| Workspace file/process tools | `WorkspaceToolService` and tool orchestration | Production cross-platform agentic capability layer | Preserve and extend | Inspect-edit-build-test-repair loop on Windows/Android required. |
| SQLite persistence/migrations | `SqliteDatabase`, repositories/migrations | Forward-only expanded state model | Preserve data | Fixtures from every prior schema and backup/recovery tests required. |
| User preferences | `UserPreferencesService` | Theme/model/residency/voice/settings persistence | Preserve and extend | Restart/reboot and migration proof required. |
| Tidal background | `TidalBackground`, surface-aware routing | Canonical HavenUI background | Preserve and standardise | Theme/accent/motion/performance proof required. |
| Global call widget/floating UI | `GlobalCallWidget` | Floating Activity architecture | Preserve and generalise | Independent transparent desktop/Android host proof required. |
| Accessibility/motion preferences | existing styles and `MotionPreferencesService` | Global HavenUI contracts | Preserve and enforce | Keyboard, automation names, focus, reduced motion, high contrast and scaling evidence. |
| UI typography | Bundled Montserrat is global on Avalonia; native Android Haven UI packages Medium/SemiBold/ExtraBold | Montserrat-only Haven chrome with thick weight hierarchy | Preserve family, tighten enforcement | `USER-UI-0001`; static enforcement, 0-warning Android build and DPI-correct Windows screenshots visibly confirm the bundled family/thick hierarchy; Android device font-load proof remains. |

## Deletion gate

No row marked `Retire after parity`, `Retire after migration`, or `Migrate then retire` may be deleted until its destination is implemented, persisted data is migrated, all routes use the destination, release builds pass, and desktop/Android runtime evidence is recorded. A clean textual search is necessary but not sufficient.
