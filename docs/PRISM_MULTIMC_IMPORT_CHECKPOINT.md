# Prism/MultiMC — verified bounded local workflow

> **Current continuation 2026-11-02:** [MINECRAFT_LAUNCHER_IMPORT_CHECKPOINT.md](MINECRAFT_LAUNCHER_IMPORT_CHECKPOINT.md) records the verified AI manual-test additions, separate Minecraft Launcher adapter, 947/945/two-not-run regression and exact cleanup. The dated source/evidence below is historical, not a missing-feature claim. UX-01–UX-06 matrix and retained hand-test build: [HEX_TRANSLATION_CHECKPOINT.md](HEX_TRANSLATION_CHECKPOINT.md).

<!-- CURRENT-INSTANCE-IMPORT-20261102 -->
## Current Prism/MultiMC local import — 2026-11-02

Resumed on the owner's explicit instruction; the earlier pause records below are historical. At entry, all 235 source hashes from the AI UX checkpoint matched. The AI UX, MODS-19, updater, .mrpack/ZIP implementations were preserved, not restarted. Source mirror has no .git; no applicable AGENTS.md was found.

Implemented **MODPACKS-03 first adapter**: explicit OneSix/component-v1 directory or single-instance ZIP (root/one wrapper), strict metadata and exact Minecraft/loader requests, read-only source, bounded static JAR/dependency/nested preview, exclusions, new profile/name/local pins, exact source-bound consent, source/content mutation checks, shared staging/durable journal/insert-only SQLite/recovery, eight-language controls and progress. No accounts/worlds/runtime/game files or commands are transferred/executed. Unknown/custom/ancillary component requests and ambiguous/external/linked game roots remain blocked. Minecraft Launcher is still a separate missing adapter, so MODPACKS-03 remains **Partially Implemented**, not complete. Usage/limits: **docs/PRISM_MULTIMC_IMPORT.md**.

**Fresh final verification:** Release **0 errors / 0 warnings** (`verification-build-20261102-183218.log`); full suite **891 total / 889 executed and passed / two explicit junction cases NOT RUN / zero failures/errors** (`tests/verification-all-20261102-183256.trx`). This includes **63 new instance-import scenarios**, including three actual Avalonia workflows and the source-identity guard. A preceding scoped import run passed **158/158** before four final hardening cases; the preceding full run was 890/888/two-not-run. Those are retained as earlier runs, not recast as final-code checks. Twelve new messages in all eight catalogs; placeholder/catalog regression passes.

**Separate real process/native proof:** owned synthetic directory imported offline with exact Fabric 0.16.10, one verified content file, unchanged source, zero parent importer HTTP requests, no game receipt. Current framework-dependent Desktop then opened Modpacks initialized with one profile at **980×680** and exited normally; screenshot inspected. Evidence: `artifacts/test-lifecycle/instance-native-verification-instance-native-20261102-183256-c9f1f9820eed49f493d1249ba9aa11d4` / `records/instance-native-result.json` (`verification-instance-native-20261102-183256.log`). Native picker interaction, other physical DPI settings, a real third-party source profile, actual component installation and gameplay are **NOT VERIFIED**. This is not self-contained distribution, Installed lifecycle or production update delivery. No new full updater E2E, live AI/paid/key test or vulnerability audit.

Test lifecycle for this session: **1521 owned operations**, approximately **306.126 MiB measured-created / 306.126 MiB removed**, **zero new-run payload roots remain**. An older ownership-conflicted native-probe root remains **86209 bytes**; it was not adopted/deleted. Compact logs/TRX/JSON/hashes/PNG evidence is retained. Exact paths/sizes: **artifacts/0.2.0/instance-import-cleanup-20261102.json**. No broad historical cleanup, user/product cache deletion or user-process termination.

**Approval mail check:** no approval message found in the two connected Gmail accounts (including spam/trash); the previously shown Entra account mailbox is not connected, so absence in all mailboxes or official rejection is not claimed. Details: **docs/APPROVAL_MAIL_CHECK_2026-11-02.md**. Microsoft approval, production updater key/feed, final distribution and registry-guarded Installed lifecycle remain external/unverified blockers. No guard bypass or remote push/release/signing occurred.

All **189 requirements / 184 original IDs / 44 module rows / five DEFERRED Sinytra/Kilt/Cross-Loader rows** are preserved. Eight languages/onboarding, profiles/worlds/caches/translations/backups, core independence from AI and auth/licensing gates remain. Next independent adapter: **Minecraft Launcher**, followed by Modrinth/CurseForge App (MODPACKS-04), then license-aware export/comparison. Always carry **docs/CONTINUATION_RULES.md** into the next continuation prompt.
<!-- END-CURRENT-INSTANCE-IMPORT-20261102 -->

