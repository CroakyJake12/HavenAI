# Pass report: Experience rail, Call completion, and Plan automations

Branch: `haven-continuation`  
Main touched or merged: **No**  
Training work: **Skipped by instruction**

## Scope

This pass used the supplied mockup only as an information-architecture reference. It did not copy the mockup's visual theme.

The implemented scope is:

1. replace the global product dropdown with a persistent vertical experience rail;
2. group modes only when they share the same primary workspace;
3. add persisted custom-mode pins and an all-modes viewport;
4. finish the durable local Call lifecycle around the existing coordinator;
5. bring automation creation, execution, history, retries, condition evaluation and delivery into Plan.

## Vertical experience rail

`ExperienceShellHost` wraps the existing MainWindow content instead of replacing pages, tabs, contextual sidebars or shell commands.

### Fixed experience structure

- **Home** is permanently fixed at the top and is not part of the configurable pin list.
- **Chat** opens one flyout containing Haven Chat, Haven Teach and Haven Do because all three use the conversation workspace.
- **Studio** is a direct experience button.
- **Call** is a direct experience button.
- **Plan** opens one flyout containing Plan and Automations.
- **Browse** is a direct experience button.
- **Settings** remains fixed at the bottom.

The previous global product dropdown is hidden so there is one top-level experience switcher.

### Pins

- Users can pin up to six non-fixed modes.
- Pins are loaded and persisted through `IPinRepository`.
- Right-clicking a pin exposes `Re-order pinned modes` and `Unpin`.
- Reorder mode enables drag-and-drop movement between pins.
- The resulting sort order is written back to the repository.
- Built-in experiences already represented on the rail cannot create duplicate pins.

### All-modes viewport

The grid button below the pin area opens a full-window overlay containing:

- every enabled mode from `IModeRegistry`;
- search by name, description or tags;
- Open, Pin and Unpin actions;
- an explicit six-pin counter;
- fixed-on-rail status for built-in experiences.

The rail retries mode loading during first-run seeding, so a new profile does not require an app restart.

### Custom-mode activation

Custom modes reuse their declared Chat, Teach, Do or Studio base workspace rather than creating a second chat engine.

Activation:

- starts a clean conversation in the correct workspace;
- adds a deterministic active mode-profile prompt, including for modes with no custom prompt text;
- activates only declared plugins that are available in that workspace and backed by a runtime;
- snapshots the user's previous active-plugin state;
- removes the mode prompt and restores the previous plugin state when returning to the ordinary built-in mode.

## Call completion

The existing Call coordinator, local transcription, screen-share privacy boundary, modern Windows voice bank, interruption and barge-in remain authoritative.

This pass adds the missing durable completion layer.

### Durable summary

`CallCompletionController` observes the process-wide coordinator and, after a completed session:

- reads only persisted user and assistant text turns;
- creates a concise summary through the selected model;
- falls back to bounded recent-turn highlights if model summarisation fails;
- writes one System message marked with `call.summary=true`;
- records session timing and screen-share usage as metadata;
- updates the conversation timestamp;
- prevents duplicate summaries when completion signals repeat;
- permits a later retry when repository persistence fails.

Raw microphone audio and screen frames are never accepted by this service. Screen-frame metadata attached to an existing transcript message is not copied into the summarisation prompt.

### Transcript export

Call setup now exposes `Export transcript` once transcript entries exist.

Exports use Markdown or plain text and include:

- speaker;
- displayed turn time;
- transcript text;
- interrupted-response marker;
- partial-transcript marker.

The export uses a user-selected destination and does not add media data.

### Voice output

The desktop Call host continues to use the modern Windows `SpeechSynthesizer.AllVoices` bank and one singleton speech-output path shared by:

- Call playback;
- selected-voice preview;
- interruption and cleanup.

The preview diagnostics now use the domain model's `Culture` property consistently.

## Plan and Automation

Plan now owns a production automation builder instead of routing users to a disconnected placeholder screen.

### Builder

The Plan automation flyout supports:

- create and edit;
- Chat, Teach, Do or Studio execution context;
- Once, Hourly, Daily, Weekly and Condition Watch schedules;
- friendly date, time, day and interval controls;
- enabled/disabled state;
- schedule preview text;
- run now;
- five-run history;
- two-step delete confirmation;
- Windows background-worker registration and removal.

Definitions are stored through `IAutomationRepository`, and next-run values are calculated by the production `ScheduleCalculator`.

### Selected run-now execution

`AutomationRunner.RunOneAsync`:

