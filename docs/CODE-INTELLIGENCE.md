# Haven code intelligence

Haven's code-intelligence layer adds optional Language Server Protocol support to Studio while preserving useful local fallbacks when no server is installed.

## Security model

Language-server definitions are stored in Haven's app-data directory as `language-servers.json`.

- Repository files cannot register or enable a language-server command.
- Built-in suggestions are disabled by default.
- A user must review and enable a definition in Settings.
- The process starts with the selected Studio project as its working directory.
- Haven opens only a workspace-confined file supplied through `IWorkspaceToolService`.
- Protocol messages and header lines have fixed safety limits.
- Process stderr is bounded.
- Cancellation and shutdown attempt to terminate the complete server process tree.
- Formatting is never applied directly from a server response.

Formatting follows this path:

1. Read the current file.
2. Request `textDocument/formatting`.
3. Validate and apply returned UTF-16 text-edit ranges in memory.
4. Produce a unified diff.
5. Require an explicit **Apply reviewed diff** action.
6. Re-check the original SHA-256 content hash.
7. Apply through `IWorkspaceTransactionService`.
8. Reject the operation if the file changed after preview.

## Protocol support

The stdio client implements the LSP 3.18 base framing and the subset needed by this tranche:

- JSON-RPC 2.0 requests, responses, and notifications;
- ASCII `Content-Length` headers and UTF-8 JSON payloads;
- `initialize`, `initialized`, `shutdown`, and `exit`;
- `$/cancelRequest`;
- `textDocument/didOpen`;
- pull diagnostics through `textDocument/diagnostic`;
- push diagnostics through `textDocument/publishDiagnostics`;
- `workspace/symbol`;
- `textDocument/formatting`;
- UTF-16 position encoding.

Primary specification:

- Language Server Protocol 3.18: `https://microsoft.github.io/language-server-protocol/specifications/lsp/3.18/specification/`

## Fallback behaviour

### Diagnostics

Haven prefers the enabled matching language server. When that server is absent or fails and the selected project contains a .NET solution or project, Haven runs:

```text
dotnet build <target> --no-restore -nologo -v:minimal
```

Compiler diagnostics are parsed into the shared `CodeDiagnostic` model. Haven does not run restore during this fallback, so it cannot unexpectedly download packages merely because the diagnostics button was clicked.

Primary command reference:

- `dotnet build`: `https://learn.microsoft.com/dotnet/core/tools/dotnet-build`

### Symbols

Language-server workspace symbols are combined with a bounded lexical fallback. The fallback:

- visits at most 2,000 files;
- skips reparse points;
- skips generated and dependency directories;
- ignores files larger than 2 MiB;
- supports common declarations in C#, F#, VB, Python, JavaScript/TypeScript, Rust, Go, and Java;
- labels every result as `Haven lexical fallback` so it is never presented as semantic certainty.

### Formatting

Formatting has no heuristic fallback. It is available only when a trusted, enabled language server matches the selected file and its command is available. This avoids silently changing code with an unrelated formatter.

Microsoft documents `dotnet format` separately, but Haven does not invoke it automatically in this tranche because formatting must remain previewable and tied to the selected file's configured server:

- `dotnet format`: `https://learn.microsoft.com/dotnet/core/tools/dotnet-format`

## Built-in disabled definitions

Haven creates disabled suggestions for:

| ID | Command | Languages |
|---|---|---|
| `csharp-ls` | `csharp-ls` | `.cs` |
| `typescript-language-server` | `typescript-language-server --stdio` | `.ts`, `.tsx`, `.js`, `.jsx` |
| `pylsp` | `pylsp` | `.py` |
| `rust-analyzer` | `rust-analyzer` | `.rs` |

These entries are configuration suggestions only. Haven does not install the tools or claim they are available.

## Studio workflow

Open a Studio project and expand **Conversation tools**. The code-intelligence panel supports:

- inspecting the configured server and fallback status for a workspace-relative file;
- loading diagnostics;
- searching project symbols;
- inserting diagnostics or symbols into the active composer;
- previewing a formatter diff;
- applying the reviewed diff transactionally.

## Validation

Run from the repository root on Windows:

```powershell
dotnet restore Haven.sln
dotnet build Haven.sln -c Debug --no-restore
dotnet test Haven.sln -c Debug --no-build
dotnet build Haven.sln -c Release --no-restore
dotnet test Haven.sln -c Release --no-build
dotnet build src/Haven.AutomationWorker/Haven.AutomationWorker.csproj -c Release --no-restore
```

Focused tests added by this tranche:

```powershell
dotnet test tests/Haven.Infrastructure.Tests/Haven.Infrastructure.Tests.csproj -c Debug --filter "FullyQualifiedName~CodeIntelligence|FullyQualifiedName~LanguageServer"
```

## Manual smoke checks

1. Start Haven and open Settings.
2. Confirm language-server suggestions are disabled initially.
3. Enable only an installed server after reviewing its command and arguments.
4. Open a Studio project and expand Conversation tools.
5. Enter a workspace-relative source file and inspect status.
6. Run diagnostics and confirm source, severity, path, line, and column appear.
7. Search a known symbol and distinguish server results from lexical fallback results.
8. Preview formatting and confirm the file is unchanged.
9. Modify the file externally, then verify the old preview is rejected.
10. Refresh the preview, apply it, and verify the transaction result.
11. Cancel a slow request and verify the server process does not remain orphaned.

This checkpoint must not be marked fully validated until the build, automated tests, and Windows smoke checks above pass.
