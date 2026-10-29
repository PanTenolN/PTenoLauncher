# 0.2.0 — Development Preview

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

## Eight languages and optional online AI - 2026-10-28

Development resumed and the interrupted eight-language block was recovered. Eight selectable offline UI catalogs (uk/en/pl/de/es/fr/pt-BR/ru) now cover 230 keys each, including AI controls and major offline readiness findings. Catalog completeness/format validation, actual picker switching and SQLite reload pass. Low-level mod/security diagnostics, some already-formatted live statuses and native-speaker/DPI review remain incomplete; this is not full application-wide localization.

Implemented the optional OpenAI Responses client (fixed HTTPS endpoint, store=false, bounded output, no tools/retries), protected per-user API key configuration, assistant and selected-log analysis, modern JAR language import, selected-namespace and bulk translation, validated batches with atomic resumption, local manual editor, incremental source update handling and profile-owned resource-pack generation. AI off cancels provider operations and keeps local translation editing/export available. No local model or Ollama. See docs/ONLINE_AI.md for usage and limits.

Verification: Release build 0 errors/warnings; **319/319 tests passed**, 0 failed/errors/skipped/not-run. Logs: artifacts/0.2.0/online-ai-final-build.log; online-ai-final-tests.log; tests/online-ai-final.trx. Prior eight-language-only suite: 305/305 passed. OpenAI tests use synthetic HTTP/JAR/profile fixtures, not paid live API requests. Existing mod/core/auth/deletion tests remain passing. Real OpenAI account/model/translation-quality and in-game resource-pack tests are NOT VERIFIED. Minecraft application approval, licensed gameplay and latest installer lifecycle remain pending.

Resource packs currently require an explicit numeric pack_format. Legacy .lang, nested JAR language resources and arbitrary component-valued language JSON are unsupported. The editor is JSON-based; no auto-activation or game-file modification is claimed. Missing/unreviewed entries are reported and excluded from the generated pack. Ownership registration recognizes only the selected profile's fixed generated-pack paths; deletion preview remains safe.

All 184 approved requirements are retained. GitHub source/release results must be checked for this exact new commit; earlier binaries do not include these changes. Development disclosure remains prominent: created and developed with AI assistance (OpenAI Codex).

Adds graphite navigation and profile dialogs; five live themes; safe permanent profile deletion; default-off AI policy; managed Temurin Java; DPAPI multi-account management; bounded launch console; snapshots/history filters; Modrinth search and a deterministic recursive dependency cart; safe embedded Fabric metadata inspection; stale-plan checks and transaction recovery.

Mods navigation is hidden for Vanilla and no-profile states. Switching from Mods to Vanilla returns Home without deleting any profile data.

Build: 0 errors / 0 warnings. Source tests: 209 passed, 0 failed, 0 skipped.
Prior Fabric-payload installer lifecycle: 5/5 isolated scenarios passed. Current Setup rebuild is compiled; its lifecycle retest is blocked before installation by registry Access is denied. Portable execution and archive checks pass.
Real Modrinth dependency/download/offline-cache installation passed for Reese's Sodium Options + Sodium. Native UI and real Java 21 installation/reuse passed.

Adds official Fabric version selection, pinned verified library installation and offline repair. Real 1.21.1/Fabric 0.20.1 preparation passed (3969 files, 79 classpath entries).

Full Microsoft authentication and actual Minecraft game/world launch remain unverified. Forge/NeoForge installation, Quilt mod metadata, full cross-provider/local-JAR resolver, modpacks, optional AI translation and servers remain pending.
The unsigned Setup/Portable files are development previews, not a completed production release. See DEVELOPMENT_STATUS.md and TEST_REPORT.md.

Combined Fabric + Modrinth integration was blocked by DNS and is not marked passed. The installed-mod offline-readiness block follows this checkpoint.

Installed Fabric mods now participate in Offline Readiness: persistent SHA-256 baselines, missing/changed JARs, root duplicate IDs, required dependencies, breaks and real prerelease ranges. Committed-journal recovery restores the baseline. Old/manual files require explicit review in the new Ukrainian/English JAR/hash dialog; recording a baseline is not an antivirus verdict. Other loader metadata and complete in-game compatibility remain pending.

Local JARs can now be identified exactly by SHA512 and resolved through Modrinth, including secondary published files. Selected local files and catalog roots share one confirmed cart and durable installation transaction. Local roots are not downloaded again; source/installed-set changes invalidate plans. Unknown sources keep local metadata and require explicit acknowledgment; project slugs are never guessed. Warm-cache real integration passed with 0 HTTP calls. Actual licensed game launch and current Setup lifecycle remain unverified.

