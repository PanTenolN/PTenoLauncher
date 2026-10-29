# PTenoLauncher Roadmap

## Managed Java integrity and runtime verification - 2026-10-29

Restored all 27 newer first-run-language source/report files from main 21eefdd57b2a95c786ea7ecd1601a8925eb85d51 after Git-blob comparison (only CRLF normalization differed); previous local files were backed up. The eight-language menu and 344-test baseline are preserved.

Fixed OfflineReadiness invoking java.exe after detecting managed-runtime corruption. Added exact tracked-executable/folder identity, complete bounded inventory and hash/size checks, untracked-DLL/link rejection, strict Temurin major/platform/source validation, bounded x64-only version inspection and probe-only inherited Java-option removal. Corrupt runtime directories are retained and cached archives can repair into a new verified directory. Added explicit managed-only installation and online/offline Minecraft Java requirement lookup. See docs/JAVA_RUNTIME_VERIFICATION.md.

Local Release build: **0 errors, 0 warnings**. Full suite: **386/386 passed**, zero failed/errors/skipped/not-run, including 42 new JavaRuntimeIntegrityTests. Evidence: artifacts/0.2.0/java-runtime-release-build.log; java-runtime-tests.log; tests/java-runtime.trx. The real local runtime matrix failed before its first install because api.adoptium.net was network-unreachable (SocketException 10051). That failed/blocked evidence is retained and is NOT a successful live install.

Windows CI now gates packaging/publication on the actual managed Java 8/17/21/25 matrix: official download/hash/extraction/version, zero-HTTP offline reuse, Java 21 cached-archive corruption repair, Java 17 process-local option-isolation probe, synthetic-data preservation and cached Mojang Java metadata. Its new source commit/run/release result must be verified separately in docs/JAVA_RUNTIME_CHECKPOINT.md. All 184 original / 189 current requirements and 44 module grades remain; bridges remain DEFERRED. No Microsoft approval, Minecraft gameplay, live AI quality, installer lifecycle or stable release is claimed. Next core work: historical virtual/mapped assets/arguments and licensed gameplay after approval.

## Verified first-run language preview - 2026-10-29

Release source: `780f22642a658ddb4f8fee5bc9ccbdc795fcf3c0`. Windows CI **40827117958 SUCCESS**: https://github.com/PanTenolN/PTenoLauncher/actions/runs/40827117958 . The complete suite passed **344/344 tests**, zero failed/errors/skipped/not-run; Release build **0 errors, 0 warnings**. This includes 14 new actual-XAML/offline/SQLite/restart/preservation/write-failure cases for language onboarding. Both ZIP integrity checks, required-source/progress checks and freshly extracted self-contained Portable startup passed. Requirements guard: 184 original IDs retained, 189 current rows, 44 graded modules.

Actual prerelease: https://github.com/PanTenolN/PTenoLauncher/releases/tag/v0.2.0-preview.780f22642a65 . Setup, Portable, Source ZIP and SHA256SUMS were read back and their package hashes matched the Windows build log. The earlier Linux-only startup failures remain recorded as Windows-DPAPI platform limitations; they were not reclassified as passing Linux tests. The first-run selection feature uses 238-key catalogs across eight languages, preserves supported saved preferences and leaves authentication/readiness gates unchanged.

This supersedes older first-run notes awaiting Windows CI/publication. Interactive native language-menu/DPI review, Microsoft approval/licensed gameplay, live AI quality, Java 8/17/25 and current installer lifecycle remain unverified. Checkpoint: `docs/FIRST_RUN_LANGUAGE_CHECKPOINT.md`. Resume independent P0 Java/runtime/asset work after this completed UI block; do not recreate the project or undo the deferred-bridge decision.

## First-run language selection - 2026-10-29

Added the owner's requested language menu before the first normal launcher session. Eight native language names, immediate preview, explicit SQLite confirmation, saved-preference skipping, unsupported/case-normalized preferences and write-failure retry use the existing architecture. Cancel/exit leaves no saved preview. The hidden Settings picker cannot auto-save onboarding changes. Microsoft/AI/network access is not required; authentication and licensing are unchanged. See docs/FIRST_RUN_LANGUAGE.md.

