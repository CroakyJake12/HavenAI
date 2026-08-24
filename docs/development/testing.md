# Testing

All commands below were verified against this repository. Run them from the
repository root unless noted.

## Prerequisites

- .NET SDK from `global.json` (`dotnet --version` should resolve via
  `rollForward: latestFeature`; `10.0.301` is the minimum).
- Windows host for Desktop suites; Android suites need an emulator/device.

Opt out of Avalonia telemetry when log files are locked:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
```

## Builds

```powershell
# Restore + Release build of the whole solution (0 warnings expected)
dotnet build Haven.sln -c Release

# Single-project build useful while iterating on Desktop
dotnet build src/Haven.Desktop/Haven.Desktop.csproj -c Release
```

`Haven.Android` builds only on Windows hosts with the Android workload.

## Tests

```powershell
# Full suite (xUnit; headless Avalonia where marked [AvaloniaFact])
dotnet test Haven.sln -c Release

# Scope to the Desktop headless project when iterating on personalisation
dotnet test tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj -c Release
dotnet test tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj -c Release --filter "PersonalisationTests"

# Framework-only suite (no Avalonia, fast)
dotnet test tests/Haven.UI.Tests/Haven.UI.Tests.csproj -c Release

# Core + Infrastructure suites
dotnet test tests/Haven.Core.Tests/Haven.Core.Tests.csproj -c Release
dotnet test tests/Haven.Infrastructure.Tests/Haven.Infrastructure.Tests.csproj -c Release
```

Focused tests first: run the narrow `--filter` you touched, then dependants,
then the full suite. Fix regressions immediately; do not delete failing tests
to obtain a green run.

## Desktop validation (beyond `dotnet test`)

Headless tests do not prove a route rendered. For every changed major
surface exercise it at:

- desktop width (≥ 1280) and compact width (≤ 430) on Windows,
- every changed theme × light and dark appearances,
- idle/hover/press/focus/selected/disabled/loading and overlay open,
- reduced-motion on,
- real launch: navigate from `MainView.LaunchAppAsync` and inspect the
  rendered result (see `docs/VALIDATION_RULES.md` for the full evidence
  checklist).

## Personalisation validation

Covered by `PersonalisationTests` (theme baseline, accent precedence,
fallback/persistence, live resource mutation) plus headless scene checks
for swatch accessibility and font/avatar wiring. Glow requires explicit
regression comparison against the pre-change baseline.
