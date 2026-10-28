# Quilt Loader — current scope

Quilt is prepared over a verified Mojang Vanilla installation. The launcher reads the official Quilt v3 catalog and inherited profile, downloads verified official Maven libraries and writes a launch receipt. It never executes an installer JAR.

## Use

Create or select a Quilt profile. On Home, click **Версії Quilt**, select the loader and click **Встановити / відновити**. With no explicit choice, an existing Quilt receipt keeps its loader pin; a new installation chooses the highest numeric non-prerelease version. The API does not supply a stable flag and is not ordered chronologically. A prerelease must be selected explicitly. Switching profiles clears the version selection.

Warm metadata and verified files support repair without the internet. Missing uncached files require an online connection. Shared Java/libraries and worlds are not deleted. Active-profile and world locks block installation; ordinary installation failure restores an earlier valid receipt. Abrupt interruption can still require repair, and receipt/profile loader mismatches block launch.

## Verification and boundaries

Minecraft 1.21.1 / Quilt 0.30.1 was prepared from real official artifacts: 3972 verified files, 82 classpath entries and Java 21. Repeated cache-only repair preserved the installed pin and a synthetic world sentinel; the deny-network handler saw zero requests. The initial official artifacts were staged by an opt-in fixture helper, not silently substituted by production code.

A fresh native C# HTTP catalog fetch independently passed with 307 versions and release 0.30.1. A second test removed only the verified private cached loader JAR, then used the production C# installer to download it again and verify its hash. The synthetic world stayed unchanged. A direct clean online test in the restricted exec context failed DNS; its failure is retained. This does not establish a successful full clean native-online installation.

The actual Avalonia picker opened and its render was reviewed. Thirteen Quilt automated cases and the full 203-test suite passed. See TEST_REPORT.md for named logs.

**Minecraft/Quilt gameplay has NOT RUN.** Official Microsoft entitlement/profile verification is still needed; the integration test correctly remains blocked by account.missing. No unverified offline identity is created. Test only a new isolated world when licensed login is available.

**Native Quilt mod metadata is partially implemented.** Root dependency/version/conflict checks are now integrated with cart review and offline readiness; conditional nested selection and unsupported constraints stay Unknown. See QUILT_MOD_METADATA.md for the tested scope. No QSL download, automatic Fabric-on-Quilt bridge or Sinytra support is claimed. Gameplay, modpacks and historical coverage remain incomplete.

## Integrity controls

Only fixed official HTTPS metadata/Maven origins are accepted. Maven coordinates, selected loader coordinates, inherited Minecraft version and KnotClient main class are validated. Libraries require SHA512 or SHA256; missing or invalid checksums fail closed. Cache envelopes and receipts are written atomically, and source size/count limits apply.

JARs are read statically with expanded-size/entry limits. Ordinary classfile headers determine a minimum Java requirement, merged with the inherited Minecraft and catalog requirements; multi-release classes and module descriptors are excluded. The Quilt installer application's runtime requirement is not assumed to be the game loader requirement. Static checks do not prove in-game compatibility or antivirus safety.

## Opt-in isolated reproduction

Use an existing private .local test root with a verified Vanilla 1.21.1 installation. Never point fixture/install/repair checks at real player worlds.

~~~powershell
python scripts/run-live-check.py --check quilt --root .local/live-integration
# Explicit fixture preparation, only when independently exercising cached installation:
python scripts/prepare-quilt-fixture.py --root .local/live-integration
python scripts/run-live-check.py --check quilt-offline --root .local/live-integration
# Fresh production C# metadata check uses an empty private data root:
./scripts/dotnet-local.ps1 run --project tests/PTenoLauncher.IntegrationSmoke -c Release -- .local/quilt-catalog-check --quilt-metadata
# After a successful private Quilt receipt exists, verify actual C# redownload/repair:
./scripts/dotnet-local.ps1 run --project tests/PTenoLauncher.IntegrationSmoke -c Release -- .local/live-integration --quilt-repair-online
~~~

The repair test backs up and restores its single private verified loader artifact on failure. No registry/system-network settings are changed by these checks. The fixture helper is a test utility, not a launcher fallback or substitute for native HTTP integration.

Official sources:
- https://quiltmc.org/en/install/client/
- https://meta.quiltmc.org/v3/versions/loader/1.21.1
- https://meta.quiltmc.org/v3/versions/loader/1.21.1/0.30.1/profile/json
- https://maven.quiltmc.org/repository/release/
- https://maven.fabricmc.net/
