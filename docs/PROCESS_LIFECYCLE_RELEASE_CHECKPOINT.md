# Process lifecycle return point — 2026-10-28

**Created and developed with AI assistance (OpenAI Codex).**

Base release source: aec1846517865a0953b1037bd6b55f8ce8807efc; verified prior CI 40759155278; previous documentation head 4374db9f44bacb2fac2960b2f01a63d532b7e603. Source mirror has no local .git; preserve the existing files and remote history. Do not recreate the project.

New block: post-start cancellation and owned-process cleanup, redacted bounded stdout/stderr and exit codes, bounded inherited-output drain, Stop Minecraft on Home/Game console, default-cancel acknowledgement of possible unsaved data loss, stale-launch confirmation guard. Retains account/readiness gates. See PROCESS_LIFECYCLE.md and ../PROJECT_PROGRESS_SUMMARY.md.

Local evidence: Release solution build zero errors/warnings; full xUnit suite 330/330 passed, zero failures/errors/skips/not-run. Ten new cases use real isolated Windows subprocesses and actual Avalonia XAML. Existing real Java 21 x64 probe passes (-version exit 0, deliberate invalid option exit 1), zero HTTP requests, gameLaunched=false. Requirement guard passes: 184 original IDs retained, 188 current rows, 43 graded modules. New brief is preserved in DEVELOPMENT_BRIEF_2026-10-28.md; Sinytra/Kilt/all listed cross-loader bridges are DEFERRED.

Publication of this new block requires its own source commit, Windows workflow and prerelease verification. Until the verified result is recorded below, the actual known prior binary release is https://github.com/PanTenolN/PTenoLauncher/releases/tag/v0.2.0-preview.aec184651786 . A checkpoint in the Source ZIP may predate documentation-only follow-up commits recording its CI result.

Still unverified: Microsoft approval/entitlement/profile/refresh, licensed Vanilla/Fabric/Quilt gameplay and real test-world saves, real-game stop behavior, Java 8/17/25 live matrix, historical virtual/mapped assets, live OpenAI quality, translation-pack activation, current installer install/upgrade/uninstall lifecycle. Binaries remain unsigned. Never bypass the earlier installer registry ownership guard, authentication or licensing.

Next independent P0 block: Java matrix, managed-runtime receipt/path/architecture integrity and historical asset/launch preparation. On approval, use HOME_TEST_CHECKLIST.md with disposable profiles/worlds. Continue remaining stages from ROADMAP.md; no stable release until verified P0 acceptance.
