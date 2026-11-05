# Historical assets / arguments — local return point — 2026-10-29

Latest local return point: **NATIVE_NESTED_CHECKPOINT.md** (576 total / 574 passed / 2 explicit not run; Release 0 errors/warnings). Results below are retained historical/scoped evidence; do not restore them over current native nested work. No new release was published.

Latest source/results: **FORGE_EXECUTION_CHECKPOINT.md** (524 total / 522 passed / 2 explicit not run; Release 0 errors/warnings). The owner explicitly resumed development. Any earlier PAUSED instruction below records the older session only.

Current local return point: **FORGE_EXECUTION_CHECKPOINT.md**. The owner resumed development; results below are historical. No new release was published.

## Latest source — dangling-link hardening — 2026-10-29

Final code review found that File.Exists/Directory.Exists can hide a reparse point whose target is absent. SafePath.RejectLinks now inspects entry attributes directly and only tolerates FileNotFound/DirectoryNotFound; other access failures remain fail-closed. The explicit isolated junction test now has two cases (existing and removed target). Source rebuilt locally with **0 errors/warnings**; full suite **426 total, 424 executed/passed, 0 failed/errors/skipped, 2 explicit not run** (historical-assets-link-hardening-build.log; historical-assets-link-hardening.log; tests/historical-assets-link-hardening.trx).

The owner-run 1/1 junction result below applies to the prior binary and is preserved as such; it is not silently transferred to this changed source. Repeating the same explicit method command in the owner's ordinary Windows session now exercises both cases. Current new two-case manual run is pending until its TRX is read. No reparse-point permission guard was bypassed inside Codex. Native historical installation remains socket-blocked; game execution remains NOT RUN. Latest implementation/checkpoint is local only; next independent block remains Java update/removal UI. 189 requirement IDs / 44 modules / five DEFERRED bridge rows are unchanged.

## Junction follow-up — verified owner Windows run — 2026-10-29

The owner ran only the explicit isolated junction test in ordinary Windows PowerShell. Read back artifacts/0.2.0/tests/junction-manual.trx: **1 total/executed/passed; 0 failed/errors/skipped/not-run**, 2026-10-29 09:37 UTC. All 12 changed source/test files still matched the saved source SHA-256 manifest. Test assembly SHA-256: a41e1306f5005e07faf9e0a9cca81fea5059b18b6e18a14616964061a00eb848.

For the prior binary, this separately verifies rejection of a real junction without writing through it in the owner's ordinary Windows session. The normal Codex-environment suite remains **424 passed / 1 explicit not run out of 425**; for that prior binary, all 425 distinct scenarios have passing evidence across two runs. This is not a single 425/425 runner invocation and does not grant Codex additional OS permissions. The earlier Codex Access-is-denied failure remains historical evidence. Native .NET historical download still failed with socket 10013; Minecraft gameplay remains NOT RUN. No code, authentication gate, remote repository or release was changed for this follow-up.

Створено та розробляється за допомогою ШІ (OpenAI Codex).
Created and developed with AI assistance (OpenAI Codex).

Local source only. No push, new release, signing, installer compilation or remote CI. The existing public preview remains `v0.2.0-preview.e7b058cf31c3`; it does **not** contain this local block. Working directory: `%USERPROFILE%\Documents\PTenoLaucher`. No `.git` exists here: do not initialize/recreate or replace the project. Read-only comparison used known main `33583786707e1b5091c60f73ebc8913dd0704094`. 235 tracked files were compared by Git blob hash with CRLF normalization; two pre-existing differences in old AI/process checkpoint documents were preserved. Backups and baseline hashes: `.local/checkpoints/pre-historical-assets-1793265171038`.

## Implementation — MINECRAFT-03 / MINECRAFT-05

- `AssetIndexPlan.cs`: bounded static index reader, optional virtual/map_to_resources flags, logical names, SHA-1/size bounds, duplicate names/properties, Windows path/device names, case aliases, file/directory collisions and conflicting sizes rejected. Maximum 16 MiB index, 30,000 entries, 256 MiB per object, 4 GiB logical total. Unsupported data fails, never guessed.
- `AssetManager.cs`: unique object hashes use the existing verified HTTPS downloader/cache. Virtual projections are shared under `cache/assets/virtual/<index-id>-<index-sha1>/`; hash suffix isolates index revisions. `${game_assets}` explicitly points there. Mapped resources live under the selected profile's `game/resources/`; mapping-only `${game_assets}` points there. With both flags, virtual is the argument target and both projections are prepared. Modern `${assets_root}` remains `cache/assets`, with separate `${assets_index_name}`.
- Shared virtual journals: `cache/assets/projections/<id>-<index-sha1>.json`; mapped journal: `instances/<profile-id>/mapped-assets.json`. A `.lock` sidecar serializes shared projection writers. Profile install/world leases remain. Shared objects/virtual data are retained when a profile is deleted; mapped files/journal/backups are inside its subtree.
- Journals distinguish committed `owned` files from intended `pending` creations. Durable intent precedes writes. Exact hash-matching pending bytes can be recovered after interruption; changed/unrecognized pending files fail closed. Unknown pre-existing files are never adopted merely because they match. New destinations use a no-overwrite atomic move, so a late unknown file blocks commit.
- Verified copies are flushed and atomically moved. Damaged previously owned projection bytes are backed up under `<journal>.backups/<GUID>/<logical-path>` before repair. These backups remain launcher-owned within the corresponding profile/cache scope. Current shared-cache cleanup is not implemented and must not indiscriminately delete these directories.
- Cancellation leaves a recoverable in-progress journal; incomplete state is not ready. Retry can use all verified cached objects offline, with no HTTP. Installation receipt is committed after complete preparation. A physical power-loss test was not run. Orphan `.part` files after a hard kill are not automatically purged; retry uses fresh exclusive staging and never treats those files as ready resources. There is no whole-profile transactional rollback for a version switch: pending projections require repair before launch.
- `LegacyAssetLayout` and `VersionType` are optional receipt additions; old Vanilla/Fabric/Quilt receipts remain readable. ReceiptValidator checks structural inventory/index linkage, while readiness re-parses the hash-verified index and compares exact logical entries/flags to the layout and journals. Projection corruption cannot become a ready report.
- GameLauncher uses receipt `VersionType` and the selected asset layout. Bounded single-pass `LaunchArgumentExpansion` preserves literal values, rejects unknown placeholders and uses `ProcessStartInfo.ArgumentList` with no shell. Legacy tokenization retains empty quoted arguments. Authentication/ownership checks remain before the existing actual game launch; component preparation requires no account.