Local Release compilation: 0 errors/warnings. Seventeen catalog/picker cases pass on Linux. The broader Linux filter ran 38 cases: 23 passed, 15 failed at the existing Windows DPAPI startup guard. This is retained as failed/platform-limited evidence, not a successful startup test. Fourteen new actual-XAML/SQLite/preservation cases are added for the full Windows runner (expected suite 344); Windows CI and a new prerelease must be verified separately in docs/FIRST_RUN_LANGUAGE_CHECKPOINT.md. Prior actual Windows preview remains source 827e8832a95baf5d2c9f805fb3c43f563427c90b, CI 40773211706, 330 passed.

UI-FIRSTLANG adds one explicit requirement: all original 184 IDs and four previous additions remain, 189 total; 44 module grades are tracked. Catalogs now contain 238 keys each. Overall localization/DPI remains partial; deferred compatibility bridges remain deferred. Next P0 core/Java/gameplay/installer and live AI checks remain as recorded in the roadmap. No current Microsoft approval, game launch or latest installer lifecycle is implied.

## Verified process-lifecycle preview - 2026-10-28

Final release source: `827e8832a95baf5d2c9f805fb3c43f563427c90b`; implementation commit: `95d7d7e7ec94dbfee0514f390f239ef35c419505`. Windows CI **40773211706 SUCCESS**: https://github.com/PanTenolN/PTenoLauncher/actions/runs/40773211706 . Release build **0 errors/warnings**, **330/330 tests passed**, 184 original / 188 current requirements and 43 graded modules validated, both ZIP CRC checks passed, fresh extracted self-contained Portable initialized successfully. `gameLaunched=false`.

Actual prerelease: https://github.com/PanTenolN/PTenoLauncher/releases/tag/v0.2.0-preview.827e8832a95b . Setup EXE, Portable ZIP, Source ZIP and SHA256SUMS were verified by release metadata; the final package checks require the progress report and continuation/source files. This supersedes older entries awaiting CI/publication. No installer lifecycle, Microsoft approval, actual Minecraft gameplay or live AI/provider-quality result is implied. Binaries remain unsigned. Detailed return point: `docs/PROCESS_LIFECYCLE_RELEASE_CHECKPOINT.md`; module grades: `PROJECT_PROGRESS_SUMMARY.md`. Next independent P0 work remains the Java 8/17/21/25 matrix, managed-runtime receipt/path integrity and historical assets/arguments.

## Critical stabilization: process lifecycle - 2026-10-28

Resumed from release source aec1846517865a0953b1037bd6b55f8ce8807efc and main documentation commit 4374db9f44bacb2fac2960b2f01a63d532b7e603; verified Windows CI 40759155278 remains the prior binary checkpoint. Local workspace is a source mirror without .git; remote main/history was checked before edits. Existing architecture and newer localization/AI work were preserved.

Fixed post-start process cancellation, process-tree cleanup, bounded inherited-console draining and cleanup after console-consumer failure. Added a default-cancel confirmation dialog, explicit unsaved-world acknowledgement and per-launch-ID stop guard. Generic operation cancellation cannot silently kill a running game. See docs/PROCESS_LIFECYCLE.md for exact behavior and limits.

Release build: **0 errors, 0 warnings**. Full suite: **330/330 passed**, no failed/errors/skipped/not-run, including 10 new process/UI cases. Evidence: artifacts/0.2.0/critical-process-final-build.log, critical-process-tests.log and tests/critical-process.trx. Real existing Java 21 x64 probe returned 0 for -version and 1 for an intentionally invalid JVM option, with zero HTTP requests (critical-java-process.log). This was not a Minecraft session.

PROJECT_PROGRESS_SUMMARY.md now gives module-specific grades and evidence using the owner's 0/25/50/75/100 scale. The complete new brief is docs/DEVELOPMENT_BRIEF_2026-10-28.md; the auth setup document is MICROSOFT_AUTH_SETUP.md at repository root. All 184 original requirement IDs are retained, with four explicit additions. Sinytra, Kilt and all listed cross-loader bridges are DEFERRED; no implementation work is authorized for them now. scripts/verify-requirements.py checks the retained IDs/statuses/grades in CI.

