# UI Map

## Global styling

`src/Haven.Desktop/App.axaml` contains:

- theme brush keys (`HavenBackgroundBrush`, `HavenPanelBrush`, `HavenAccentBrush`, and others);
- global TextBlock, Button, TextBox, ComboBox, ComboBoxItem, Flyout and MenuItem styles;
- compact sidebar utility styles;
- horizontal workspace-tab styles;
- page DataTemplates.

Runtime themes are applied by `UserPreferencesService.ApplyTheme`. If you add a required brush, add a default resource in `App.axaml` and update `ApplyTheme` so custom/light themes also receive it.

## Shell

`MainWindow.axaml` has three vertical regions:

1. 44px product/menu bar;
2. 38px horizontal workspace tab strip;
3. sidebar plus current page.

The tab strip binds to `MainWindowViewModel.OpenTabs`. Each `WorkspaceTabViewModel` owns a stable key, title, page, closeability and selected state. Reuse a stable key for a single-instance surface; include a project or file ID when instances need independent state.

The normal sidebar shows groups/projects, pinned chats and recent chats. In an active Studio project it switches to:

- project identity and home;
- New project chat;
- project-filtered pinned/recent chats;
- project files;
- Build, Test, Project settings and Studio Home.

The bottom global destinations are deliberately compact. Full feature navigation remains in the top menus and Ctrl+K command palette.

## Major pages

| Page | View | View-model |
|---|---|---|
| Chat / Teach / Do / Studio chat | `ChatView.axaml` | `ChatPageViewModel.cs` |
| Studio / Do Home | `WorkspaceHomeView.axaml` | `WorkspaceHomePageViewModel` in `WorkspaceSurfacesViewModels.cs` |
| New Studio project | `ProjectCreatorView.axaml` | `ProjectCreatorPageViewModel.cs` |
| Project home | `StudioProjectView.axaml` | `StudioProjectPageViewModel` |
| File editor and versions | `WorkspaceEditorView.axaml` | `WorkspaceEditorPageViewModel` |
| Browse | `BrowserView.axaml` | `BrowserPageViewModel.cs` |
| Agents / Plugins / Prompts | `CatalogView.axaml` | `CatalogPageViewModel` in `UtilityPagesViewModels.cs` |
| Settings and theme creator | `SettingsView.axaml` | `SettingsPageViewModel` |
| Scheduled Actions | `AutomationsView.axaml` | `AutomationsPageViewModel` |
| Macros | `MacrosView.axaml` | `MacrosPageViewModel` |
| Archive | `ArchiveView.axaml` | `ArchivePageViewModel` |

## Project creation screen

Keep this screen focused. It intentionally exposes only:

- a new .NET project (Console, Class Library, Web API or Worker);
- a package-ready NuGet class library and initial Release pack;
- an existing project/solution file;
- an existing local folder.

Do not add Decision Memory, Bug Time Machine, risk forecasting or prompt cards here. Those are contextual Studio-chat behaviors.

The folder/file pickers are in `ProjectCreatorView.axaml.cs`; validation, process execution, failure cleanup and persistence are in `ProjectCreationService.cs`.

New projects run a fixed `dotnet new` template into a direct child of the selected destination. A NuGet choice creates a class library, adds package ID/version (`0.1.0`)/author/description/build properties, then runs the first Release pack. If Haven created the target directory and creation fails, it removes only that direct child; it does not remove a folder that existed before the attempt. Connecting a file uses its containing folder, and registering an already-connected root reuses the existing Studio project.

## Dropdowns and popovers

Pass 8 used dark, rounded, compact popovers. The Avalonia equivalents are centralized in `App.axaml`:

- closed selectors use a panel-2 background, 11px radius and strong border;
- popup presenters use 16px radius and 7px padding;
- items use 10px radius, compact 8–10px padding and explicit hover/selected fills;
- model selection remains a searchable custom panel in `ChatView.axaml` because it needs RAM and capability details.

Do not introduce an unstyled native selector on one page. Use the shared ComboBox styles for simple enums and a Button/Flyout panel for searchable or title-plus-description choices.

## Assets

All app icons are under `src/Haven.Desktop/Assets`. They are bundled by the `AvaloniaResource Include="Assets\**"` item in `Haven.Desktop.csproj`.

Use `/Assets/name.svg` or `/Assets/name.png` from XAML. Validate SVGs before use; an invalid SVG can be treated as a bitmap and crash view creation. The window icon is `haven.ico`; product images are `haven-32.png` and `haven-192.png`.