## Earlier local verification — before dangling-link hardening

Release solution build: **0 errors, 0 warnings**. Full direct xUnit run: **425 total; 424 executed/passed; 0 failed/error/skipped; 1 not runnable/not run**. The historical baseline was 386; 39 additional asset/argument cases were added, 38 passed and the isolated Windows junction case is explicit.

Evidence under `artifacts/0.2.0`:

- `historical-assets-final-integrity-build.log`
- `historical-assets-final.log`
- `tests/historical-assets-final.trx` (counters: total=425, executed=424, passed=424, failed=0, notRunnable=1)
- `historical-assets-targeted.log` and `historical-assets-junction-probe.log`: retained earlier failure creating a junction; not a passing security integration result.
- `historical-assets-native-live.log` and `.local/historical-live-20261029/historical-result.json`: FAILED/NETWORK-BLOCKED before the first official component install, SocketException 10013 at piston-meta.mojang.com.

Tests cover virtual/mapped/both/modern installs, hash deduplication, cache-only reuse/repair, preservation backups, user files before and during preparation, changed pending-file refusal, cancellation, new-installer restart across the journal boundary, corrupt downloads/cold offline refusal, corrupt journals, forged receipt names/hashes, path/collision guards, profile/shared-cache separation and a real controlled argument echo process. That process only echoes non-account fixture arguments; no Java or Minecraft is executed.

The junction fixture was attempted and failed with `Access is denied` before creating the test link. It is now `[Fact(Explicit = true)]` and must only run in an environment already permitted to create isolated junctions. Do not retry/bypass the denial, request admin elevation or weaken SafePath to make it pass. Link protection is implemented through the existing `SafePath.RejectLinks`; the later owner-run TRX verifies this real junction scenario separately; Codex permissions remain unchanged.

An earlier Node request read public Mojang metadata before the native-network attempt: 1.6.4/1.7.2 advertise the virtual `legacy` index and `${game_assets}`; 1.7.10 advertises a modern index. Captured parsed metadata is in `.local/historical-official/metadata.json`; original raw JSON bytes were not retained, so no successful raw-index SHA-1 verification or full official installation is claimed from that observation. Do not reconstruct/re-hash this into a fake official manifest. No alternate transport was used to work around the later socket denial.

The opt-in real component checker is `LiveHistoricalAssets.cs`:

```powershell
dotnet tests/PTenoLauncher.IntegrationSmoke/bin/Release/net10.0/PTenoLauncher.IntegrationSmoke.dll <fresh-isolated-root> --historical-assets
```

It prepares 1.6.4 and 1.7.10, verifies receipts/files/projections, checks offline repeat/virtual corruption repair and preserves synthetic data. It writes durable phase/results JSON. `--historical-assets-offline` requires a complete legitimately obtained cache in the isolated fixture; it never fabricates downloads. The current live run failed; no success is recorded. map_to_resources has controlled-fixture coverage, not a real official mapped-index installation. Never use real user saves with this checker.

## Evidence boundaries / next work

All 189 requirement IDs and all 44 progress modules remain. The verifier additionally protects all five retained added IDs, not only the 184 original IDs. MINECRAFT-03 and MINECRAFT-05 remain **Partially Implemented**: controlled preparation/arguments are verified, real historical installation is network-blocked, actual Minecraft gameplay is NOT RUN. No readiness percentage was raised because of synthetic tests alone.

Existing eight UI languages/first-run choice, SQLite, mod cart/dependencies, Fabric/Quilt, local translations/manual edits/offline pack export, backups, protected credentials and default-off independent AI remain. Current traceability statements about missing translations/offline packs/OpenAI key UI/Quilt root metadata/prerelease were corrected after inspecting their implementations and tests. Full localization, nested formats, automatic pack format, live AI/quality and game activation still remain incomplete.

The old Windows runner Java 8/17/21/25 matrix and 386/386 tests remain historical evidence in JAVA_RUNTIME_CHECKPOINT.md; they were not rerun locally here. The current Setup/Portable payload was not rebuilt or installed. The prior installer registry ownership guard remains blocked and must not be bypassed. Microsoft approval/entitlement/profile/refresh and licensed gameplay remain unverified. No account/key/paid API/user world was used.

Next independent complete block: Java Manager update/removal UI, ownership-aware inventory, profile/process usage checks and retain the old runtime until a replacement passes verification. Continue official Forge/NeoForge and retained roadmap afterwards. Sinytra, Kilt and all Cross-Loader bridges remain DEFERRED. Current user instruction prohibits remote writes/releases/signing without a separate request.
