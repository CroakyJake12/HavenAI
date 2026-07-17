# Pass report: Browser native policy, permissions, popups, and recovery

Branch: `haven-continuation`  
Main touched or merged: **No**  
Training work: **Skipped by instruction**

## Scope and source-of-truth review

The pushed continuation branch was treated as authoritative. Before editing, this pass reviewed:

- the continuation head and recent Browser pass commits;
- `docs/HAVEN-MASTER-AUDIT-CHECKPOINTS.md`;
- `docs/PASS-REPORT-BROWSER-DATA-RELIABILITY.md`;
- `docs/PASS-REPORT-BROWSER-UTILITY-POLICY-LIFECYCLE.md`;
- the real Browser entry path through `BrowserView`, `NativeWebViewHost`, `BrowserSessionService`, and `BrowserPageViewModel`;
- Browser safety approvals, audit, and download UI;
- Browser persistence and infrastructure tests;
- the Avalonia `NativeWebView` package contract used by the Desktop project.

The repository, not an earlier pass report, remained the source of truth. The selected dependency-linked tranche advanced four Browser boundaries without introducing a second WebView or navigation stack.

## Checkpoint 1 — Origin-scoped site permission persistence and administration

Commits:

- `5f98d95b916ac03773a7dddeea80e9199e3d9b7e`
- `41b85443460ab1237a7fe883886e4c4318ef838a`
- `7e69b0f75ce4aaddb87e2576c35d29b8e820e9cf`
- `adb9e96c8d632a4f0a2a989a95ce105512a0b4f9`
- `ca48c66cfd1ba07911f147382f74fde8d6700e75`
- `271bd3125191bc21c7c4c4b08b668474afaab2ee`
- `4baa12ae17dd63e576d40f46df8369800bc90477`

Runtime path:

`Browser safety flyout` -> `BrowserSafetyViewModel` -> shared `BrowserSitePermissionStore` -> atomic JSON replacement and backup

Implemented:

- Exact-origin Allow, Deny, and Ask decisions for supported Browser permission kinds.
- HTTP/HTTPS-only canonical origins; embedded credentials, file origins, and unsupported schemes are rejected.
- Ask removes the persistent override rather than storing a misleading third grant.
- All decisions for the current origin can be revoked in one operation.
- Versioned schema, bounded permission and audit collections, duplicate repair, corrupt-primary quarantine, last-valid backup recovery, and future-schema fail-closed behaviour.
- Semaphore-serialized mutations with in-memory rollback and temporary-file cleanup on failure or cancellation.
- One shared store per Haven data directory, used by the Browser safety UI and native popup host.
- A real Permissions tab alongside approvals, downloads, and audit, with current-origin status, permission and decision selection, save, origin reset, and persisted-decision review.
- View-model cancellation and disposal on flyout detach, with recreation when the flyout is attached again.

This completes the persistence, migration, review, revocation, rollback, and user-facing administration slice beneath `BROWSE-016`. Native enforcement is complete for WindowManagement/popups in this tranche. Other WebView permission kinds remain explicitly adapter-dependent because the installed Avalonia `NativeWebView` public event surface does not expose a generic permission-request event.

## Checkpoint 2 — Adapter-level top-level navigation interception

Commits:

- `fb6755730eb4f0c350e6cfd90e5bc90df7fe0bd0`
- `36292aefe7fe039916f63ddb05d7d56acbcfea11`
- `83f738367a3d596fe814dee3c95b2c5d534bf332`

Runtime path:

`NativeWebView.NavigationStarted` -> `BrowserNativeRequestPolicy` -> `WebViewNavigationStartingEventArgs.Cancel` -> visible `BrowserSnapshot.Status`

Implemented:

- Direct user/runtime navigation and native navigation-start callbacks use the same policy.
- HTTP and HTTPS top-level pages are accepted; the native initialization document `about:blank` is accepted only for initialization.
- File, FTP, custom-scheme, relative, and embedded-credential top-level addresses fail closed.
- Disallowed native navigation is cancelled before completion and a visible reason is published to the existing Browser status path.
- The previous duplicated policy helpers were replaced with one testable Browser-domain policy.
- DataContext changes now detach and dispose the old host before assigning the new view model, preventing the previous Browser session from remaining attached to a stale native host.

