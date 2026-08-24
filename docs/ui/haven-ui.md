# HavenUI (HUI) — the UI framework

Haven's user-facing interfaces are built with **HavenUI (HUI)**, Haven's own
scene-based UI layer in `src/Haven.UI`. HUI is deliberately **platform-free**:
it has zero Avalonia references. It parses `.hui` markup into a `HavenElement`
scene tree, lays it out, produces backend-neutral draw commands and routes
input; a thin host bridge renders those commands with Avalonia.

```text
.hui markup / C# scenes
        ↓  HavenMarkupParser
HavenElement scene tree  (src/Haven.UI)
        ↓  HavenLayoutEngine → HavenSceneRenderer → HavenDrawCommands
HavenSceneControl (Avalonia Panel, src/Haven.Desktop/HavenUI/Backend)
        ↓
Avalonia rendering → Desktop / Android
```

Avalonia remains the platform layer (windowing, input plumbing, native
controls, headless testing). Removing AXAML pages does **not** mean removing
Avalonia.

## Core pieces

| Piece | Where | Notes |
|---|---|---|
| Parser | `Markup/HavenMarkupParser.cs` | Parses `<Page>`, containers, components, render conditions (`Platform`, screen ranges), `OnClick` actions |
| Scene model | `Scene/HavenElement.cs`, `HavenProperties.cs` | Property precedence, class tokens, accessibility roles |
| Layout | `Layout/HavenLayoutEngine.cs` | Vertical/horizontal/wrap/grid/overlay/canvas + scroll |
| Renderer | `Rendering/HavenSceneRenderer.cs`, `Drawing/HavenDrawing.cs` | Emits commands — never Avalonia controls (asserted by tests) |
| Input | `Input/HavenInputRouter.cs` | Pointer/keyboard routing, direct manipulation sessions |
| Animations | `Animation/HavenAnimationEngine.cs` | Keyframes/transitions from `.hui` resources; honours reduced motion via `HavenMotionPolicy` |
| Classes & animations | `Resources/*.hui` + `HavenResourceParser` | CSS-like central resources: `SystemClasses.hui`, `UserClasses.hui`, `SystemAnimations.hui`, `UserAnimations.hui`; user files override by kind |
| Prefabs | `Components/Prefab/Prefab.cs` | `X.hui` + paired `X.hui.cs` (`HavenPrefabDefinition`), discovered via `FromAssembly`, 1:1 pairing enforced |
| DynamicUI | `Components/DynamicUI/DynamicUI.cs` | Template catalog + `CreateItem(template, location, id, variables)` for data-driven rows/cards with `{{VAR}}` interpolation |

## How surfaces are composed today

1. **Page hosts**: most pages are small AXAML user controls hosting one
   `<backend:HavenSceneControl x:Name="Scene"/>` (e.g.
   `Views/Pages/Settings/SettingsHavenPage.axaml`). The visible product is the
   scene, not the host.
2. **Scenes**: C# classes building/owning the `Page` root — either inline
   markup (`ChatHavenScene.BuildRoot()` parses a raw string) or pure element
   construction. Scenes expose typed component properties
   (`GetComponent<Button>("Name")`) for wiring events to services.
3. **Prefabs**: reusable pieces as real embedded `.hui` files with code-behind
   (e.g., `Prefabs/Chatbox.hui(.cs)`).
4. **DynamicUI templates**: list rows/cards as `.hui` templates fed by
   variables (e.g., `DynamicUI/ChatUserMessage.hui`).

Embedded resources: `.hui` files are `<EmbeddedResource>` entries in each
consuming `.csproj`; catalogs discover them by assembly manifest names.

## Theming

Scenes never hard-code colours. They reference semantic tokens
(`Foreground="TextSecondary"`) resolved through
`Haven.Desktop.HavenUI.Backend.HavenAvaloniaThemeResolver` into the same
application resources that Avalonia controls use — so themes, appearances,
accent overrides and fonts flow into every surface automatically. See
[theming-and-personalisation.md](theming-and-personalisation.md).

## Testing

`tests/Haven.UI.Tests` covers parser/layout/renderer/input/animations/prefabs
without Avalonia. Desktop-side headless tests (`tests/Haven.Desktop.Tests`)
cover real pages and the backend bridge. When you add framework features, add
tests there first; keep the renderer free of Avalonia types.
