# Haven continuation — build failure triage

**Date:** 17 July 2026  
**Branch:** `haven-continuation`  
**Status:** source fixes committed; clean Windows build evidence still required.

## Evidence received

The first local build produced 389 diagnostics representing 109 unique messages. The apparent volume was highly misleading: most missing-type and interface errors were downstream effects from a few upstream compilation failures plus stale project outputs.

## Confirmed root causes repaired

### Browser JavaScript raw strings

`BrowserSessionService` used interpolated raw strings with one `$` while containing JavaScript object literals. C# therefore treated JavaScript braces as interpolation boundaries and subsequently parsed `const`, JavaScript character-like strings and empty JavaScript strings as C# tokens.

Repairs:

- changed the two model-generated element-action scripts to `$$"""` raw strings;
- changed C# interpolation sites to `{{ expression }}`;
- retained ordinary JavaScript braces as literal content.

This accounts for the raw-string diagnostics, both `Unexpected token 'const'` diagnostics, both `Invalid expression term 'const'` diagnostics and the large character-literal cascade.

### Notes model member collision

`NotesBlock` declared both:

- the persisted `Paragraph` formatting property; and
- a static `Paragraph(...)` factory.

C# does not permit those members to share a name. The collision prevented `Haven.Core` from exposing the Notes object graph, producing hundreds of downstream missing-type diagnostics.

Repairs:

- kept the persisted `Paragraph` property unchanged for native-document JSON compatibility;
- renamed the factory to `CreateParagraph(...)`;
- updated production, migration, import/export, Desktop and test call sites;
- retained all existing Notes schema property names.

### Stale project-reference assemblies

The error list reported expanded `IAppCommandRegistry`, `IPlatformShellService`, `IBrowserTabHostManager` and `IWorkspaceRetrievalIndexer` contracts that do not match the current branch source. Current source contracts and current implementations agree.

The likely sequence was:

1. Core failed because of the Notes collision.
2. Application could not rebuild against Core.
3. An older `Haven.Application.dll` remained under `bin`.
4. Infrastructure then compiled against that stale assembly and reported obsolete interface requirements and duplicate imported types.

Repairs:

- `scripts/validate-continuation.ps1` now deletes every repository `bin` and `obj` directory before restore;
- restore now uses `--force-evaluate`;
- native `dotnet` exit codes remain fail-closed.

Do not implement the obsolete interfaces from the contaminated error list unless they reappear after the clean rebuild with current file/line evidence.

### Processor architecture mismatch

`Haven.Infrastructure` is intentionally x64 because it hosts Windows-native integrations. `Haven.Infrastructure.Tests` was still MSIL/AnyCPU while directly referencing the x64 assembly.

Repair:

- set the Infrastructure test host `Platform` and `PlatformTarget` to `x64`.

### Language-server member warning

`LanguageServerRequestException.Data` intentionally preserves an existing JSON-RPC payload surface while hiding `Exception.Data`.

Repair:

- scoped CS0114 suppression to `LanguageServerProtocolClient.cs` only through `.editorconfig`;
- warnings-as-errors remains enabled elsewhere.

## Diagnostics expected to disappear after clean rebuild

The following families were downstream assembly-cascade diagnostics and should not be individually patched before fresh evidence exists:

- Notes document/block/media/table/canvas/flashcard/search/version types;
- production diagnostics and recovery types;
- database maintenance, backup and restore types;
- Generative UI/theme types;
- provider routing and local Ollama abstractions;
- automation-delivery types;
- obsolete expanded command, shell, retrieval and browser-tab interface members;
- the imported `IBrowserTabSession` conflict.

## Required next command

Close Visual Studio first so it does not retain output files, pull the current branch, then run from the repository root:

```powershell
.\scripts\validate-continuation.ps1 -SkipRelease
```

The script now performs a destructive repository-local `bin`/`obj` cleanup before restoring and building. It does not delete source, user data, NuGet caches or files outside the repository.

If Debug succeeds, run the complete matrix:

```powershell
.\scripts\validate-continuation.ps1
```

## Evidence requested from the next failure

If the clean Debug run fails, capture the actual `dotnet` console output including:

- project name;
- source file path;
- line and column;
- compiler diagnostic code;
- the first error in dependency order.

Do not use an old Visual Studio Error List snapshot after the branch changes. The previous 389-entry list is now superseded by these root-cause repairs.
