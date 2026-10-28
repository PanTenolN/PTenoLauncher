# 0.2.0 — Development Preview

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
