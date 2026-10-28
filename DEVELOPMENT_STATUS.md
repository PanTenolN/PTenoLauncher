# PTenoLauncher 0.2.0 — Development Preview

## Final Java-format guard - 2026-10-28

Added a regression guard against reordering unnumbered Java printf placeholders (for example %s then %d). Numbered placeholders can still be reordered safely. Final local verification: **320/320 tests passed**, zero failures/errors/skips/not-run; Release build 0 errors/warnings. Evidence: artifacts/0.2.0/online-ai-placeholder-build.log; online-ai-placeholder-tests.log; tests/online-ai-placeholder.trx. Prior 319-case checkpoint/native publish and GitHub run 40758215281 succeeded; the new guard requires its own subsequent CI/prerelease. No real OpenAI/Minecraft or installer-lifecycle test is claimed.

## Eight languages and optional online AI - 2026-10-28

Development resumed and the interrupted eight-language block was recovered. Eight selectable offline UI catalogs (uk/en/pl/de/es/fr/pt-BR/ru) now cover 230 keys each, including AI controls and major offline readiness findings. Catalog completeness/format validation, actual picker switching and SQLite reload pass. Low-level mod/security diagnostics, some already-formatted live statuses and native-speaker/DPI review remain incomplete; this is not full application-wide localization.

Implemented the optional OpenAI Responses client (fixed HTTPS endpoint, store=false, bounded output, no tools/retries), protected per-user API key configuration, assistant and selected-log analysis, modern JAR language import, selected-namespace and bulk translation, validated batches with atomic resumption, local manual editor, incremental source update handling and profile-owned resource-pack generation. AI off cancels provider operations and keeps local translation editing/export available. No local model or Ollama. See docs/ONLINE_AI.md for usage and limits.

Verification: Release build 0 errors/warnings; **319/319 tests passed**, 0 failed/errors/skipped/not-run. Logs: artifacts/0.2.0/online-ai-final-build.log; online-ai-final-tests.log; tests/online-ai-final.trx. Prior eight-language-only suite: 305/305 passed. OpenAI tests use synthetic HTTP/JAR/profile fixtures, not paid live API requests. Existing mod/core/auth/deletion tests remain passing. Real OpenAI account/model/translation-quality and in-game resource-pack tests are NOT VERIFIED. Minecraft application approval, licensed gameplay and latest installer lifecycle remain pending.

Resource packs currently require an explicit numeric pack_format. Legacy .lang, nested JAR language resources and arbitrary component-valued language JSON are unsupported. The editor is JSON-based; no auto-activation or game-file modification is claimed. Missing/unreviewed entries are reported and excluded from the generated pack. Ownership registration recognizes only the selected profile's fixed generated-pack paths; deletion preview remains safe.

All 184 approved requirements are retained. GitHub source/release results must be checked for this exact new commit; earlier binaries do not include these changes. Development disclosure remains prominent: created and developed with AI assistance (OpenAI Codex).

## Localization and sign-in diagnostics - 2026-10-28

Development resumed at the owner's request. Added 94 Ukrainian/English catalog messages, localized ViewModel actions/statuses, picker/dialog titles, snapshots, profile/mod counts and major readiness causes. Language switching refreshes computed labels and persisted language is explicitly applied during initialization. Original user data and game console content are not translated. Authentication failures now include a fixed enum stage; no response bodies, tokens or full URLs are logged. See docs/MICROSOFT_REVIEW_FOLLOWUP.md.

Local build/test evidence is recorded in artifacts/0.2.0/localization-final-build.log, localization-tests.log and tests/localization.trx. The previous source checkpoint's Windows runner and prerelease publication both succeeded (run 40728874523, commit 92998deeb0907798a02fef7404f98c5a1e58c0a7). Actual Setup, Portable, Source ZIP and SHA256 sums are public; current localization changes require their own new build. Current installer lifecycle/gameplay remain unverified.

Localization is still PARTIAL: low-level mod/security diagnostics, previously formatted dynamic messages after a live language change, theme names and exhaustive linguistic/accessibility review remain. UI-07/UI-08 must not be promoted to complete. No additional language is claimed. All 184 requirements are preserved. Microsoft review submission is owner-reported; approval remains pending. A scoped connected-mail search for PTenoLauncher or its exact Client ID returned no messages; this does not prove that no approval email exists.

## Publication and localization - 2026-10-28

Prepared browsable source publication and a Windows workflow that tests/builds/verifies packages before publishing a uniquely tagged prerelease. Remote workflow/release execution is not yet verified. Ukrainian/English remain partial; docs/LOCALIZATION.md lists missing coverage. No additional language or complete localization is claimed. Microsoft review submission was reported by the owner; approval is pending.

