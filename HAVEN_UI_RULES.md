# Mandatory HavenUI Rules

These rules are mandatory for every Haven-owned desktop, Android, Floating Activity, generated App, template, editor and settings surface. A UI change is invalid if it breaks an applicable rule, even when it compiles or looks correct in one screenshot.

## Theme Modification Lock

During the active Generative UI release, implement the initial HavenUI migration exactly against the release ledger and authoritative mockups. After that migration is recorded as complete, agents may repair conformance bugs and migrate newly discovered bypasses, but must not redesign HavenUI or change its approved primitives, geometry, motion, typography, four appearances or interaction language unless the user explicitly requests a design-system change.

## Canonical visual language

- Use one shared HavenUI component system. Do not create screen-local copies of common buttons, fields, cards, menus, tabs, pop-ups, model controls, headers or context actions.
- Use semantic resources/tokens. Hard-coded colours are forbidden in Haven-owned production UI except inside a named canonical token/palette definition or a deliberate test fixture.
- Super Bright, Bright, Dark and Super Dark are colour appearances of one layout/component system. Theme changes alter colours only; geometry, spacing, navigation and control identity do not change.
- Accent colour is scoped to the owning App/thread/Floating Activity. It must not mutate unrelated global UI.
- The background is an anchored darker-accent-to-black tidal gradient. It may animate gradient flow/colour and morph between accents, but it does not travel with page-layout transitions.
- Translucent overlays blur the content behind them and retain real transparent corners where the platform host supports transparency.

## Typography — USER-UI-0001

- All Haven-owned UI chrome uses the bundled Montserrat family. This includes labels, navigation, buttons, menus, pop-ups, settings, headers, editors and generated/template chrome.
- Prefer thick weights: Medium or SemiBold is the normal UI baseline; Bold/ExtraBold establishes prominent hierarchy. Light and Thin weights are forbidden.
- Code, terminal output, mathematical notation and user-authored document content may use a semantically appropriate content font. This exception must not leak into surrounding Haven controls.
- Never depend on a system-installed Montserrat font. Use the bundled assets under `src/Haven.Desktop/Assets/Fonts` so desktop and Android render consistently.

## Canonical interaction

- Buttons visibly morph into hover variants and bounce/brighten on activation, subject to reduced-motion settings. Negative destructive controls use the approved hold-to-confirm interaction where specified.
- Dropdowns, cards and pop-ups follow the shared style/state contracts. Pop-ups have a mandatory Close action, optional primary action, outside-click dismissal and vertical-only overflow.
- Meaningful context actions are reachable by right-click on desktop and long-press on mobile, with touch-sized accessible menus.
- Preserve identity for controls that logically persist across layout changes; prefer layout/shared-element motion rather than destroying and recreating the object.
- Respect reduced motion, scalable text, keyboard navigation, automation names, focus restoration and high-contrast fallbacks.

## Responsive and platform rules

- Desktop and Android use the same semantic components and state contracts. Platform hosts/providers may differ where required by the OS.
- Flexible windows and mobile screens must not clip, overlap, hide required actions or introduce horizontal scrolling accidentally.
- Every changed major surface must be exercised at desktop and mobile dimensions in all four appearances.
- A genuinely unavailable OS capability must show an honest unsupported/setup-required state; do not simulate success.

## Floating Activities

- Floating Activities use the same HavenUI tokens, components, typography, motion, accessibility and scoped App state as the main shell.
- Their content must render independently of the main window. Do not couple reusable controls to `MainWindow`.
- Windows and Android hosts use real host transparency; a painted rectangle that resembles transparency is not compliant.
- State shared with a main-window surface uses one authoritative state owner rather than two competing copies.

## Implementation and validation

- New release UI uses minimal AXAML hosts plus code-behind wiring consistent with the repository architecture; do not introduce a parallel page tree or a second UI framework.
- A green build is insufficient. Add/run headless route/style tests, launch the actual route, inspect the rendered result, and record desktop/mobile/theme evidence required by `docs/VALIDATION_RULES.md`.
- If a UI rule is knowingly broken or required runtime rendering is unvalidated, report the task as incomplete.
