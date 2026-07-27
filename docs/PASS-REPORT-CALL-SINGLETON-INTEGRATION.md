# Pass report: Call singleton integration and modern Windows speech

## Scope

This pass completed a dependency-linked non-Training Call tranche on `haven-continuation`.
`main` was not changed or merged. Manual-only CI and Ollama-first Call model selection were preserved.

Audit checkpoints advanced:

- `CALL-005` — Text-to-speech: modern Windows voice bank, deterministic cleanup, cancellation and failure reporting.
- `CALL-007` — Barge-in/interruption: Stop no longer waits behind the active utterance semaphore.
- `CALL-011` — In-chat widget/singleton: Call coordinator and preview resolve the same process-wide speech output instance.
- `CALL-015` — Acceptance validation: focused integration tests added; full Windows matrix remains pending because workflow dispatch was unavailable in this execution environment.

## Checkpoint 1: interrupt-safe Windows speech output

Commit: `cf12ed93e43e959d36e7367b730b8b31efbddb35`

Changed runtime path:

`CallCoordinator` -> `ISpeechOutputService` -> `WindowsNaturalSpeechOutputService` -> Windows `SpeechSynthesizer` -> `MediaPlayer`

Implemented:

- Replaced the shared playback semaphore stop path with a separate synchronized playback-state boundary.
- `StopAsync` now cancels active playback immediately and never queues behind `SpeakAsync`.
- Preserved one-at-a-time utterance serialization.
- Validates the only advertised output device (`default`) and rejects stale/unsupported IDs.
- Unsubscribes `MediaEnded` and `MediaFailed` handlers.
- Disposes player, media source, synthesis stream, synthesizer, linked cancellation source and registrations deterministically.
- Handles cancellation, playback failure, application shutdown and queued utterances without partial persisted media.

Files:

- `src/Haven.Desktop/Services/WindowsNaturalSpeechOutputService.cs`

## Checkpoint 2: one desktop Call singleton

Commits:

- `318bb4f5820567233b66524c21a4625de6a7084b`
- `0e1a437cf54d2a698aaf251c90277a5e16dd428d`

Changed runtime path:

`App.OnFrameworkInitializationCompleted` -> `AddHavenInfrastructure` -> `AddHavenDesktopCallServices` -> `ISpeechOutputService`

Implemented:

- Centralized Windows desktop Call overrides in `AddHavenDesktopCallServices`.
- Registers `WindowsNaturalSpeechOutputService` as a concrete singleton.
- Maps `ISpeechOutputService` to that exact concrete singleton.
- Registers the preview controller against the same interface instance used when `ICallCoordinator` is resolved.
- Keeps the infrastructure `SystemSpeechOutputService` only as a non-desktop fallback; the desktop host has one active service resolution path.
- Keeps `WindowsGraphicsCaptureService` as the desktop screen-share override.

Files:

- `src/Haven.Desktop/Services/DesktopCallServiceRegistration.cs`
- `src/Haven.Desktop/App.axaml.cs`

## Checkpoint 3: production voice preview

Commits:

- `87be0f9b8e023d2df3765c5b1267c249fac8a1db`
- `349b5cdfcbdd9fa8d0e715d7548aa5b70cf6cbaa`

Changed runtime path:

`CallView` -> selected `CallVoice`/output device -> `CallVoicePreviewController` -> shared `ISpeechOutputService`

Implemented:

- Adds a real Preview selected voice action beside the existing voice selector.
- Uses fixed local preview text; no transcript or raw audio is persisted.
- Cancels a previous preview before starting another.
- Stops preview when the Call view leaves the visual tree.
- Uses the existing Haven notification surface for actionable failures.
- Records start, completion, cancellation and redacted failure diagnostics.
- Diagnostics include language/default-output metadata but not preview text, voice ID, user text, paths or secrets.
- Disposes operation cancellation and singleton resources deterministically.

Files:

- `src/Haven.Desktop/Services/CallVoicePreviewController.cs`
- `src/Haven.Desktop/Views/CallView.axaml.cs`

