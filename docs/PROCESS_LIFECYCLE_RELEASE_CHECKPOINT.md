# Process lifecycle return point — 2026-10-28

**Created and developed with AI assistance (OpenAI Codex).**

Verified release source: `827e8832a95baf5d2c9f805fb3c43f563427c90b`. Implementation commit: `95d7d7e7ec94dbfee0514f390f239ef35c419505`; final packaging follow-up: `827e8832a95baf5d2c9f805fb3c43f563427c90b`. Windows workflow **40773211706 SUCCESS**: https://github.com/PanTenolN/PTenoLauncher/actions/runs/40773211706 . Both Windows build/package job 123116557565 and publication job 123118214475 succeeded.

The final Windows logs confirm **330/330 tests passed**, zero failed/errors/skips/not-run, Release build zero errors/warnings, 184 original requirement IDs retained / 188 current rows / 43 graded modules, two ZIP CRC checks and a fresh extracted Portable startup with initialized=True. gameLaunched=false. Local final evidence: critical-process-release-build.log; critical-process-final-tests.log; tests/critical-process-final.trx. A real existing Java 21 x64 process returned 0 for -version and 1 for a deliberately invalid JVM option, with zero HTTP requests; it was not Minecraft.

Actual prerelease: https://github.com/PanTenolN/PTenoLauncher/releases/tag/v0.2.0-preview.827e8832a95b

| Asset | Bytes | SHA-256 |
|---|---:|---|
| PTenoLauncher-Portable-0.2.0-win-x64.zip | 50205592 | 1d56bdee81a837972d23b7f81a733b707fcaeff5d87c6767658dd231078448cd |
| PTenoLauncher-Setup-0.2.0-win-x64.exe | 37448859 | fad88be2ad4ecd8be5af14e82cb9675637a263bae76cc73a7e67221715b15058 |
| PTenoLauncher-Source-0.2.0.zip | 753764 | 123c3a53260e2d74f8a6492fdceb1759f992cb6647eeec018f59390a650cfe50 |
| SHA256SUMS.txt | 410 | 87556a10379742c0896f33c72b140bcb37210af359563cee6bc3073a1dff55c8 |

New block: post-start cancellation and owned-process cleanup, redacted bounded stdout/stderr and exit codes, bounded inherited-output drain, Stop Minecraft on Home/Game console, default-cancel acknowledgement of possible unsaved data loss and stale-launch confirmation guard. Authentication/readiness gates remain. Tests use isolated owned Windows processes and actual XAML, not fabricated Minecraft authorization. See PROCESS_LIFECYCLE.md.

New documentation: ../PROJECT_PROGRESS_SUMMARY.md grades 43 modules using the owner's explicit control scale; DEVELOPMENT_BRIEF_2026-10-28.md preserves the complete new brief. All 184 original requirements remain, with four explicit additions; Sinytra, Kilt and all listed cross-loader bridges are DEFERRED. scripts/verify-requirements.py checks retained IDs, statuses and grades. Source/Portable include the progress report; source verification checks required continuation, AI, locale and lifecycle files.

The local workspace is an existing source mirror without .git; remote main/history was checked before changes. Preserve this architecture and source. Documentation-only follow-up commits recording final CI do not change the released binary source. Reports inside the Source ZIP may predate this final result-recording update.

Still unverified: Microsoft approval/entitlement/profile/refresh, licensed Vanilla/Fabric/Quilt gameplay and real disposable-world saves/reopening, real-game stop behavior, Java 8/17/25 live matrix, historical virtual/mapped assets, live OpenAI quality, translation-pack activation and the current installer install/upgrade/reinstall/uninstall lifecycle. Binaries remain unsigned. Never bypass the earlier installer registry ownership guard, authentication or licensing.

Next independent P0 block: Java matrix, managed-runtime receipt/path/architecture integrity and historical asset/launch preparation. On approval, use HOME_TEST_CHECKLIST.md with disposable profiles/worlds. Continue remaining stages from ROADMAP.md; no stable release until verified P0 acceptance. Do not recreate the project.
