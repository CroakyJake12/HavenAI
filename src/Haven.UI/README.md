# Haven.UI

`Haven.UI` is the platform-neutral owner of normal Haven UI. It deliberately does not reference Avalonia. Avalonia belongs in a backend/host project and translates Haven scene/layout/drawing/input abstractions instead of defining Haven's component identity.

## Finding a component

Standard components live under `Components/<Component>/<Component>.cs`. The component file contains its public API, canonical defaults, state behavior, and the names of shared classes/animations it uses.

| Component | Canonical source | Shared resources |
| --- | --- | --- |
| Button | `Components/Button/Button.cs` | `Resources/SystemClasses.hui`, `Resources/SystemAnimations.hui` |
| Text | `Components/Text/Text.cs` | `Resources/SystemClasses.hui` |
| Container | `Components/Container/Container.cs` | `Layout/HavenLayoutEngine.cs` + shared properties |
| Toggle | `Components/Toggle/Toggle.cs` | `Resources/SystemClasses.hui`, `Resources/SystemAnimations.hui` |
| Slider | `Components/Slider/Slider.cs` | `Resources/SystemClasses.hui`, `Resources/SystemAnimations.hui` |

`Scene/` owns the scene tree and property precedence. `Layout/` owns normal layout and Haven units. Its Pass C contract includes vertical/horizontal/wrap/overlay/canvas layout, fixed/Auto/fraction grid tracks, spans, margins, padding, alignment, min/max/aspect constraints, viewport conditions, scroll extents and backend-neutral clipping. `Conditions/` owns platform/screen render conditions. Reusable classes and animations live only in obvious central `.hui` resources.

The existing `Haven.Desktop/HavenUI` controls are pre-migration Avalonia controls, not the canonical Phase 2 implementation. They remain compatibility surfaces only while consumers move to the Haven scene tree/backend.

Migration must preserve Haven product features, page structure, navigation, interaction flows, Page Accents, spacing/typography/theme principles and design language except where authoritative mockups or a verified accidental framework artefact require correction.
