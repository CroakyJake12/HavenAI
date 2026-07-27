# Pass report: Generative UI, unified model control, Browser utilities, and Call voices

Branch: `haven-continuation`  
Main touched or merged: **No**  
Training work: **Skipped by instruction**

## Completed source checkpoints

### 1. Generative UI Settings integration

- `SettingsView.axaml` contains one first-class `GenerativeUiThemeSelectorView`.
- The obsolete fixed theme editor is no longer active.
- The appearance copy now describes the explicit global Light/Dark choice rather than an unsupported System option.
- `GenerativeUiAdvancedPageHandoffView` remains directly under Theme Studio for reviewed richer-page handoff.

Status: **Source complete; validation pending.**

### 2. Provider-routed Theme Studio

- Added `IProviderModelClient : IOllamaClient` as the compatibility contract for user-facing surfaces that need local and provider-qualified models.
- `ProviderRoutingModelClient` implements that contract.
- Production DI explicitly constructs `GenerativeThemeAiService` with `IProviderModelClient`.
- Theme Studio receives `IProviderModelClient`, so its model list and completion calls can use configured Ollama, OpenAI, OpenRouter, Anthropic, Gemini, and OpenAI-compatible providers.
- Ollama remains the first-class local provider and model installation/deletion still uses the local Ollama client.
- Safe Mode continues to filter remote models and reject cloud-provider execution.

Status: **Source complete; validation pending.**

### 3. Reviewed Mode Creator / Studio handoff

- Safe timer, shortcut, text, divider, and approved-command pages remain declarative theme-file widgets.
- Richer page requests use `GenerativeModeStudioHandoff`.
- The handoff opens Studio, creates a fresh Studio chat, and inserts a complete reviewed implementation specification.
- It does not send, install, activate, or execute generated code automatically.
- The handoff requires native AXAML, existing Haven services, persistence/recovery where required, permission checks, tests, and package review before activation.

Status: **Source complete; validation pending.**

## Unified prompt-bar model configuration

The fullscreen model-picker overlay has been removed from `ChatView.axaml`.

`chat.model` now renders one `ModelConfigurationControl` drop-up. The legacy `chat.effort` placement is absorbed by this control so existing theme files do not create a duplicate selector.

The compact prompt-bar summary shows:

- simplified model display name;
- current effort percentage;
- capability glyphs for Vision, Tools, Browser, and Audio where supported;
- one switch/configuration glyph.

Only the compact summary removes packaging fluff. The nested Model list retains complete provider/model names.

Examples:

- `qwen3.6vl:latest` -> `Qwen 3.6`
- `openrouter:qwen3.6-vl-32b-instruct` -> `Qwen 3.6`
- `llama3.3:70b-q4_k_m` -> `Llama 3.3`

The drop-up contains:

- Advanced configurations: temperature, context limit, and tool-action limit, persisted through `UserPreferencesService`.
- Resolve errors: Looping, Hallucinating, Ignoring instructions, and Overcomplicating. Each action stops an active response, adds a focused corrective message, and continues through the existing chat command path.
- Model submenu: searchable full configured model catalogue.
- Effort slider: 20%, 40%, 60%, 80%, and 100%, with Lightning/Fire endpoints and contextual explanation.

Existing Send, Stop, model selection, effort selection, context, provider routing, and chat-session paths remain authoritative.

Status: **Source complete; validation pending.**

## Call voice quality and selection

The Windows desktop host now uses `WindowsNaturalSpeechOutputService` instead of the legacy `System.Speech` fallback.

- Enumerates the modern Windows `SpeechSynthesizer.AllVoices` bank.
- Uses installed language/region speech packs.
- Synthesizes to a local stream and plays through one singleton `MediaPlayer` path.
- Supports interruption and cancellation.
- Shares one `ISpeechOutputService` instance between Call, voice preview, and coordinator interruption.
- Adds a selected-voice preview action to Call setup.
- Preview uses fixed local text, stores no transcript, and writes redacted operational diagnostics.
- Only the default Windows output device is advertised until explicit device routing is implemented.

Status: **Source complete; Windows runtime validation pending.**

## Browser UI and policy checkpoint

Added one utility cluster to the existing stable native WebView host:

- Find on page with previous/next and clear.
- Bounded 50-200% page zoom with 10% snapping and reset.
- Live HTTPS/unencrypted origin status.
- Real model-automation policy assessment using `IBrowserNavigationPolicy`.
- Direct access to existing approval, download, and audit UI.

The utility cluster does not create another WebView and does not claim independent process-isolated tabs.

Existing model-navigation policy still:

- permits only absolute HTTP/HTTPS destinations;
- rejects embedded URL credentials;
- rejects localhost and `.local` / `.internal` hosts;
- resolves DNS and rejects loopback, private, link-local, multicast, and other non-public targets;
- rechecks redirects through the pinned transport path.

Status: **Source complete for this UI/policy slice; full tab/profile architecture remains separate.**

## Tests added or retained

- `ModelConfigurationControlTests`
  - compact name normalization;
  - one compact button with one unified flyout.
- `BrowserUtilitiesControlTests`
  - Find, Zoom, Policy, and Safety flyout construction;
  - interactive Find/Zoom content.
- `GenerativeThemeProviderRoutingTests`
  - production DI shares the provider-routed model client with Theme Studio.
- Existing `BrowserAutomationTests`
  - credentials, unsupported schemes, loopback/private address rejection;
  - public-address allowance;
  - approval and sensitive-field handling.
- Existing `CallSingletonIntegrationTests`
  - one speech-output singleton for Call and preview;
  - selected voice forwarding;
  - interruption and cancellation;
  - unavailable-output failure behaviour.

## Primary documentation checked

- Avalonia `Slider.TickFrequency` and `IsSnapToTickEnabled`.
- Avalonia `Button.Flyout`, `Flyout`, and `PlacementMode`.
- Microsoft `SpeechSynthesizer.AllVoices`.
- Microsoft `MediaPlayer.MediaEnded` and `MediaPlayer.MediaFailed` typed event contracts.

## Validation status

No build or test result is claimed for the current head.

The repository workflow remains manual-only:

```yaml
on:
  workflow_dispatch:
```

The connected GitHub action surface can inspect and re-run an existing run, but it does not expose a fresh workflow-dispatch action. The latest commit therefore has no status checks yet.

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

1. Open the model/effort drop-up above the composer; confirm no fullscreen overlay appears.
2. Select 20/40/60/80/100 effort points and confirm the summary/explanation changes.
3. Select local and provider-qualified models; confirm raw names remain in the Model submenu while the bar uses the compact name.
4. Save advanced configurations and restart Haven.
5. Trigger each recovery action during generation and confirm Stop -> correction -> Send order.
6. Enumerate and preview modern Windows voices; start and interrupt a call.
7. Use Browser Find, Zoom, site-policy inspector, and Browser Safety.
8. Confirm private/internal model-navigation targets remain blocked.

## Remaining related work

- Run and repair the complete validation matrix.
- Add explicit browser download/pop-up/native-permission event integration where the NativeWebView adapter exposes those events.
- Replace the current shared WebView tab projection with genuinely independent tab hosts/profiles before calling tab isolation complete.
- Add persisted per-site zoom only after a reviewed site-settings store exists.
- Add explicit non-default audio-device routing only when the Windows media output contract is implemented and tested.
