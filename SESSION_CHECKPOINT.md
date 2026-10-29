# Resume checkpoint — PTenoLauncher 0.2.0

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

## Latest verified return point - 2026-10-28

Resume from release source aec1846517865a0953b1037bd6b55f8ce8807efc and the final evidence in docs/ONLINE_AI_RELEASE_CHECKPOINT.md. Its original GitHub documentation commit is 4374db9f44bacb2fac2960b2f01a63d532b7e603. The missing final-release checkpoint has now been restored locally; workspace read/write access worked during this documentation handoff. Historical entries below that await final CI/publication are superseded.

Verified Windows workflow: https://github.com/PanTenolN/PTenoLauncher/actions/runs/40759155278 . Release build 0 errors/warnings; 320/320 tests pass; ZIP integrity and freshly extracted Portable startup pass. Actual Setup/Portable/Source/checksums: https://github.com/PanTenolN/PTenoLauncher/releases/tag/v0.2.0-preview.aec184651786 . No new code, build, gameplay or live-provider test was performed during this handoff.

Preserve existing eight-language catalogs, optional AI policies/consent and translation drafts, profiles, installed-mod metadata and all 184 traceability requirements. Continue remaining low-level localization and independent roadmap work without recreating the project. Microsoft approval/real sign-in and gameplay, live OpenAI quality, resource-pack activation and latest installer lifecycle still require verification. Keep the prior installer ownership guard intact; never bypass authentication or licensing.

## Final Java-format guard - 2026-10-28

Added a regression guard against reordering unnumbered Java printf placeholders (for example %s then %d). Numbered placeholders can still be reordered safely. Final local verification: **320/320 tests passed**, zero failures/errors/skips/not-run; Release build 0 errors/warnings. Evidence: artifacts/0.2.0/online-ai-placeholder-build.log; online-ai-placeholder-tests.log; tests/online-ai-placeholder.trx. Prior 319-case checkpoint/native publish and GitHub run 40758215281 succeeded; the new guard requires its own subsequent CI/prerelease. No real OpenAI/Minecraft or installer-lifecycle test is claimed.

Remote interrupted-block checkpoint 745b45502541bb4ff0a10771aeecde1cccb7bf05 is retained in GitHub history; its unverified state is superseded by the recovered, tested block below. No original architecture or files were recreated.

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

## Source publication work - 2026-10-28

Core feature work remains paused; the owner asked why the launcher was not published and about languages. Source publication and a gated Windows prerelease workflow are being prepared. scripts/build.ps1 now uses the verified xUnit v3 direct runner. docs/LOCALIZATION.md records the partial Ukrainian/English scope; no additional language or complete translation is claimed. This heading records preparation only; inspect docs/GITHUB_PUBLICATION.md and actual remote results for publication status.

## Microsoft AppID review submission - 2026-10-28

The owner reports submitting the Microsoft AppID review form for PTenoLauncher. Status: SUBMITTED, APPROVAL PENDING. No approval or successful downstream Minecraft Services authentication has been observed. The latest real result remains browser OAuth complete followed by api.minecraftservices.com HTTP 403; its cause is not confirmed. After a decision, retest entitlement/profile and licensed online/offline gameplay. Documentation only; no new build/test execution, binaries or public publication. Core development remains paused.

## Public project overview published - 2026-10-28

README.md only is now public at https://github.com/PanTenolN/PTenoLauncher, commit 48d317b74a70a0821b28c4ed823a8fe5ab247955. Its content matches docs/GITHUB_README.md and includes the required AI-development disclosure. The connector has verified push permission; no user credentials were requested. No source/binary/user data/workflows were published. See docs/GITHUB_PUBLICATION.md. The owner reports submitting the Microsoft AppID review form; approval and actual sign-in/gameplay remain pending. Core development remains paused until resumed.

## Owner publication instruction

