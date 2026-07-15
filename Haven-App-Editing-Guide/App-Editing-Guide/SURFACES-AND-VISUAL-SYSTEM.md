# Surfaces and Visual System

## `HavenSurface` versus `HavenMode`

`src/Haven.Core/HavenSurface.cs` defines transient shell destinations:

```text
Home, Chat, Teach, Call, Do, Studio, Browse, Plan, Training
```

`HavenSurface` belongs to an open workspace tab. It drives the product title, checked menu item and sidebar even when the selected page is not a conversation. It is intentionally not stored in SQLite.

`HavenMode` is the persisted conversation mode and retains its existing numeric values (`Chat = 0`, `Teach = 1`, `Do = 2`, `Studio = 3`). Do not add Home, Call, Browse or Plan to `HavenMode`. Call transcripts use `ConversationKind.Call`; planner records have their own entities.

## Workspace tabs and activation

`WorkspaceTabViewModel` in `MainWindowViewModel.cs` owns:

- a stable key;
- a title and page object;
- closeability and selection state;
- a `HavenSurface` identity.

Home, Call and Plan use stable keys (`home`, `call`, `plan`) and are singleton pages. Chat and Teach have independent stable tabs. Project, file, group and conversation keys include their IDs where state must remain independent.

Use `MainWindowViewModel.AddOrSelectTab` for new shell pages. Pass the intended surface explicitly when inference would be ambiguous. Activation-aware pages implement `IActivatablePage`; the shell deactivates the old page and activates the new one. Home uses this to run its one-minute refresh only while visible.

When adding a surface:

1. append a non-persisted `HavenSurface` member;
2. add the command and stable tab key in `MainWindowViewModel`;
3. include it in product name/menu/sidebar state;
4. add a DataTemplate in `App.axaml`;
5. test switching to it from a chat and project without leaking the previous sidebar.

## Global theme contract

`src/Haven.Desktop/App.axaml` is the single source for shared control appearance. `UserPreferencesService.ApplyTheme` updates the theme-dependent brushes for dark, light, system and custom themes.

The current visual metrics are:

- standard controls: 36px high;
- buttons: 10px corner radius;
- cards: 14px corner radius;
- flyout and menu presenters: 16px corner radius.

Use the shared button classes instead of a page-local template:

- `primary` for the main positive action;
- `secondary` for bordered alternatives;
- `subtle` for low-emphasis actions;
- `icon` and `compact` for square/compact utilities;
- `navigation` / `sidebar` / `tab` for navigation;
- `danger` for destructive actions;
- `send`, `stop` and `chip` for their named interaction roles.

Shared themes own pointer-over, pressed, disabled and focus-visible states. A page may set layout properties such as width or margin, but should not copy the global control template or invent another colour system.

## Reusable acrylic

`Controls/AcrylicSurface.cs` is a `ContentControl` with themeable `TintColor`, `FallbackColor`, `TintOpacity` and `MaterialOpacity`. Its global ControlTheme in `App.axaml` uses Avalonia's compositor-backed Digger acrylic and an opaque fallback colour. The same resource file defines:

- `HavenAcrylicFlyoutPresenterTheme`;
- `HavenAcrylicMenuPresenterTheme`.

Use it directly for a reusable elevated panel:

```xml
<controls:AcrylicSurface Padding="12">
  <!-- panel content -->
</controls:AcrylicSurface>
```

Use the shared presenter theme for a flyout rather than reproducing an acrylic brush locally. Always check readability with Windows transparency disabled; fallback rendering is part of the contract, not an optional visual effect.

## `HavenIcon` registry

`Controls/HavenIcon.cs` replaces SVG-as-bitmap loading, open geometries and font glyph icons. It maps a case-insensitive `IconKey` to a closed filled `StreamGeometry` and renders `info` for an unknown key. Because it derives from Avalonia's `PathIcon`, keep its `StyleKeyOverride` pointing at `PathIcon`; removing that override leaves accessible geometry in the tree but no drawn template.

Use it in XAML:

```xml
<controls:HavenIcon IconKey="plan" Width="18" Height="18" />
<controls:HavenIcon IconKey="{Binding IconKey}" Width="18" Height="18" />
```

Core surface keys include `home`, `chat`, `teach`, `call`, `tasks`, `studio`, `browse`, `plan` and `training`. Utility keys include `settings`, `archive`, `folder`, `file`, `edit`, `delete`, `refresh`, `search`, `mic`, `mute`, `screen-share`, `hang-up`, `pin` and `bookmark`. The registry also contains aliases for persisted built-in Agent, Plugin and Prompt keys.

When adding an icon:

1. add one closed, filled, 24-unit geometry to `BuildIcons`;
2. keep persisted aliases if an old catalog key already exists;
3. render it at 16, 20 and 24px in dark and light themes;
4. test an unknown key so the fallback remains visible;
5. do not add an `<Image Source="/Assets/example.svg">` for a UI glyph.

`src/Haven.Desktop/Assets/haven.ico` is the native application/window icon. If it changes, retain 16, 24, 32, 48, 64, 128 and 256px frames and verify the executable and taskbar at normal and high DPI.

## Visual smoke test

Check all of the following after theme or shell edits:

1. dark, light and system theme;
2. Windows transparency on and off;
3. 100%, 150% and 200% scaling;
4. every button state, including keyboard focus and disabled;
5. both product switchers and ordinary flyouts while the window moves;
6. singleton Home/Call/Plan tab selection and correct menu/sidebar state;
7. known, persisted-alias and unknown icons.
