# Mandatory Desktop and Android Platform Rules

- Preserve semantic capability parity across Windows and Android wherever the feature exists. Platform-specific providers/hosts are required for OS integration; fake cross-platform stubs are not completion.
- Shared HavenUI, App, GenUI, template, state and event contracts remain platform-neutral. Windows/Android implementation details stay behind interfaces/providers.
- Android Tasks support applicable commands/scripts, device actions and Intents through real Android providers and the same risk/permission policy.
- Haven Home is a real Android launcher/home activity, not an in-app imitation. Validate launcher selection, Home intent, app launch, lifecycle, cold start and device behaviour.
- Model residency uses OS-appropriate background ownership independent of the main UI: Windows tray/startup integration and Android ongoing-notification/background execution. Validate close/swipe-away and reboot restoration.
- Floating Activity hosts require real transparency on both supported platforms and honest fallbacks where an OS version cannot provide a capability.
- Required desktop and Android runtime, render, responsive, cold-start and reboot checks are not interchangeable. An unavailable device/runtime yields `Unvalidated`, not `Passed`.
