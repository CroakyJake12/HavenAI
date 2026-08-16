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
| Prefab | `Components/Prefab/Prefab.cs` | paired `Prefabs/<ID>.hui` + `Prefabs/<ID>.hui.cs` |
| DynamicUI | `Components/DynamicUI/DynamicUI.cs` | `DynamicUI/*.hui` template declarations + runtime API |

## Prefabs

A prefab is a reusable Haven component tree. Its markup lives in `Prefabs/<PrefabID>.hui`; optional behavior/state wiring lives in the normal compiled C# code-behind file `Prefabs/<PrefabID>.hui.cs`, analogous to `.xaml` + `.xaml.cs`. The code-behind derives from `HavenPrefabDefinition` and can use `OnCreated(Prefab instance)` plus `GetComponent<T>("Name")` to access and wire named elements.

Pages reference prefabs with a case-insensitive `Prefab` tag. `InstanceID`, `InstID`, and `iID` are equivalent; `PrefabID`, `pID`, and `ID` are equivalent, including attribute casing. For example: `<Prefab InstID="Go-Chatbox" ID="Chatbox" />`.

`PrefabMode.Dynamic` is the default. Dynamic state is scoped app-wide by `PrefabID` + `InstanceID`, so two IDs may expose different feature sets while reconstructed instances with the same IDs retain their state. `PrefabMode.Static` shares one state for a PrefabID across all current and future instances. `SetComponentEnabled("AddMenu", false)` collapses the named component with a dedicated prefab-state override; re-enabling removes that override and restores the component's authored/class/state visibility.

Prefab instances create their own Haven name scope, so repeated instances may safely reuse internal `Name` values and internal `OnClick` selectors resolve inside the originating prefab rather than another instance.

## Dynamic UI

Dynamic UI is the zero-to-many runtime counterpart to Prefabs. A `DynamicUI/*.hui` file declares a parsed-once template with `<DynamicUI Name="ConversationRow">...</DynamicUI>`; the declaration itself never enters the scene tree. Pages place an empty `<DynamicUIRuntime Name="ConversationRows" />` where runtime instances belong. Code creates a scoped `DynamicUI` API from a page/name-scope root plus `HavenDynamicUITemplateCatalog`, then calls `CreateItem(template, location, instanceId, values)` and receives a stable `DynamicUIItem` handle.

`{{VARIABLE}}` placeholders work both in element text and inside attributes. A whole attribute such as `Enabled="{{ISENABLED}}"` flows through the normal Haven property codec, so Boolean, numeric, enum and unit parsing remains canonical; mixed strings such as `Hello {{NAME}}` stay string interpolation. `Button Type="..."` is accepted as the markup alias for `Variant`, matching DynamicUI examples.

A `DynamicUIItem` supports `SetVariable`, `SetVariables`, targeted `SetProperty`, `ClearProperty`, and `Delete`. The scoped `DynamicUI` API also supports get/try-get, delete, clear, indexed creation, and move operations. Explicit `InstanceID` values are stable; omitted IDs are generated. Non-structural variable changes update only the affected existing Haven properties/text, preserving component identity and transient state; structural bindings such as Prefab identity, click actions, and render conditions intentionally rebuild that item. Every item creates its own Haven name scope so repeated instances may safely reuse internal `Name` values and internal click actions cannot target sibling instances.

Dynamic UI state is deliberately runtime-owned and non-persistent: reconstructing a host does not resurrect its item list, variables, ordering, or property overrides. Application models/services remain the source of truth and repopulate the host. DynamicUI templates may contain Prefab references, and Prefabs may contain `DynamicUIRuntime` hosts; both ultimately become ordinary framework-neutral Haven scene elements for the backend.

`Scene/` owns the scene tree and property precedence. `Layout/` owns normal layout and Haven units. Its Pass C contract includes vertical/horizontal/wrap/overlay/canvas layout, fixed/Auto/fraction grid tracks, spans, margins, padding, alignment, min/max/aspect constraints, viewport conditions, scroll extents and backend-neutral clipping. `Conditions/` owns platform/screen render conditions. Reusable classes and animations live only in obvious central `.hui` resources.

The existing `Haven.Desktop/HavenUI` controls are pre-migration Avalonia controls, not the canonical Phase 2 implementation. They remain compatibility surfaces only while consumers move to the Haven scene tree/backend.

Migration must preserve Haven product features, page structure, navigation, interaction flows, Page Accents, spacing/typography/theme principles and design language except where authoritative mockups or a verified accidental framework artefact require correction.
