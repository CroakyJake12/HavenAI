# Home, Call and Plan

## Home dashboard

`HomePageViewModel` is an `IActivatablePage`. It refreshes when selected, on the Refresh command and once per minute while visible. Deactivation stops the timer, and a new refresh cancels the previous one.

`DashboardRepository.GetSnapshotAsync` uses aggregate SQLite queries for counts, agenda and recent work. Do not replace it with one repository call per tile or an activity-log scan. The snapshot includes conversation, project/group/subject, task, event, automation and call activity plus today/overdue agenda items.

Dashboard extension contracts are in `DashboardModels.cs` and `DashboardAbstractions.cs`:

- `IDashboardTileProvider` supplies a stable definition and data derived from the snapshot;
- `IDashboardTileProviderRegistry` resolves built-in providers through DI and gives application registrations precedence over defaults;
- `DashboardTileLayout` stores version, key, order, visibility and size;
- `DashboardPluginTileManifest` describes a declarative plugin tile.

Layout is versioned JSON under the `settings` key `dashboard.layout.v1`. Built-in providers are registered in code. `DashboardTileManifestPolicy` is the single import allow-list for provider/navigation keys, duplicate keys, reserved keys and the tile-count limit. Imported plugin manifests are parsed as data and cannot load or execute arbitrary .NET code.

When adding a tile, keep its key stable forever, calculate shared counts in `DashboardRepository`, supply an accessible empty/error state, and verify reorder/hide persistence after restart.

## Fully local Haven Call

The call boundary is defined in `src/Haven.Application/CallAbstractions.cs`. `CallCoordinator` owns the one-active-call invariant and deterministic states:

```text
Idle -> Listening -> Transcribing -> Thinking -> Speaking
                         |              |          |
                         +--------- Paused / Error
```

The normal pipeline is:

1. `WindowsSpeechInputService` captures 16 kHz mono audio with NAudio.
2. WebRTC VAD detects speech turns; push-to-talk can delimit a turn manually.
3. Whisper.net transcribes locally with the selected multilingual model.
4. The existing Ollama client streams the assistant response.
5. `CallCoordinator` splits complete sentences and sends them to System Speech synthesis.
6. New detected user speech interrupts generation/speech (barge-in) and starts another turn.

The package versions live in `Haven.Infrastructure.csproj`: NAudio 2.3.0, Whisper.net 1.9.1 (normal and no-AVX runtimes), WebRtcVadSharp 1.3.2 and System.Speech 10.0.0. The main implementation files are `CallCoordinator.cs`, `SentenceChunker.cs`, `WindowsSpeechInputService.cs`, `WhisperModelManager.cs`, `CallFallbackServices.cs`, `CallRepository.cs` and Desktop's `WindowsGraphicsCaptureService.cs`. The desktop capture service owns the Windows system picker and most-recent-frame preview.

First use downloads Tiny, Base or Small whisper.cpp model data into `SpeechModels`; Base is the recommended default. Downloads are cancellable and use a `.download` partial file that is atomically renamed on completion.

Screen sharing keeps only the most recent downsampled JPEG frame in memory and clears it on stop. The coordinator attaches it only to the current Ollama request at the end of a user turn and only for a vision-capable model. Raw PCM/WAV exists only for the current utterance and is discarded after local inference. Source closure, End Call, fatal error, application shutdown and coordinator disposal must stop capture, microphone and speech output.

Persistence is deliberately narrow:

- transcript messages are ordinary conversation messages;
- the conversation uses `ConversationKind.Call` (numeric value 7);
- `call_sessions` stores device/model/voice, status, timestamps and whether sharing was used;
- raw audio and frame bytes are never written to the database, token store, logs or call metadata.

If you add another media service, keep its raw buffers behind the service interface and add a cleanup assertion to `CallCoordinatorTests`.

## Haven Plan

Planner domain records and enums live in `Haven.Core/Entities.cs` and `Enums.cs`. `IPlannerRepository` is implemented by `PlannerRepository`; the desktop owns only presentation and commands.

The database starts with stable Personal, College and Work collection IDs plus one stable local Haven calendar. Collections can be created, renamed, reordered and archived. Tasks support hierarchy, notes, priority, status, tags, estimates, start/due/reminder times, recurrence, time zone and completion history. Events support local/provider calendars, recurrence, reminders, all-day values, read-only provider state and soft deletion.

