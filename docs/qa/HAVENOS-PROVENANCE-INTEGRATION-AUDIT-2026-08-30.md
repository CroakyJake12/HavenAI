# HavenOS provenance and integration audit — 2026-08-30

## Scope and invariant

Authoritative target: `havenos-main` at `7b2acae6175e5c380a3812b531b90ca82dbf85c3` (`docs: complete HavenOS provenance SHAs`).

QA lane: `havenos/worker/qa-provenance-integration-20260830`, initially identical to the authoritative target (0 ahead / 0 behind).

This audit is read-only with respect to every product lane. No branch or worktree was deleted, reset, cleaned, stashed, rebased, force-pushed, or rewritten. Dirty and otherwise unverified worktrees remain retained.

## Existing composition provenance

`docs/HAVENOS-COMPOSITION-PROVENANCE.md` records eight source SHAs and eight composition SHAs. The eight source provenance anchors all resolve in `CroakyJake12/HavenAI`:

1. Write — `f038d52191b2e8558e6036a25eda8f9ce79dbf70`
2. Browse — `ec48a80d4da14f80dbb4f578a17f170ae70ddd5b`
3. Data Spreadsheet — `8e778b70dfa3cddb0ccc3ff1b9481017ac21830a`
4. Data Database — `ef3ee8782504e64e4114a9c58605f6299aef37ca`
5. Quick Settings — `74a3866b44a7aaa34d9ff7f2e53883d6cd43193f`
6. Projector — `6bd5f5424a28e8b54bf78579f7a7f75baefac80a`
7. Shell launcher — `06614a2195f2290da5b32b207ead968dafd07827`
8. Linux packaging — `7e930d149979f80222737ffaf23e509580bcd140`

The ledger therefore provides internally consistent commit-chain provenance. It does **not** identify a donor repository/origin or license for the source anchors. The `havenos-main` tree also has no root `LICENSE` file and no license-named file in the recursive tree snapshot. Treat distribution/license clearance as unproven until a first-party ownership statement or donor/license manifest is added. This is a release-compliance gate, not a claim that any observed source is unlawfully licensed.

## Branch and commit hygiene snapshot

`havenos-main` is currently unprotected, has no required status checks, and its head commit is unsigned. No open pull requests matching the current `havenos/worker` lanes were found. The committed worker heads checked during this audit have empty combined commit-status sets, so GitHub does not independently prove their builds/tests.

Seven current HavenOS worker branches were ahead of `havenos-main` at the final branch inventory snapshot; each was 0 commits behind:

| Lane | Head SHA | Ahead | Changed-area summary | QA gate |
| --- | --- | ---: | --- | --- |
| `havenos/worker/os-gnome-platform-20260830` | `61e4f6102d629758557ded95272778ab996df4fc` | 1 | `docs/`, `scripts/linux/`, `tests/linux/` | **HOLD** — explicit GNOME source SHA is format-checked but not verified against source content/repository HEAD. |
| `havenos/worker/os-performance-capabilities-20260830` | `1f84fdd0e70af03bf7e20653d0fe66e9824dbfc9` | 2 | legacy `src/Haven.Infrastructure/...` + tests | **HOLD** — shared platform-service work is outside the required HavenOS-root placement; no independent test evidence. |
| `havenos/worker/os-wine-compatibility-20260830` | `0369902f0afb25b0eed8a03f7b88ffd3063b123d` | 1 | legacy `src/Haven.Infrastructure/WindowsCompatibility/...` + tests | **HOLD** — shared platform-service work is outside the required HavenOS-root placement; no independent test evidence. |
| `havenos/worker/os-llm-runtime-20260830` | `fcc5c9c77b5fd5dec967a3b23031a306db744dd8` | 2 | `docs/` plus legacy `src/Haven.Application` / `src/Haven.Infrastructure` + tests | **HOLD** — documentation calls this an OS-root runtime but implementation is in legacy `src/`; no independent test evidence. |
| `havenos/worker/os-apk-launcher-20260830` | `5cd6dcfe97be9d6fc7248efe9392e4651b03a628` | 2 | legacy `src/Haven.Application` / `src/Haven.Infrastructure` + tests | **HOLD** — shared platform-service work is outside the required HavenOS-root placement; no independent test evidence. |
| `havenos/worker/os-linux-packaging-runtime-20260830` | `047795641210cb3d2c9bc993626be20bee330edf` | 2 | `.github/workflows/`, `eng/` | **HOLD-TEST** — placement is root-level and diff is isolated, but there is no independent build/CI evidence; distribution license metadata is also unproven. |
| `havenos/worker/canvas-app-20260830` | `68a3c40ae5340acf2dc66945cf8328362d05df19` | 1 | `HavenOS Apps/Canvas/` only | **HOLD-TEST** — app placement is correct and changes are isolated, but no independent build/CI evidence exists. |

