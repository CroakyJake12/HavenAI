# Haven Notes branch recovery report

Branch: `haven-continuation`  
Main touched or merged: **No**  
Training work: **Not changed**

## What happened

The interrupted Notes pass encountered two separate GitHub connector behaviours:

1. file updates occasionally arrived after another commit had moved the branch, so GitHub rejected the stale blob SHA rather than overwriting newer work;
2. branch and code-search indexes became inconsistent, returning no `haven-continuation` branch or only the initial repository commit even while direct reads and writes to `haven-continuation` continued to succeed.

The branch was not deleted, force-reset or split. A direct compare against the original project showed the continuation branch hundreds of commits ahead and zero commits behind. Direct `fetch_file` and contents-API writes resolve the branch normally.

## Recovery rule

For this branch, the authoritative state is:

- direct file reads from `ref=haven-continuation`;
- the current blob SHA returned by that read immediately before an update;
- successful contents-API commit results.

Search-index branch and commit results are not authoritative while the index is stale. Every update must re-fetch the target file first, and rejected stale-SHA writes must be rebased onto the newest blob rather than forced.

## Recovery commits

The recovery tranche continued from current branch blobs and added or corrected:

- `0ef63d96af71bf772778e6a245931757d33d6603` — selected Notes table and verified-media inspector;
- `cfda33fc8e5c7da671b1d02a7139b2097cee09d9` — Block inspector tab wiring;
- `2c6e76058264b361599ef8e9b57bb735879e3921` — selected-block refresh integration;
- `0273453957dd7388fabd46452d56b418b911d397` — headless block-inspector tests;
- `4683a48972943612ebc9906457de1e471590dc5f` — nullable/warnings-as-errors test cleanup;
- `accbd599ccfb17dc97f59479ab800c1ced54c9e5` — large Notes stress suite;
- `5bb6c3440995afc02120e972aa369caccd167d9a` — corrected stress fixture accounting;
- `d924f2a31e0878d36249c590ac634b9412ac8b79` — accessibility and focus regression coverage.

## Notes state recovered

The current branch contains coherent source for:

- schema migration before managed load and native import;
- atomic storage, versions, integrity manifests and corrupt-current recovery;
- managed attachment and media SHA-256 verification;
- selected table sort/sum/tab-delimited tools;
- selected media verify/open/replace/save-copy tools;
- document productivity, headers/footers, fields, bookmarks, styles, privacy and version comparison;
- paginated, freeform and infinite layouts;
- equations, canvas geometry/connectors/bookmarks, study attempts and conflict review;
- provider-routed Notes AI with explicit review;
- headless UI, migration, media, stress and accessibility tests.

## Validation boundary

No green build or test result is claimed for this recovered head. The connected GitHub surface cannot start a new manual `workflow_dispatch` run, and search/status indexing is not reliable evidence of branch health.

Required validation remains:

```powershell
dotnet restore Haven.sln
dotnet build Haven.sln -c Debug --no-restore
dotnet test Haven.sln -c Debug --no-build
dotnet build Haven.sln -c Release --no-restore
dotnet test Haven.sln -c Release --no-build
dotnet build src/Haven.AutomationWorker/Haven.AutomationWorker.csproj -c Debug --no-restore
dotnet build src/Haven.AutomationWorker/Haven.AutomationWorker.csproj -c Release --no-restore
```

Until those commands pass, Notes remains source-implemented and validation-pending rather than release-complete.
