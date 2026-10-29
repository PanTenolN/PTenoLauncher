# Managed Java integrity and live runtime matrix

This block preserves the existing architecture and hardens Java preparation and offline readiness. It does not start Minecraft or replace Microsoft authentication.

## Runtime validation

Managed Java is accepted only when its bounded receipt identifies the exact executable, major, archive SHA-256, release and a complete unique file inventory. Every file hash/size is checked, java.exe must have an x64 PE machine type, and untracked files/DLLs, symlinks/junctions, duplicate paths, traversal, invalid hashes and oversized inventories are rejected. The runtime folder identity must match its receipt/archive prefix. A valid receipt for one executable does not authorize a different executable in that directory.

OfflineReadiness now stops before invoking a corrupt managed runtime. Previously it recorded java.corrupt but still called java.exe -version. JavaManager checks PE architecture before execution, bounds version output to 65536 characters per stream, limits probe time, terminates owned probe processes and removes JAVA_TOOL_OPTIONS/JDK_JAVA_OPTIONS/_JAVA_OPTIONS/CLASSPATH from that child process only. No user/system environment setting is changed.

Corrupt cached runtime directories are retained; verified cached archives can repair into a new owned directory without network access. Malformed receipts are skipped rather than stopping discovery. Download metadata is checked for matching major, Eclipse vendor, Windows x64 JRE/HotSpot/JDK project, bounded ZIP size, SHA-256 and exact HTTPS Adoptium GitHub release origin. Invalid metadata can be refreshed once online. No MSI/system Java installation or administrator privilege is used.

RequiredMajorAsync uses a validated installed receipt or verified Mojang metadata; online preparation can fetch an absent catalog. Historical missing javaVersion metadata retains the existing Java 8 default. EnsureManagedAsync explicitly tests/chooses launcher-managed Java without silently substituting a system installation.

## Evidence and reproducibility

JavaRuntimeIntegrityTests adds 42 cases covering inventory, exact path, archive/folder identity, corrupt metadata, PE architecture, offline/no-network refusal, cancellation, Minecraft Java metadata caching and Temurin origin/platform/version validation. The local full suite passes **386/386**, zero failed/errors/skipped/not-run; Release build has zero errors/warnings. Logs: artifacts/0.2.0/java-runtime-release-build.log; java-runtime-tests.log; tests/java-runtime.trx.

The opt-in live executable check uses an isolated root without profiles/worlds:

```powershell
dotnet tests/PTenoLauncher.IntegrationSmoke/bin/Release/net10.0/PTenoLauncher.IntegrationSmoke.dll .local/ci-java-matrix --java-matrix
```

It installs actual managed Windows x64 Temurin 8/17/21/25, verifies inventory/architecture/actual -version exit, checks reuse with zero offline HTTP requests, tests process-local inherited-option isolation on Java 17, corrupts/restores one owned non-executable Java 21 fixture file and repairs from cached archive offline. It preserves a synthetic data sentinel and checks verified Minecraft Java metadata online/offline for historical/modern/latest release entries. It never downloads/runs Minecraft, uses account fixtures, changes the registry or touches real saves. Phase/results JSON is saved after each stage; a failed stage remains FAILED with its exception type.

The local live attempt was blocked before the first download by SocketException 10051 at api.adoptium.net (zero completed runtimes). It remains failed/network-blocked evidence in .local/java-live-20261029 and live-java-matrix.log. Windows CI runs the same live matrix as a separate acceptance check before packaging/publication and uploads its report/logs. Its exact result belongs in JAVA_RUNTIME_CHECKPOINT.md; do not infer success from unit tests.

Official source/API references: https://adoptium.net/installation/ci-scripts and https://github.com/adoptium/api.adoptium.net/blob/main/docs/cookbook.adoc . Runtime hashes/inventory detect corruption; they are not an antivirus verdict or protection against an attacker who can rewrite the whole local receipt. Java signatures/independent trust roots and stronger process containment remain separate security work.

## Limits

Actual licensed Minecraft launch/world saves, JVM compatibility with every historical/modded game version, Java updates/removal UI, native game stopping, Microsoft approval and current installer lifecycle remain unverified. The four-major runtime matrix cannot prove gameplay. The first-run language feature and all prior requirements are preserved. No authentication/license bypass, borrowed ID, local AI model or antivirus disabling is introduced.

## Verified Windows outcome — 2026-10-29

Windows CI 40833002559 passed all 386 tests and the actual 8/17/21/25 managed-runtime matrix. Each major installed from official Temurin metadata, passed inventory/x64/-version checks and reused offline with zero HTTP requests. Java 21 corruption detection/cached repair, Java 17 inherited-option isolation and synthetic-data preservation also passed. Observed Minecraft metadata mappings were 1.12.2→8, 1.20.1→17, 1.21.1→21 and 26.3→25, each reusable offline. No Minecraft game was launched. Final source e7b058cf31c32e7f3d44ee9b2acedb821e2c148e has actual Setup/Portable/Source/checksum assets at https://github.com/PanTenolN/PTenoLauncher/releases/tag/v0.2.0-preview.e7b058cf31c3 . See JAVA_RUNTIME_CHECKPOINT.md for hashes and the separately retained local network failure.
