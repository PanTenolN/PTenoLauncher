# PTenoLauncher 0.2.0 — Test Report

## Localization and sign-in diagnostics - 2026-10-28

Development resumed at the owner's request. Added 94 Ukrainian/English catalog messages, localized ViewModel actions/statuses, picker/dialog titles, snapshots, profile/mod counts and major readiness causes. Language switching refreshes computed labels and persisted language is explicitly applied during initialization. Original user data and game console content are not translated. Authentication failures now include a fixed enum stage; no response bodies, tokens or full URLs are logged. See docs/MICROSOFT_REVIEW_FOLLOWUP.md.

Local build/test evidence is recorded in artifacts/0.2.0/localization-final-build.log, localization-tests.log and tests/localization.trx. The previous source checkpoint's Windows runner and prerelease publication both succeeded (run 40728874523, commit 92998deeb0907798a02fef7404f98c5a1e58c0a7). Actual Setup, Portable, Source ZIP and SHA256 sums are public; current localization changes require their own new build. Current installer lifecycle/gameplay remain unverified.

Localization is still PARTIAL: low-level mod/security diagnostics, previously formatted dynamic messages after a live language change, theme names and exhaustive linguistic/accessibility review remain. UI-07/UI-08 must not be promoted to complete. No additional language is claimed. All 184 requirements are preserved. Microsoft review submission is owner-reported; approval remains pending. A scoped connected-mail search for PTenoLauncher or its exact Client ID returned no messages; this does not prove that no approval email exists.

## Publication verification - 2026-10-28

Release build repeated successfully: 0 errors, 0 warnings (artifacts/0.2.0/github-publication-build.log). PowerShell build-script syntax passed; it now invokes the same xUnit v3 runner used locally. The full suite was repeated: 281 passed, 0 failed/errors/skipped/not-run; evidence: artifacts/0.2.0/github-publication-tests.log and tests/github-publication.trx. The new remote Windows build/prerelease workflow has not run at this documentation checkpoint; no remote Setup or release success is claimed.

## External review update - 2026-10-28

Owner-reported AppID review submission: SUBMITTED; approval PENDING, not independently confirmed. This is an administrative update, not a successful authentication test. Browser OAuth completed previously; downstream Minecraft Services returned HTTP 403. No new tests or game launch were run for this update; previous evidence below is retained.

## Forge/NeoForge metadata pause checkpoint - 2026-10-28

Development is **PAUSED at the user request before powering off the laptop**. No build/test/integration is left running. Source is saved in the existing workspace and artifacts/PTenoLauncher-Source-0.2.0-forge-metadata.zip. Existing Quilt/auth-fix executables, Portable and Setup remain previous payloads; this block has not republished or installed them.

Forge/NeoForge native root TOML metadata is partially implemented: multi-mod JAR identities/versions, dependency types/sides, Java features, numeric Maven intervals/unions, main-manifest version substitution and BEFORE/AFTER cycle detection. Integrated with the existing local cart, transaction/inventory and installed-mod readiness. No JAR code is executed. See docs/FORGE_MOD_METADATA.md.

Release build: **0 errors, 0 warnings**. Full source suite: **281/281 passed**, 0 failed/skipped/not-run, including 40 new ForgeModMetadataTests cases. Logs: artifacts/0.2.0/forge-metadata-build.log; forge-metadata-targeted.log; forge-metadata-full.log; tests/forge-metadata-full.trx. xUnit v3 direct runner used; no real game launch.

Forge/NeoForge loader installation, Jar-in-Jar selection, Maven qualifier ordering and real mod/game integration remain pending. NeoForge FML version is independently versioned and remains Unknown until actual installer metadata is available; it is never inferred from the NeoForge version. Unsupported metadata cannot establish Ready.

Tomlyn 2.10.1 is pinned in project/lock files; downloaded package SHA-512 matches the official NuGet catalog. Native NuGet restore/audit was network-blocked (NU1301/NU1900). A workspace-only feed/existing cache was used, with NuGetAudit=false for that isolated restore invocation only; the repository default remains NuGetAudit=true. A live vulnerability audit is NOT VERIFIED. See tomlyn-package-verification.json and docs/FORGE_MOD_METADATA.md.

Resume from this source checkpoint. Next independent work: actual Forge/NeoForge installer/catalog and FML metadata, complete nested candidate handling, then the retained roadmap. Microsoft Services HTTP 403/manual licensed gameplay remains external. Preserve all 184 requirements; do not recreate the project, bypass auth, use real worlds or retry the blocked installer registry guard.

