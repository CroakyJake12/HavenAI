# Haven continuation — security, protocol and reliability audit pass

**Date:** 17 July 2026  
**Repository:** `CroakyJake12/HavenAI`  
**Branch:** `haven-continuation`  
**Status:** source-fixed and regression-tested in code; **not VERIFIED** until the complete Windows restore/build/test/smoke gate runs successfully.

## Scope and status language

This pass reviewed and repaired high-risk continuation code across model providers, Windows secrets and speech, browser automation and persistence, desktop bootstrap lifetimes, automation delivery, Generative UI theme application, Notes speech controllers, and build/validation scripts.

The following routed experiences are intentionally shells in the current product plan and were not treated as unfinished defects:

- Haven Present;
- Haven Data;
- Haven Tasks;
- Haven Imagine;
- other explicitly blank routed workspace shells.

The environment available to this pass could read and write the private repository through the GitHub connector, but could not clone the private repository into a local SDK environment, enumerate the complete tree through code search, dispatch the manual workflow, or run `dotnet`. GitHub reported that the private repository was not code-search indexed. Therefore this report does **not** claim a literal complete line-by-line audit of every repository file or a successful build.

## Primary API contracts checked

The affected code was checked against current primary vendor documentation for:

- OpenAI Chat Completions function tool calls and matching `tool_call_id` values;
- OpenRouter OpenAI-compatible tool-call history;
- Anthropic Messages `tool_use` and immediately following `tool_result` blocks with matching IDs;
- Google Gemini function calling and matching `functionCall.id` / `functionResponse.id` values;
- Microsoft Windows Credential Manager `CredWriteW`, `CredReadW`, `CREDENTIALW`, blob-size limits and `CredFree` ownership;
- Microsoft Windows speech synthesis voice enumeration and stream playback;
- PowerShell native-process exit-code behaviour used by validation scripts.

## Repairs completed in source

### 1. Confirmed compile blockers

- Corrected Notes read-aloud use of `ISpeechOutputService.Devices` rather than the nonexistent `OutputDevices` member.
- Passed a voice identifier string to `SpeakAsync` rather than a `CallVoice` object.
- Corrected the corresponding Desktop test fake and added a selected-voice regression.
- Removed the invalid `private sealed class Marker;` declaration from the regeneration bootstrap.
- Removed an unused Windows microphone callback field that could fail Haven's warnings-as-errors build.

### 2. Provider tool-call protocol

- Added an optional provider-issued ID to `OllamaToolCall` without breaking existing call sites.
- Preserved OpenAI/OpenRouter `tool_calls[].id` from the provider response.
- Preserved Anthropic `tool_use.id` from the provider response.
- Preserved Gemini `functionCall.id` when supplied by the model.
- Reused the exact provider-issued ID in the following tool-result/function-response request.
- Retained deterministic request-local IDs only for local Ollama or legacy turns that genuinely do not supply an ID.
- Rejected duplicate, oversized and orphan tool-call identifiers before sending malformed history.
- Implemented Anthropic assistant `tool_use` blocks and grouped immediately following user `tool_result` blocks.
- Preserved image content in tool-enabled OpenAI-compatible, Anthropic and Gemini requests.
- Rejected malformed OpenAI function-call structures, missing IDs/names, non-object arguments, invalid JSON and duplicate argument keys.
- Rejected malformed Anthropic tool-use input and duplicate keys.
- Rejected malformed Gemini function calls, invalid IDs, non-object args and duplicate keys.
- Propagated caller cancellation from provider health probes instead of converting cancellation into a false unhealthy result.

### 3. Provider routing and dependency injection

- Removed the reflection-based bootstrap that rewrote private chat-session fields after construction.
- Registered resilient provider routing directly as `IProviderModelClient`.
- Added a separate `ILocalOllamaClient` boundary and local adapter so provider routing can call the direct loopback Ollama transport without resolving itself recursively.
- Added a service-provider validation test proving local transport, routed client and compatibility alias resolve without a circular dependency.

