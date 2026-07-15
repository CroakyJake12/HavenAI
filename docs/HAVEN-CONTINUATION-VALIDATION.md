# Haven continuation validation

This document is the final merge gate for `haven-continuation`. The branch must remain separate from `main` until the automated commands and the Windows smoke checks below pass.

## Automated validation

Run from the repository root:

```powershell
.\scripts\validate-continuation.ps1
```

Equivalent commands:

```powershell
dotnet restore Haven.sln
dotnet build Haven.sln -c Debug --no-restore
dotnet test Haven.sln -c Debug --no-build
dotnet build Haven.sln -c Release --no-restore
dotnet test Haven.sln -c Release --no-build
dotnet build src\Haven.AutomationWorker\Haven.AutomationWorker.csproj -c Release --no-restore
```

The GitHub workflow **Haven Continuation Validation** is deliberately `workflow_dispatch` only. It does not run on pushes or pull requests.

## Checkpoint coverage

### 1. Providers and settings

- Ollama remains available without a cloud account.
- OpenAI, Anthropic, Gemini, OpenRouter, and custom OpenAI-compatible cards appear in Settings.
- API keys are masked and stored through Windows Credential Manager.
- Connect, Test, and Disconnect update the visible state.
- Routing and Pricing appears below provider connections.
- Cloud fallback is opt-in for local providers.
- Ordered fallback keys are saved.
- Cost stays unavailable until explicit per-million rates and a currency are configured.

### 2. Conversations, branches, versions, and drafts

- Existing conversations open after schema migration.
- Creating and switching branches changes the visible saved path.
- Editing a user message in **New Branch** preserves the old branch.
- **Overwrite Current Branch** saves a recovery version before truncation.
- Regenerating the latest answer replaces it without duplicating the preceding user turn.
- Regenerating an older answer always creates a new branch.
- Previous/next version restoration keeps the replaced content in version history.
- A partially typed draft restores after restarting Haven.
- Draft attachment chips restore with the draft and are associated with the sent user message.

### 3. Composer and attachments

Test the same files through picker, drag/drop, and paste:

- plain text/source;
- image;
- DOCX, PPTX, XLSX;
- PDF when `pdftotext` is available;
- audio when a configured `whisper.cpp` executable and model are available;
- video when `ffprobe`/`ffmpeg` are available.

Verify:

- copied files live under the Haven attachment directory, not the original path;
- analysis notices state the actual extraction method and limitations;
- unsupported optional tools produce a clear unavailable state, not fabricated analysis;
- deleting an attachment removes the database record, derived frames/context, and retrieval index entries;
- Enter sends, Shift+Enter inserts a newline, and an IME confirmation Enter does not send unexpectedly.

### 4. Rendering and message actions

- headings, paragraphs, emphasis, lists, task lists, quotes, tables, links, inline math, display math, and fenced code render natively;
- code blocks show their language and scroll when long;
- Copy copies only the code;
- Ask to run/apply inserts an explicit, permission-gated request and never executes silently;
- message bookmark state persists;
- renderer contains no web content or remote script dependency.

### 5. Routing, fallback, usage, and cost

- Ollama usage uses provider-confirmed `prompt_eval_count` and `eval_count` when returned;
- OpenAI-compatible, Anthropic, and Gemini usage is labelled provider-confirmed when their API returns counters;
- missing counters are labelled estimated;
- multi-call tool turns aggregate every provider call into the final saved response;
- local responses have no monetary cost unless a local rate is deliberately configured;
- a recoverable failure before output may use the configured compatible fallback;
- after one streamed chunk, fallback does not replay the turn;
- after any tool call/result, fallback does not replay side effects;
- local-to-cloud fallback does not occur unless explicitly enabled.

### 6. Retrieval and citations

- newly saved messages are indexed incrementally;
- extracted attachment text is indexed in attachment and conversation scopes;
- re-indexing unchanged content does not duplicate chunks;
- conversation search returns cited chunks only from that conversation;
- Studio indexing stays inside the selected project root, skips reparse points/build folders/oversized files, and removes stale entries;
- Teach indexing includes only the selected subject and its lessons;
- another project/subject's unique phrase cannot appear in results;
- result count, per-document diversity, and token budget are enforced;
- inserted context uses `[source N]` labels and instructs the model not to infer beyond the cited text.

### 7. Export and temporary LAN sharing

- Markdown, JSON, and plain-text export contain the active conversation content;
- attachments are identified in structured/Markdown exports;
- LAN sharing binds only to a private interface;
- the URL contains a random token, while SQLite stores only its hash;
- the shared page is read-only;
- Stop immediately invalidates the link;
- expiry invalidates the link after one hour;
- no cloud upload is performed by LAN sharing.

## Windows smoke run

Use a disposable profile:

```powershell
$env:HAVEN_DATA_DIR = "$env:TEMP\haven-continuation-smoke"
dotnet run --project .\src\Haven.Desktop\Haven.Desktop.csproj
```

Complete one normal local Ollama chat first. Then connect only the cloud providers whose credentials are available and repeat a text response, streaming response, vision request where supported, and tool request where supported.

Also verify:

1. Settings and Chat open repeatedly without an Avalonia binding/XAML exception.
2. Long conversations remain scrollable and switching branches does not freeze the UI.
3. Cancelling streaming stops promptly and does not leave a fake completed usage entry.
4. Restarting during a draft recovers text and attachments.
5. A corrupt optional extraction tool or unavailable provider surfaces a bounded error.
6. Temporary chats do not persist conversation/draft records.
7. Provider keys do not appear in `model-providers.json`, logs, exports, or LAN pages.
8. The application closes cleanly after a LAN share, active stream, and attachment extraction.

## Merge rule

Do not merge solely because the source compiles. Merge only after:

- Debug and Release builds have zero warnings and zero errors;
- all Core, Infrastructure, and Desktop tests pass;
- the automation worker builds;
- the Windows smoke run above passes;
- any optional tool reported as unavailable was genuinely absent rather than silently ignored.