The owner requires a visible "Created and developed with AI assistance (OpenAI Codex)" disclosure on GitHub publications. The Ukrainian/English note is now in the local README; follow docs/PUBLICATION_POLICY.md for repository and release descriptions. No GitHub publication has been performed. This documentation-only update does not rerun the 281 passing code tests or regenerate the earlier archives.

## Forge/NeoForge metadata pause checkpoint - 2026-10-28

Development is **PAUSED at the user request before powering off the laptop**. No build/test/integration is left running. Source is saved in the existing workspace and artifacts/PTenoLauncher-Source-0.2.0-forge-metadata.zip. Existing Quilt/auth-fix executables, Portable and Setup remain previous payloads; this block has not republished or installed them.

Forge/NeoForge native root TOML metadata is partially implemented: multi-mod JAR identities/versions, dependency types/sides, Java features, numeric Maven intervals/unions, main-manifest version substitution and BEFORE/AFTER cycle detection. Integrated with the existing local cart, transaction/inventory and installed-mod readiness. No JAR code is executed. See docs/FORGE_MOD_METADATA.md.

Release build: **0 errors, 0 warnings**. Full source suite: **281/281 passed**, 0 failed/skipped/not-run, including 40 new ForgeModMetadataTests cases. Logs: artifacts/0.2.0/forge-metadata-build.log; forge-metadata-targeted.log; forge-metadata-full.log; tests/forge-metadata-full.trx. xUnit v3 direct runner used; no real game launch.

Forge/NeoForge loader installation, Jar-in-Jar selection, Maven qualifier ordering and real mod/game integration remain pending. NeoForge FML version is independently versioned and remains Unknown until actual installer metadata is available; it is never inferred from the NeoForge version. Unsupported metadata cannot establish Ready.

Tomlyn 2.10.1 is pinned in project/lock files; downloaded package SHA-512 matches the official NuGet catalog. Native NuGet restore/audit was network-blocked (NU1301/NU1900). A workspace-only feed/existing cache was used, with NuGetAudit=false for that isolated restore invocation only; the repository default remains NuGetAudit=true. A live vulnerability audit is NOT VERIFIED. See tomlyn-package-verification.json and docs/FORGE_MOD_METADATA.md.

Resume from this source checkpoint. Next independent work: actual Forge/NeoForge installer/catalog and FML metadata, complete nested candidate handling, then the retained roadmap. Microsoft Services HTTP 403/manual licensed gameplay remains external. Preserve all 184 requirements; do not recreate the project, bypass auth, use real worlds or retry the blocked installer registry guard.

## Retained earlier checkpoints

## Quilt mod metadata checkpoint - 2026-10-28

User is away and authorized independent work from the phone. Windows workspace access is restored; preserve this existing architecture.

Native Quilt schema-1 identity, depends/breaks, optional/unless, alternative groups, qualified/provided IDs and separate Quilt version rules are now integrated with cart review and offline installed-mod readiness. Conditional nested candidate selection, plugins/mappings and unsupported constraints remain Unknown. Fabric-on-Quilt/QSL/Sinytra are not claimed. See docs/QUILT_MOD_METADATA.md.

Release solution build: 0 errors/warnings. Full source suite: **241/241 tests passed**, 0 failed/skipped/not-run, including 32 new Quilt metadata cases. Evidence: artifacts/0.2.0/quilt-mods-final-build.log; quilt-mods-final-tests.log; tests/quilt-mods-final.trx. xUnit v3 direct in-process runner is used because the previously recorded VSTest collector handshake failed before cases.

The opt-in real OK Zoomer/Modrinth check is BLOCKED by unreachable api.modrinth.com (SocketException 10051), before a real mod download. Synthetic JAR test success is not real-artifact/gameplay evidence. No system/proxy/DNS settings were changed.

User retest of the auth-fix payload completed browser OAuth, then failed with HTTP 403 from api.minecraftservices.com. The screenshot identifies the host/status but not the exact endpoint or safe server reason. An application-service access/registration issue is possible, not proven. Full entitlement/profile/refresh and online/offline gameplay remain unverified. No new application, borrowed Client ID or authentication bypass was used.