Actual Microsoft entitlement/profile/refresh and licensed Vanilla/Fabric/Quilt gameplay remain BLOCKED/NOT RUN. Real AI/provider quality and current installer lifecycle remain unverified. Do not retry the blocked registry guard or present synthetic process tests as gameplay. Continue the independent P0 Java 8/17/21/25 matrix, runtime receipt/path checks and historical assets/arguments; use docs/HOME_TEST_CHECKLIST.md when licensed authentication is available. New source commit, CI and actual published artifacts are recorded in docs/PROCESS_LIFECYCLE_RELEASE_CHECKPOINT.md; until verified, use the prior release rather than claiming new binaries exist.

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

## Publication and language coverage - 2026-10-28

Publish current source with the required AI-development disclosure; execute the gated Windows build/prerelease workflow and inspect actual results. Complete Ukrainian/English labels, dynamic statuses, diagnostics and language-switch verification before adding reviewed translations for further languages. See docs/LOCALIZATION.md.

## Microsoft Services review - 2026-10-28

The owner reports submitting the AppID review request. Await the decision, then retest official entitlement/profile and licensed online/offline gameplay; submission alone does not establish access or explain the observed HTTP 403. Independent roadmap work remains available when development resumes.

0.2.0 is an intermediate development preview. No approved feature is dropped; REQUIREMENTS_TRACEABILITY.md is the detailed 184-item inventory.

1. Current verified source checkpoint: 281 tests, prior isolated installer lifecycle and real Modrinth install/offline reuse. Current Setup is compiled but lifecycle retest is blocked at registry preflight by Access is denied. Finish licensed game/authentication verification and interactive distribution checks.
2. Modrinth provider, recursive required dependencies, backtracking and cached install are implemented. Hash-known local Modrinth JAR dependencies and a combined local/catalog cart are now implemented. Modrinth optional choices are now explicit and verified; continue unidentified-JAR workflows and permitted cross-provider compatibility.
3. Permitted CurseForge provider after API access; Forge/NeoForge/Quilt mod metadata and full compatibility engine.
4. Own application registration and browser OAuth are observed; retest fixed Xbox/XSTS/Minecraft exchange, verify service access/refresh/multi-account and actual Vanilla online/offline world tests.
5. Fabric and Quilt version/install/offline preparation is verified; actual licensed gameplay and Forge/NeoForge installation remain pending. Continue historical manifests and virtual/mapped assets.
6. .mrpack/ZIP/other launcher imports, shareable legal modpack exports, locking, updates and friend sync.
7. World/resourcepack/shader managers, selectable backup location, storage accounting and safe cleanup.
8. Diagnostics/performance, supported Defender/threat feed, source provenance, dependency-aware offline Conflict Finder in isolated profile copies.
9. Optional online AI assistant/translation, per-profile resourcepacks, editor/manual edits/incremental updates. No local AI models; core works with AI off.
10. Free local dedicated servers/EULA/dashboard/whitelist/backups, Share My World and supported installed Porthole CLI/UI fallback. Explicit networking permission.
11. Aternos manual official-panel checklist/copy/exports/progress/search/ping; no website bots/private APIs. No exaroton or paid hosting.
12. Real process-isolated plugin SDK/manager/permissions, authentic updater/rollback, GitHub release automation, real certificate signing, full accessibility/localization/performance/release audit.

Optional future Sinytra Connector: compatible Fabric mods on NeoForge only with exact-version official metadata/dependencies (including Forgified Fabric API and version-dependent Launchpad when required), official compatibility results, clear unknown/conflict states, isolated test profiles, catalog/cart/compatibility-engine and server-manager integration. This is not universal cross-loader support; not implemented.

Retained block: exact local-JAR Modrinth identity/dependencies and combined installation cart. Installed Fabric-mod integrity readiness and explicit baseline review remain verified. Continue core licensed launch/authentication, native loader formats, stable dependencies and distribution. Sinytra and broader cross-loader bridging are explicitly deferred.

Latest block adds Modrinth optional checkboxes/recalculation, selected-profile mods-folder navigation and a concrete home verification handoff. Actual Explorer interaction, Microsoft/game tests and current Setup lifecycle remain manual/unverified.

Latest checkpoint: Quilt official catalog/pinning/install preparation, static Java checks, real cached install/repair, fresh native C# catalog and real native loader download verified. Next independent work is Quilt mod metadata and Forge/NeoForge installers; real Microsoft/Vanilla/Fabric/Quilt gameplay awaits the user’s licensed sign-in. No roadmap entry is complete merely because a loader can be installed.

