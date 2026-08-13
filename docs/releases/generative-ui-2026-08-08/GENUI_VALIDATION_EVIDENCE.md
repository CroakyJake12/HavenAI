# Generative UI Release Validation Evidence

Overall status: **INCOMPLETE**

## Baseline — 8 August 2026

| Check | Result | Evidence |
| --- | --- | --- |
| Git preflight | Passed | Clean `main` at `39f8f3c8dcda75f3558a1bb5657c9d539235486f`; implementation branch created as `codex/genui-release-overhaul-20260808`. |
| Restore | Passed | `dotnet restore Haven.sln`; all projects up to date. Initial sandbox attempt was denied access to `%APPDATA%\NuGet\NuGet.Config`; approved normal-user execution passed. |
| Debug build | Passed | `dotnet build Haven.sln -c Debug --no-restore`; 0 warnings, 0 errors. |
| Debug tests | Passed | `dotnet test Haven.sln -c Debug --no-build`; 462 passed, 0 failed, 0 skipped. Core 139, Infrastructure 189, Desktop 134. |
| PowerPoint inspection | Passed for source intake | 21/21 rendered slides and all 8 notes-slide relationships inspected. This is not product visual validation. |
| Repository rule integrity | Passed | `powershell -NoProfile -ExecutionPolicy Bypass -File tools/release/Test-HavenRules.ps1`; exit 0. Required rule paths, `AGENTS.md` references, HavenUI lock phrases and requirement-index count/SHA-256 matched. A fresh-agent compliance audit remains outstanding, so GENUI-17 is still `In progress`. |
| Implementation Debug build | Passed | `dotnet build Haven.sln -c Debug --no-restore`; exit 0, 0 warnings, 0 errors after startup-route, App-route, attachment, Montserrat, historical-migration and repository-rule changes. |
| Implementation Debug tests | Passed | `dotnet test Haven.sln -c Debug --no-build`; 491 passed, 0 failed, 0 skipped. Core 139, Infrastructure 196, Desktop 156. This is not a substitute for the pending runtime/visual/device checks below. |
| Android restore | Passed | `dotnet restore src/Haven.Android/Haven.Android.csproj`; exit 0. |
| Android composition diagnosis | Repaired | Initial Android build exposed 122 errors because linked Desktop `Interface/**/*.axaml.cs` lacked matching `AvaloniaXaml` items, plus one stale mobile shell field and two removed SDK drawable IDs. The project now links Interface AXAML, the stale field is removed, and launcher icons are Haven-owned vector resources. |
| Android Debug APK build | Passed | `dotnet build src/Haven.Android/Haven.Android.csproj -c Debug --no-restore`; exit 0 after composition repair. The initial 28 documented AVLN3001 warnings were explicitly suppressed and multiple later confirmation builds, including the final 9 August build below, passed with 0 warnings/errors. Device launch remains pending. |
| HavenUI appearance/hold focused tests | Passed | Appearance, bundled-font, fresh-install Bright, surface palette and five-second hold behavior are included in the final passing 178-test Desktop suite below. Runtime proof currently covers Bright on Windows; the complete four-theme/device visual matrix remains pending. |

## Foundation implementation evidence - 9 August 2026

