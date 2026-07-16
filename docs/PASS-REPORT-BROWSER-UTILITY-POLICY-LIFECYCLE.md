# Pass report: Browser utility, policy visibility, and lifecycle hardening

Branch: `haven-continuation`  
Main touched or merged: **No**  
Training work: **Skipped by instruction**

## Scope and source-of-truth review

The pushed repository was treated as authoritative. This pass reviewed the current audit ledger, the Call/Plan/Automation and Generative UI/Browser pass reports, the active `BrowserUtilitiesControl`, `BrowserPageViewModel`, `BrowserSafetyView`, and the existing headless Browser utility tests before editing.

The branch had advanced beyond the earlier Call report, including the existing Browser find/zoom/policy utility slice. This pass therefore continued that real implementation rather than repeating an older progress-log recommendation.

## Checkpoint 1 — BROWSE-006 visible navigation and policy feedback

Commits:

- `9deeebb0831c280afc0f14889456b0636532f577`
- `55aa13212d5b14a887e76738fd729841fc4a1983`

Runtime path:

`BrowserPageViewModel.Status/Address/SelectedTab` -> `BrowserUtilitiesControl` -> Site and automation policy flyout

Implemented:

- The policy flyout now shows the latest real browser status beside HTTPS/origin and model-navigation assessment.
- Status refreshes when `Address`, `SelectedTab`, or `Status` changes.
- Model-navigation policy remains the existing `IBrowserNavigationPolicy`; no competing policy engine was introduced.
- Policy failures remain fail-closed and are visibly reported.
- Stale asynchronous policy results are discarded through a monotonically increasing request version.
- Address and tab changes reset the page-only zoom indicator to 100%, avoiding a misleading cross-navigation display.

This advances the visible-feedback portion of `BROWSE-006`. Adapter-level cancellable navigation interception remains dependent on the NativeWebView event surface and is not claimed complete.

## Checkpoint 2 — BROWSE-019 native Print and DevTools completion

Commit:

- `9deeebb0831c280afc0f14889456b0636532f577`

Runtime path:

`BrowserUtilitiesControl` -> mounted `BrowserPageViewModel.Browser` -> `PrintAsync` / `OpenDeveloperToolsAsync`

Implemented:

- Added one Page Tools flyout to the existing mounted browser utility cluster.
- Exposes `Print current page` through the production browser session.
- Exposes `Open developer tools` through the production browser session.
- Shows success, cancellation, unavailable-document, and exception states in the flyout.
- Includes an explicit warning that developer tools can inspect site content and storage.
- Does not create a second WebView or bypass the existing Browser session service.

Find and zoom remain in the same cluster, so the user-facing `BROWSE-019` tool group is no longer split between wired view-model commands and hidden functionality.

## Checkpoint 3 — Browser utility cancellation and detach/recovery lifecycle

Commits:

- `9deeebb0831c280afc0f14889456b0636532f577`
- `55aa13212d5b14a887e76738fd729841fc4a1983`

Implemented:

- Replaced `CancellationToken.None` in Browser utility operations with linked operation tokens.
- Find, clear-highlight, zoom, policy assessment, Print, and DevTools now cancel when the control leaves the visual tree or is disposed.
- A detached control recreates its lifetime token when reattached, so temporary shell navigation does not permanently disable the utility cluster.
- View-model property subscriptions are removed on data-context replacement and disposal.
- Flyouts are hidden during disposal.
- A first implementation used a separately tracked operation source; source review found that a `using` owner could leave a disposed source in the tracking field. The follow-up commit removed that competing ownership model and uses one lifetime source plus locally owned linked operation sources.

This advances deterministic disposal and Browser crash/host-cleanup safety without claiming full `BROWSE-024` WebView process recovery.

## Tests

Commit:

- `cda41bda3be3afa22b9db94469bc20510d84f846`

Updated `tests/Haven.Desktop.Tests/BrowserUtilitiesControlTests.cs`:

- verifies the real cluster exposes Find, Zoom, Policy, Page Tools, and Browser Safety;
- verifies all five entries have flyouts;
- verifies the policy flyout contains latest-browser-status UI;
- verifies Page Tools contains real Print and DevTools actions;
- verifies detach and reattach leave the utility control reusable.

Existing Browser automation/policy tests remain authoritative for credential, scheme, loopback/private-network, DNS and approval boundaries.

## Primary documentation consulted

- MDN `Window.find()` documentation, including its non-standard status and argument/return behaviour: https://developer.mozilla.org/en-US/docs/Web/API/Window/find
- Microsoft Learn `CoreWebView2.OpenDevToolsWindow`, confirming that it opens DevTools for the current WebView document and is idempotent while already open: https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.opendevtoolswindow

Repository contracts consulted:

- `src/Haven.Desktop/Controls/BrowserUtilitiesControl.cs`
- `src/Haven.Desktop/ViewModels/BrowserPageViewModel.cs`
- `src/Haven.Desktop/Views/BrowserSafetyView.axaml.cs`
- `tests/Haven.Desktop.Tests/BrowserUtilitiesControlTests.cs`
- `docs/HAVEN-MASTER-AUDIT-CHECKPOINTS.md`
- `docs/PASS-REPORT-GENERATIVE-UI-MODEL-BROWSER-VOICE.md`
- `docs/PASS-REPORT-EXPERIENCE-CALL-PLAN-AUTOMATION.md`

## Validation

The branch remains manual-only CI. The required validation commands are:

```powershell
dotnet restore Haven.sln
dotnet build Haven.sln -c Debug --no-restore
dotnet test Haven.sln -c Debug --no-build
dotnet build Haven.sln -c Release --no-restore
dotnet test Haven.sln -c Release --no-build
dotnet build src/Haven.AutomationWorker/Haven.AutomationWorker.csproj -c Release --no-restore
```

Result in this pass: **not run**.

Reasons:

- The execution container has no outbound GitHub/DNS access and therefore could not clone the repository for a local .NET build.
- The connected GitHub surface can inspect and re-run existing workflow runs but does not expose fresh `workflow_dispatch` execution.
- A combined-status lookup encountered a transient GitHub connector 502 and no green status is claimed.

Source-level checks performed:

- all writes targeted `haven-continuation`;
- branch comparison after the final source commit reported it as the current branch head;
- no Training file changed;
- no workflow trigger changed;
- Ollama/provider code was not changed;
- the existing Browser session and `IBrowserNavigationPolicy` remain authoritative;
- operation cancellation ownership was re-reviewed and repaired before the report was written.

## Hard blockers and honest boundaries

The full Windows restore/build/test matrix remains the hard validation blocker.

This pass does **not** claim completion of:

- adapter-level navigation-start cancellation;
- persisted per-site permissions;
- independent process/profile-isolated tabs;
- disposable private WebView profiles;
- popup/new-window event handling;
- native download event integration;
- full WebView process crash detection and recreation.

## Next large non-Training tranche

Continue the Browser architecture tranche rather than moving to a tiny unrelated checkpoint:

1. add a versioned, atomic per-site permission store with allow/deny/ask decisions and revocation UI;
2. integrate NativeWebView permission, popup/new-window, download and navigation-start events where the adapter exposes them;
3. implement isolated private profile creation, close cleanup, startup orphan cleanup and failure diagnostics;
4. implement WebView process-failure detection, bounded recreation and tab/session recovery;
5. add integration tests through the real browser entry point for permission rollback, popup denial, download cancellation, private cleanup and crash recovery;
6. run and repair the complete Debug/Release/AutomationWorker validation matrix.
