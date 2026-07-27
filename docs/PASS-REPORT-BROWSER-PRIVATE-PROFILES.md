# Pass report: Browser private-profile isolation and cleanup

Branch: `haven-continuation`  
Main touched or merged: **No**  
Training work: **Skipped by instruction**

## Scope and source-of-truth review

The pushed continuation head `e9d5a6d019df8a605137cfbd137156c7b7b82cd6` was treated as authoritative. Before editing, this pass reviewed the current Browser native host, tab/view-model lifecycle, Browser persistence, native policy and recovery tests, the master audit, and the preceding Browser pass report.

The repository confirmed that a private tab changed only history/tab persistence behaviour: every tab still used the single XAML-created `NativeWebView`, the same `BrowserSessionService`, and `BrowserSessionService.ProfileDirectory`. This pass replaces that shared-profile path rather than adding another privacy label or isolated backend.

## Checkpoint 1 — Per-tab private profile ownership

Commits:

- `b64a67530c0bd081e059cb447401dc49eddd7d9f`
- `780f28a048a66048e9003265cde487e5d25aa9cd`

Runtime path:

`BrowserPageViewModel.SelectedTab` -> `BrowserView` -> `BrowserPrivateProfileManager` -> unique tab-ID user-data directory -> newly created `NativeWebView` -> existing `NativeWebViewHost` -> existing `BrowserSessionService`

Implemented:

- Every private tab receives a deterministic, unique user-data directory under the standard Browser profile root.
- Standard tabs continue to use the existing standard Haven profile.
- Crossing between standard and private tabs, or between two private tabs, destroys the old Haven host subscriptions and mounts a fresh native WebView configured before adapter creation.
- The existing `BrowserSessionService`, navigation policy, popup policy, recovery limiter, permission store, and user-facing Browser state remain authoritative; no second Browser service or navigation stack was introduced.
- Profile names contain only Haven-owned text and a normalized GUID. No address, origin, title, username, or other page-controlled value reaches a filesystem path.
- Empty tab identifiers are rejected.

## Checkpoint 2 — Private host lifecycle integration

Commit:

- `780f28a048a66048e9003265cde487e5d25aa9cd`

Implemented:

- `BrowserView` observes the actual selected-tab and privacy properties.
- Native Browser replacement occurs only when the mounted tab identity/privacy boundary changes.
- The previous host is detached from `BrowserSessionService` and disposed before the replacement is attached.
- Data-context replacement unsubscribes the old view model before mounting the new one.
- Visual-tree detachment cancels pending profile creation and cleanup work.
- Reattachment creates a fresh lifetime cancellation source.
- Native environment options are applied from fixed Haven state before WebView adapter creation.
- Private profiles use distinct user-data folders and distinct profile names; standard tabs retain the `Haven` profile.
- The reflection boundary ignores unavailable cross-platform environment properties instead of making the Browser unusable on a platform adapter that does not expose a Windows-only option.

## Checkpoint 3 — Close cleanup, orphan recovery, and locked-file handling

Commits:

- `b64a67530c0bd081e059cb447401dc49eddd7d9f`
- `d070f74b791f7d7c9a59140ab028e2c1cb16f4d8`

Runtime path:

`BrowserPageViewModel.Tabs.CollectionChanged` or Browser startup/data-context mount -> active private tab ID snapshot -> `BrowserPrivateProfileManager.CleanupOrphansAsync` -> bounded deletion and root cleanup

Implemented:

- Closing a private tab removes its profile after it is absent from the real tab collection.
- Startup/data-context mounting removes orphaned profiles because private tabs are intentionally not restored from Browser persistence.
- Active private-tab profiles are preserved while their tabs remain open.
- Malformed/unrecognized directories under the Haven-owned private-profile root are treated as orphaned remnants and removed.
- Cleanup is semaphore-serialized with profile creation and other cleanup operations.
- Cancellation is checked before and during enumeration/deletion.
- Read-only file attributes are normalized before recursive deletion.
- Native-process file locks receive four bounded deletion attempts with cancellation-aware delay.
- A profile that remains locked produces a clear IO failure rather than a false success; the UI reports the failure and startup cleanup can retry later.
- The private root is removed when empty.

## Tests

Commit:

- `0d57743a0205d8a2d0dd5ada3ddac841ace44da9`

Added `BrowserPrivateProfileManagerTests` covering:

- unique per-tab directories;
- containment beneath the Haven-managed private root;
- deleting one closed tab without affecting another active private profile;
- startup orphan cleanup while preserving active profiles;
- removal of malformed orphan directories;
- cancellation rollback before deletion;
- empty tab-ID rejection.

The tests exercise the production filesystem implementation, including nested profile files, rather than an in-memory adapter.

## External primary documentation consulted

- Avalonia `NativeWebView.EnvironmentRequested`: the event fires before the native adapter is created and is the supported boundary for environment/private-mode customization.  
  https://docs.avaloniaui.net/controls/web/nativewebview
- Avalonia Windows WebView2 environment options: `UserDataFolder`, `ProfileName`, and `IsInPrivateModeEnabled`.  
  https://docs.avaloniaui.net/api/avalonia/platform/windowswebview2environmentrequestedeventargs
- Microsoft WebView2 multi-profile support: controls associated with different profiles have dedicated profile folders for cookies, preferences, and cache; profile name and InPrivate mode must be selected at controller creation.  
  https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/multi-profile-support
- Microsoft WebView2 user-data-folder guidance: UDFs contain cookies, permissions, cache, and DOM/profile state and custom locations require application read/write access.  
  https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder

## Files changed

- `src/Haven.Browser/BrowserPrivateProfileManager.cs`
- `src/Haven.Desktop/Views/BrowserView.axaml.cs`
- `tests/Haven.Infrastructure.Tests/BrowserPrivateProfileManagerTests.cs`
- `docs/PASS-REPORT-BROWSER-PRIVATE-PROFILES.md`

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

A direct clone attempt failed because the execution container could not resolve `github.com`, and the connected GitHub surface does not expose fresh `workflow_dispatch`. No build-green or test-green claim is made.

Source-level checks completed:

- all writes targeted `haven-continuation`;
- `main` was not touched or merged;
- no Training or workflow files changed;
- Ollama remains the Browser side-assistant provider path;
- private paths are derived only from the standard Haven profile path and normalized GUIDs;
- host event ownership is released before native-control replacement;
- active profiles are preserved and orphan cleanup is bounded/cancellable;
- tests cover isolation, close cleanup, startup recovery, malformed remnants, and cancellation.

## Hard blockers and explicit boundaries

- The complete Windows restore/build/test/AutomationWorker matrix remains the hard verification blocker.
- The installed Avalonia adapter exposes environment configuration through platform-specific event arguments. The implementation is deliberately reflective so unsupported platforms fail by omission rather than by startup crash; Windows runtime verification is still required to prove the exact WebView2 option mapping at current package versions.
- Native download-start interception, progress, cancellation, retry, reveal, and failed-download cleanup remain unfinished `BROWSE-009` work.

## Next large non-Training tranche

Complete Browser downloads and then leave the Browser tranche only after validation:

1. connect native download-start events or the lowest available WebView2 adapter boundary to the existing approval transport;
2. require explicit approval for model-initiated and automatic downloads;
3. add safe destination selection, filename/path validation, partial-file cleanup, progress, cancellation, retry, reveal, and bounded durable history;
4. keep private-tab downloads out of persistent history while retaining explicit user-chosen files;
5. add real-entry integration tests for allow, deny, cancel, adapter failure, collision, and rollback;
6. run and repair the complete Windows Debug, Release, desktop-startup, and AutomationWorker matrix.