## Retained earlier checkpoints

## Quilt mod metadata checkpoint - 2026-10-28

Release solution build: 0 errors/warnings. Full source suite: **241/241 tests passed**, 0 failed/skipped/not-run, including 32 new Quilt metadata cases. Evidence: artifacts/0.2.0/quilt-mods-final-build.log; quilt-mods-final-tests.log; tests/quilt-mods-final.trx. xUnit v3 direct in-process runner is used because the previously recorded VSTest collector handshake failed before cases.

The opt-in real OK Zoomer/Modrinth check is BLOCKED by unreachable api.modrinth.com (SocketException 10051), before a real mod download. Synthetic JAR test success is not real-artifact/gameplay evidence. No system/proxy/DNS settings were changed.

User retest of the auth-fix payload completed browser OAuth, then failed with HTTP 403 from api.minecraftservices.com. The screenshot identifies the host/status but not the exact endpoint or safe server reason. An application-service access/registration issue is possible, not proven. Full entitlement/profile/refresh and online/offline gameplay remain unverified. No new application, borrowed Client ID or authentication bypass was used.

Separate self-contained Windows publish and freshly extracted Portable startup: PASS. Preview artifacts: artifacts/0.2.0/quilt-mods-win-x64, PTenoLauncher-Portable-0.2.0-quilt-mods-win-x64.zip and PTenoLauncher-Source-0.2.0-quilt-mods.zip. Checksums/CRC/private-data exclusions and startup evidence: artifacts/QUILT_MODS_SHA256SUMS.txt and artifacts/0.2.0/quilt-mods-checkpoint.json. Standard Setup and previous auth-fix packages remain unchanged; no installer rebuild/lifecycle test is claimed.

Updated 2026-10-28; Windows 11 x64; .NET SDK 10.0.403; Avalonia 12.1.4.

| Check | Result | Evidence |
| --- | --- | --- |
| Release solution build | PASS: 0 errors, 0 warnings | artifacts/0.2.0/forge-metadata-build.log |
| Automatic tests | PASS: 281 passed, 0 failed, 0 skipped/not-run | artifacts/0.2.0/forge-metadata-full.log; tests/forge-metadata-full.trx |
| Previous Quilt preview self-contained publish/native and Portable startup | PASS; separate quilt-mods preview; no installer update | artifacts/0.2.0/quilt-mods-publish-retry.log; quilt-mods-native-smoke.log; quilt-mods-portable-smoke.log |
| Native Mods UI/local initialization | PASS: code 0, page=Mods, initialized=True | artifacts/0.2.0/native-mods-smoke.log; .local/live-modrinth-1793091630115/gui-smoke.txt |
| Mods hidden for Vanilla/no profile; return Home | PASS for all four modded loader kinds versus Vanilla | DesktopTests.ModsNavigationIsHiddenForVanillaAndReturnsHomeWhenSwitching |
| Real Modrinth filtered search + recursive dependency selection | PASS: Reese's Sodium Options -> Sodium | artifacts/0.2.0/live-modrinth.log |
| Real verified JAR download + embedded Fabric metadata | PASS: 2 outer JARs, six Sodium embedded modules | live-modrinth.log; EmbeddedFabricTests |
| Offline Modrinth install into second profile | PASS with NetworkPolicy.Offline=true; unit test asserts no additional HTTP calls | live-modrinth.log; ModrinthIntegrationTests |
| Latest rebuilt Setup lifecycle | BLOCKED before installation: WinError 5 / registry Access is denied | artifacts/0.2.0/installer-lifecycle.log; installer-lifecycle-blocked.json |
| Prior Fabric-payload installer lifecycle | Retained PASS: 5 scenarios | artifacts/0.2.0/installer-lifecycle-20261027-100828-8ec007.json |
| External SQLite reader prevents partial cleanup | PASS: synthetic world retained on failure | Uninstaller020Tests.LockedDatabasePreventsAnyDestructiveCleanup |
| Real Temurin Java 21 x64 install/offline reuse | Prior PASS retained | artifacts/0.2.0/live-java.log |
| Pre-auth-fix Inno Setup compilation | Retained PASS: actual unsigned Setup EXE | artifacts/0.2.0/quilt-installer-compile.log |
| Pre-auth-fix Portable execution / archive integrity | Retained PASS: extracted current portable exit 0; SHA256/CRC/exclusions valid | final-artifact-verification.json/log |
| Full Microsoft login/refresh/ownership e2e | FAIL/BLOCKED: current Minecraft Services HTTP 403 after browser OAuth | User screenshot; exact endpoint/reason not provided; prior Xbox 400 retained as historical evidence |
| Actual Minecraft online/offline game/world launch | NOT RUN | Licensed authentication test pending |
| Fabric version picker / inherited launch preparation | PASS: 3969 files, 79 classpath entries; offline repair and synthetic save preserved | live-fabric.log; FabricInstallerTests |
| Native offline Fabric picker | PASS: initialized=True, fabric=0.20.1, operation=True | native-fabric-smoke.log |
| Combined Fabric + Modrinth integration | NOT VERIFIED: failed with DNS resolution error for api.modrinth.com | live-fabric-mods.log; live-fabric-mods.status.json |
| Actual Fabric/multi-loader game launch | NOT RUN | Fabric/Quilt prepared; licensed game tests pending; Forge/NeoForge not installed |
| Vanilla 1.21.1 install/repair/offline reinstall | Retained 0.1 PASS: 3960 files | Historical integration evidence; not a game-launch test |

