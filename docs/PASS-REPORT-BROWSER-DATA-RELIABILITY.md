# Pass report: Browser data reliability, recovery, and privacy migration

Branch: `haven-continuation`  
Main touched or merged: **No**  
Training work: **Skipped by instruction**

## Scope and source-of-truth review

The pushed `haven-continuation` branch was treated as authoritative. Before editing, this pass reviewed:

- the current branch head and recent continuation commits;
- `docs/HAVEN-MASTER-AUDIT-CHECKPOINTS.md`;
- the Browser utility/policy lifecycle pass report;
- the active `BrowserDataService` implementation and its real callers through `BrowserPageViewModel`;
- the existing `BrowserDataServiceTests` persistence tests;
- the repository-wide nullable and warnings-as-errors policy.

The previous Browser pass correctly identified remaining Browser architecture gaps. This pass selected the dependency-linked persistence/recovery tranche first because the current Browser store mutated shared in-memory state before durable writes, allowed concurrent lost updates, silently discarded corrupt state without quarantine or backup recovery, and had no explicit schema migration boundary.

## Checkpoint 1 — REL-004 Browser atomic settings and state storage

Commits:

- `e1e0b9b0b58a8e78e46e51c632b7e076d751be24`
- `4faba79ebc41a50a3627e059618d903af8b6ce59`

Runtime path:

`BrowserPageViewModel` -> `BrowserDataService` -> serialized mutation candidate -> temporary file -> atomic replacement -> in-memory commit

Implemented:

- All bookmark, history, tab, settings, login metadata, and extension mutations now pass through one semaphore-protected transaction boundary.
- Mutation candidates are computed while the gate is held, preventing concurrent callers from overwriting each other's in-memory changes.
- `_data` is updated only after the durable replacement succeeds.
- Failed or cancelled writes restore the original in-memory snapshot.
- Each write uses a unique temporary file and cleans it in `finally`.
- Existing primary files are replaced with `File.Replace`, producing a last-valid `.bak` copy.
- First writes use a non-overwriting `File.Move` from the completed temporary file.
- Disposed stores reject new mutations deterministically.
- Nullable migration handling was tightened so the repository's `TreatWarningsAsErrors=true` policy does not leak nullable warnings through every store read.

This advances the Browser portion of `REL-004 — Atomic settings storage` from partial toward source-complete. It does not claim that every settings store in Haven has received the same treatment.

## Checkpoint 2 — REL-005 Browser corruption quarantine and recovery

Commit:

- `e1e0b9b0b58a8e78e46e51c632b7e076d751be24`

Runtime path:

`BrowserDataService` startup -> primary parse/normalize -> quarantine invalid primary -> load last-valid backup -> safe empty fallback

Implemented:

- Invalid, unreadable, unsupported, or malformed primary Browser data no longer disappears silently.
- The invalid primary is moved to a timestamped `browser-data.json.corrupt-*` quarantine file when possible.
- Startup attempts recovery from `browser-data.json.bak` after quarantining the invalid primary.
- If neither primary nor backup is valid, the service starts from an explicit safe empty/default state.
- Future schema versions are rejected rather than being interpreted with older assumptions.
- Recovery does not overwrite the quarantined evidence.
- The next successful mutation recreates a valid primary while retaining the recovery backup.

This advances the Browser slice of `REL-005 — Corruption quarantine` and `REL-009 — Crash markers/safe recovery` without claiming whole-product completion.

## Checkpoint 3 — REL-006/BROWSE privacy migration and bounded state hygiene

Commit:

- `e1e0b9b0b58a8e78e46e51c632b7e076d751be24`

Runtime path:

legacy/current Browser JSON -> schema normalization -> private-state purge and bounded collections -> normal Browser tab/history restore

Implemented:

- Added an explicit Browser data schema version.
- Existing versionless Browser JSON remains loadable and is normalized to schema version 1.
- Legacy private tabs are removed during startup normalization, not merely filtered on future saves.
- New tab persistence filters private tabs, rejects non-HTTP(S) addresses, deduplicates repeated tab IDs by newest update, and caps restored tab state at 60 records.
- History is normalized to its 2,000-entry bound.
- Missing/null legacy collections and settings are repaired to safe defaults.
- Private browsing visits continue to bypass history persistence.
- Windows Credential Manager writes are compensated if metadata persistence fails, preventing a newly written orphan credential.
- Login deletion persists metadata removal before deleting the credential and restores metadata if credential deletion fails.

