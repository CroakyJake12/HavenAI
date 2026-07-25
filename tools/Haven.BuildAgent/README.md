# Haven Build and Visual Test Agent

This is a locked-down Windows helper for a private GPT Action. It can:

- build `Haven.sln` and return structured compiler diagnostics;
- run the Haven test suite and return the TRX result summary;
- launch the allow-listed Haven desktop executable;
- use a fresh `HAVEN_DATA_DIR` for isolated UI checks;
- capture only the Haven application window;
- compare the captured window with a configured PNG mockup;
- produce a difference image and pixel similarity metrics;
- optionally send the actual screenshot and mockup to an OpenAI vision-capable API model for a semantic UI review.

It intentionally cannot accept arbitrary shell commands, executable paths, repository paths, or full-desktop capture requests.

## 1. Add local mockups

Create these files, or change the keys in `appsettings.json`:

```text
.haven-agent/references/dashboard.png
.haven-agent/references/projects.png
.haven-agent/references/voice-session.png
```

The entire `.haven-agent` folder is local-only and should remain ignored by Git.

For the most useful comparisons, capture the mockup at the same expected window size and Windows display scaling as the running app.

## 2. Start the agent locally

From the repository root:

```powershell
$env:HAVEN_BUILD_AGENT_KEY = "replace-with-a-long-random-secret"
.\scripts\start-haven-build-agent.ps1
```

The script listens on loopback HTTP only. Do not expose the port directly to the internet.

## 3. Optional semantic visual review

The local pixel comparison does not require an external model. To receive descriptions such as “the chat sidebar is missing” or “the call popup is too large”, configure an API key and a vision-capable Responses API model:

```powershell
$env:OPENAI_API_KEY = "your-api-key"
$env:HAVEN_VISUAL_MODEL = "a-vision-capable-model-name"
```

This uses API billing separately from a ChatGPT subscription. The model name is deliberately configuration rather than a hard-coded dependency.

## 4. Give the GPT Action an HTTPS endpoint

GPT Actions call an HTTPS API. Put a secure authenticated reverse proxy or tunnel in front of the loopback endpoint, then replace the server URL at the top of `openapi.yaml`.

The external endpoint must preserve the `X-Haven-Agent-Key` header. Keep the GPT private and store the same secret as the Action API key.

Do not configure a tunnel that publishes other local services or allows direct filesystem access.

## 5. Add the Action

In the custom GPT editor:

1. Create a new Action.
2. Paste `openapi.yaml`.
3. Select API key authentication.
4. Use a custom header named `X-Haven-Agent-Key`.
5. Paste the value from `HAVEN_BUILD_AGENT_KEY`.
6. Test `getHavenAgentHealth`.

## Intended GPT workflow

```text
1. startHavenBuild
2. poll getHavenJob until succeeded or failed
3. if successful, startHavenTests
4. poll getHavenJob
5. startHavenApplication
6. compareHavenToMockup
7. use diagnostics and visual issues to edit the source
8. repeat build, test, launch, and compare
9. stopHavenApplication
```

Build and test operations are asynchronous so a long restore or test run does not exceed an Action request timeout.

## API boundaries

The Action accepts profile names such as `haven` and `haven-desktop`. Those names map to paths in `appsettings.json`. Requests never provide an arbitrary command or path.

Artifacts are written below:

```text
.haven-agent/artifacts/
```

They include text logs, MSBuild binary logs, TRX files, application logs, screenshots, and difference images. Artifact downloads require the same agent API key.

## Current visual-test scope

This version launches Haven, waits for the main window, captures it, and compares it with a reference image. It does not yet click arbitrary controls or type into arbitrary applications. A later UI-automation layer should use allow-listed test scripts and control Automation IDs rather than general mouse and keyboard remote control.