## Intermediate outcomes retained — 2026-11-02

- `artifacts/0.2.0/verification-build-20261102-182109.log`: FAILED, two CS0120 errors after adding a per-reader budget to the previously static directory walker. Fixed by making the walker an instance method; no guard was removed.
- `verification-build-20261102-182202.log`: successful compilation with one xUnit1013 warning for a public fixture helper. The helper is now internal; diagnostics were not suppressed.
- `verification-imports-20261102-182348.log`: 158/158 PASS before four final hardening cases. `verification-all-20261102-182727.log`: 890 total / 888 PASS / two explicit NOT RUN before the final source-identity binding. Neither is claimed as the final-code result.
- Final-code build/full/native evidence is identified above. Original unsuccessful logs were retained, not overwritten or renamed as success.

# Historical paused implementation — 2026-10-30

This is an unfinished MODPACKS-03 block, not a completed importer. No import, game, launcher, helper, build or test process was started for this block. No user's installed Prism/MultiMC profile/account was accessed or changed. Source mirror has no .git; no applicable AGENTS.md was found.

## Completed before pause

- Rechecked the current ZIP checkpoint against its 250 recorded source/report hashes: no newer changes were present.
- Read the requested current documents. Existing .mrpack/ZIP, MODS-19 and updater were not reimplemented.
- Pre-edit copy: `.local/checkpoints/pre-prism-1793342860`.
- Read primary official source through the GitHub connector: Prism 9.4 and current develop, MultiMC develop (commit `a93647e8e9144bd656080919d1e21cfea4cf5d1d`). References/blob hashes and semantic notes: `PRISM_MULTIMC_SPEC_REFERENCES.json`. Browser raw/blob fetches failed; the connected repository reader succeeded. No local network workaround was attempted. The guessed MultiMC `0.7.0` tag returned 404; do not claim that release was verified.
- Verified common component format v1, uid/version versus nonauthoritative cachedVersion, disabled/dependencyOnly/cached fields, INI handling and custom command settings. Prism and MultiMC choose different defaults when both minecraft and .minecraft exist; planned adapter must block that ambiguity. Producer cannot be identified from the common format alone.

## Unfinished local code — NOT BUILT / NOT TESTED

1. `Infrastructure/Modpacks/ZipPackReader.cs`: extracted the existing eligibility/private-path predicate into shared internal helpers. Intended behavior is unchanged, but regression has not run after this refactor.
2. New `Infrastructure/Modpacks/InstanceSource.cs`: initial bounded explicit-directory/export-ZIP reader, path/link/container guards and selective directory traversal (allowed content only; no recursive worlds/account/runtime scan). This reader has not been connected to a metadata parser, preview, transaction or UI and has not been verified. Review it before use; it does not itself define supported exports or authorize copying arbitrary Open/Bytes paths.

No readiness or requirement status was raised for merely adding a class. The last successful Release binaries and 787-total / 785-passed / two explicit-not-run results are **historical ZIP-block evidence**, not a verification of these current source edits. There is no new gameplay, third-party profile, component installation, real Installed lifecycle or production updater evidence.

## Resume

Resume only on a new user request. Read this checkpoint and inspect the current files; do not restore the snapshot over newer edits. First finish the strict metadata/source adapter and tests, then compile/test; do not repeat .mrpack/ZIP/MODS-19/updater.

Planned bounded pipeline: explicit instance directory or single root/one-wrapper instance ZIP; required instance.cfg + mmc-pack.json; exact OneSix component requirements; reject duplicate/missing/unknown/conflicting components, custom patches/hooks/external game roots. Only allowed client content; saves, accounts/tokens/sessions/runtime/library/game/launcher settings excluded. Default PTeno memory policy, no arbitrary JVM args. Safe immutable local snapshot with source metadata/content fingerprint before and after copying, then reuse existing staged/journaled insert-only publication/recovery and static JAR/nested inspection. Preserve original source, provider identity=null for local JARs, exact requirements/provenance without raw secrets, stale-preview/consent guards and all eight languages.

Ancillary LWJGL/intermediary components need an explicit conservative policy and verified requirements; never silently discard a custom version or claim compatibility. Distinguish imported content from installed components and authorized gameplay. Minecraft Launcher remains a separate MODPACKS-03 adapter; Modrinth/CurseForge App are MODPACKS-04, export follows separately.

All 189 requirements, 44 modules and five DEFERRED Sinytra/Kilt/Cross-Loader rows must remain. Existing profiles/worlds/cache/translations/backups, eight locales/onboarding, auth/license gates and AI independence are unchanged. No push, publication, signing, paid API or installer registry guard bypass.