This advances `BROWSE-006` through the real adapter entry point. The stricter model-network/DNS policy remains separately authoritative for model-driven navigation; this host boundary prevents unsupported top-level schemes and credential-bearing addresses from bypassing the visible user navigation path.

## Checkpoint 3 — Popup and new-window interception

Commits:

- `fb6755730eb4f0c350e6cfd90e5bc90df7fe0bd0`
- `36292aefe7fe039916f63ddb05d7d56acbcfea11`
- `83f738367a3d596fe814dee3c95b2c5d534bf332`

Runtime path:

`NativeWebView.NewWindowRequested` -> mark handled -> assess requester and target -> exact-origin WindowManagement decision -> block visibly or navigate the current managed tab

Implemented:

- Every native new-window request is marked handled, preventing an unmanaged platform window from becoming the competing implementation.
- Unsafe or unavailable requesting origins fail closed.
- Unsafe popup targets fail closed.
- Ask blocks with instructions to review Browser Safety.
- Deny blocks with the saved-decision reason.
- Allow opens the target inside Haven's existing managed Browser tab rather than spawning an untracked window.
- The decision is exact-origin scoped and comes from the same durable store displayed in the safety UI.

This completes the currently available `BROWSE-018` native event path without adding a second Browser session.

## Checkpoint 4 — Bounded native adapter recovery

Commits:

- `fb6755730eb4f0c350e6cfd90e5bc90df7fe0bd0`
- `ca8554ce606bd525e415380f74749b50608f45ea`
- `83f738367a3d596fe814dee3c95b2c5d534bf332`

Runtime path:

`NativeWebView.AdapterDestroyed` -> visible failure state -> `AdapterCreated` -> bounded `BrowserRecoveryLimiter` -> restore last successfully committed HTTP/HTTPS page

Implemented:

- Adapter loss is surfaced immediately through the existing Browser state/status channel.
- The last successfully completed safe web address is retained for session restoration.
- Adapter recreation restores that address only after a real loss event.
- Automatic restoration is bounded to two attempts in one minute.
- Further repeated failures pause automatic recovery and require an explicit user reload.
- The limiter is synchronized and deterministic under concurrent calls.
- Host event subscriptions are removed during disposal, and stale hosts cannot continue publishing state.

This advances `BROWSE-024` through the current Avalonia adapter lifecycle. Windows process-failure reason codes are not exposed by the package-level event used here, so the report does not claim reason-specific WebView2 recovery telemetry.

## Tests

Commits:

- `26c431b95171fbaa2c9665bca16cd71ba8d5f1df`
- `0dbaab1dc29361a4e6f9719e5348735044f95e35`
- `d4cc75489f335cd3e4b152c48a3a727cc88c5345`
- `d9b9518c87765695385e52b437fc7c98993d2811`

Added `BrowserSitePermissionStoreTests` covering:

- persistence and reload;
- exact-origin isolation;
- Ask removing an override;
- unsafe and credential-bearing origin rejection;
- concurrent mutation serialization;
- origin-wide revocation and audit records;
- failed-write rollback and temporary-file cleanup;
- corrupt-primary quarantine and backup recovery;
- unsupported future-schema fail-closed behaviour;
- shared-store identity for one data directory.

Added `BrowserNativePolicyAndRecoveryTests` covering:

- allowed HTTP, HTTPS, and initialization addresses;
- blocked file, FTP, and embedded-credential addresses;
- popup Ask, Deny, Allow, unsafe-target, and unsafe-requester outcomes;
- bounded recovery attempts;
- recovery-window expiry;
- invalid limiter configuration.

Added `BrowserSafetyViewTests` covering:

- the real permission administration controls in the compiled Avalonia view;
- coexistence with approvals, audit, and downloads;
- detach/reattach reuse of the flyout surface.

## External primary documentation consulted

- Avalonia NativeWebView control and events: AdapterCreated, AdapterDestroyed, NavigationStarted, NavigationCompleted, NewWindowRequested, WebMessageReceived, and WebResourceRequested.  
  https://docs.avaloniaui.net/controls/web/nativewebview  
  https://docs.avaloniaui.net/api/avalonia/controls/nativewebview
- Avalonia `WebViewNavigationStartingEventArgs.Cancel`, used to stop unsafe navigation at the adapter boundary.  
  https://docs.avaloniaui.net/api/avalonia/controls/webviewnavigationstartingeventargs