## Microsoft AppID review update - 2026-10-28

The owner reports that the application review request has been submitted. Approval is pending; Minecraft Services access and full licensed sign-in/gameplay are not verified. The latest observed service response remains HTTP 403. This documentation-only update does not change code, the previous 281-test result or distributed binaries.

## Forge/NeoForge metadata pause checkpoint - 2026-10-28

Development is **PAUSED at the user request before powering off the laptop**. No build/test/integration is left running. Source is saved in the existing workspace and artifacts/PTenoLauncher-Source-0.2.0-forge-metadata.zip. Existing Quilt/auth-fix executables, Portable and Setup remain previous payloads; this block has not republished or installed them.

Forge/NeoForge native root TOML metadata is partially implemented: multi-mod JAR identities/versions, dependency types/sides, Java features, numeric Maven intervals/unions, main-manifest version substitution and BEFORE/AFTER cycle detection. Integrated with the existing local cart, transaction/inventory and installed-mod readiness. No JAR code is executed. See docs/FORGE_MOD_METADATA.md.

Release build: **0 errors, 0 warnings**. Full source suite: **281/281 passed**, 0 failed/skipped/not-run, including 40 new ForgeModMetadataTests cases. Logs: artifacts/0.2.0/forge-metadata-build.log; forge-metadata-targeted.log; forge-metadata-full.log; tests/forge-metadata-full.trx. xUnit v3 direct runner used; no real game launch.

Forge/NeoForge loader installation, Jar-in-Jar selection, Maven qualifier ordering and real mod/game integration remain pending. NeoForge FML version is independently versioned and remains Unknown until actual installer metadata is available; it is never inferred from the NeoForge version. Unsupported metadata cannot establish Ready.

Tomlyn 2.10.1 is pinned in project/lock files; downloaded package SHA-512 matches the official NuGet catalog. Native NuGet restore/audit was network-blocked (NU1301/NU1900). A workspace-only feed/existing cache was used, with NuGetAudit=false for that isolated restore invocation only; the repository default remains NuGetAudit=true. A live vulnerability audit is NOT VERIFIED. See tomlyn-package-verification.json and docs/FORGE_MOD_METADATA.md.

Resume from this source checkpoint. Next independent work: actual Forge/NeoForge installer/catalog and FML metadata, complete nested candidate handling, then the retained roadmap. Microsoft Services HTTP 403/manual licensed gameplay remains external. Preserve all 184 requirements; do not recreate the project, bypass auth, use real worlds or retry the blocked installer registry guard.

## Retained earlier checkpoints

## Quilt mod metadata checkpoint - 2026-10-28

Native Quilt schema-1 identity, depends/breaks, optional/unless, alternative groups, qualified/provided IDs and separate Quilt version rules are now integrated with cart review and offline installed-mod readiness. Conditional nested candidate selection, plugins/mappings and unsupported constraints remain Unknown. Fabric-on-Quilt/QSL/Sinytra are not claimed. See docs/QUILT_MOD_METADATA.md.

Release solution build: 0 errors/warnings. Full source suite: **241/241 tests passed**, 0 failed/skipped/not-run, including 32 new Quilt metadata cases. Evidence: artifacts/0.2.0/quilt-mods-final-build.log; quilt-mods-final-tests.log; tests/quilt-mods-final.trx. xUnit v3 direct in-process runner is used because the previously recorded VSTest collector handshake failed before cases.

The opt-in real OK Zoomer/Modrinth check is BLOCKED by unreachable api.modrinth.com (SocketException 10051), before a real mod download. Synthetic JAR test success is not real-artifact/gameplay evidence. No system/proxy/DNS settings were changed.

User retest of the auth-fix payload completed browser OAuth, then failed with HTTP 403 from api.minecraftservices.com. The screenshot identifies the host/status but not the exact endpoint or safe server reason. An application-service access/registration issue is possible, not proven. Full entitlement/profile/refresh and online/offline gameplay remain unverified. No new application, borrowed Client ID or authentication bypass was used.

Separate self-contained Windows publish and freshly extracted Portable startup: PASS. Preview artifacts: artifacts/0.2.0/quilt-mods-win-x64, PTenoLauncher-Portable-0.2.0-quilt-mods-win-x64.zip and PTenoLauncher-Source-0.2.0-quilt-mods.zip. Checksums/CRC/private-data exclusions and startup evidence: artifacts/QUILT_MODS_SHA256SUMS.txt and artifacts/0.2.0/quilt-mods-checkpoint.json. Standard Setup and previous auth-fix packages remain unchanged; no installer rebuild/lifecycle test is claimed.

