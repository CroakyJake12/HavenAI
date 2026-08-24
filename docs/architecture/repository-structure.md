# Repository Structure

The solution (`Haven.sln`) contains these production projects under `src/` and
xUnit test projects under `tests/`. This page reflects the repository as of
the personalisation pass; empty legacy skeletons (`Haven.OldHaven`,
`Haven.Automations`, `Haven.AutomationWorker`) were removed in that pass.

## Projects and ownership

| Project | Owns | Must not contain |
|---|---|---|
| `src/Haven.Core` | Stable entities, enums, value objects, contracts (incl. `HavenMode`, `HavenSurface`, `HavenUiAppearance`, `HavenUiTheme`, `HavenAccentColour`, GenUI contracts) | UI, platform, persistence details |
| `src/Haven.Application` | Use cases, mode registration/routing policy, chat orchestration, planner/call coordination, automation graph, mesh coordination, projector catalogue | Windows/Android/SQLite specifics |
| `src/Haven.Infrastructure` | SQLite persistence + migrations, Ollama client, filesystem, calendar/audio/OS integrations | Avalonia/UI |
| `src/Haven.Browser` | Browser engine integration behind contracts | Product pages |
| `src/Haven.UI` (**HUI framework**) | Parser (`HavenMarkupParser`), scene model (`HavenElement`), layout engine, command renderer, input router, animation engine, classes/animations resources (`.hui`), prefabs, DynamicUI templates, accessibility model | Any Avalonia type; any Haven product screen |
| `src/Haven.Desktop` | Desktop shell + all shared presentation today: pages (AXAML hosts or HUI scenes), `Views/Shell`, `Controls`, `Styles/DefaultTheme.axaml`, theme/personalisation engine (`HavenUI/Tokens`), HUI→Avalonia backend (`HavenUI/Backend`), services | Business rules that belong in Application/Core |
| `src/Haven.Android` | Android lifetime/host glue, projector host, launcher; reuses Desktop's shared semantic UI via project reference | A parallel product UI |

Test projects: `tests/Haven.Core.Tests`, `tests/Haven.Infrastructure.Tests`,
`tests/Haven.Desktop.Tests` (headless Avalonia via `Avalonia.Headless.XUnit`),
`tests/Haven.UI.Tests` (framework-only, no Avalonia).

## Notes on the current shape

- **Shared UI lives in `Haven.Desktop`.** Android references `Haven.Desktop`
  and reuses its scenes/resources; only host/lifetime code is Android-side.
  Do not duplicate screens per platform.
- **AI infrastructure is split by contract**: provider/model abstractions and
  orchestration live in `Application`; concrete Ollama integration lives in
  `Infrastructure`. There is no separate AI assembly; keep it that way unless
  a real ownership problem emerges.
- App backends (chat sessions, document workspaces, browser policies) live in
  `Application` (+ `Browser` where engine-specific). Presentation models for
  individual surfaces live beside their pages in `Desktop`.
- Legacy naming such as `Interface/` inside Desktop is historical; new shell
  work goes through `Views/Shell` and HUI scenes.

## Dependency direction

Allowed (arrow = "references"):

```text
Core ← Application ← Infrastructure
Core ← Application ← Browser
(nothing) ← Haven.UI
Core+Application+Infrastructure+Haven.UI+Desktop ← Desktop   (self-contained host)
Core+Application+Infrastructure+Haven.UI+Desktop ← Android
```

Forbidden: any reference from Core/Application/Infrastructure to a UI project;
any reference into `Haven.UI` from Application-layer code; cycles of any kind.
