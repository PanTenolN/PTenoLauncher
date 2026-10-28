# PTenoLauncher Roadmap

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