Updated 2026-10-28. Existing five-project architecture preserved. This is an intermediate preview, not completion of the entire roadmap.

## Latest authentication/UI checkpoint

The owner has registered the application and completed the real Microsoft browser step. The previous payload then failed at Xbox user authentication with HTTP 400. The request now preserves exact protocol field names and includes the Xbox contract header. A UI-thread DPAPI atomic-save deadlock is fixed. The visible **Виконати вхід** button starts official sign-in from the header without finding Settings; the owner's public application ID is provided by default, with saved advanced overrides retained.

Final source verification: 209/209 tests, 0 build warnings/errors. Separate self-contained retest build: artifacts/0.2.0/auth-fix-win-x64. See docs/AUTHENTICATION_FIX.md. The standard Setup/Portable still contain the earlier Quilt payload and have not been rebuilt for this auth fix. Full Xbox/XSTS/Minecraft sign-in after the fix and actual game launch remain pending the user's retest.

## Retained verification and current source tests

- Release build: 0 errors, 0 warnings. Automatic tests: **209 passed, 0 failed, 0 skipped**.
- Prior Fabric-payload installer lifecycle: **5/5 passed** in isolated workspace paths. The latest rebuilt Setup lifecycle is **BLOCKED** at registry preflight (WinError 5 / Access is denied); installation did not begin. Install, upgrade from the 0.1.0 published payload, reinstall, default uninstall preserving synthetic profiles/settings/world files/backups, and explicit owned-data cleanup preserving an unrelated root file.
- The test 0.1.0 installer was generated from the retained 0.1.0 payload using the current installer recipe; it is not an independently archived historical Setup.
- SQLite cleanup now acquires a database file lease before deleting owned directories. An external database reader blocks cleanup before any worlds are deleted.
- Real Modrinth search, version metadata, recursive required-dependency selection, SHA512-verified downloads, installed-file identification and a second profile installation with network policy Offline succeeded.
- Live pair: Reese's Sodium Options + Sodium for Minecraft 1.21.1 / Fabric. Sodium's six embedded Fabric modules are inspected without execution/extraction.
- Native Mods-page UI smoke: initialized=True, page=Mods, local startup without network requests. Render reviewed.
- Vanilla profiles hide the Mods navigation and mod count. Switching from an open Mods page to Vanilla returns Home; direct navigation is guarded. Tested for Fabric/Forge/NeoForge/Quilt versus Vanilla and no-profile state.
- Self-contained Windows publish and Inno Setup compilation succeed. Current Setup is compiled, but its full installation lifecycle cannot be confirmed in this restricted session. Portable execution, SHA256 and archive CRC checks passed. Final package verification is recorded in TEST_REPORT.md and artifacts/0.2.0 logs.

## Fabric block — retained verified checkpoint

Official Fabric catalog/version picker, pinned installation receipt, SHA512 Maven library verification, bounded static JAR inspection, inherited classpath/arguments and offline repair are implemented.
Real 1.21.1 + Fabric 0.20.1 verification: 3969 files, 79 classpath entries, Java requirement 21; offline repair and synthetic save preservation passed. Native offline version-picker smoke passed. FabricInstallerTests and the full 160-test suite pass.
A combined Fabric + Modrinth integration run reached Fabric installation/repair but failed on DNS resolution of api.modrinth.com. It remains unverified; no game was launched.
See docs/FABRIC.md and artifacts/0.2.0/live-fabric*.log.

## Installed-mod offline readiness block

- Persistent per-profile SHA-256/size inventory is written after a committed local or Modrinth installation and reconstructed from committed journals after interruption. Prepared journals preflight every target before rollback.
- The readiness/launch path now inspects installed JARs without network access or code execution: hashes, missing files, loader mismatch, duplicate root IDs, client/server environment, required dependencies, version ranges and breaks. Embedded Fabric modules and provided IDs participate.
- Unknown schema, unsupported formats/ranges, ambiguous bundled versions, untracked files and nested mods directories remain explicitly unknown. No automatic trust baseline is created for older/manual installations.
- A Ukrainian/English modal lists current JAR paths, sizes and SHA-256 plus missing baseline entries. Explicit confirmation records a local integrity baseline after rescanning; a stale preview is rejected. It does not establish provenance or antivirus safety and does not modify JAR/world files.
- Fabric predicates support ordered/empty prereleases, build metadata, major caret ceilings including 0.x, minor tilde ceilings, AND/OR and trailing wildcards. Unsupported inputs remain unknown.
- Fresh online Fabric + Modrinth retry still failed at api.modrinth.com DNS. Cache-only real integration passed: Minecraft 1.21.1/Fabric 0.20.1, 3969 verified Minecraft files, 2 verified mods, metadataFromCache=true, zero HTTP requests, synthetic save preserved. Full readiness correctly blocked only on account.missing; gameLaunched=false.

