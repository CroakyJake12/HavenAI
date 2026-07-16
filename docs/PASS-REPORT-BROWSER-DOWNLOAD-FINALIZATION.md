# Browser download finalization continuation pass

This report records the continuation pass applied after the browser automation safety tranche on `haven-continuation`.

## Scope

The pass completed four dependency-linked checkpoints in the real approved-download runtime:

1. hostile and misleading filename normalization;
2. download-root confinement and bounded collision allocation;
3. abandoned partial-file recovery and atomic completion;
4. transport-level integration coverage through the pinned HTTP path.

No changes were made to `main`. Ollama and model/provider routing were not altered.

## Checkpoint 1: hostile filename normalization

Added `BrowserDownloadFilePolicy.SanitizeFileName` and routed suggested names, `Content-Disposition` names, and URL path names through it before any destination is allocated.

The policy:

- applies Unicode NFKC normalization;
- removes Unicode control and format characters, including bidirectional direction controls;
- removes path traversal by reducing the value to its final path component;
- replaces Windows-invalid filename characters;
- trims trailing dots and spaces;
- protects Windows device names such as `CON`, `NUL`, `COM1`, and `LPT1`, including names with multiple extensions;
- preserves the final extension when truncating;
- truncates only at complete Unicode rune boundaries;
- bounds the final base filename to 180 UTF-16 code units.

## Checkpoint 2: root confinement and collision allocation

`BrowserDownloadFilePolicy.AllocateUniquePath` now owns destination allocation.

It:

- canonicalizes the configured download root;
- re-sanitizes the proposed filename at the boundary;
- verifies every destination remains below the canonical root;
- treats both files and directories as collisions;
- allocates deterministic ` (2)`, ` (3)`, and later suffixes;
- shortens the stem when required so a collision suffix cannot exceed the filename limit.

The production `BrowserDownloadTransport` uses this allocator for every approved download.

## Checkpoint 3: partial-file recovery and atomic completion

Temporary files use a Haven-specific marker:

```text
<destination>.haven-download-<guid>.tmp
```

Before an approved transfer begins, the transport removes only Haven partials older than 24 hours from the top level of the configured Haven download directory. Recent partials and unrelated `.tmp` files are left untouched.

Streaming still uses `FileMode.CreateNew`, bounded streaming, SHA-256 accumulation, flush-before-move, final move only after success, and best-effort cleanup on failure or cancellation.

## Checkpoint 4: real transport integration

`BrowserDownloadTransport` now has an explicit download-directory constructor used by integration tests while the production constructor continues to resolve `%USERPROFILE%\Downloads\Haven` with the existing app-data fallback.

The integration tests exercise the real pinned HTTP transport against a local TCP server and prove:

- RFC 5987 `filename*` values are decoded and sanitized;
- traversal and Unicode direction controls cannot influence the stored path;
- Windows reserved names are made safe;
- bytes are written unchanged;
- size and SHA-256 records match the completed file;
- completed paths remain under the configured root;
- no partial file remains after success;
- stale Haven partials are removed before a later approved transfer;
- existing completed files are preserved and collision names are allocated.

## Files changed

- `src/Haven.Browser/BrowserDownloadFilePolicy.cs`
- `src/Haven.Browser/BrowserDownloadTransport.cs`
- `tests/Haven.Desktop.Tests/BrowserDownloadFilePolicyTests.cs`
- `tests/Haven.Desktop.Tests/BrowserDownloadTransportIntegrationTests.cs`

## Primary documentation and upstream source consulted

- Avalonia NativeWebView upstream repository and component documentation: `AvaloniaUI/Avalonia.Controls.WebView`.
- Microsoft .NET `System.Text.Rune` and Unicode category APIs.
- Microsoft .NET `System.IO.Path.GetFileName` and canonical path APIs.
- Microsoft .NET `System.IO.File.Move` and asynchronous `FileStream` APIs.
- Microsoft .NET `ContentDispositionHeaderValue.FileNameStar` handling.
- Microsoft .NET `SocketsHttpHandler.ConnectCallback` guidance already used by the pinned browser transport.
- RFC 5987 / RFC 6266 extended filename parameter behaviour as represented by .NET `ContentDispositionHeaderValue`.

## Tests added

`BrowserDownloadFilePolicyTests` covers:

- slash and backslash traversal;
- simple and multi-extension Windows device names;
- Unicode direction-control removal;
- trailing-dot cleanup;
- Unicode-safe extension-preserving truncation;
- file and directory collision handling;
- bounded collision suffixes;
- selective stale-partial cleanup;
- unique Haven partial naming.

`BrowserDownloadTransportIntegrationTests` covers:

- approved download through the pinned transport;
- RFC 5987 `filename*` sanitization;
- confined final storage;
- SHA-256 and byte-count records;
- successful partial cleanup;
- stale partial recovery;
- non-destructive collision allocation.

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
dotnet test tests\Haven.Desktop.Tests\Haven.Desktop.Tests.csproj -c Debug --filter "FullyQualifiedName~BrowserDownload"
```

At the time of this report, the connected GitHub interface did not expose manual workflow dispatch and no build was claimed without an actual run.

## Remaining browser checkpoint

The largest remaining Browse security gap is still visible-WebView navigation interception. The current generic Avalonia host exposes navigation state but not a portable cancellable pre-navigation contract covering script navigation and every redirect. The next browser pass should either add a verified adapter-specific WebView2 interceptor with tests, or move to the next major audit tranche while leaving that limitation explicit.
