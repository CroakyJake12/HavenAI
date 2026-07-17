# Pass report: Browser private-profile cleanup hardening

Branch: `haven-continuation`  
Main touched or merged: **No**  
Training work: **Skipped by instruction**

## Source-of-truth review

This pass began from the pushed continuation state and reviewed the current private-profile report, `BrowserView` native-host lifecycle, `BrowserPrivateProfileManager`, its production filesystem tests, the Browser safety surface, and the explicit unfinished Browser boundaries. The repository remained authoritative when earlier progress text differed.

The branch already routed private tabs through `BrowserPrivateProfileManager`, but profile removal still deleted a live directory tree in place. Cleanup also trusted every directory entry beneath the private root and had no recoverable state if the process stopped after removal began. This pass hardens that real runtime path rather than adding a competing Browser/profile implementation.

## Checkpoint 1 — Managed-root containment and link resistance

Commit:

- `bd90942b2392b995836c40ef68c5e1b042748aab`

Runtime path:

`BrowserView` tab/profile lifecycle -> `BrowserPrivateProfileManager.CreateAsync`, `CleanupAsync`, or `CleanupOrphansAsync` -> canonical managed root validation -> filesystem mutation

Implemented:

- Profile paths are canonicalized and checked to remain beneath Haven's private-profile root before mutation.
- Empty tab identifiers remain rejected.
- Existing standard roots, private roots, and active profile directories are rejected when they are symbolic links or junctions.
- Cleanup treats a reparse-point directory as a link object and removes the link itself without recursively enumerating its target.
- Recursive attribute normalization skips reparse-point descendants.
- No address, origin, title, username, or page-controlled value is used in a profile path.

Security result: a malformed or replaced directory entry beneath the private root cannot redirect recursive cleanup into an unrelated target tree.

## Checkpoint 2 — Quarantined deletion and crash recovery

Commit:

- `bd90942b2392b995836c40ef68c5e1b042748aab`

Implemented:

- A profile selected for removal is first atomically renamed inside the managed root to a unique `.deleting-*` tombstone.
- The old active profile identity disappears before recursive deletion starts.
- A cancelled or failed deletion leaves only the quarantined tombstone; it is not made active again and is not reported as deleted.
- Startup/orphan cleanup recognizes and removes tombstones left by interruption or process termination.
- Tombstone paths are generated exclusively from Haven-owned fixed text, the prior managed directory name, and a new GUID.
- Existing four-attempt locked-file retry behaviour remains bounded and cancellation-aware.
- The private root is removed after the final profile/tombstone is gone.

Recovery result: interruption cannot leave a half-deleted directory under an active private-tab ID. The next Browser startup deterministically retries quarantined cleanup.

## Checkpoint 3 — Cancellation-safe mutation boundary

Commits:

- `bd90942b2392b995836c40ef68c5e1b042748aab`
- `9d5247468f532c4adc39eb36ab3bb12901fb9847`

Implemented:

- Create, direct cleanup, orphan cleanup, and tombstone recovery remain serialized through the existing semaphore.
- Cancellation is checked before directory creation, before quarantine, during enumeration, before each deletion attempt, while normalizing files, and during retry delays.
- A token cancelled before cleanup leaves the active profile in place and creates no tombstone.
- Cancellation after quarantine deliberately preserves the tombstone for later recovery instead of attempting a misleading rollback to an active profile name.
- Cleanup errors remain visible to the existing `BrowserView` error-reporting path.

## Tests

Commit:

- `9d5247468f532c4adc39eb36ab3bb12901fb9847`

Expanded `BrowserPrivateProfileManagerTests` through the production filesystem implementation. Coverage now includes:

- unique contained profile directories;
- deleting one closed profile while preserving another active profile;
- no tombstone residue after successful cleanup;
- startup removal of orphan and malformed directories;
- recovery of an interrupted `.deleting-*` tombstone;
- pre-cancelled cleanup leaving the active profile and namespace untouched;
- symbolic-link cleanup removing the link without deleting the external target;
- empty tab-ID rejection.

The symbolic-link test exits only on platforms that do not permit link creation; on supported environments it verifies the external marker file remains intact.

## Files changed

- `src/Haven.Browser/BrowserPrivateProfileManager.cs`
- `tests/Haven.Infrastructure.Tests/BrowserPrivateProfileManagerTests.cs`
- `docs/PASS-REPORT-BROWSER-PRIVATE-CLEANUP-HARDENING.md`

No Training, workflow, Ollama, provider-routing, or `main` files were changed.

## Primary documentation consulted

- Microsoft WebView2 user-data-folder guidance: user-data folders hold cookies, permissions, cache, and DOM/profile state; the WebView2 session must end before deletion, and browser processes can retain file locks briefly after host shutdown.  
  https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder
- Microsoft .NET `SemaphoreSlim` guidance: `WaitAsync` supports cancellation, release counts must match successful waits, and disposal is not safe concurrently with other semaphore members. The existing long-lived manager therefore retains one serialized mutation gate instead of adding unsafe concurrent disposal.  
  https://learn.microsoft.com/en-us/dotnet/standard/threading/semaphore-and-semaphoreslim  
  https://learn.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim.dispose

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

Environment blockers:

- direct clone failed with `Could not resolve host: github.com`;
- the execution image does not contain the `dotnet` CLI;
- the connected GitHub surface exposes status inspection and reruns but not a fresh `workflow_dispatch` action;
- the source head had no combined status checks when inspected.

No build-green or test-green claim is made.

Source-level checks completed:

- the final Browser source and tests were re-read from `haven-continuation` after both commits;
- the branch was confirmed identical to commit `9d5247468f532c4adc39eb36ab3bb12901fb9847` before this report commit;
- changes are confined to the existing private-profile runtime and tests;
- no parallel or placeholder profile implementation remains;
- cleanup retains bounded retries, cancellation, explicit IO failure, and startup recovery;
- the concurrent Notes continuation already present on the branch was not overwritten.

## Hard blockers and explicit boundaries

- The complete Windows restore/build/test/AutomationWorker matrix remains the hard verification blocker.
- Exact Windows junction behaviour and WebView2 locked-file release timing still require the real Windows validation workflow.
- Native download-start interception, destination approval, progress, cancellation, retry, reveal, and partial-file cleanup remain unfinished Browser work.

## Next large non-Training tranche

Complete Browser downloads as one end-to-end tranche:

1. connect the lowest available native download-start boundary to the existing Browser approval transport;
2. require explicit approval for automatic and agent-initiated downloads;
3. implement destination and filename validation, collision-safe creation, partial-file cleanup, progress, cancellation, retry, and reveal;
4. keep private-tab download activity out of durable Browser history while preserving explicitly chosen output files;
5. add integration tests through the real Browser entry point for allow, deny, cancellation, collision, adapter failure, retry, and rollback;
6. run and repair the complete Windows Debug, Release, desktop-startup, and AutomationWorker matrix before leaving the Browser tranche.