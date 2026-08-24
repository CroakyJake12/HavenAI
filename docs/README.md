# Haven Developer Documentation

Haven is a local-first AI assistant for Windows desktop and Android, built on
.NET 10 and Avalonia. Its user-facing interfaces are authored in **HavenUI
(HUI)** — Haven's own scene-based UI layer — which renders through an
Avalonia-backed host. All AI inference is local-first (Ollama), data stays on
device by default, and the product is organised around **Apps** (Chat, Study,
Tasks, Studio, Browser, Plan, Imagine, and more).

## Start here

| Question | Read |
|---|---|
| What does each source project own? | [architecture/repository-structure.md](architecture/repository-structure.md) |
| Where does an existing system live? | [architecture/system-map.md](architecture/system-map.md) |
| What is HUI and how does it use Avalonia? | [ui/haven-ui.md](ui/haven-ui.md) |
| How do I build a normal Haven surface? | [ui/building-ui.md](ui/building-ui.md) |
| How do themes/accent/fonts/avatars work? | [ui/theming-and-personalisation.md](ui/theming-and-personalisation.md) |
| Where does state belong? | [architecture/state-and-persistence.md](architecture/state-and-persistence.md) |
| How do I add a feature or an app? | [development/adding-a-feature.md](development/adding-a-feature.md), [development/adding-an-app.md](development/adding-an-app.md) |
| How do I validate a change? | [development/testing.md](development/testing.md) |
| Which mistakes should I avoid? | [development/common-mistakes.md](development/common-mistakes.md) |

## The one-minute architecture

```text
Haven.Desktop / Haven.Android        platform hosts (Avalonia Desktop / Android)
        │                            user-facing surfaces: pages, shell, scenes
        ▼
Haven.UI                             HUI framework: parser, layout, renderer,
        │                            input, prefabs, DynamicUI  (no Avalonia)
        ▼
Haven.Application                    use cases, routing policy, orchestration
        ▼
Haven.Core                           stable entities, enums, contracts
        ▲
Haven.Infrastructure                 SQLite, Ollama, filesystem, OS integrations
Haven.Browser                        browser engine integration
```

Dependency direction points downward for UI and upward for infrastructure:
`Core` depends on nothing; `Application` depends on `Core`; `Infrastructure`
and `Browser` depend on `Application`/`Core` contracts; `Haven.UI` depends on
nothing; `Desktop`/`Android` depend on everything above them. Never reverse
these arrows.

## Mandatory rule files

These remain authoritative and override any summary here:

- `AGENTS.md` — editing contract and required reads
- `HAVEN_UI_RULES.md` — canonical visual language, typography, interaction
- `docs/ARCHITECTURE_RULES.md`, `docs/SECURITY_RULES.md`,
  `docs/PLATFORM_RULES.md`, `docs/GENUI_RULES.md`,
  `docs/BACKGROUND_LEARNING_RULES.md`, `docs/AGENT_TOOL_EXECUTION_RULES.md`,
  `docs/VALIDATION_RULES.md`

## Code vs documentation

If production code and these documents disagree, do not silently pick one:
inspect history, decide whether docs are stale or code is transitional, keep
working behaviour unless your task requires a change, correct stale docs in
the same pass, and record intentional contract changes in the affected page.
See `AGENTS.md` for the full policy.