## Local-JAR dependency and combined-cart block

- Explicit dependency resolution identifies selected local JARs with SHA512 using the official Modrinth version-file endpoint. Only hashes are sent, never JAR bodies. Import/drag-and-drop itself stays local.
- Exact local identity seeds the existing deterministic resolver, including secondary files from a published version, so a local root is neither silently upgraded nor downloaded again. Required transitive dependencies and explicit remote roots share one plan. Optional dependencies remain separate.
- Source hashes and the installed-set fingerprint are checked before downloads and again before commit. Actual local/dependency JAR metadata is reviewed together before one durable installation transaction and inventory commit. Failed hashes or real Mod-ID mismatches leave the profile unchanged.
- The Mods page now uses one installation cart and confirmation flow. Cart changes invalidate the plan and both confirmations. Unknown files retain local metadata and require explicit internal-warning acceptance; missing identities never become guessed project slugs.
- Warm metadata/file caches support the combined workflow offline; cold unknown files with complete local dependencies can still be installed after confirmation. Unknown missing dependencies stay unresolved.
- 14 new backend tests plus one real-XAML workflow test pass. Fixed initial ComboBox selection events starting unnecessary preference/account operations when opening an initialized view.
- Real-artifact cache-only verification passed: exact local Reese’s Sodium Options -> Sodium, 3969 Minecraft files and 2 mods verified, source JAR/synthetic save unchanged, 0 HTTP calls, account.missing/Ready=false as expected. Fresh online integration was not retried in this block.

## Pre-download compatibility diagnostics

Definite duplicate files/IDs, incompatible loader and known Minecraft/runtime constraints now block a combined cart before dependency downloads. Missing or uncertain dependency-version relationships remain preliminary when actual catalog JARs are still unavailable; all actual JARs are reviewed before profile mutation. Three regression tests pass; the full suite is 178/178. No Minecraft gameplay or Microsoft login was performed.

## Optional selection, mods folder and home verification block

- Modrinth optional components now have explicit checkboxes, Apply selection and Clear optional selection. Editing choices invalidates the install plan and confirmations; no network starts until apply. Nested selected components resolve required dependencies, pin their exact parents, reject conflicts and prune orphan choices. Unchecking never removes installed mods.
- Nine resolver cases, one cache/transaction integration case, one actual-XAML checkbox case and one selected-profile folder case pass. Full suite: 190/190; build: 0 warnings/errors.
- Real cached optional metadata check passed with two optional components: selecting an uncached child blocked offline, retained the choice, and clearing restored the base plan. Zero HTTP calls; no profile mod copies; no game execution.
- Mods now includes Перейти до теки / Open mods folder, targeting only the selected profile’s mods directory. Path routing and Vanilla guard are tested; actual interactive Explorer opening remains a home manual test.
- docs/HOME_TEST_CHECKLIST.md and HOME_TEST_RESULTS.md provide concrete user steps for licensed Microsoft/Vanilla/Fabric/offline gameplay, DnD, folder navigation and isolated installer validation. All manual results start NOT RUN.

## Quilt Loader checkpoint

- Added the official Quilt v3 catalog and version picker, release/prerelease distinction, explicit version pinning, inherited Vanilla launch preparation and verified official Quilt/Fabric Maven libraries. The API has no stable flag and its ordering is not chronological; releases are sorted numerically and prereleases require an explicit choice.
- Static bounded JAR inspection validates the loader main class and classfile Java requirements without executing code. Mojang authentication, assets and natives are retained. Active-profile/world locks, path/origin checks, SHA512/SHA256 and atomic receipts remain enforced.
- Fresh native .NET catalog fetch passed: 307 versions, latest numeric release 0.30.1. Actual native .NET online repair downloaded the real 0.30.1 loader, verified its hash and preserved the synthetic world sentinel.
- Official-artifact cache-only install and repeated repair passed: Minecraft 1.21.1 / Quilt 0.30.1, 3972 files, 82 classpath entries, Java 21, zero HTTP requests; installed pin and synthetic save preserved. Full readiness correctly remains blocked by account.missing. Official artifacts were initially staged by the opt-in Python fixture helper, so this is not a full clean native-online installation test.
- Direct online integration through the restricted exec context failed DNS. Native .NET HTTP via the Node execution context succeeded; no DNS/proxy/system settings were changed. Both results are retained separately.
- Native Quilt picker opened with real cached metadata, initialized=True, quilt=0.30.1; screenshot reviewed. Final Release build has 0 warnings/errors and 203/203 tests pass, including 13 Quilt cases.
- Quilt mod dependency metadata, modded Quilt gameplay and unmodded Quilt gameplay remain unverified/unimplemented as applicable. No automatic Fabric-on-Quilt bridge, QSL installation or Sinytra support is claimed. See docs/QUILT.md and TEST_REPORT.md.

