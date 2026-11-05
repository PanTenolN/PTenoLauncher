# AI hand-test + Minecraft Launcher import — 2026-11-02

Current verified local block, developed with AI assistance (OpenAI Codex). Prior pause and 891/889 checkpoint are historical. Existing features were extended, not restored/reimplemented. Source directory has no `.git`; applicable ancestor/source/test AGENTS.md checks found none. Carry [CONTINUATION_RULES.md](CONTINUATION_RULES.md) into every continuation.

## Implemented

- AI UX-01–UX-06: preserve connection/chat/restricted-key/catalog-versus-Responses separation; add exact small trial selection/consent, row/library search and filters, retention of hidden edits/selection, human directions/dates, exact-scope confirmed deletion with durable tombstone, unsupported-resource discovery, translation-only exact-version profile and explicit local-client pack metadata. Details and UX matrix: [HEX_TRANSLATION_CHECKPOINT.md](HEX_TRANSLATION_CHECKPOINT.md). Manual guide: [HEX_CASTING_MANUAL_TEST.md](HEX_CASTING_MANUAL_TEST.md). Source=target, AI-off/offline, migration/manual/stale/ownership/backup guarantees retained.
- MODPACKS-03 separate Minecraft Launcher adapter: explicitly select profiles JSON, profile key and exact content directory; read-only bounded profile/version/base metadata; modern Vanilla and bounded Fabric/Quilt contracts; exact components, exclusions/static JAR/nested review, new independent profile, source-bound preview/consent/rechecks, shared staged durable transaction/insert-only SQLite/recovery, local pins and eight-language UI. No copied credentials, worlds, launch commands, runtime, assets/game binaries or fake installation receipts. Usage, source references and strict limits: [MINECRAFT_LAUNCHER_IMPORT.md](MINECRAFT_LAUNCHER_IMPORT.md). Prism/MultiMC and .mrpack/ZIP pipelines preserved.
- Found during regression: an already started editor catalog refresh survived an Offline transition, leaving UI busy. Offline now cancels the linked refresh CTS. A controlled pending HTTP handler proves cancellation, UI release and no second request while offline; no timeout extension/assertion removal. The production transport remains unchanged; injection is a code-level fixture dependency, unavailable through settings/feed/arguments.

## Actual verification

| Scope | Actual result / evidence |
|---|---|
| Final current Release | **0 errors / 0 warnings**, `verification-build-20261102-194009.log` |
| Final full regression | **947 total / 945 executed and PASS / 2 explicit junction NOT RUN / 0 failures/errors**, `verification-all-20261102-194114.log` and `tests/verification-all-20261102-194114.trx`, 251.630s |
| AI subphase | 101/101 scoped; then 913 total / 911 PASS / 2 explicit NOT RUN on productive AI source before the independent Minecraft adapter; `verification-ai-20261102-190524.log`, `verification-all-20261102-190845.log` |
| Minecraft/Prism/ZIP/.mrpack scoped | **195/195 PASS**, `verification-imports-20261102-192858.log`; 33 new Minecraft scenarios including 3 actual Avalonia workflows |
| Controlled offline transition + localization | **8/8 PASS**, `verification-offline-ui-20261102-194040.log` |
| Final native Windows AI startup | `verification-native-20261102-194606.log`: 3 connection/chat/library probes PASSED, exact current Desktop binaries, normal exits and owned cleanup |
| Final native Modpacks regression | `verification-instance-native-20261102-194623.log`: synthetic Prism import offline followed by current native Modpacks initialization, PASSED. This runner is **not** a real Minecraft Launcher source-profile test. Minecraft-specific control workflow is verified in the three headless Avalonia cases above. |
| Retained hand-test copy | Separate exact inventory/native startup verified, normal exit 0; `artifacts/test-lifecycle/hex-native-verification-manual-20261102-191448-58091721983f4fe8b9942d2b6ddfd41a` |

Failed evidence stays failed: initial XAML/build failures `190054/190218/190439`; initial imports **192/195 PASS, 3 FAIL** (`192505`) exposed rejection of valid opaque version-reference IDs. Fixed reference validation without guessing requirements. Interrupted full `193103` observed a localization/UI-busy failure and has **no final counters/TRX**; environment access failed with invalid Windows `/workspace` permission URI, then recovered. See `verification-all-20261102-193103.interruption.json`. No claim that it completed. Successful final full above is a new run.