| Check | Result | Evidence |
| --- | --- | --- |
| Debug solution build | Passed | `dotnet build Haven.sln -c Debug --no-restore`; 0 warnings, 0 errors after Capability Registry, GenUI contracts/router/store/renderer, Chat request path, Template Preview Lab and high-DPI route fixes. |
| Sequential automated suites | Passed | Core 160/160, Infrastructure 202/202, Desktop 178/178; 540 passed, 0 failed, 0 skipped. Projects were run sequentially because the parallel solution runner reproduced one existing Notes headless timing collision; the complete Desktop suite and isolated affected Notes test both passed. |
| Android Debug APK build | Passed | Final `dotnet build src/Haven.Android/Haven.Android.csproj -c Debug --no-restore`; 0 warnings, 0 errors. This is compile/package evidence, not device-launch evidence. |
| Repository rules/index | Passed | `powershell -NoProfile -ExecutionPolicy Bypass -File tools/release/Test-HavenRules.ps1`; mandatory rule paths, Montserrat contracts and source-index integrity valid. |
| Windows Go startup | Passed for observed route | Compiled branch launched into New Haven Go with no old/new chooser. DPI-correct DWM screenshot: `artifacts/validation/generative-ui-2026-08-09/desktop-startup-highdpi-dwm.png`. Bundled Montserrat and the thick hierarchy are visibly active. |
| High-DPI containment | Repaired and headless-tested | Reduced the impossible logical minimum from 1050x700 to 720x520, centred startup, made Go's composer column fluid, and added compact viewport coverage. |
| Capability Registry route | Passed for observed Windows route | Windows UI Automation exercised Capabilities -> Edit Capabilities. The initial check found that Edit Capabilities incorrectly opened Settings; the handler was repaired to open the real v11 registry. Screenshot: `artifacts/validation/generative-ui-2026-08-09/desktop-capability-registry-corrected.png`. |
| Template Preview Lab route | Passed for observed Windows route | Opened from the Capability Registry and displayed searchable versioned foundation metadata plus the trusted Calculator renderer. Screenshot: `artifacts/validation/generative-ui-2026-08-09/desktop-template-preview-lab.png`. |
| Calculator semantic loop | Passed for observed Windows behavior | Windows UI Automation set `calculator.expression` to `sqrt(81) + 2^3`, invoked `calculator.calculate`, and read accessible result `17`. The visible trace recorded the semantic event, Completed result and four incremental patches without a model. Screenshot: `artifacts/validation/generative-ui-2026-08-09/desktop-template-preview-calculated.png`. |
| Windows automation skill | Environment-limited fallback | The bundled Computer Use runtime could not initialise because its Node host was denied access to the Codex app-data directory. Validation used Windows UI Automation and DPI-correct DWM target-window captures instead. |

## Haven.UI Phase 2 Pass C - 13 August 2026

This package advances the framework-neutral layout, scrolling and clipping layer. It does not claim a migrated product route, Android device result or release completion.

| Check | Result | Evidence |
| --- | --- | --- |
| Current mockup intake | Passed for source inspection | The user-provided `Haven_AI_Generative_UI_Update_REPAIRED_LIGHTWEIGHT.pptx` was rendered and inspected slide-by-slide: 24/24 slides, SHA-256 `38C59F3A757EB2430E47706AF0DE664638F0278BFA479AA954C86BFAAD94AEFA`. This is a newer source snapshot than the historical 21-slide baseline recorded above; its source-index reconciliation remains outstanding. |
| Haven.UI layout contract | Implemented and focused-tested | Framework-neutral vertical, horizontal, wrap, grid, canvas and overlay layout now covers margin, padding, gap, alignment, responsive minimum/maximum/aspect rules, fixed/Auto/fraction tracks, row/column spans, viewport extents, clamped scrolling and clip-aware hit testing. `dotnet test tests/Haven.UI.Tests/Haven.UI.Tests.csproj -c Release` passed 41/41. |
| Avalonia backend host | Passed headless runtime exercise | A real headless Avalonia `Window` hosted one `HavenSceneControl`; the test asserted an empty Avalonia child-control tree, exercised fractional layout, clipping and dynamic scene-node invalidation, and passed 2/2 backend tests. The opt-in frame test separately passed 1/1. |
| Rendered frame | Passed for this bounded scene | `artifacts/validation/haven-ui-phase-2-2026-08-13/haven-scene-pass-c.png` was inspected at 320x200. The 1fr/2fr buttons render without wrapping, clipping or overlap. This is backend-scene evidence, not a production-route comparison. |
| Release clean/restore/build | Passed | `dotnet clean Haven.sln -c Release`; `dotnet restore Haven.sln`; `dotnet build Haven.sln -c Release --no-restore`. Final build: 0 warnings, 0 errors. `Haven.UI` and `Haven.UI.Tests` are now included in the solution. |
| Release automated suites | Passed | `dotnet test Haven.sln -c Release --no-build`: UI 41, Core 208, Infrastructure 190, Desktop 187; 626 passed, 0 failed, 0 skipped. |
| Repository rules/index | Passed | `powershell -NoProfile -ExecutionPolicy Bypass -File tools/release/Test-HavenRules.ps1`; mandatory rule paths, Montserrat contracts and release-index integrity valid. |
| Android shared-source integration | Repaired in source; package unvalidated | Because Android explicitly links the Desktop backend source, it now references `Haven.UI` and excludes custom `bin-*`/`obj-*` trees. The normal Debug build advanced through managed compilation, then failed in Xamarin cleanup with `XARDF7024` because the existing OneDrive-generated `onnxruntime` directory inherits `Everyone: Deny DeleteSubdirectoriesAndFiles`. A disposable-intermediate retry avoided that ACL but failed native linking with `XA3007` missing generated object files. No APK or device success is claimed. |