## Installer scope

The latest Setup EXE was actually compiled with Inno Setup. Its lifecycle retry stopped at a read-only registry preflight with PermissionError / WinError 5 before creating a test installation. Do not report the new payload as lifecycle-tested. The script now emits an explicit blocked record and refuses to skip the ownership/pre-existing-installation check. No registry permissions, system settings or existing installation were changed.

The following scope describes the retained earlier passing report installer-lifecycle-20261027-100828-8ec007.json. All app/data paths were private .local/installer-lifecycle-* directories. Test profile, SQLite settings, a synthetic world sentinel, synthetic backup and unrelated root note were checked. Default uninstall retained data. Explicit cleanup removed owned data and preserved the unknown note. Installed Apps registration and app removal were checked.

The older Setup was compiled from the 0.1.0 published payload with the current recipe. Interactive wizard language/desktop shortcut behavior and a real user installation were not exercised. No real user worlds were used.

## Modrinth scope

Automatic cases cover transitive dependencies, exact conflicts, backtracking to an older compatible version, cycles, existing-mod reuse, incompatibility, unknown file-only dependencies, wrong MC/loader rejection, explicit confirmation, changed-plan refusal, corrupted downloads, traversal/unapproved hosts and cold offline cache behavior.

A live run downloaded actual Modrinth files, inspected them without executing Java code, installed them, identified the installed hashes and installed the same set into another profile with network policy Offline. It did not run Minecraft or prove in-game mod compatibility.

Nested JAR tests check provided dependencies, constraints of embedded modules and traversal rejection. Current bounds: depth 4, at most 64 nested archives, 128 MiB total nested bytes, 32 MiB per nested archive, 1 MiB metadata.

Tests never use real user worlds or Microsoft credentials. Compiling code does not establish full game/authentication readiness.

## Fabric checkpoint

113 tests pass, including hashes/sidecars, offline reuse, safe coordinates/hosts, inheritance mismatch, corrupt-download receipt preservation, active-profile locks, Java minimum and old Vanilla receipt compatibility. Real 1.21.1/Fabric 0.20.1 installation and offline repair passed; the loader main class was found statically in its JAR. Minecraft was never executed. Combined Fabric + Modrinth remains unverified due DNS. This prior checkpoint was resumed by the user.

## Installed-mod offline readiness checkpoint

160/160 automatic tests pass, 0 failed/0 skipped; Release build 0 warnings/0 errors. InstalledModReadinessTests cover persistent hashes, tampering/missing files, duplicates, ranges/breaks, server-only dependencies, corrupt/traversal inventory, unsupported schema, incomplete transactions, explicit/stale/foreign confirmations, reconciliation, active-profile locks, committed recovery, all-target preflight, loader mismatch, cancellation and auth preservation. FabricVersionConstraintTests add 23 cases including the real nested Sodium requirement >=1.15-alpha.19.39.a, ordered/empty prereleases, build metadata, caret/tilde ceilings and extended numeric components. DesktopTests exercise the real review XAML and acceptance gate.

Real cache-only combined run: live-mods-offline.log and live-mods-offline.status.json, exit 0. Minecraft 1.21.1 / Fabric 0.20.1: 3969 verified Minecraft files, 2 verified actual JARs (Reese’s Sodium Options + Sodium), dependency metadata from prior verified cache, 0 HTTP calls using a deny-network handler, synthetic save unchanged. Full readiness had only account.missing and Ready=false. The test does not fabricate an account, bypass ownership or execute Minecraft.