### 4. Provider configuration and secrets

- Forbid credentials, query strings and fragments in provider endpoint URLs.
- Reject public plaintext HTTP endpoints; HTTP is limited to loopback/private-network hosts.
- Force known cloud providers to remain non-local so JSON cannot bypass local-to-cloud consent.
- Permit local OpenAI-compatible providers only when their endpoint is actually local/private.
- Bound provider count, JSON file size, endpoint/display lengths and metadata count/key/value lengths.
- Screen canonicalised metadata keys such as `api_key`, `access-token`, `authorization`, `secret`, `password` and similar terms.
- Quarantine malformed/oversized provider JSON and recover from the atomic backup when possible.
- Use write-through temporary files plus atomic replacement and backup.
- Validate the `OLLAMA_HOST` environment value and fall back safely to loopback when invalid.
- Harden Windows Credential Manager encoding and decoding:
  - enforce the native 2,560-byte blob limit;
  - use strict UTF-16;
  - reject whitespace-only, embedded-null, odd-sized, oversized and malformed blobs;
  - validate returned pointers and sizes;
  - zero managed and unmanaged secret buffers;
  - clear managed bytes even when unmanaged allocation fails;
  - use nullable native string fields to avoid nullable warnings;
  - always release `CredReadW` buffers with `CredFree`.

### 5. Shared speech and microphone ownership

- Notes read aloud now stops the process-wide speech output only when Notes owns an active utterance.
- Notes dictation now stops the process-wide microphone only when Notes owns the capture.
- Active Call state is rechecked after asynchronous model lookup before microphone capture begins.
- Call voice preview is blocked during an active Call and cannot interrupt shared Call speech while inactive.
- Windows microphone capture now:
  - has terminal/idempotent disposal;
  - stops native capture when its worker exits unexpectedly;
  - throttles nonessential audio-level events to 10 Hz while preserving all utterance audio;
  - guards push-to-talk after disposal;
  - avoids waiting on its own worker during failure cleanup.

### 6. Desktop bootstrap lifetime and composition

- Replaced copied module-initializer polling loops with one guarded `VisualBootstrapHost`.
- Removed strong static collections that permanently rooted Browser, Settings, retrieval and chat views.
- Converted per-view installation tracking to `ConditionalWeakTable` markers.
- Removed unobserved async dispatcher callbacks from the bootstrap family.
- Made draft-attachment recovery one-shot after success rather than rerunning on every layout pass.
- Preserved existing insertion/replacement behaviour for:
  - Browser safety;
  - code intelligence;
  - cross-mode retrieval;
  - language-server settings;
  - model-routing settings;
  - Markdown renderer upgrade;
  - regeneration replay;
  - draft attachment recovery.

### 7. Automation delivery

- Reset startup state when the initial outbox drain is cancelled or fails, allowing retry.
- Re-enqueue the current and remaining deliveries when notification presentation fails after the durable outbox was drained.
- Preserve delivery order and IDs while requeueing.
- Wait for an in-flight drain during controller disposal.

### 8. Generative UI themes

- Treat persisted theme selection and visible Avalonia resources as one transition.
- Roll back both persisted selection and visible resources when application fails.
- Update public runtime state only after visual application succeeds.
- Restore persisted state after preview failure.
- Isolate and diagnose failing `ThemeChanged` subscribers rather than turning a successful apply into a failed transaction.

### 9. Browser automation and persistence