## Mandatory release matrix

All rows below remain unresolved until evidence is added. Build success alone cannot pass route, behaviour, visual, device, accessibility, persistence or performance requirements.

| Validation family | Debug | Release | Windows runtime | Android runtime | Cold start | Reboot/closed UI | Visual four-theme | Accessibility | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Core/Application/Infrastructure | Baseline passed | Pending | Pending where applicable | Pending | Pending | Pending | N/A | Pending | Incomplete |
| Desktop shell and Apps | Baseline compiled | Pending | Pending | N/A | Pending | Pending | Pending | Pending | Incomplete |
| Android shared UI/Haven Home | Debug APK compiled | Pending | N/A | Pending device launch | Pending | Pending | Pending | Pending | Incomplete |
| Model lifecycle/residency | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Incomplete |
| GenUI event/state/action loop | Debug contracts/integration passed | Pending | Calculator local loop observed | Android compiled; device pending | Pending | Pending | Bright screenshot only | Automation IDs/names exercised; full audit pending | Incomplete |
| Templates | Registry/Calculator tests passed | Pending | Registry/Lab/Calculator observed | Android compiled; device pending | Pending | Pending | Bright screenshot only | Calculator automation route passed; full audit pending | Incomplete |
| Voice/Lesson Voice | Pending | Pending | Pending 10-minute run | Pending 10-minute run | Pending | Pending | Pending | Pending | Incomplete |
| Floating Activities | Pending | Pending | Pending real transparency | Pending real transparency | Pending | Pending | Pending | Pending | Incomplete |
| Agentic tools/permissions | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Incomplete |

## User-reported visual remediation - 9 August 2026

| Finding | Current state | Evidence / remaining gate |
| --- | --- | --- |
| Chat displayed `Avalonia.Controls.Grid` instead of message text | Repaired in source and observed in the user's follow-up runtime screenshot | `MessageBubble` now uses `ProductionMarkdownView`; the focused component regression passed 1/1 before the execution allowance closed. Full routed-chat automated coverage and final build remain pending. |
| Chat answered that it could not generate UI even though the trusted runtime was registered | Repaired in source, unvalidated after final edit | Availability/status questions are now answered deterministically from Haven's own registry state and include the live Calculator directive as proof. Specific generated-UI requests still flow to the model and bounded template router. Parser coverage was added but could not be executed after the local execution allowance closed. |
| Startup flashed white before settling dark | Repaired in source, unvalidated after final edit | `TidalBackground` now initializes its first frame directly from the stored appearance palette instead of white and uses a broader dark-to-accent blend. Cold-start capture is pending. |
| Reasoning slider track disappeared at endpoints | Repaired in source, unvalidated after final edit | `HavenSliderTrack` now draws a permanent inactive and proportional live-gradient active bar underneath the accessible Avalonia Track. Visual and interaction tests are pending. |
| Add menu appeared as unstyled page content; model lists leaked Fluent grey popup | Repaired in source, unvalidated after final edit | Both routes now use `HavenDropdown`, `HavenDropdownCard` and `HavenDropdownItemButton`; model/GenUI selection uses the fully Haven-owned `HavenSelect`. Popup placement, keyboard and visual evidence remain pending. |
| Browse utility panels overlapped simultaneously | Repaired in source, unvalidated after final edit | Panels are mutually exclusive and the containing card is hidden until invoked. Browse runtime/functionality and responsive evidence remain pending. |
| App Library, Projects and Plan retained legacy/overpacked presentation | Active remediation, incomplete | App Library card grid, live-gradient Projects surfaces/create action, and readable Plan navigation/provider actions are implemented in source. Complete screen-by-screen reconstruction and runtime comparison remain mandatory. |

The next automated test/build invocation was rejected before execution because the Codex local execution allowance was exhausted. No source edit made after that rejection is recorded as passed merely from inspection.

## Evidence discipline

- Record exact command, configuration, platform/runtime, exit code and artifact/log path.
- Provider acknowledgement is not an observed outcome.
- If a required runtime is unavailable, mark `Unvalidated`; do not translate environmental absence into success.
- Screenshots must identify route, theme, viewport/device and state. They supplement behaviour evidence; they do not replace it.
