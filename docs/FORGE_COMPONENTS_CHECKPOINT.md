# PTenoLauncher — paused after Forge/NeoForge component preparation — 2026-10-29

Latest local return point: **NATIVE_NESTED_CHECKPOINT.md** (576 total / 574 passed / 2 explicit not run; Release 0 errors/warnings). Results below are retained historical/scoped evidence; do not restore them over current native nested work. No new release was published.

Latest source/results: **FORGE_EXECUTION_CHECKPOINT.md** (524 total / 522 passed / 2 explicit not run; Release 0 errors/warnings). The owner explicitly resumed development. Any earlier PAUSED instruction below records the older session only.

**PAUSED at the owner's request before the session limit. All local source and documentation are saved. No build/test/integration is left running.**

Створено та розробляється за допомогою ШІ (OpenAI Codex).
Created and developed with AI assistance (OpenAI Codex).

Workspace: `%USERPROFILE%\Documents\PTenoLaucher`. This is the existing five-project .NET 10/Avalonia source mirror, without `.git`. No remote writes, release publication, package signing, installer rebuild or Windows CI occurred. The existing public preview is still the earlier `v0.2.0-preview.e7b058cf31c3`, not this local code.

## Restore safely

Read this checkpoint and the current root reports. Do not restore the old 386-test release over local historical-assets or Forge work. Before this block, all 13 changed source/test files matched the saved historical checkpoint hashes. Pre-edit copies: `.local/checkpoints/pre-forge-catalog-1793267500175` (233 code/test/document/script files). Previous asset checkpoint and the two pre-existing old AI/process document differences were preserved.

The latest owner's priority is Forge/NeoForge, then mod metadata/updates, modpacks, content managers, downloads, Java management and diagnostics/accessibility. It supersedes earlier Java-first continuation notes. Sinytra, Kilt and all Cross-Loader bridges remain DEFERRED. Resume development only when the owner asks.

## Historical assets — existing implementation retained

MINECRAFT-03/05 are already implemented for controlled preparation in AssetIndexPlan, AssetManager, VanillaInstaller, LegacyArguments, LaunchArgumentExpansion and readiness/receipt validation. They no longer reject virtual/map_to_resources globally. Virtual/mapped ownership journals, verified objects, offline reuse/repair, cancellation/restart, unknown-file protection, collision guards and arguments remain. All 40 HistoricalAssetTests cases remain: 38 ordinary cases passed again, two explicit real-junction cases are not run in Codex. Actual historical installation is still blocked by the earlier native socket 10013 result. Game execution remains NOT RUN.

See HISTORICAL_ASSETS_CHECKPOINT.md for the complete implementation and retained failures. The owner's earlier ordinary PowerShell junction result was 1/1 on the prior binary; it is not transferred to current source. The newer existing-target/dangling-target pair remains explicit and awaits its own permitted run. No permission guard was bypassed.

## This completed preparation block — MINECRAFT-11 / MINECRAFT-12

- `ForgeCatalog.cs`: official Maven XML catalogs with bounded parsing, prohibited DTD/external entities, exact group/artifact identity, duplicate/version guards, checksum-protected local metadata cache, explicit refresh and offline/network-failure cache reuse. Forge MC prefix bindings are read from the published version string. The NeoForge index does not declare MC bindings; exact MC compatibility must be confirmed from its installer, not guessed. Versions preserve/reverse vendor list order; this is not a universal Maven qualifier ordering implementation.
- Installer URLs are constructed from the official repository and exact selected coordinate. SHA-512/SHA-256/SHA-1 sidecars are tried in that order; only HTTP 404 permits a weaker fallback. Malformed checksums or other failures stop. JARs are bounded to 128 MiB, hash checked before atomic cache write, pinned and rechecked for reuse. Hashes from the same HTTPS origin do not provide a separate publisher signature or antivirus verdict.
- `ForgeMaven.cs` and TrustedSources: strict Maven coordinates/path matching and HTTPS/default-port/no-credentials/no-query rules. Forge Maven, NeoForged releases and Mojang libraries are the supported origins. No third-party metadata service, borrowed client ID or bridge is used.
- `ForgeInstallerMetadata.cs`: statically reads spec-1 install_profile/version JSON from the verified JAR; no executable extraction or execution. Checks archive paths/links/size limits, duplicate JSON fields, version inheritance and exact selected loader artifact. Reads runtime/processor libraries, client-only processors, sided data, game/JVM arguments and declared FML library coordinate independently of the NeoForge version. Missing or ambiguous FML remains unknown. Raw processor arguments/outputs are retained for a future adapter; they are not execution-ready validated commands.
- `ForgeComponentPreparer.cs`: downloads or reads embedded libraries, verifies declared SHA-1/size, deduplicates and reuses hash-addressed cache inputs; embedded input repair works offline. Profile usage/world locks and cancellation apply. A profile-local `loader-components.json` is written only after all downloadable/embedded inputs are verified. Previous pointer and real game receipt survive failure/cancellation; synthetic worlds are preserved.
- This is **not a full loader installation**. Generated libraries and processors remain pending. ForgeInstallPlan is a separate type with GameInstallationComplete=false, never an InstallationReceipt. Offline readiness and game launch still reject unsupported Forge/NeoForge installations and require the existing licensed-account checks.
- Actual UI: compact Forge/NeoForge panel, official version list, explicit component download/verification and summary showing verified file count, pending processor/generated counts and FML coordinate. It resets on profile changes and supports language changes/offline cached inputs. Full Install/repair is disabled for these loaders until the real installer exists; the UI does not advertise the preparation as installed gameplay.
- Eight new strings are present in all eight catalogs: **246 keys per language**. Ukrainian/English and the six additional embedded JSON catalogs preserve matching format placeholders. Full app localization/DPI/native-speaker review remains partial.