Adds explicit optional Modrinth selection, reapply/clear controls, exact parent-version binding, required dependency resolution, conflict display and orphan-choice pruning. Checkbox edits invalidate the previous plan and confirmations. Adds Перейти до теки / Open mods folder for the selected modded profile. Includes Ukrainian home verification checklist/result template for the manual account/game/installer scenarios that remain unverified.

Adds official Quilt version selection/pinning, inherited Vanilla launch preparation, SHA512/SHA256 library verification and bounded static Java-bytecode inspection. Real Minecraft 1.21.1 / Quilt 0.30.1 cached install/repair passed (3972 files, 82 classpath entries), plus fresh native C# catalog fetch and real loader download/repair. The direct exec-context online test failed DNS and is retained; full clean online installation, Quilt mod metadata and licensed gameplay remain unverified.

Current source/retest build adds a prominent Sign in button in the shared header and Account page, the owner-registered public application ID as the default, exact Xbox/XSTS JSON casing/contract headers, safe host/status diagnostics and UI-safe synchronous atomic DPAPI writes. Browser OAuth was observed, but the old payload failed Xbox HTTP 400; full live post-fix sign-in/gameplay remains unverified. Standard Setup/Portable have not yet been rebuilt with these changes; use the separate auth-fix-win-x64 test payload.


## Quilt metadata source checkpoint

Native Quilt schema-1 identity, depends/breaks, optional/unless, alternative groups, qualified/provided IDs and separate Quilt version rules are now integrated with cart review and offline installed-mod readiness. Conditional nested candidate selection, plugins/mappings and unsupported constraints remain Unknown. Fabric-on-Quilt/QSL/Sinytra are not claimed. See docs/QUILT_MOD_METADATA.md.

Release solution build: 0 errors/warnings. Full source suite: **241/241 tests passed**, 0 failed/skipped/not-run, including 32 new Quilt metadata cases. Evidence: artifacts/0.2.0/quilt-mods-build.log; quilt-mods-full.log; tests/quilt-mods-full.trx. xUnit v3 direct in-process runner is used because the previously recorded VSTest collector handshake failed before cases.

The opt-in real OK Zoomer/Modrinth check is BLOCKED by unreachable api.modrinth.com (SocketException 10051), before a real mod download. Synthetic JAR test success is not real-artifact/gameplay evidence. No system/proxy/DNS settings were changed.

User retest of the auth-fix payload completed browser OAuth, then failed with HTTP 403 from api.minecraftservices.com. The screenshot identifies the host/status but not the exact endpoint or safe server reason. An application-service access/registration issue is possible, not proven. Full entitlement/profile/refresh and online/offline gameplay remain unverified. No new application, borrowed Client ID or authentication bypass was used.


## Forge/NeoForge metadata pause checkpoint - 2026-10-28

Forge/NeoForge native root TOML metadata is partially implemented: multi-mod JAR identities/versions, dependency types/sides, Java features, numeric Maven intervals/unions, main-manifest version substitution and BEFORE/AFTER cycle detection. Integrated with the existing local cart, transaction/inventory and installed-mod readiness. No JAR code is executed. See docs/FORGE_MOD_METADATA.md.

Release build: **0 errors, 0 warnings**. Full source suite: **281/281 passed**, 0 failed/skipped/not-run, including 40 new ForgeModMetadataTests cases. Logs: artifacts/0.2.0/forge-metadata-build.log; forge-metadata-targeted.log; forge-metadata-full.log; tests/forge-metadata-full.trx. xUnit v3 direct runner used; no real game launch.

Forge/NeoForge loader installation, Jar-in-Jar selection, Maven qualifier ordering and real mod/game integration remain pending. NeoForge FML version is independently versioned and remains Unknown until actual installer metadata is available; it is never inferred from the NeoForge version. Unsupported metadata cannot establish Ready.

Tomlyn 2.10.1 is pinned in project/lock files; downloaded package SHA-512 matches the official NuGet catalog. Native NuGet restore/audit was network-blocked (NU1301/NU1900). A workspace-only feed/existing cache was used, with NuGetAudit=false for that isolated restore invocation only; the repository default remains NuGetAudit=true. A live vulnerability audit is NOT VERIFIED. See tomlyn-package-verification.json and docs/FORGE_MOD_METADATA.md.

Development is **PAUSED at the user request before powering off the laptop**. No build/test/integration is left running. Source is saved in the existing workspace and artifacts/PTenoLauncher-Source-0.2.0-forge-metadata.zip. Existing Quilt/auth-fix executables, Portable and Setup remain previous payloads; this block has not republished or installed them.