Separate self-contained Windows publish and freshly extracted Portable startup: PASS. Preview artifacts: artifacts/0.2.0/quilt-mods-win-x64, PTenoLauncher-Portable-0.2.0-quilt-mods-win-x64.zip and PTenoLauncher-Source-0.2.0-quilt-mods.zip. Checksums/CRC/private-data exclusions and startup evidence: artifacts/QUILT_MODS_SHA256SUMS.txt and artifacts/0.2.0/quilt-mods-checkpoint.json. Standard Setup and previous auth-fix packages remain unchanged; no installer rebuild/lifecycle test is claimed.

This independent block is COMPLETE and saved. No build/test/integration process is left running. Next: Forge/NeoForge metadata/installers or full conditional Quilt candidate selection; continue the remaining roadmap. The user is away; authentication approval/manual gameplay requires their later participation. Do not retry the blocked installer registry preflight. Keep all 184 traceability requirements. No account secrets were read and no real worlds used.

2026-10-28. Workspace C:/Users/tatar/Documents/PTenoLaucher. Development active. The owner is home, registered the personal-account public app/localhost redirect, completed browser Microsoft OAuth and reproduced Xbox user.auth.xboxlive.com HTTP 400. Current task: fix and hand off a separate retest build. No real credentials/callback/DPAPI file was collected. Preserve all existing architecture/features and give Ukrainian progress updates.

## Latest authentication/button source checkpoint

- Exact Xbox/XSTS JSON property casing and x-xbl-contract-version: 1 fixed; Minecraft identityToken casing retained. Error logs now include only fixed host and HTTP code.
- Owner-registered public default application ID: LauncherMicrosoftApplication.cs. It is not a secret, is not borrowed, and does not establish API approval or a license. Saved advanced overrides remain supported. Normal players use the visible header button and do not register Entra apps.
- Header and Account-page Виконати вхід buttons directly bind SignInCommand, are disabled while busy, turn into the selected-account nickname and return after logout. Account display refreshes before the optional skin download.
- Actual-XAML test found SecretVault.Write synchronously waiting on async AtomicFile.WriteAsync on the UI thread. Added synchronous AtomicFile.Write with safe paths, durable flush and atomic replacement; DPAPI unchanged. Legacy unmapped accounts no longer query MSAL during logout.
- Release build: 0 errors/warnings; **209/209 tests pass, none failed/skipped/not-run**. auth-fix-build.log; auth-fix-tests-direct.log; tests/auth-fix-direct.trx; auth-header-test.log. Tests use the bundled xUnit v3 direct in-process runner; VSTest data-collector handshake aborted before cases and is not marked PASS. Initial UI busy regression logs retained separately.
- Current self-contained retest payload: artifacts/0.2.0/auth-fix-win-x64/PTenoLauncher.exe with accompanying files. auth-fix-publish.log; auth-fix-native-smoke.log; .local/auth-fix-smoke/gui-smoke.txt; ui-auth-fix.png. Inspect logs before assuming an interrupted packaging/smoke step completed. Standard Setup/Portable under artifacts remain pre-fix Quilt payloads and must not be presented as updated.
- Full real Xbox/XSTS/Minecraft sign-in AFTER this fix: PENDING USER RETEST. Old real attempt FAIL HTTP 400; browser step PASS. Actual Minecraft gameplay still NOT RUN. See docs/AUTHENTICATION_FIX.md and HOME_TEST_RESULTS.md. No authentication/license bypass.

## Retained completed block: Quilt