## Working components

Graphite UI and modal profile editor; five live themes; Ukrainian/English primary resources; SQLite settings/profiles/history; archive/unarchive; safe permanent deletion with preview/name confirmation/ownership/usage checks and recovery; backup ownership tracking; default-off AI gate/cancellation; managed Temurin Java; official MSAL/Xbox/XSTS/Minecraft code with DPAPI multi-account storage; bounded/redacted launch console; release/snapshot/history catalog with online refresh-on-open and offline cache.

New Modrinth module: filtered search, version choice, recursive bounded deterministic backtracking, exact pins, duplicate/installed checks, incompatible dependencies, separate optional list, automatic required entries with reason/size/source, user confirmation, verified CDN downloads, stale-plan rejection and existing transaction journal/recovery. No AI dependency or API key is needed for this public catalog flow.

Fabric JAR inspection now includes declared embedded JARs and provided IDs. Nested dependency constraints remain checked. Unsafe paths and bounded-archive violations are rejected.

## Remaining gaps

- Microsoft browser sign-in: observed PASS; old Xbox exchange: FAIL HTTP 400. Full post-fix sign-in/entitlement/refresh/multi-account and actual online/offline game launch remain unverified; own application is registered but downstream API access still needs verification; see MICROSOFT_AUTH_SETUP.md.
- Fabric installation/launch preparation is implemented; real gameplay remains unverified. Quilt installation/launch preparation is now verified within the scope below; Forge/NeoForge installation is still pending.
- Full cross-provider resolution and recovery of dependencies from unidentified local JARs remain partial. Hash-identified Modrinth local JARs now resolve exact dependencies. CurseForge needs permitted API access. Forge/NeoForge/Quilt metadata/ranges remain pending.
- Unknown local JAR identities or unsupported ranges are explicitly unknown; dependencies are not guessed.
- Local and Modrinth selections share a plan/transaction/UI cart; typed per-row failure previews and other-provider optional selection need improvement. Modrinth optional checkboxes are implemented.
- Java 8/17/25 live tests, full non-Fabric mod-aware Offline Readiness, legacy virtual/map-to-resources assets, full English localization, DPI/accessibility coverage and interactive bilingual installer/desktop shortcut checks remain pending.
- Installer/app are unsigned. Full packaged-profile relocation and an external GitHub Actions run have not been verified.
- Modpacks, world/resourcepack/shader managers, optional online AI translation, security scanner/Defender/threat feed, conflict finder, Server Manager/Porthole/Aternos, plugins and updater remain in the roadmap.

All approved requirements, including Vanilla navigation, are retained in the 184-row REQUIREMENTS_TRACEABILITY.md.

## Next work

1. Retry fresh online Fabric + Modrinth integration when DNS is restored; the real cache-only combined installation/readiness check passed.
2. Continue typed local compatibility previews, other-provider optional handling, Quilt mod metadata and Forge/NeoForge metadata/installers. Keep Sinytra and broader cross-loader bridging deferred.
3. Complete official app registration and manual licensed Vanilla online/offline launch.
4. Continue docs/ROADMAP.md without dropping remaining requirements.

Development resumed on user request for the installed-mod offline-readiness block. Sinytra Connector future requirements remain Not Implemented and are retained in the roadmap.

Offline scope clarification: adding an existing local JAR or verified cached file does not download a new mod from the internet. Missing uncached files/dependencies require online access. Static checks of installed mods remain useful before offline gameplay. Further development priority is core licensed launch/authentication and native loader support; no additional offline-install subsystem is planned for this block.


Final local verification for the localization/sign-in-stage block: Release build 0 errors, 0 warnings; 288/288 tests passed, no failures/errors/skips/not-run. Includes 7 new expanded cases in LocalizationTests. No real authentication/gameplay or current installer lifecycle is claimed.


Native online-AI checkpoint: self-contained win-x64 publish PASS (online-ai-publish.log); isolated native startup PASS, initialized=True, code 0 (.local/online-ai-native/gui-smoke.txt; online-ai-native-smoke.log). This verifies launcher startup, not live AI/Minecraft or installer lifecycle.