2026-10-28: exact Xbox JSON/header fix, UI-safe atomic DPAPI writes, visible header Sign in button and owner-registered public default ID are implemented; full 209-test source suite passes. Full real post-fix sign-in/gameplay awaits the owner; normal players need no separate Entra registration. Refresh standard distribution packages after the live authentication retest.


## Quilt mod metadata checkpoint - 2026-10-28

Native Quilt schema-1 identity, depends/breaks, optional/unless, alternative groups, qualified/provided IDs and separate Quilt version rules are now integrated with cart review and offline installed-mod readiness. Conditional nested candidate selection, plugins/mappings and unsupported constraints remain Unknown. Fabric-on-Quilt/QSL/Sinytra are not claimed. See docs/QUILT_MOD_METADATA.md.

Release solution build: 0 errors/warnings. Full source suite: **241/241 tests passed**, 0 failed/skipped/not-run, including 32 new Quilt metadata cases. Evidence: artifacts/0.2.0/quilt-mods-build.log; quilt-mods-full.log; tests/quilt-mods-full.trx. xUnit v3 direct in-process runner is used because the previously recorded VSTest collector handshake failed before cases.

User retest of the auth-fix payload completed browser OAuth, then failed with HTTP 403 from api.minecraftservices.com. The screenshot identifies the host/status but not the exact endpoint or safe server reason. An application-service access/registration issue is possible, not proven. Full entitlement/profile/refresh and online/offline gameplay remain unverified. No new application, borrowed Client ID or authentication bypass was used.

Continue full conditional Quilt candidate resolution and permitted Forge/NeoForge formats. Actual modded gameplay still requires licensed authentication.


## Forge/NeoForge metadata pause checkpoint - 2026-10-28

Forge/NeoForge native root TOML metadata is partially implemented: multi-mod JAR identities/versions, dependency types/sides, Java features, numeric Maven intervals/unions, main-manifest version substitution and BEFORE/AFTER cycle detection. Integrated with the existing local cart, transaction/inventory and installed-mod readiness. No JAR code is executed. See docs/FORGE_MOD_METADATA.md.

Forge/NeoForge loader installation, Jar-in-Jar selection, Maven qualifier ordering and real mod/game integration remain pending. NeoForge FML version is independently versioned and remains Unknown until actual installer metadata is available; it is never inferred from the NeoForge version. Unsupported metadata cannot establish Ready.

Resume from this source checkpoint. Next independent work: actual Forge/NeoForge installer/catalog and FML metadata, complete nested candidate handling, then the retained roadmap. Microsoft Services HTTP 403/manual licensed gameplay remains external. Preserve all 184 requirements; do not recreate the project, bypass auth, use real worlds or retry the blocked installer registry guard.

## Current owner priority order (supersedes historical ordering above)

1. P0: auth diagnostics/official review, launch engine, Java 8/17/21/25, licensed Vanilla/Fabric/Quilt tests and isolated installer lifecycle. Continue independent Java/asset/argument work while approval is external.
2. Forge and NeoForge official catalogs/installers and separate FML metadata; Quilt nested/conditional improvements; Fabric final integration.
3. Modrinth/allowed CurseForge, unified deterministic dependencies, updates/locking/cart/recovery.
4. Modpacks/import/export/friend comparison; worlds/resourcepacks/shaders/backups/storage.
5. Security scanner, conflict finder in authorized isolated copies, downloads queue/resume and performance.
6. Optional live AI/legacy and nested translations, version-aware pack format; server cores including Paper/Purpur, supported Porthole/manual Aternos; isolated plugins and authenticated updater.
7. Full eight-language localization/accessibility, real security/distribution/game tests and stable-release audit. Stable is not authorized before verified P0 acceptance.

DEFERRED: Sinytra Connector, Kilt, Cross-Loader Compatibility Manager, Fabric-to-Forge, Forge-to-Fabric, Forge-to-NeoForge, NeoForge-to-Forge and all other third-party bridges. Retained as explicit requirements; do not implement now. Detailed grades: ../PROJECT_PROGRESS_SUMMARY.md.

First-run language selection is the requested UI addition before resuming P0 Java/runtime/asset work. Keep the eight-language preference and existing data across upgrades. See FIRST_RUN_LANGUAGE_CHECKPOINT.md for current evidence.