The remaining 19 current `havenos/worker/*-20260830` branches were still exactly at the common baseline SHA `7b2acae6175e5c380a3812b531b90ca82dbf85c3` at the final branch inventory snapshot and therefore had no remote slice to merge yet:

`boards-app`, `browse-app`, `data-app`, `dev-app`, `hui-accessibility-performance`, `hui-android`, `hui-core`, `hui-desktop`, `images-app`, `motion-app`, `os-settings-model-picker`, `os-shell-taskbar`, `present-app`, `qa-functional-release`, `qa-provenance-integration` (before this audit commit), `spaces-app`, `terminal-app`, `wave-app`, and `write-app`.

Baseline-only is **not** a deletion signal. These branches may have active local work and must remain retained until the owning lane reports a clean, captured state.

## Exact integration queue

No product branch is authorized for immediate merge by this audit because independent validation evidence is absent. Once each row's gate is cleared, integrate in this order to keep shared/platform changes ahead of packaging and app surfaces:

1. `havenos/worker/os-gnome-platform-20260830` @ `61e4f6102d629758557ded95272778ab996df4fc` — first fix source-SHA verification, then independently run its smoke test.
2. `havenos/worker/os-performance-capabilities-20260830` @ `1f84fdd0e70af03bf7e20653d0fe66e9824dbfc9` — first resolve OS-root placement, then independently run focused infrastructure tests.
3. `havenos/worker/os-wine-compatibility-20260830` @ `0369902f0afb25b0eed8a03f7b88ffd3063b123d` — first resolve OS-root placement, then independently run focused tests.
4. `havenos/worker/os-llm-runtime-20260830` @ `fcc5c9c77b5fd5dec967a3b23031a306db744dd8` — first reconcile the documented OS-root contract with actual placement, then independently run focused tests.
5. `havenos/worker/os-apk-launcher-20260830` @ `5cd6dcfe97be9d6fc7248efe9392e4651b03a628` — first resolve OS-root placement, then independently run focused tests.
6. `havenos/worker/os-linux-packaging-runtime-20260830` @ `047795641210cb3d2c9bc993626be20bee330edf` — independently validate Linux publish/package path and establish release license metadata before distributing artifacts.
7. `havenos/worker/canvas-app-20260830` @ `68a3c40ae5340acf2dc66945cf8328362d05df19` — independently build/test the standalone Canvas journey.

After each merge, re-compare every later head against the new `havenos-main`; do not mechanically merge the next SHA if the base moved or overlaps changed.

## Local worktree preservation gate

Sandbox preflight exposed active worktrees. Ten legacy agent worktrees are currently dirty and must not be deleted or retired until their owners capture/verify them:

- `agent/a01-a02-hub-spaces-20260829`
- `agent/r11-build-ci-arm64-windows-portability-20260829`
- `agent/r04-hui-linux-gnome-20260829`
- `agent/r10-hardware-linux-services-20260829`
- `agent/r08-packaging-lifecycle-permissions-20260829`
- `agent/r06-native-apk-runtime-20260829`
- `agent/r02-shared-platform-sdk-ipc-20260829`
- `agent/r05-native-exe-wine-20260829`
- `agent/r03-ubuntu-gnome-base-20260829`
- `agent/r01-programme-architecture-ledger-v2-20260829`

Other active clean worktrees are also retained because clean does not imply independently integrated/verified.

## Independent validation limitation

The Sandbox refused switching the registered Haven checkout to `havenos/worker/qa-provenance-integration-20260830` because active task worktrees are registered. That refusal was respected; no destructive worktree operation was attempted. Consequently this QA lane could not independently execute local `dotnet`/shell builds or tests during this audit.

Evidence completed instead:

- authoritative/base branch and QA branch identity checked;
- recursive repository tree inspected for provenance/license records;
- all eight existing source provenance anchors resolved;
- all committed current worker heads compared to `havenos-main` for ahead/behind state and changed paths;
- current worker branch inventory refreshed during the audit;
- open PR search performed;
- commit-status checks inspected on committed heads (none present at audit time);
- active/dirty local worktree inventory captured through Sandbox preflight.

## Blocking actions

1. Add a repository-level ownership/license statement and, for any imported/donor code, a donor manifest containing source repository, immutable source revision, license/SPDX identifier, imported paths, and required attribution/notice handling.
2. Fix GNOME explicit-source verification so the recorded SHA is derived from or cryptographically tied to the supplied source tree, rather than merely accepting a well-formed caller-supplied hex string.
3. Resolve the four shared platform/runtime lanes currently implemented under legacy `src/Haven.*` paths against the programme rule that HavenOS shared platform services live at the OS root.
4. Obtain independent focused build/test evidence for every queued SHA; do not rely solely on test files or worker-authored README commands.
5. Add branch protection / required checks for `havenos-main` before using it as a multi-worker integration target.
6. Preserve every dirty or unverified active worktree/branch until its owner explicitly reports it captured and safe to retire.