- Official Quilt v3 catalog, release/prerelease numeric selection, explicit pinning, inherited Vanilla launch receipt and version-picker panel. Fixed origins, Maven coordinates, selected-loader matching, SHA512/SHA256, bounded archives and static classfile Java minimum. No installer JAR execution; no auth bypass.
- Final Release solution build: 0 errors/warnings; 203/203 automatic tests pass, none skipped. Logs: quilt-final-build.log, quilt-final-tests.log, tests/quilt-final.trx. QuiltInstallerTests adds 13 cases; DesktopTests verifies loader-panel switching/reset.
- Real cached official install/repeated repair passed: MC 1.21.1 / Quilt 0.30.1, 3972 files, 82 classpath entries, Java 21, zero HTTP requests; installed pin and synthetic world sentinel preserved. Full readiness correctly reports only account.missing. Evidence: live-quilt-offline.log/status.json.
- The official cache was initially seeded by scripts/prepare-quilt-fixture.py from 11 real HTTPS/checksummed Maven artifacts, not production mock data. quilt-fixture-preparation.log and quilt-official-fixture.json retain this distinction.
- Fresh native .NET HTTP catalog check passed: 307 versions, latest numeric release 0.30.1 (quilt-native-metadata.log). Actual C# online loader-JAR redownload/hash verification and synthetic-save preservation passed (quilt-native-repair.log). No game launched.
- Direct online integration from restricted exec failed DNS (live-quilt.log/status.json); native .NET launched via Node context succeeded. No DNS/proxy/system configuration changed. Full clean native-online installation remains unverified.
- Native actual Avalonia Quilt picker passed and render reviewed: native-quilt-smoke.log, ui-quilt.png, initialized=True, quilt=0.30.1. See docs/QUILT.md for usage/reproduction/limits.
- Quilt mod dependency metadata remains NOT implemented (MODS-06). Nonempty unsupported Quilt mod sets stay unknown. No QSL, Fabric-on-Quilt or Sinytra bridge claimed. Actual unmodded/modded Quilt gameplay remains NOT RUN.

## Retained working blocks

Graphite themes/profiles/SQLite; safe confirmed deletion and backup ownership; default-off AI gate; managed Java; MSAL/Xbox/XSTS/Minecraft code and DPAPI account storage; Vanilla/Fabric preparation; latest/releases/snapshots/history catalog; redacted process console; Modrinth deterministic required/optional resolver and combined local/catalog cart, durable recovery and Fabric installed-mod integrity/dependency readiness. Mods stays hidden for Vanilla/no profile.

Optional components are unchecked by default and only explicit Apply recalculates; edits invalidate plan/confirmations without HTTP. The selected-profile Перейти до теки button uses an owned safe path and absolute Explorer executable; routing is tested, interactive Explorer opening is NOT RUN. Earlier real Reese -> Sodium cached install/readiness and optional-metadata evidence remain in previous reports. Offline install means existing local JARs/verified cache, not downloading missing Internet content.

## User handoff / external blockers

- MICROSOFT_AUTH_SETUP.md: own desktop public-client registration/Client ID; Microsoft service permission may also be needed. The owner has now completed app registration/localhost/browser OAuth; downstream API/entitlement and gameplay tests remain unverified. Never borrow client IDs, ask for password/MFA/tokens or bypass entitlement checks.
- docs/HOME_TEST_CHECKLIST.md / HOME_TEST_RESULTS.md: Microsoft/Vanilla/Fabric and optional unmodded Quilt online/offline tests, themes/snapshot catalog, local mods/DnD/optional choices/folder navigation, isolated Setup lifecycle. All user cases are NOT RUN until actual results arrive.
- Real Microsoft login/entitlement/refresh/multi-account e2e and actual Minecraft game/world launch remain NOT RUN. Installer/app unsigned; external CI and installed-game portable relocation unverified.
- Current Setup lifecycle remains blocked by earlier registry preflight WinError 5. Do not bypass that ownership guard or retry a blocked registry operation. Earlier Fabric-payload lifecycle 5/5 retained in installer-lifecycle-20261027-100828-8ec007.json; it does not verify the new Quilt payload.

## Distribution / reproduce

