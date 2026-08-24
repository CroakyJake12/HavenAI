# Mandatory Haven Validation Rules

Validation is proportional to affected risk and always includes more than compilation for UI/runtime work.

## Required evidence

- Restore/build/test Debug and Release for the affected solution/projects, with 0 warnings/errors unless an explicit existing waiver applies.
- Focused tests first, then the full relevant suite. Add preservation/regression tests for fixed failures and migration fixtures for data changes.
- Launch the exact route the user will use. Verify real render and interaction; a compiled but crashing/clipped/wrong route fails.
- UI: desktop and Android sizes, all four HavenUI appearances, theme/accent changes, responsive layout, keyboard/touch, accessibility, reduced motion and representative animations.
- Floating Activity: independent host plus real corner/background transparency on Windows and Android.
- Platform providers: real browser, microphone/audio, screen capture, notifications/tray, Android Intents/device actions, launcher lifecycle and model residency as applicable.
- Agent/tool changes: real file edit, command, vision and multi-step observe/repair smoke paths under permissions where applicable.
- Lifecycle requirements: cold start, close/swipe-away, closed-UI background behaviour and reboot restoration where specified.
- Record exact command/configuration/platform, outcome, counts, logs/screenshots and unresolved limitations in the active release ledger/evidence file.

## Failure rules

- Green compilation/tests prove only the paths they exercised.
- Environment-limited required checks remain `Unvalidated`; they do not become success by inference.
- A provider acknowledgement is not an observed device/service outcome.
- If any applicable mandatory rule is knowingly broken, any required check fails, or any required runtime remains unvalidated, the task/pass is not successful and must be reported incomplete.
