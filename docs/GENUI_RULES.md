# Mandatory Generative UI, App and Template Rules

- GenUI supplements conversation; it never removes the user's free-text escape hatch.
- All meaningful interactions use the shared bidirectional Action/Event Router with stable IDs, structured state, results, errors and originating task/agent context. Do not create bespoke parallel tool paths.
- Intermediate pointer movement stays local. Emit semantic completed operations/selections, not pointer noise.
- Deterministic computation and local presentation state remain deterministic/local. Do not call an LLM for arithmetic, toggles, reveals, filtering or other operations that do not require inference.
- Apps own authoritative business state. Templates bind to real App/services state and update incrementally; no fake repositories, terminals, calendars, dashboards or device results.
- Apps, templates, generated components and User Templates use canonical HavenUI and cannot redefine the design system without explicit user authority.
- Templates are composable, versioned, dependency-aware, trust-aware and lifecycle-managed. Canonical first-party templates are immutable to generated instances; duplicate/fork creates a new identity.
- A template name, enum, empty renderer, screenshot or generic card is not coverage. Each configured experience must meet its feature-completeness, usability, mobile, accessibility and event/state contract.
- Generated/imported UI remains sandboxed and permission-aware. Capability discovery does not grant capability execution.
- Attach File/App enriches the active App/thread/task. Selection alone must not navigate to Chat or launch the App.
- Visible architecture uses Apps and Capabilities. Do not reintroduce obsolete Actions, Plugins, Macros or major-product Modes after their migration is complete.