Current source includes the Quilt checkpoint; self-contained win-x64 publish and ISCC compilation are recorded in the logs below. Outputs: artifacts/0.2.0/win-x64, artifacts/PTenoLauncher-Setup-0.2.0-win-x64.exe, Portable ZIP, Source ZIP and SHA256SUMS.txt. This block uses quilt-publish.log, quilt-installer-compile.log and quilt-package*.log; final-artifact-verification.json/log is authoritative for final hashes/CRC/fresh extracted Portable startup. Check these logs before assuming packaging completed if interrupted. The Source must include QuiltInstaller.cs, MainViewModel.Quilt.cs, QuiltInstallerTests.cs, prepare-quilt-fixture.py and docs/QUILT.md. Keep earlier artifacts/checkpoints.

Tools: .tools/dotnet/dotnet.exe 10.0.403; .tools/InnoSetup6/ISCC.exe 6.7.3; .local/exec-context.mjs. ctx.run via Node can access official network where exec DNS fails. Long integration runs use scripts/run-live-check.py and durable exec sessions; no integration should be left running. No git commit/public release/signature claimed. All test worlds are synthetic/private.

## Continue next

1. Incorporate user’s home auth/game/Explorer/installer results when available; fix without bypasses. Registration/localhost prerequisites are now completed; retest the corrected native payload and record downstream service results.
2. Quilt mod metadata and Forge/NeoForge native metadata/installers; permitted CurseForge; richer per-row diagnostics/unidentified-JAR workflows.
3. Follow all 184 unique traceability requirements: modpacks/world/resourcepacks/shaders/security/conflict diagnostics/optional online AI/servers/Porthole/manual Aternos/plugins/updater. No approved feature dropped. Sinytra and broad cross-loader bridging remain explicitly deferred.


## Source publication completed - 2026-10-28

Published 193 reviewed source/documentation files to main, commit 92998deeb0907798a02fef7404f98c5a1e58c0a7, tree 0b2740d149b97d8cb65ef036be97a12b1c8b5f82. Branch update succeeded using the expected previous head. AI-development disclosure retained. Local Release build: 0 errors/warnings; full suite: 281 passed, no failures/skips/not-run. Private data/tool/cache/build directories excluded. The Windows workflow is configured to build/test/package/smoke then publish an explicitly marked prerelease; remote execution and release assets remain unverified at this checkpoint. No binary upload is claimed. Ukrainian/English remain partially localized.


Final local verification for the localization/sign-in-stage block: Release build 0 errors, 0 warnings; 288/288 tests passed, no failures/errors/skips/not-run. Includes 7 new expanded cases in LocalizationTests. No real authentication/gameplay or current installer lifecycle is claimed.


Localization/auth-stage source published: commit 8fe5ae248f430e096f479e3e32294da5b413306d. 288 tests pass; 0 build errors/warnings. New CI/release awaits verification; prior preview is actually public. Continue low-level diagnostic localization and live formatted-message refresh. Approval pending. No account tokens or user worlds used.


Native online-AI checkpoint: self-contained win-x64 publish PASS (online-ai-publish.log); isolated native startup PASS, initialized=True, code 0 (.local/online-ai-native/gui-smoke.txt; online-ai-native-smoke.log). This verifies launcher startup, not live AI/Minecraft or installer lifecycle.


Published recovered eight-language/online-AI source: commit 6337ef265bf342e4e137000a975f6256190e5adf; tree b626e88a5661692d485eef28bf353fbeb958a836. 319 tests pass, native startup passes. New CI/prerelease awaits inspection. Live provider and Minecraft tests remain unverified; see docs/ONLINE_AI.md. No real key or account token used.


Final AI placeholder guard published: aec1846517865a0953b1037bd6b55f8ce8807efc, 320 passing tests. Previous AI/eight-language release 6337ef265bf3 actually exists with Setup/Portable/Source/hashes. Inspect final commit CI before claiming its binary release. Live OpenAI and licensed Minecraft remain unverified; no secrets were used.