There are **56 added scenarios** relative to the previous 891 checkpoint: 22 translation tools, 33 Minecraft import, 1 deterministic offline-catalog transition. Previous 891/889 and Windows Java matrix are historical evidence. The updater's full two-build/rollback E2E is not rerun here. No new vulnerability audit (service still unavailable); no new distribution/Installed test.

## Retained owner hand-test build

`%USERPROFILE%\Documents\PTenoLaucher\manual-testing\HexCasting\Start-HexCasting-Test.cmd`

- `46` application files / **36,041,201 bytes**, all inventory hashes still match; retained total folder **36,062,749 bytes** at measurement. Separate `data` and `inputs`, `KEEP_FOR_USER.json` disallows automated cleanup. Local project .NET is used; framework-dependent Windows x64, not installer/self-contained release.
- The manual copy contains the verified **AI subphase source (913/911)**, deliberately preserved while independent Minecraft import/current-main fixes were developed. It is not silently replaced with newer main binaries. Current main source/binaries are covered by **947/945**. Do not claim these are the same delivery. No user process or manual data was terminated/overwritten.
- No original Discord Hex Casting JAR or exact target Minecraft version supplied. Owner must choose the actual file/version; no substitute/download guessed. Owner alone executes paid requests and game test in the other launcher; PTeno only generates a separate resource-pack ZIP.

## Cleanup evidence

**2642 owned operations**: **630,940,786 measured-created bytes (601.712 MiB)** / **630,940,786 removed bytes (601.712 MiB)**; **zero current-task payload roots remain**. Measurements sum per-operation payload at disposal, not cumulative writes or peak disk use. **2,144,121 bytes** of compact lifecycle evidence retained separately; TRX/logs/source/build scopes also retained. Exact operation list: `artifacts/0.2.0/hex-minecraft-cleanup-20261102.json`.

The two interrupted `verification-all-20261102-193103` roots were removed by guarded owned-root recovery after their exact owner processes were no longer present. No broad cleanup, lock bypass or process kill occurred. Earlier in-progress aggregate preserved separately.

Old retained root: `%USERPROFILE%\Documents\PTenoLaucher\.local\test-runs\ai-native-ai-verified-native-20261030-104542-834ecbe0f75d41fdb759ffc4f5d399aa` — **86,209 bytes**, missing ownership marker/newer bytes. Not adopted or deleted. User profiles/worlds/accounts/translations/backups/product caches/Java, SDK/dependencies/current Desktop/helper, original files/manual delivery/docs/evidence preserved. No unused XAML view was identified/deleted; [UI_VIEW_INVENTORY.md](UI_VIEW_INVENTORY.md) documents all nine active views.

## Unsupported / unverified / external

- Static mod-language import supports only bounded flat outer-JAR JSON in eight locales. Patchouli/book/guidebook resource paths, legacy `.lang`, nested JAR language payloads and component-valued resources are reported as unsupported, not translated or declared complete. Exact Discord resource layout is unverified.
- Minecraft import blocks symbolic latest, missing/conflicting metadata, custom commands/patches/libraries/arguments, unsupported Forge/NeoForge/OptiFine/historic bootstrap and external/network/link ambiguity. Source directories must be explicitly confirmed. No real installed third-party profile read/modified. Component install/readiness/gameplay remain separate.
- Actual native current-host startup and logical-size/headless control checks do not verify physical Windows 125/150/200% DPI, every native file picker, full keyboard/contrast/linguistic accessibility, live AI quality or in-game book/fonts/formatting. Owner checks same exact mod build and compatible game environment.
- Production updater key/feed, final self-contained packages, registry-guarded Installed lifecycle, Microsoft approval/authorized gameplay and fresh vulnerability audit remain external/unverified. No guard/network bypass, paid API, real secret/account use, push/release/production signing or change to another launcher.

All **189 requirements, 184 original IDs, 44 module rows and five DEFERRED Sinytra/Kilt/Cross-Loader rows** preserved, with maturity/status grades unchanged. Next independent block: **MODPACKS-04, bounded Modrinth App then CurseForge App local adapters**, then license-aware export/comparison. Do not repeat completed .mrpack/ZIP/Prism/Minecraft adapters, MODS-19 or updater.