- Made browser audit writes best-effort rather than authoritative execution state.
- Prevented audit failure after a successful form submission/download from rewriting the action as a simple failure.
- Persist final state with a non-cancellable token after an irreversible side effect.
- Report an explicit uncertain state when the page side effect may have completed but final persistence failed; do not imply that nothing happened or replay automatically.
- Delete a completed download when its durable download record cannot be written and rollback remains possible.
- Remove ephemeral signed targets when pending persistence fails.
- Made browser automation-store mutations copy-save-swap transactions so failed disk writes cannot mutate visible in-memory state.
- Added a 16 MB browser automation-store safety limit and corruption quarantine.
- Hardened disposal against semaphore release races.
- Hardened private-profile cleanup:
  - reject ancestor junctions/symbolic links;
  - retry permission failures as well as sharing violations;
  - keep startup orphan cleanup strict;
  - do not fail one tab's successful closure merely because an unrelated old tombstone remains locked.
- Hardened site-permission persistence:
  - keep the newest 500 decisions rather than evicting a recent entry before sorting;
  - canonicalise and validate persisted audit origins;
  - add a 4 MB safety limit;
  - reject embedded credentials and invalid origins;
  - avoid disposing the semaphore beneath an atomic write.

### 10. Build and validation gates

- Validation and packaging PowerShell scripts now fail immediately when a native `dotnet` command returns a nonzero exit code.
- Validation builds the Automation Worker in Debug and Release.
- Packaging validates configuration/RID values and restores the selected RID before self-contained publish.
- The manual-only GitHub workflow now builds the Automation Worker in Debug and Release.
- `docs/HAVEN-CONTINUATION-VALIDATION.md` now matches the executable script and workflow.
- The workflow remains `workflow_dispatch` only; push/PR email noise was not enabled.

## Regression coverage added

New or expanded tests cover:

- Notes read-aloud interface/voice ID and shared-speech ownership;
- Notes dictation shared-microphone ownership and disposal;
- Call voice-preview ownership;
- Windows speech-input disposal and push-to-talk lifecycle;
- OpenAI-compatible parallel tool IDs, images, malformed arguments and cancellation;
- Anthropic tool-use/result ordering, IDs and images;
- Gemini function-call/response IDs and images;
- provider-issued ID round trips from response parsing into the next request;
- orphan/duplicate tool-call correlation failures;
- provider routing DI construction without recursion;
- provider configuration endpoint/metadata security and corruption quarantine;
- Credential Manager codec bounds and malformed UTF-16;
- automation delivery requeue and startup retry;
- Generative UI rollback and failing listeners;
- browser side-effect accounting after audit/final-state failures;
- browser automation-store rollback after forced filesystem failures;
- site-permission newest-first capacity trimming.

## Known gaps not repaired in this pass

These remain **PARTIAL**, **SOURCE-COMPLETE**, **RE-AUDIT** or unvalidated; none should be inferred complete from this report:

1. The complete repository has not been physically restored, compiled or tested on the current head.
2. Avalonia XAML compilation and Windows startup have not run.
3. The manual GitHub validation workflow has not been dispatched from this environment.
4. Call conversation creation and Call session creation still use separate repository operations rather than one shared database transaction; a session-write failure can leave an orphan Call conversation.
5. The following large areas still require their own complete source and runtime audits:
   - `AutomationRunner`, condition evaluation, retries and worker leases;
   - ordered SQLite migrations, backup, integrity and restore;
   - workspace transactions and change-set recovery;
   - attachments and optional document/media extraction;
   - Notes repository/import/export/migrations and large-document stress paths;
   - LSP process lifecycle and protocol framing;
   - retrieval indexing/citation isolation;
   - shell/keybindings/accessibility application;
   - remaining browser native WebView platform events and crash recovery.
6. Intentional Present/Data/Tasks/Imagine shells remain shells by design.

## Required next validation

Run on the actual current `haven-continuation` head:

```powershell
.\scripts\validate-continuation.ps1
```

Then run the disposable-profile Windows smoke checklist in `docs/HAVEN-CONTINUATION-VALIDATION.md`.

Any compiler, nullable, XAML, test, platform or runtime failure discovered by that gate must be fixed and the complete matrix rerun. This pass must remain **SOURCE-COMPLETE / unvalidated**, never **VERIFIED**, until that evidence exists.
