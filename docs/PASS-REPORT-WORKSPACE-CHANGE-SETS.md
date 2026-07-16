# Transactional workspace change-set continuation pass

This report records the coding-core continuation applied after the browser download finalisation pass on `haven-continuation`.

## Scope

The pass completed four dependency-linked checkpoints in the real model-facing workspace runtime:

1. structured multi-file change-set parsing and bounded preflight;
2. non-mutating preview with hashes and line impact;
3. transactional application with reverse rollback;
4. permission routing, durable per-file history, diagnostics, and integration tests.

No changes were made to `main`. Manual-only CI and Ollama routing were preserved.

## Checkpoint 1: structured and bounded change sets

`WorkspaceChangeSetService` accepts a JSON array of complete-file updates containing `path`, `content`, and optional `expectedSha256` fields.

Before any write it validates:

- one to fifty entries;
- non-empty workspace-relative paths;
- a two-million-character limit per entry;
- no case-insensitive duplicate targets;
- every resolved path remains inside the selected workspace;
- targets are files rather than directories;
- existing targets are text rather than binary;
- optional SHA-256 preconditions are syntactically valid and match the inspected content.

All entries are fully preflighted before the first mutation.

## Checkpoint 2: reviewable preview

The model-facing `preview_change_set` tool uses the same parser and preflight path as application but performs no writes.

It reports for every entry:

- create or modify state;
- workspace-relative path;
- before and after SHA-256 values;
- estimated added and removed lines.

This gives the agent and visible tool activity a deterministic review surface before applying a multi-file change.

## Checkpoint 3: transactional application and rollback

The model-facing `apply_change_set` tool writes each entry through the existing workspace-confined atomic file service.

If any later write or cancellation fails:

- previously existing files are restored in reverse order through atomic writes;
- files created earlier in the failed transaction are deleted;
- rollback uses a non-cancelled token so caller cancellation cannot strand a partial set;
- rollback failures are surfaced explicitly rather than being silently ignored.

No competing filesystem implementation was introduced. Single-file `write_file` and `replace_in_file` remain available for focused edits, while multi-file operations now have a transaction boundary.

## Checkpoint 4: permissions, history, activity, and real entry-point tests

`ToolAvailabilityPlanner` treats:

- `preview_change_set` as a read operation;
- `apply_change_set` as a mutation requiring Auto Safe or Full Access file permission.

The existing exact-name runtime router dispatches both tools to `WorkspaceToolRuntime`.

Successful applied entries are recorded individually in the existing `IWorkspaceStateRepository` version history with before and after content, line counts, conversation/container identity, workspace root, summary, and timestamp. Aggregate line counts flow into normal `ToolActivity` output.

Integration tests exercise the real `WorkspaceToolRuntime.ExecuteAsync` entry point and cover:

- preview without mutation;
- successful two-file application;
- create and modify operations in one transaction;
- stale SHA-256 rejection before any write;
- traversal rejection;
- case-insensitive duplicate rejection;
- injected second-write failure and reverse rollback;
- deletion of a file created by a failed transaction.

## Files changed

- `src/Haven.Application/WorkspaceChangeSetService.cs`
- `src/Haven.Application/WorkspaceToolRuntime.cs`
- `src/Haven.Application/ToolAvailability.cs`
- `tests/Haven.Infrastructure.Tests/WorkspaceToolRuntimeTests.cs`
- `docs/PASS-REPORT-WORKSPACE-CHANGE-SETS.md`

## Primary documentation consulted

- Microsoft .NET documentation for `File.WriteAllTextAsync`, `File.Move`, canonical `Path` handling, and cancellation.
- Microsoft .NET documentation for `SHA256.HashData` and `CryptographicOperations.FixedTimeEquals`.
- Microsoft .NET `System.Text.Json` deserialisation behaviour.
- Existing Haven workspace confinement, atomic-write, tool-routing, permission, activity, and version-history contracts.

## Required validation

Run on Windows x64 from the branch checkout:

```powershell
dotnet restore Haven.sln
dotnet build Haven.sln -c Debug --no-restore
dotnet test Haven.sln -c Debug --no-build
dotnet build Haven.sln -c Release --no-restore
dotnet test Haven.sln -c Release --no-build
dotnet build src\Haven.AutomationWorker\Haven.AutomationWorker.csproj -c Release --no-restore
```

Focused validation:

```powershell
dotnet test tests\Haven.Infrastructure.Tests\Haven.Infrastructure.Tests.csproj -c Debug --filter WorkspaceToolRuntimeTests
```

The connected GitHub interface did not expose workflow dispatch during this pass, so no build or test success is claimed without an actual run.

## Next large tranche

The next coding-core tranche should add a first-class persisted change-set aggregate and review UI: named sets, accept/reject state, unified diff rendering, grouped rollback/redo, crash recovery for approved-but-incomplete sets, Studio project-surface integration, and end-to-end UI/runtime tests. The current pass supplies the safe transactional execution boundary that tranche requires.