This advances `REL-006 — Settings migrations` and the persistence/privacy prerequisites beneath `BROWSE-013 — Private profiles`. It does **not** claim isolated disposable WebView profiles or profile-directory cleanup, which remain separate Browser architecture work.

## Tests

Commit:

- `1047438ac6496c86e4d6c93b0b51231e3a8e9513`

Expanded `tests/Haven.Infrastructure.Tests/BrowserDataServiceTests.cs` with real store-entry tests covering:

- standard bookmark/history/tab persistence while private history and private tabs remain absent;
- bookmark update/removal and Browser setting reload;
- failed durable persistence rolling back the in-memory mutation;
- unique temporary-file cleanup after a failed write;
- 24 concurrent bookmark mutations completing without lost updates;
- reload preserving all concurrent results;
- corrupt primary quarantine;
- last-valid backup recovery;
- versionless legacy JSON migration;
- startup purge of a legacy private tab;
- rejection and quarantine of an unsupported future schema.

These tests use the production `BrowserDataService` and filesystem entry point rather than a mocked repository.

## External primary documentation consulted

- Microsoft Learn, `File.Replace`: replacement of an existing file with another file while creating a backup of the replaced file.  
  https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace
- Microsoft Learn, `System.Text.Json` deserialization guidance.  
  https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/deserialization
- Microsoft Learn, required constructor parameters and backward-compatible constructor deserialization behaviour.  
  https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializeroptions.respectrequiredconstructorparameters

Repository contracts consulted:

- `src/Haven.Browser/BrowserDataService.cs`
- `src/Haven.Desktop/ViewModels/BrowserPageViewModel.cs`
- `tests/Haven.Infrastructure.Tests/BrowserDataServiceTests.cs`
- `Directory.Build.props`
- `docs/HAVEN-MASTER-AUDIT-CHECKPOINTS.md`
- `docs/PASS-REPORT-BROWSER-UTILITY-POLICY-LIFECYCLE.md`

## Files changed

- `src/Haven.Browser/BrowserDataService.cs`
- `tests/Haven.Infrastructure.Tests/BrowserDataServiceTests.cs`
- `docs/PASS-REPORT-BROWSER-DATA-RELIABILITY.md`

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

Reasons:

- The execution container could not resolve `github.com`, so it could not clone the pushed branch for local .NET execution.
- The available GitHub connection can inspect commits, statuses, and existing runs, but it does not expose fresh `workflow_dispatch` execution.
- The final source head had no combined commit statuses.

No build-green or test-green claim is made.

Source-level validation completed:

- `haven-continuation` was confirmed at `4faba79ebc41a50a3627e059618d903af8b6ce59` before this report commit;
- all writes targeted `haven-continuation`;
- `main` was not touched or merged;
- no Training files changed;
- no workflow files changed, preserving manual-only CI;
- no Ollama/provider files changed;
- the Browser store remains the existing production service used by the real Browser page;
- nullable implications were re-reviewed against repository warnings-as-errors policy;
- rollback, concurrency, backup, migration, quarantine, and private-state tests were added.

## Hard blocker

The complete Windows restore/build/test/AutomationWorker matrix remains the verification blocker. This tranche is **source-complete, not verified** until that matrix runs successfully and any platform-specific `File.Replace`, Windows Credential Manager, or test failures are repaired.

## Next large non-Training tranche

Continue Haven Browse as one architecture tranche rather than moving to a tiny unrelated item:

1. introduce a versioned origin-scoped site permission model with Allow, Deny, and Ask decisions;
2. add permission review and revocation UI through the mounted Browser settings/safety surfaces;
3. integrate native WebView permission, popup/new-window, navigation-start, download, and process-failure events where the current adapter exposes them;
4. implement disposable private WebView profiles with close cleanup and startup orphan cleanup;
5. add bounded WebView process recovery and tab/session restoration diagnostics;
6. add entry-point integration tests for permission rollback, popup denial, download cancellation, private-profile cleanup, and crash recovery;
7. run and repair the complete Debug, Release, test, desktop-startup, and AutomationWorker validation matrix.