- Avalonia `WebViewAdapterEventArgs`, used for adapter lifecycle recovery.  
  https://docs.avaloniaui.net/api/avalonia/controls/webviewadaptereventargs
- Microsoft WebView2 permission kinds, including microphone, camera, geolocation, notifications, clipboard, automatic downloads, file read/write, autoplay, local fonts, MIDI system-exclusive messages, and WindowManagement.  
  https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/winrt/microsoft_web_webview2_core/corewebview2permissionkind
- Microsoft WebView2 permission-request behaviour, including origin, state, deferral, and default profile persistence.  
  https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/winrt/microsoft_web_webview2_core/corewebview2permissionrequestedeventargs
- Microsoft .NET `File.Replace`, used to replace the durable permission file while preserving the last valid backup.  
  https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace

## Files changed

- `src/Haven.Browser/BrowserSitePermissionStore.cs`
- `src/Haven.Browser/BrowserSitePermissionStoreProvider.cs`
- `src/Haven.Browser/BrowserNativeRequestPolicy.cs`
- `src/Haven.Browser/BrowserRecoveryLimiter.cs`
- `src/Haven.Desktop/ViewModels/BrowserSafetyViewModel.cs`
- `src/Haven.Desktop/Views/BrowserSafetyView.axaml`
- `src/Haven.Desktop/Views/BrowserSafetyView.axaml.cs`
- `src/Haven.Desktop/Views/BrowserView.axaml.cs`
- `tests/Haven.Infrastructure.Tests/BrowserSitePermissionStoreTests.cs`
- `tests/Haven.Infrastructure.Tests/BrowserNativePolicyAndRecoveryTests.cs`
- `tests/Haven.Desktop.Tests/BrowserSafetyViewTests.cs`
- `docs/PASS-REPORT-BROWSER-NATIVE-POLICY-RECOVERY.md`

No Training, workflow, Ollama, provider-routing, or `main` files were changed.

## Validation

Required commands:

```powershell
dotnet restore Haven.sln
dotnet build Haven.sln -c Debug --no-restore
dotnet test Haven.sln -c Debug --no-build
dotnet build Haven.sln -c Release --no-restore
dotnet test Haven.sln -c Release --no-build
dotnet build src/Haven.AutomationWorker/Haven.AutomationWorker.csproj -c Release --no-restore
```

Result in this pass: **not run**.

The execution container could not obtain a local clone, and the connected GitHub surface does not expose fresh `workflow_dispatch`. No build-green or test-green claim is made.

Source-level checks completed:

- all writes targeted `haven-continuation`;
- `main` was not touched or merged;
- no Training files changed;
- no workflow files changed, preserving manual-only CI;
- no Ollama or provider-routing files changed;
- one native Browser host remains authoritative;
- popup requests are always handled by Haven rather than spawning unmanaged windows;
- persistence uses atomic replacement, backup, quarantine, rollback, and bounded collections;
- event ownership and host disposal were reviewed after the DataContext-switch bug was found and corrected;
- native policy, persistence, rollback, concurrency, recovery limiting, and real UI tests were added.

## Hard blockers and explicit boundaries

- The complete Windows restore/build/test/AutomationWorker matrix remains the verification blocker.
- The current Avalonia `NativeWebView` API documented for this package does not expose a generic permission-request event. Camera, microphone, geolocation, notification, clipboard, file-system, and similar decisions are therefore durable and user-manageable but are not falsely claimed as native-enforced in this pass.
- Private tabs still use the shared native profile. Disposable private environments and verified profile-directory cleanup remain `BROWSE-013` work.
- Native download-start interception and progress/cancel/retry remain `BROWSE-009` work.

## Next large non-Training tranche

Finish the remaining Browser privacy and download architecture as one tranche:

1. create disposable private `NativeWebView` environments with unique profile directories;
2. clean each private profile on tab close and recover orphaned private profiles at startup;
3. separate private and standard host lifecycle without persisting private addresses or browser data;
4. integrate native download-start interception into the existing approval/download transport;
5. add progress, cancellation, retry, reveal, bounded history, and failure cleanup;
6. add private-profile and native-download integration tests through the Browser entry point;
7. run and repair the complete Debug, Release, desktop-startup, and AutomationWorker validation matrix.
