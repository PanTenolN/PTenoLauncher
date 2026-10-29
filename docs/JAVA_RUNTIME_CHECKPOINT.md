# Java runtime return point — 2026-10-29

**Created and developed with AI assistance (OpenAI Codex).**

Resumed from main 21eefdd57b2a95c786ea7ecd1601a8925eb85d51, previous first-run-language release source 780f22642a658ddb4f8fee5bc9ccbdc795fcf3c0 / Windows CI 40827117958 / 344 passing tests. The laptop source was still at the older process-lifecycle version. All 27 newer files were compared with their known base Git blobs; differences were only CRLF normalization, not user edits. Previous local copies were backed up under .local/checkpoints/pre-language-sync-* and the verified newer source was restored without recreating the project.

New code hardens managed runtime receipts/inventory/exact executable identity, Temurin metadata validation and architecture/version probing. OfflineReadiness no longer executes an already-corrupt managed runtime. EnsureManagedAsync and LiveJavaMatrix allow real managed-only 8/17/21/25 installation/offline/recovery verification without accounts or Minecraft. See JAVA_RUNTIME_VERIFICATION.md.

Local final Release build passes with zero errors/warnings. Full suite **386/386 passes**, zero failed/errors/skipped/not-run, including 42 new JavaRuntimeIntegrityTests. The actual local live matrix failed at api.adoptium.net with network-unreachable SocketException 10051, zero runtime installs; do not call it PASS. Its progress/error JSON and log were retained. No unverified game launch occurred.

The Windows workflow now runs the actual four-major Java matrix before archive creation/publication. Its result, new source commit and actual prerelease must be recorded below after verification. Until then the last verified binary preview remains https://github.com/PanTenolN/PTenoLauncher/releases/tag/v0.2.0-preview.780f22642a65 .

Preserve all 184 original requirement IDs / 189 current rows / 44 module grades, the first-run eight-language picker, optional AI gates and DEFERRED bridge requirements. No new Microsoft approval, licensed Vanilla/Fabric/Quilt gameplay, live AI quality, installer lifecycle or stable release is claimed. Next core work: historical virtual/mapped assets and launch arguments, then approved-account gameplay. Do not bypass authentication or the earlier blocked installer ownership guard.