Fresh online combined retry still failed because api.modrinth.com did not resolve; live-fabric-mods.log/status retain that separate failure. This is not an online integration PASS. The new offline check needed a parser correction for an actual nested Fabric prerelease range, covered by regression tests before its successful rerun.

Native review-dialog smoke opened real Avalonia and rendered the current JAR/hash list; explicit acceptance starts unchecked/disabled. See native-mod-baseline-smoke.log and ui-mod-baseline.png. User-owned worlds and credentials were never used.

## Local-JAR and combined-cart checkpoint

Release source: 178/178 tests, 0 failed/0 skipped, build 0 errors/0 warnings. Evidence: local-deps-build.log, local-deps-tests.log and tests/local-deps.trx. LocalDependencyTests adds 14 backend cases: exact secondary published file pinning without redownload, recursive dependencies, unchanged original inputs, one durable transaction/inventory, confirmations, stale source/installed-set rejection, unknown IDs without slug guessing, cold/warm offline caches, corrupted download refusal, final actual-JAR identity checks, mixed local/remote roots, duplicates, MC/pin conflicts, usage locks and installed reuse. The new DesktopTests real-XAML workflow verifies one cart, offline local imports, invalidation of both confirmations and explicit install.

An initial real-XAML test exposed initial theme selection starting an unnecessary asynchronous save; initial theme/language/account selection events now avoid that operation. Normal user changes remain wired.

Real cached local-JAR integration: live-local-deps-offline.log/status.json, exit 0. A fresh isolated profile reused an actual prepared Fabric installation receipt backed by the shared cache; full readiness reverified 3969 Minecraft files. The actual selected Reese’s Sodium Options mc1.21.1-2.2.4+fabric was identified by SHA512; Sodium mc1.21.1-0.8.13-fabric was resolved/installed from verified cache. Two installed mod hashes and nested metadata passed readiness. Original local JAR unchanged, synthetic save unchanged, metadataFromCache=true, 0 HTTP calls enforced by a deny-network handler. Full readiness correctly remained false with only account.missing; Minecraft was not executed.

The first real fixture attempt selected an old Fabric-labeled profile with a Vanilla receipt and was correctly rejected by readiness. Fixture selection was corrected to require an actual Fabric receipt with loader/version metadata; no production guard was relaxed. The successful rerun above is the current result.

Fresh online integration was not retried in this block; its last recorded attempt failed DNS. Microsoft interactive login/entitlement and real online/offline gameplay remain NOT RUN. Latest Setup lifecycle remains unverified due registry permissions; no bypass or installation retry was performed in this block.

Native current combined-cart Mods view: native-combined-cart-smoke.log, exit 0 with initialized=True / operation=True and ui-combined-cart.png reviewed. This complements the headless interactive cart test; it is not a game-launch test.

## Pre-download compatibility diagnostics

178/178 tests pass. typed-compat-build.log: 0 errors/warnings; typed-compat-tests.log and tests/typed-compat.trx record the run. Regression tests confirm a definite Minecraft mismatch and wrong loader block before any dependency download, while an unresolved local dependency can be satisfied by an actual downloaded JAR. Existing real cached integration evidence predates this change; no new gameplay/authentication test was run.

## Optional selection and folder-navigation checkpoint

190/190 tests pass; optional-build.log reports 0 errors/warnings. Evidence: optional-tests.log and tests/optional.trx. Nine new resolver cases test opt-in behavior, exact version-only selection, required descendants, orphan pruning, required/shared reuse, conflicts with recoverable choices, file-only unknown identity, stale parent keys, incompatible MC/installed pins, missing metadata and cycles. Added a combined local/optional install and warm offline cache test, real Avalonia checkbox/reapply test, and active-profile folder routing/Vanilla guard test.

Real metadata integration: live-optional-offline.log/status.json PASS. Actual cached Reese’s Sodium Options mc1.21.1-2.2.4+fabric exposes two optional components. Selecting an uncached component blocks offline; the choice remains visible; clearing restores the base plan. Deny-network handler: 0 HTTP calls; no profile mods copied; gameLaunched=false. This is not a successful live optional-JAR download or game-compatibility test.

Native optional view: native-optional-smoke.log, exit 0, initialized=True, operation=True, optional=2; ui-optional.png reviewed. Folder-button path selection is automatic-test verified; interactive Explorer opening is NOT RUN. Home checklist and result template are included, with every user manual case initially NOT RUN. Current Setup lifecycle remains blocked by registry permissions; no blocked-operation retry or Microsoft login was performed.

