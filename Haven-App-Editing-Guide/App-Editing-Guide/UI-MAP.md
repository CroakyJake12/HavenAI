# UI Map

## Global styling and controls

`src/Haven.Desktop/App.axaml` contains theme brushes, control themes, button variants, cards, chips, dropdown/flyout presenters and page DataTemplates. Runtime theme values are applied by `UserPreferencesService.ApplyTheme`.

Shared visual code lives in:

| Concern | Owner |
|---|---|
| Global control metrics and states | `App.axaml` |
| Theme-dependent brush values | `UserPreferencesService.cs` |
| Live acrylic/fallback control | `Controls/AcrylicSurface.cs` |
| Closed path icon registry | `Controls/HavenIcon.cs` |
| Markdown/LaTeX visual tree | `Controls/MarkdownView.cs` |

Do not add window-level copies of Button, ComboBox, card or flyout templates. See `SURFACES-AND-VISUAL-SYSTEM.md` for the current metrics and variants.

## Shell

`MainWindow.axaml` has three vertical regions:

1. 44px product/menu bar;
2. 38px horizontal workspace tab strip;
3. surface-specific sidebar plus selected page.

`WorkspaceTabViewModel.Surface` drives product identity, checked menu state and sidebar. Home, Call and Plan are singleton tabs. General Chat and Teach retain independent pages. Groups, projects, files and other multi-instance tabs use stable ID-based keys.

The General Chat sidebar shows Chat Groups, pinned chats and recent General Chat conversations. Opening a group changes it to group-specific identity and conversations. Teach always shows Quick Chats, Subjects and Create Subject; it never hides the sidebar merely because no subject exists. Studio project sidebars show project home, new project chat, project-filtered conversations, files, build/test/settings and Studio Home.

## Major pages

| Surface/page | View | View-model / owner |
|---|---|---|
| Home dashboard | `Views/HomeView.axaml` | `HomePageViewModel.cs` |
| General Chat / Teach / Do / Studio chat | `Views/ChatView.axaml` | `ChatPageViewModel.cs` |
| Chat Group home | `Views/ChatGroupView.axaml` | `ChatGroupPageViewModel.cs` |
| Local Call | `Views/CallView.axaml` | `CallPageViewModel.cs` |
| Plan | `Views/PlanView.axaml` | `PlanPageViewModel.cs` |
| Studio / Do Home | `Views/WorkspaceHomeView.axaml` | `WorkspaceHomePageViewModel` |
| New Studio project | `Views/ProjectCreatorView.axaml` | `ProjectCreatorPageViewModel.cs` |
| Studio project home | `Views/StudioProjectView.axaml` | `StudioProjectPageViewModel` |
| File editor and versions | `Views/WorkspaceEditorView.axaml` | `WorkspaceEditorPageViewModel` |
| Browse | `Views/BrowserView.axaml` | `BrowserPageViewModel.cs` |
| Agents / Plugins / Prompts | `Views/CatalogView.axaml` | `CatalogPageViewModel` |
| Settings and theme creator | `Views/SettingsView.axaml` | `SettingsPageViewModel` |
| Scheduled Actions | `Views/AutomationsView.axaml` | `AutomationsPageViewModel` |
| Macros | `Views/MacrosView.axaml` | `MacrosPageViewModel` |
| Archive | `Views/ArchiveView.axaml` | `ArchivePageViewModel` |

## Home layout

Home contains greeting/date/model health, refresh status, visible dashboard tiles, a customization area for ordering/hiding/restoring tiles, agenda and recent work. Navigation must go through the shell action map, not instantiate a second page directly. Keep loading, empty and failed-refresh states readable.

## Teach and group layout

Teach's sidebar has a permanent Quick Chats entry, a Subjects section, Create Subject, expandable lessons and settings/delete actions. A new subject creates and selects its default General lesson. Rapid changes must not paint lessons from an old subject.

Chat Group home contains New Chat, group stats, recent chats, instructions/context, references, Settings and Archive. File selection belongs in `ChatGroupView.axaml.cs`; validation/copy/deduplication belongs in `ContainerResourceRepository`.

## Call layout

Call exposes model, speech model, microphone/output/voice, input mode, mute, pause/resume, push-to-talk, share/stop share, interrupt, end call, waveform, live transcript and capability/error status. Pointer handling for hold-to-talk belongs in the view code-behind; call state belongs in `CallPageViewModel`/`CallCoordinator`.

Never show a working-looking screen-share or microphone control when the corresponding capability reports unavailable. Typed transcript entry remains the local fallback.

## Plan layout

Plan has collection navigation, view navigation, quick task/event capture, list/board/calendar areas, task/event editor panes, AI proposal review and provider status. `PlanPageViewModel` supplies Today, Inbox, Upcoming, List, Board, Day, Week, Month and Agenda views. Keep provider read-only events visibly non-editable.

## Project creation screen

This screen intentionally exposes only a new .NET project, a package-ready NuGet class library, an existing project/solution file or an existing local folder. Decision memory and diagnostic features belong in contextual Studio chat, not project creation.

Folder/file pickers live in `ProjectCreatorView.axaml.cs`; validation, process execution, failure cleanup and persistence are in `ProjectCreationService.cs`.

## Dropdowns and popovers

Use the global ComboBox styles for simple enums. Use Button + Flyout for searchable or title-plus-description choices. Product/mode switchers use the global acrylic presenter themes. Keep 16px presenter radius, 10px item radius, compact padding, explicit hover/selected/disabled states and an opaque fallback.

## Assets and icons

Raster product assets and `haven.ico` are under `src/Haven.Desktop/Assets`. UI glyphs belong in `HavenIcon`, not SVG bitmap `Image` controls or font glyphs. An invalid SVG can be decoded as a bitmap and crash view creation, which is why product and catalog glyphs use the central geometry registry.
