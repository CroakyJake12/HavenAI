# System Map

Entry points for the major production systems. "Start reading" names the file
that best explains the design.

| System | Owns / does | Start reading | Key collaborators |
|---|---|---|---|
| Shell & launch routing | Window shell, tabs, `LaunchAppAsync` route table, contextual actions | `src/Haven.Desktop/Interface/Shell/MainView.axaml.cs`, `HavenAppRoutePolicy` | Mode registry, TopRail, page factories |
| HavenUI framework | `.hui` markup, scene tree, layout, draw commands, input, animations | `src/Haven.UI/Markup/HavenMarkupParser.cs`, `Rendering/HavenSceneRenderer.cs`, README in project | Backend bridge (Desktop) |
| HUI↔Avalonia backend | Renders scenes into an Avalonia control; resolves images/fonts/tokens | `src/Haven.Desktop/HavenUI/Backend/HavenSceneControl.cs` | `HavenAvaloniaThemeResolver`, `HavenDesktopImageResolver`, `HavenUiFont` |
| Prefabs & DynamicUI | Reusable `.hui` components with code-behind; data-driven list rows | `src/Haven.UI/Components/Prefab/Prefab.cs`, `Components/DynamicUI/DynamicUI.cs`; examples in `src/Haven.Desktop/Prefabs`, `DynamicUI` | Scene pages (Chat, Imagine, Studio) |
| Themes & personalisation | 5 themes × 4 appearances, accent override, fonts, avatars | `src/Haven.Desktop/HavenUI/Tokens/HavenThemeCatalog.cs`, `Controls/SurfacePaletteCatalog.cs`, `Services/UserPreferencesService.cs` | `HavenUiResourceApplier`, Settings scene |
| Tidal background | Surface-following animated gradient backdrop | `src/Haven.Desktop/Controls/TidalBackground.*` | SurfacePaletteCatalog per surface |
| Chat | Conversation streaming, tools, attachments; HUI transcript via DynamicUI | `src/Haven.Desktop/Views/Pages/Chat/NewChatPage*.cs`, `ChatHavenScene.cs` | Application chat orchestration, Ollama client |
| Browser | Embedded browsing, safety policies, private profiles | `src/Haven.Desktop/Views/Pages/Browser/BrowserPage.axaml.cs` (+ scene), `src/Haven.Browser` | Infrastructure WebView integration |
| Documents (Write/Data/Canvas/Present) | App workspaces hosted by HUI scenes | `src/Haven.Desktop/Views/Pages/{Write,Data,Canvas,Present}` | Core document models |
| Automations & scheduler | Scheduled actions, worker leases | `src/Haven.Application/Automations/*` | AutomationWorker host process |
| GenUI (generated UI) | Model-generated surfaces from validated contracts | `src/Haven.Core/GenerativeUi/*`, `src/Haven.Desktop/HavenUI/GenerativeUi/*` | GenUI rules (`docs/GENUI_RULES.md`) |
| Projector (Android display) | Phone-as-display experiences, execution parity | `src/Haven.Application/Projector/ProjectorExperienceCatalog.cs`, `src/Haven.Android/Projector/*` | GenUI instance store |
| Mesh (device sharing) | Discovery + trusted-peer sync | `src/Haven.Application/Mesh/MeshCoordinator.*.cs` | Security rules |
| Model runtime | Ollama discovery/wake, model registry, residency | `src/Haven.Infrastructure` Ollama clients; `OllamaWakeService` | Preferences (`AutoWakeOllama`, `AlwaysLoaded`) |
| Persistence | Forward-only SQLite migrations, repositories | `src/Haven.Infrastructure` SQLite services | `docs/ARCHITECTURE_RULES.md` |

When adding a system: register it here with one line and a start-reading path.