## Tests

Commit: `80ece1739e723f756bdb4a45d2d486972f28e0d4`

Added `tests/Haven.Desktop.Tests/CallSingletonIntegrationTests.cs` covering:

- Desktop interface and concrete speech registrations resolve the same singleton.
- Preview controller itself is singleton-scoped.
- Selected voice ID and default output reach the real speech contract.
- Preview uses fixed Haven-owned text.
- Stop cancels blocked playback without waiting for natural completion.
- Cancellation diagnostics are emitted.
- Unavailable speech fails before playback and does not emit sensitive data.

## Primary documentation consulted

Microsoft Learn / Windows App SDK API documentation:

- `SpeechSynthesizer.SynthesizeTextToStreamAsync`: https://learn.microsoft.com/en-us/uwp/api/windows.media.speechsynthesis.speechsynthesizer.synthesizetexttostreamasync
- `MediaPlayer`: https://learn.microsoft.com/en-us/uwp/api/windows.media.playback.mediaplayer
- `MediaPlayer.MediaEnded`: https://learn.microsoft.com/en-us/uwp/api/windows.media.playback.mediaplayer.mediaended
- `MediaPlayer.MediaFailed`: https://learn.microsoft.com/en-us/uwp/api/windows.media.playback.mediaplayer.mediafailed
- `MediaPlayer.Dispose`: https://learn.microsoft.com/en-us/uwp/api/windows.media.playback.mediaplayer.dispose

Repository primary sources cross-referenced:

- `src/Haven.Application/CallAbstractions.cs`
- `src/Haven.Infrastructure/ServiceCollectionExtensions.cs`
- `src/Haven.Desktop/App.axaml.cs`
- `src/Haven.Desktop/ViewModels/CallPageViewModel.cs`
- `src/Haven.Desktop/Views/CallView.axaml`
- `src/Haven.Desktop/Views/CallView.axaml.cs`
- `tests/Haven.Core.Tests/CallCoordinatorTests.cs`
- `docs/HAVEN-MASTER-AUDIT-CHECKPOINTS.md`
- `.github/workflows/haven-continuation-validation.yml`

## Validation

The repository's manual Windows workflow specifies:

```powershell
dotnet restore Haven.sln
dotnet build Haven.sln -c Debug --no-restore
dotnet test Haven.sln -c Debug --no-build --logger "trx;LogFileName=haven-debug.trx"
dotnet build Haven.sln -c Release --no-restore
dotnet test Haven.sln -c Release --no-build --logger "trx;LogFileName=haven-release.trx"
dotnet build src/Haven.AutomationWorker/Haven.AutomationWorker.csproj -c Release --no-restore
```

Result in this pass: **not run**.

Reason:

- The connected GitHub tooling can inspect runs, statuses and logs, but cannot dispatch the manual `workflow_dispatch` workflow.
- The final source commit had no workflow runs and no combined commit statuses at inspection time.
- No claim of a green build or passing test matrix is made.

Source-level checks performed:

- Confirmed all writes targeted `haven-continuation`.
- Confirmed `main` remained untouched.
- Confirmed manual-only workflow triggers remain unchanged.
- Confirmed no Training files were changed.
- Confirmed Ollama remains the model source shown and passed by `CallPageViewModel`.
- Confirmed the legacy infrastructure output is overridden only at the desktop composition root.
- Confirmed interruption, preview and coordinator all use the same `ISpeechOutputService` resolution.
- Confirmed cancellation and disposal paths do not persist raw speech media.

## Hard blocker

The only hard blocker is execution of the Windows restore/build/test matrix. The source tranche is complete, but `CALL-015` cannot be marked fully verified until those commands run successfully on a Windows runner.

## Next large non-Training tranche

After the Windows matrix is green, continue with the remaining Browser UI/policy tranche:

- visible navigation denial and certificate/security state;
- site permission persistence and revocation;
- find/zoom/print/devtools completion;
- browser crash recovery and private-profile cleanup;
- integration tests through the native browser entry point and download/navigation policy boundaries.
