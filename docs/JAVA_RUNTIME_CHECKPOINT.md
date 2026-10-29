# Java runtime return point — 2026-10-29

**Created and developed with AI assistance (OpenAI Codex).**

Verified release source: `e7b058cf31c32e7f3d44ee9b2acedb821e2c148e`. Runtime implementation commit: `957636b2ec16c406c8749bf00aa3e439266e9ab1`. Final workflow: **40833002559 SUCCESS**, https://github.com/PanTenolN/PTenoLauncher/actions/runs/40833002559 . Windows job 123311253225 and publish job 123312549638 both passed. Release build zero errors/warnings; **386/386 tests passed**, zero failed/errors/skipped/not-run; all 184 original / 189 current requirements and 44 graded modules remain. ZIP/source integrity and fresh Portable startup passed (initialized=True, gameLaunched=false).

Actual prerelease: https://github.com/PanTenolN/PTenoLauncher/releases/tag/v0.2.0-preview.e7b058cf31c3

| Asset | Bytes | SHA-256 |
|---|---:|---|
| PTenoLauncher-Portable-0.2.0-win-x64.zip | 50235800 | 5be4f5423b8c3d3c15a867accd13259521eeb1b27ec6dfb28600742a915aaea7 |
| PTenoLauncher-Setup-0.2.0-win-x64.exe | 37461995 | 3070bd85b443cfb65d0e552a9af25cfd1f26b9da1eaa3d3e52f14939520c2112 |
| PTenoLauncher-Source-0.2.0.zip | 793969 | 22ac96149413539a121b002a272d09584727f15ced0db64f261712935df4b12c |
| SHA256SUMS.txt | 410 | 51ba9c01d1456bf89f88d8bc5f8ccc1f17d3f4300885b3f9b92f0e8ef3d0c60f |

## Actual managed Java verification

| Java major | Real install/x64/-version exit 0 | Offline reuse HTTP requests | Additional checks |
|---|---|---:|---|
| 8 | PASS | 0 | Synthetic data preserved |
| 17 | PASS | 0 | Inherited invalid JAVA_TOOL_OPTIONS did not affect isolated probe |
| 21 | PASS | 0 | Corruption detected; repaired from cached archive offline |
| 25 | PASS | 0 | Synthetic data preserved |

Live Mojang requirement metadata (observed in this run, not hardcoded launch policy):
- Minecraft 1.12.2 requires Java 8; metadata reused offline without HTTP.
- Minecraft 1.20.1 requires Java 17; metadata reused offline without HTTP.
- Minecraft 1.21.1 requires Java 21; metadata reused offline without HTTP.
- Minecraft 26.3 requires Java 25; metadata reused offline without HTTP.

The final matrix recorded 17 total online HTTP requests and zero offline requests for each runtime. Complete JSON and version logs are retained by Windows CI under artifacts/0.2.0/java-matrix and in the workflow log. The first run 40832409126 also passed all runtime tests but failed publication because artifact upload roots changed. Source e7b058cf31c32e7f3d44ee9b2acedb821e2c148e fixes the layout and copies reports/logs into the common artifacts root, including failure reports.

## Implemented behavior and local evidence

Managed Java now requires exact executable/folder identity and complete bounded file inventory/hash/size/architecture checks, rejecting untracked DLLs/files and links. OfflineReadiness no longer invokes a runtime already detected corrupt. Temurin metadata is matched to the expected major/vendor/OS/architecture/image/project/source. Version probes are bounded, x64-only and isolate inherited Java options. Corrupt caches are retained and a verified cached archive can repair into a new owned directory. See JAVA_RUNTIME_VERIFICATION.md.

Local Release build also passed with zero errors/warnings; the full local suite passed 386 cases including 42 new integrity/metadata tests (java-runtime-release-build.log, java-runtime-tests.log, tests/java-runtime.trx). The actual local live attempt remained network-blocked at api.adoptium.net (SocketException 10051, zero completed installs); that evidence is not reclassified as a local PASS. Actual 8/17/21/25 acceptance above ran on the hosted Windows runner, not the laptop.

The laptop source initially lacked the newer first-run menu. All 27 newer files were compared against known Git blob identities; only CRLF normalization differed. Copies were backed up under .local/checkpoints/pre-language-sync-* and main 21eefdd57b2a95c786ea7ecd1601a8925eb85d51 was restored before Java edits. Preserve the architecture and eight-language UI. Documentation-only follow-up commits recording this result do not change released binary source; ZIP-contained reports may precede final CI-result notes.

Still unverified: Microsoft approval/full entitlement/profile/refresh, actual licensed Vanilla/Fabric/Quilt world launch/save/reopen, native game stopping, historical virtual/mapped assets, live OpenAI quality/pack activation, native UI/DPI and current installer lifecycle. Java update/removal UI remains incomplete. Binaries are unsigned; no stable release or complete-launcher claim is made.

Next independent P0 block: historical virtual/mapped assets and launch arguments, then licensed game acceptance after the owner's Microsoft review. Preserve all requirements and DEFERRED Sinytra/Kilt/cross-loader bridges. Do not bypass authentication/licensing or the earlier blocked installer registry ownership guard. Do not recreate the project.
