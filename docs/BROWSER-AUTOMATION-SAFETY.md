# Haven browser automation safety

This document describes the production browser-automation tranche implemented on the `haven-continuation` branch.

## Scope

The tranche adds:

- public-network destination policy for model-driven navigation;
- bounded structured page snapshots;
- stable page-element references;
- rejection of raw model-provided CSS selectors;
- sensitive-field blocking;
- explicit approval for form-submitting controls;
- explicit approval for downloads;
- expiring persistent actions;
- persistent browser audit and download records;
- redirect-by-redirect download validation;
- DNS-result pinning during approved downloads;
- streamed size limits, atomic file completion, and SHA-256 records;
- a Browser safety approval centre shared by Haven Chat and the Browse side assistant.

## Model-visible tools

The runtime advertises:

- `browser_navigate`
- `browser_snapshot`
- `browser_read_page` as a compatibility alias for `browser_snapshot`
- `browser_click_ref`
- `browser_fill_ref`
- `browser_download`
- `browser_back`
- `browser_forward`
- `browser_reload`
- `browser_scroll`

It no longer advertises the previous raw-selector tools:

- `browser_click`
- `browser_click_text`
- `browser_fill`

The legacy methods remain on the browser host for trusted UI compatibility only. They are not included in the model tool schema.

## Structured snapshots

A snapshot is generated inside the attached browser host and is bounded to:

- 120,000 visible-text characters;
- 100 visible headings;
- 400 visible interactive elements.

Existing `data-haven-ref` attributes are removed before each capture. The current visible controls then receive short references such as `haven-1` and `haven-2`. The snapshot records:

- element kind;
- visible label;
- link address where applicable;
- field name and input type;
- whether the control is sensitive;
- whether clicking the control submits a form.

References are deliberately temporary. A page change can invalidate a reference, and a stale response is treated as failure rather than success.

## Sensitive fields

Model-facing filling rejects:

- password fields;
- file inputs;
- hidden fields;
- payment and credit-card autocomplete fields;
- one-time-code fields;
- current-password and new-password fields.

The field value is not placed in the browser audit log. The trusted user-facing credential autofill path remains separate and still requires an exact saved origin.

## Navigation policy

Model-driven navigation allows only absolute HTTP and HTTPS destinations. It blocks:

- credentials embedded in a URL;
- localhost and common local-name suffixes;
- IPv4 loopback, private, link-local, carrier-grade NAT, benchmarking, multicast, reserved, and unspecified ranges;
- IPv6 loopback, unspecified, link-local, multicast, unique-local, and deprecated site-local ranges;
- hosts that fail safe DNS resolution.

Links exposed in a structured snapshot are checked before the model-facing click path follows them.

### Known WebView limitation

The native WebView may also navigate because of page scripts, browser redirects, or a direct user click. The current Haven abstraction observes navigation state but does not expose an adapter-independent cancellable `NavigationStarting` event. Therefore this tranche does **not** claim that every navigation initiated internally by arbitrary web content is pre-cancelled by the Haven policy.

The model-requested navigation path and model-clicked link targets are checked before invocation. A future adapter-specific integration should expose the native cancellable navigation-starting event and apply the same policy before every WebView navigation, including redirects. Until then, the WebView profile must continue to be treated as untrusted web content.

## Approval state machine

Form submissions and downloads use:

1. `Pending`
2. `Approved` or `Rejected`
3. `Executed`, `Failed`, or `Expired`

Pending actions expire after ten minutes. On application restart, expired actions are marked expired and no action is resumed automatically.

Approval does not bypass validation:

- a form submission checks that the active origin still matches the requested origin;
- the element reference must still resolve and report a successful click;
- every download destination and redirect is reassessed;
- cancelled execution is recorded as failed and is not resumed.

The Browser safety interface exposes pending actions, audit history, and completed download records.

## Persistence and recovery

Browser automation state is stored in:

```text
browser-automation.json
```

The store uses a unique temporary file and atomic replacement. Temporary data is cleaned in `finally`. Corrupt JSON is moved to a timestamped `.corrupt-*.json` file and Haven starts with an empty safe state.

Retention is bounded to:

- 1,000 action records;
- 2,000 audit entries;
- 500 download records.

## Approved downloads

Downloads are saved to:

```text
%USERPROFILE%\Downloads\Haven
```

When a user-profile folder is unavailable, Haven uses an app-data `Downloads` directory.

The transport:

- never follows redirects automatically;
- permits at most eight redirects;
- validates every redirect destination;
- pins the approved DNS result in `SocketsHttpHandler.ConnectCallback`;
- rejects a connection attempt whose host differs from the approved host;
- disables cookies;
- enforces a 250 MB declared and streamed limit;
- writes to a unique temporary file;
- moves the file into place only after the stream completes;
- removes abandoned temporary files;
- creates a SHA-256 record;
- never opens or executes the downloaded file automatically.

## Shared runtime

Haven Chat and the Browse side assistant resolve the same `IBrowserAutomationService`, policy, action store, and audit trail. The shared mapping is registered before `MainWindowViewModel` is created, so the existing one-argument `BrowserToolRuntime` construction in the Browse view cannot bypass the production safety coordinator.

## Tests

`BrowserAutomationTests` covers:

- private and local network rejection;
- URL credential rejection;
- safe public IP acceptance;
- action persistence and expiry;
- corrupt-state quarantine;
- structured safe clicks and fills;
- sensitive-field rejection;
- approval-gated form submission;
- audit redaction of entered field values;
- stale-reference failure;
- approval-gated download requests and rejection;
- removal of raw selector tools from the model-visible schema.

## Required validation

Run from a Windows x64 checkout of `haven-continuation`:

```powershell
dotnet restore Haven.sln
dotnet build Haven.sln -c Debug --no-restore
dotnet test Haven.sln -c Debug --no-build
dotnet build Haven.sln -c Release --no-restore
dotnet test Haven.sln -c Release --no-build
dotnet build src\Haven.AutomationWorker\Haven.AutomationWorker.csproj -c Release --no-restore
```

Focused test command:

```powershell
dotnet test tests\Haven.Desktop.Tests\Haven.Desktop.Tests.csproj -c Debug --filter BrowserAutomationTests
```

## Windows smoke checklist

1. Open Browse and confirm the browser profile loads normally.
2. Open **Browser safety** and confirm all three tabs render.
3. Ask the side assistant to inspect a page and verify it first calls `browser_snapshot` or `browser_read_page`.
4. Verify snapshot output uses `haven-N` references and does not expose raw selectors.
5. Fill a normal text field and verify the entered value does not appear in Audit.
6. Attempt a password or file field and verify the action is blocked.
7. Click a normal link and verify it executes without an approval entry.
8. Request a submit-button click and verify the page is unchanged until approval.
9. Reject a submit request and confirm it remains unexecuted.
10. Request a download, approve it, and verify the completed file appears in `Downloads\Haven` with an audit and SHA-256 record.
11. Leave a request pending for more than ten minutes and verify it expires.
12. Restart Haven with a pending request and verify it is not executed automatically.
13. Navigate to a loopback/private literal from the model path and verify it is blocked.
14. Confirm direct user browsing, bookmarks, history, extension management, and trusted credential autofill still work.

## Primary documentation consulted

- Microsoft WebView2 navigation events and `NavigationStarting` cancellation behaviour.
- Microsoft WebView2 security guidance for treating web content as untrusted and validating origins around host interactions.
- .NET `HttpClient` and `SocketsHttpHandler` lifetime and DNS guidance.
- .NET `SocketsHttpHandler.ConnectCallback` API documentation and examples.