`PlannerRecurrence` calculates supported recurrence in the record's time zone. Keep wall-clock behavior across daylight-saving transitions and add a test for every new rule form.

`PlanPageViewModel` presents Today, Inbox, Upcoming, List, Board, Day, Week, Month and Agenda views. It owns quick capture, task/event editors, subtasks, collection management, period navigation and provider status. Local tasks may appear alongside calendar data but are never sent to provider task systems.

Planner entities remain `DateTimeOffset`-based. Avalonia's `CalendarDatePicker.SelectedDate` is `DateTime?`, so every planner date picker uses the shared `DateTimeOffsetDateConverter`; binding either type directly produces an `InvalidCastException` inside the control. Keep quick-event time pickers wide enough for both 24-hour fields and use the scrollable collection-management region at reduced window heights.

## AI planner proposals

`PlannerProposalService` parses and validates structured `planner_propose_changes` arguments. The model can propose create/update/complete/delete operations, but `PlanPageViewModel` shows a human-readable pending proposal first. Nothing mutates until Apply invokes the repository transaction; Dismiss discards it.

Keep validation and application separate. Every change in a proposal must target a supported planner entity, and the repository must apply the whole proposal atomically or roll it back.

## Google and Microsoft calendars

`ICalendarSyncProvider` and `ICalendarSyncProviderRegistry` abstract provider behavior. Configuration is public-client configuration—there are no client secrets.

Set the appropriate environment variable before launching Haven:

```powershell
$env:HAVEN_GOOGLE_CALENDAR_CLIENT_ID = "your-installed-app-client-id"
$env:HAVEN_MICROSOFT_CALENDAR_CLIENT_ID = "your-desktop-public-client-id"
```

Missing values produce a visible Not Configured state instead of a crash. Sign-in uses the system browser, Authorization Code + PKCE, a five-minute callback timeout and provider scopes declared in `CalendarSyncProviders.cs`.

Register these exact public-client redirect URIs with the providers:

```text
Google:    http://127.0.0.1:53682/oauth/google/
Microsoft: http://localhost:53683/oauth/microsoft/
```

Google requests `openid`, `email` and Calendar access. Microsoft requests `openid`, `offline_access`, `User.Read` and `Calendars.ReadWrite`. The concrete transports are `GoogleCalendarProviderTransport.cs` and `MicrosoftCalendarProviderTransport.cs`; their shared PKCE/token/loopback work is in `OAuthCalendarTransportBase.cs`. `AddHavenPlannerInfrastructure` registers the repository/sync store, DPAPI token store, a 45-second named `HavenCalendarSync` client, both transports, provider wrappers and the registry.

`WindowsCalendarTokenStore` encrypts each token envelope for the current Windows user using DPAPI and atomically stores it under `CalendarTokens`. SQLite contains only account metadata, provider IDs/ETags, cursors/delta links, outbox operations and conflict snapshots.

Synchronization code must retain these rules:

- Google incremental sync uses sync tokens and resets after HTTP 410; Microsoft uses calendar-view delta links and resets after Gone/Bad Request;
- expired cursors trigger a bounded full-window refresh;
- offline writes enter `calendar_outbox` and retry with backoff;
- concurrent local/remote edits create `calendar_conflicts` for Keep Haven, Keep Provider or Duplicate;
- provider readers and meetings with attendees/another organizer remain read-only in this pass;
- new events default to the local Haven calendar unless the user explicitly chooses a writable provider calendar;
- provider deletions soft-delete the local linked event instead of erasing unrelated local records.

Never put a client secret in source, settings, SQLite or an environment-variable example.

## Focused verification

- Dashboard: aggregate counts, cancellation, timer activation, stable tile keys, order/hide persistence and manifest allow-list rejection.
- Call: fake input/STT/TTS/capture, VAD turn flow, push-to-talk, interruption, second-call rejection, vision preflight, source closure and no-media-persistence invariant; then a manual Windows mic/TTS/picker smoke test.
- Plan: hierarchy/cycle rejection, recurrence and DST, reminders, read-only provider events, atomic proposal rollback, cursors, offline outbox, retry, remote deletion, conflicts and permissions with mocked HTTP transports.

Current focused test files include `DashboardContractsTests.cs`, `DashboardRepositoryTests.cs`, `CallCoordinatorTests.cs`, `CallRepositoryTests.cs`, `PlannerRecurrenceTests.cs`, `PlannerRepositoryTests.cs` and the Avalonia headless tests in `tests/Haven.Desktop.Tests`.
