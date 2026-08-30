# HavenOS integration checkpoint provenance

Target branch: `havenos/integration-checkpoint-20260830`

Target base before composition: `f141eee9a42e76fb51da7bd2cdd13455c9e3567d`

The legacy HavenAI root checkout and its existing `.mail-worker-20260825/` content were not modified. This composition is confined to the external HavenOS integration-checkpoint worktree.

| Order | Lane | Source commit | Composition commit | Resolution/evidence |
| ---: | --- | --- | --- | --- |
| 1 | Write | `f038d52191b2e8558e6036a25eda8f9ce79dbf70` | `dfbdc92` | Clean merge; existing Write route/test retained. |
| 2 | Browse | `ec48a80d4da14f80dbb4f578a17f170ae70ddd5b` | `66415bea` | Shared route test retained with Browse `web` alias coverage. |
| 3 | Data Spreadsheet | `8e778b70dfa3cddb0ccc3ff1b9481017ac21830a` | `b2d55208` | Preserved Spreadsheet and existing Browse/Database tests; retained both data aliases. |
| 4 | Data Database | `ef3ee8782504e64e4114a9c58605f6299aef37ca` | `3eccd1f5` | Preserved Database, Spreadsheet, and Browse route/test coverage. |
| 5 | Quick Settings | `74a3866b44a7aaa34d9ff7f2e53883d6cd43193f` | `54e9772d` | Clean merge; disabled-by-default slice retained. |
| 6 | Projector | `6bd5f5424a28e8b54bf78579f7a7f75baefac80a` | `23a50324` | Clean merge; capability-gated slice retained. |
| 7 | Shell launcher | `06614a2195f2290da5b32b207ead968dafd07827` | `62703571` | Clean merge; fail-closed launcher and desktop entry retained. |
| 8 | Linux packaging | `7e930d149979f80222737ffaf23e509580bcd140` | `3b9e4da3` | Add/add conflict resolved to the verified `Haven` Linux publish entrypoint, matching `AssemblyName`. |

The source commit SHAs above remain the provenance anchors. The composition commits are local integration history only and have not been pushed or merged to legacy HavenAI `main`.