- runs exactly the selected definition;
- uses the same persisted lease as scheduled work;
- writes the result to normal run history;
- preserves the definition's enabled state;
- does not accidentally execute other overdue definitions.

### Retries and condition watches

Automation execution now:

- retries failed model execution up to three attempts;
- uses short bounded retry delays;
- records one final success, failure or cancellation result;
- requires Condition Watch output to contain structured `conditionMet` and `report` fields;
- treats missing, ambiguous or unstructured condition output as not met;
- stores the normalized condition result in run history.

### Durable delivery

A cross-process automation delivery outbox now persists:

- Condition Watch results where `conditionMet=true`;
- final automation failures.

The outbox:

- serializes worker and desktop access with a lock file;
- writes through a temporary file and atomic replacement;
- flushes data before replacement;
- quarantines malformed state;
- bounds retained deliveries;
- drains each queued delivery once.

The desktop drains the outbox at startup and once per minute:

- condition-met deliveries become success notifications;
- failures become error notifications;
- ordinary condition-not-met checks remain quietly available in run history.

Notification delivery failure never changes or duplicates the already-persisted automation run.

## Tests added

### Experience, schedules and Call export

`ExperienceAutomationAndCallTests`

- Home is fixed and the all-modes entry exists;
- six-pin contract;
- Plan automation builder construction;
- weekly schedule composition and parsing;
- condition interval minimum;
- fail-closed condition parsing;
- Call transcript export markers.

### Automation delivery

`AutomationDeliveryTests`

- outbox survives a new service instance;
- drain is exactly once;
- selected Condition Watch queues one condition-met delivery;
- failed selected run retries three times;
- one failure delivery is written;
- a disabled definition remains unscheduled after manual execution.

### Call completion

`CallCompletionControllerTests`

- repeated completion attempts create one summary;
- the summarisation request does not contain screen-frame metadata;
- the persisted summary does not contain screen-frame data;
- completion diagnostics are recorded.

Existing Call singleton, voice preview, Browser policy, Generative UI and provider-routing tests remain in place.

## Source issues corrected during the pass

- `CallVoice` uses `Culture`; preview diagnostics no longer reference a nonexistent `Language` property.
- `CalendarDatePicker.SelectedDate` is handled as `DateTime?`.
- Run-now no longer delegates to the due-work batch.
- custom mode instructions and plugin state no longer leak into normal built-in Chat.
- Call transcript collection subscriptions are released when the view detaches.
- first-run mode seeding no longer leaves the rail empty.

## Validation status

**No build or test result is claimed for the current head.**

The validation workflow remains manual-only:

```yaml
on:
  workflow_dispatch:
```

The connected GitHub action surface can inspect and re-run an existing run but cannot dispatch a fresh manual run.

Required validation:

```powershell
dotnet restore Haven.sln
dotnet build Haven.sln -c Debug --no-restore
dotnet test Haven.sln -c Debug --no-build
dotnet build Haven.sln -c Release --no-restore
dotnet test Haven.sln -c Release --no-build
dotnet build src/Haven.AutomationWorker/Haven.AutomationWorker.csproj -c Release --no-restore
```

Required Windows smoke checks:

1. Confirm Home is the first rail item and cannot move.
2. Confirm Chat opens Chat, Teach and Do in one flyout.
3. Confirm Studio, Call and Browse navigate directly.
4. Confirm Plan and Automations share one flyout and one active state.
5. Pin six custom modes, reject a seventh, enter reorder mode, drag pins and restart Haven.
6. Open the all-modes viewport, search, pin, unpin and open custom modes.
7. Verify custom mode instructions/plugins apply and ordinary mode state is restored afterward.
8. Complete a Call, verify one summary message, export the transcript and interrupt voice playback.
9. Create each schedule kind, restart Haven, enable the worker and inspect run history.
10. Run one selected automation while another definition is overdue and confirm only the selected definition runs.
11. Confirm malformed condition output records `conditionMet=false` without notifying.
12. Confirm condition-met and failure notifications survive a worker run while the desktop is closed.

## Honest remaining boundaries

This pass completes the requested **local Call lifecycle core**, not a full conferencing platform. Camera/video capture, multi-human participation, network calling and media recording remain separate projects.

This pass completes the requested **Plan automation production core**, not every enterprise planning item in the wider audit. Shared calendars, comments, task dependencies, Gantt, travel-time calculation and connector actions still require their own checkpoints.

Browser tab/process isolation remains a separate Browser architecture checkpoint.