## Quilt checkpoint

Release solution build: 0 errors/warnings; 203/203 tests pass, none skipped. Evidence: quilt-final-build.log, quilt-final-tests.log, tests/quilt-final.trx. QuiltInstallerTests has 13 cases covering numeric ordering, explicit prereleases, main-class/classfile Java checks, official Maven origins, exact selected-loader coordinates, inherited version matching, hash rejection, receipt preservation, locks, cold offline refusal, cached reuse and SHA256-sidecar fallback. DesktopTests covers Quilt-panel visibility and selection reset.

| Quilt check | Result | Evidence under artifacts/0.2.0 |
| --- | --- | --- |
| Direct native online installation via restricted exec | FAILED: metadata DNS resolution; retained separately | live-quilt.log; live-quilt.status.json |
| Official HTTPS fixture preparation via Node context | PASS: 11 actual libraries with verified SHA512/SHA256; not a production fallback | quilt-fixture-preparation.log; quilt-official-fixture.json |
| C# install/repair using those official cached artifacts | PASS: 3972 files, 82 classpath entries, Java 21, zero HTTP calls; pinned version and synthetic save preserved | live-quilt-offline.log; live-quilt-offline.status.json |
| Fresh C# HTTP catalog, empty metadata cache | PASS: 307 versions; latest numeric release 0.30.1 | quilt-native-metadata.log |
| Actual C# online library repair | PASS: real Quilt 0.30.1 loader downloaded and hash verified; synthetic save unchanged | quilt-native-repair.log |
| Native Avalonia Quilt picker | PASS: initialized=True, quilt=0.30.1, operation=True; render reviewed | native-quilt-smoke.log; ui-quilt.png |
| Actual Quilt game launch/mod metadata | NOT RUN / not implemented respectively | Manual licensed account/game test pending; MODS-06 remains Not Implemented |

The network results differ by execution context: direct exec DNS failed, while native .NET HTTP launched through Node succeeded. No DNS/proxy configuration was changed. The fixture helper seeded only a private workspace cache from fixed official HTTPS sources; the later C# repair independently exercised the production downloader. The full clean native-online installation scenario remains unverified. Full readiness in the cache test correctly reported account.missing; no offline identity or ownership bypass was supplied.

No real user worlds, Microsoft credentials or running-game sessions were used. All gameLaunched flags remain false. Current Setup lifecycle was not retried after the earlier registry denial; compiling the updated Setup does not verify installation/upgrade/uninstall.

## Authentication HTTP 400 and prominent sign-in button (2026-10-28)

The owner supplied actual Entra/launcher screenshots: personal-account app registered, desktop http://localhost redirect saved, Microsoft browser completion followed by HTTP 400 from user.auth.xboxlive.com. The exact failed service is known; no real token, callback URI or password was collected.

Five new protocol/error cases verify exact Xbox/XSTS JSON casing and contract headers, unchanged Minecraft identityToken casing, per-request header scope, and logs containing only the host/HTTP status without synthetic request/response secrets. The additional actual-Avalonia case checks the visible Sign in buttons, direct command binding, busy disabling, default application ID/saved override, synthetic DPAPI legacy-account display and targeted logout. This UI case exposed and now verifies the correction of SecretVault sync-over-async blocking on the UI context.

Final source suite: **209/209 passed, 0 failed/skipped/not-run**; build **0 errors/warnings**. auth-fix-tests-direct.log and tests/auth-fix-direct.trx are authoritative. The earlier VSTest attempt aborted because its data collector could not connect in 90 seconds; it is not a failed/passing test-case run. The bundled xUnit v3 in-process runner executed the entire unchanged test selection. Initial busy-state regression evidence is retained in auth-header-initial-failure.log/trx; the corrected UI test passes in auth-header-test.log.

Separate current Windows payload: artifacts/0.2.0/auth-fix-win-x64. Its native smoke uses a private empty .local/auth-fix-smoke data root and never signs in or launches Minecraft. Standard Setup/Portable packages remain the older payload until a subsequent rebuild. A compiled hotfix and simulated protocol tests do not prove the real account/API chain passes; user retest is still required.


Final local verification for the localization/sign-in-stage block: Release build 0 errors, 0 warnings; 288/288 tests passed, no failures/errors/skips/not-run. Includes 7 new expanded cases in LocalizationTests. No real authentication/gameplay or current installer lifecycle is claimed.