## Actual results for current code

| Check | Result | Evidence under artifacts/0.2.0 |
|---|---|---|
| Release solution build | PASS, 0 errors / 0 warnings | forge-components-release-build.log |
| Complete direct xUnit suite | 463 total; 461 executed/passed; 0 failed/error/skipped; 2 explicit not run | forge-components-full.log; tests/forge-components-full.trx |
| New ForgeComponentTests | 37 expanded cases passed | Included in full TRX; 21 test methods |
| Existing historical assets | 38 passed / 2 explicit not run | Same full TRX |
| Native Windows launcher fresh-root startup | PASS, exit 0, initialized=True, zero profiles | forge-components-native-ui.log; .local/forge-components-native-ui-20261029/gui-smoke.txt |
| Forge/NeoForge actual-XAML panel and offline component preparation | PASS with isolated synthetic cached inputs | ForgeComponentTests.ActualForgePanelPreparesCachedComponentsOfflineAndResetsForVanilla |
| Real native official catalogs | FAILED / NETWORK-BLOCKED for both Maven hosts, socket permission error 10013 | forge-components-native-catalogs.log; .local/forge-live-20261029/forge-result.json |
| Real official installer/libraries, processors, full loader install | NOT VERIFIED / processors not implemented | No successful live install or execution |
| Minecraft gameplay, Microsoft approval/entitlement/profile/refresh | NOT RUN / external | Existing approval/auth blocker unchanged |
| Current Setup/Portable lifecycle | NOT RUN in this block | Do not bypass the earlier registry ownership guard |

New tests cover official-origin XML/cache behavior, DTD rejection, path/coordinate/URL guards, installer/sidecar/library tampering, supported sidecar fallback, exact loader and Minecraft identities, separate FML metadata, unrecognized FML, embedded repair/offline reuse/cancellation, previous pointer/receipt preservation, profile leases and actual UI switching. Fixtures use synthetic non-executable content; they are not production downloads or Minecraft sessions. The first targeted UI fixture failed because it selected a record outside the stored profile collection; the corrected test uses actual persisted profile selections. The failed log is retained as forge-components-targeted.log.

The native UI smoke verified fresh-root application startup, not a physical review of the new Forge panel or DPI. The earlier 386/386 and Windows-runner real Java 8/17/21/25 matrix are historical evidence, not newly repeated live runtime checks.

## Limits and next complete block

No processor or Java was executed for this preparation. Full Forge/NeoForge install/repair, generated output validation, staged processor execution, launch receipt/classpath composition and installation rollback/recovery remain unimplemented. Legacy Forge installers, NeoForge's old net.neoforged:forge line and newer checksum/profile variants without the supported explicit metadata are not claimed supported. Unsupported metadata fails; it must not be guessed into readiness.

Next block: an execution adapter for supported official client processors, with bounded stages, verified executable inputs, resolved side/data/Maven variables, safe staging paths, explicit output/hash validation, durable operation journal, cancellation/recovery and atomic installation receipt composition. Keep existing game/account gates. Verify representative official installers when a permitted network environment is available; do not work around the current socket denial. Use controlled processes for orchestration tests and report actual loader/game verification separately.

Opt-in component checks, no account or game execution:

```powershell
.\.tools\dotnet\dotnet.exe tests/PTenoLauncher.IntegrationSmoke/bin/Release/net10.0/PTenoLauncher.IntegrationSmoke.dll <fresh-isolated-root> --forge-catalogs
.\.tools\dotnet\dotnet.exe tests/PTenoLauncher.IntegrationSmoke/bin/Release/net10.0/PTenoLauncher.IntegrationSmoke.dll <fresh-isolated-root> --forge-components Forge <exact-official-loader-version> <minecraft-version>
```

Use NeoForge instead of Forge for its separate provider. `--forge-components-offline` requires all legitimately obtained cache inputs. The new CLI is an explicit component checker, not a fake launcher or an authentication bypass.

All **189 requirements**, including the 184 initial and five retained added IDs, and **44 module grades** remain. MINECRAFT-11/12 are now Partially Implemented; Forge/NeoForge readiness grades remain 25% because full installation/real components/gameplay are not verified. All five DEFERRED rows remain unchanged. Current reports and roadmap are saved locally; no release was published.

Primary format/origin references (consulted 2026-10-29):

- https://github.com/MinecraftForge/Installer/blob/2.0/src/main/java/net/minecraftforge/installer/json/Install.java
- https://github.com/neoforged/NeoForge/discussions/3108
- https://docs.neoforged.net/user/docs/client/
- https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml
- https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml
