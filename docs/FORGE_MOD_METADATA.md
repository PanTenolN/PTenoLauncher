# Forge / NeoForge mod metadata checkpoint

Native root metadata and deterministic client-side compatibility checks are partially implemented. This does **not** install either loader or demonstrate Forge/NeoForge gameplay.

## Working scope

- Bounded static reading of META-INF/mods.toml and META-INF/neoforge.mods.toml with Tomlyn 2.10.1. Strict syntax validation rejects duplicate keys before model deserialization. No JAR classes, entrypoints or metadata URLs are executed/fetched.
- Multiple declared mods per physical JAR, each preserving its ID, own version, dependency owner and feature requirements. Secondary identities participate in version/duplicate checks.
- Forge mandatory and NeoForge required/optional/incompatible/discouraged dependency types. A missing optional dependency is allowed; an installed incompatible optional version blocks. Discouraged matches produce a cart warning.
- Client/server dependency side filtering, numeric Maven intervals/unions, exact bracket constraints and static Java features. Maven bare numeric versions remain recommendations, not Fabric-style equality constraints. Unsupported qualifier ordering remains Unknown.
- Required Minecraft/runtime constraints block known mismatches. Forge javafml/lowcodefml use the Forge major for language-loader checks. NeoForge FML is independently versioned; it is deliberately **Unknown** until an actual supported installer provides its version. The NeoForge version is never substituted for FML.
- BEFORE/AFTER cycles are detected with a bounded graph; existing and selected native mod sets share cart/readiness checks.
- Static ${file.jarVersion} substitution uses only main-section Implementation-Version, including continuation lines. Explicit file properties can be read locally; unresolved placeholders remain Unknown.
- The existing atomic installer, integrity baseline, journal recovery, path checks and default-off AI policy are retained. Tests use isolated synthetic worlds and JARs.

## Limits and pending work

Loader installation, Maven qualifier ordering, complete Jar-in-Jar selection, older NeoForge versions sharing the Forge metadata filename, custom FML language providers, unsupported features and actual gameplay remain pending. Jar-in-Jar declarations and uncertain constraints cannot establish Ready. English diagnostic localization is incomplete.

No native Forge/NeoForge game is known to be installed by this block. Receipts in unit tests are synthetic test inputs, not proof of a real FML or game runtime. No remote download/dependency-provider or authentication success is implied by a parsed TOML file.

## Build dependency and audit evidence

Tomlyn is pinned in the project and NuGet lock files. Its package was obtained from the official NuGet flat container. The SHA-512 matches the official NuGet catalog; a package signature entry is present. This is not a claim that a complete certificate or vulnerability audit was performed.

Native NuGet restore failed with unreachable api.nuget.org / NU1301 and NU1900. A workspace-only local feed containing that exact package plus the existing package cache allowed restore. For that isolated restore invocation only, NuGetAudit=false was passed because the native audit endpoint was unreachable. The repository's NuGetAudit=true default remains unchanged. Live vulnerability audit remains **NOT VERIFIED** and must be run when native NuGet connectivity is available.

Evidence: artifacts/0.2.0/tomlyn-package-verification.json, forge-metadata-restore-retry.log and forge-metadata-local-restore.log. A normal connected build continues to use the configured official NuGet source and default audit settings.

## Verification

ForgeModMetadataTests adds 40 cases. These cover multi-mod metadata, dependency owners/types/sides, static property/manifest resolution, optional/present semantics, conflicts, runtime checks, FML distinction, numeric Maven ranges, order cycles, malformed/duplicate TOML, multiline strings, unknown Jar-in-Jar/qualifier states, transactional installation, hash tampering and preservation of synthetic worlds. Existing wrong-loader tests now use valid TOML fixtures rather than empty metadata entries.

The latest full-suite result and pause checkpoint are recorded in TEST_REPORT.md and SESSION_CHECKPOINT.md. No real-mod online integration or game launch was run in this block. Existing executable/Portable/Setup packages retain their previous source checkpoint unless their own artifact manifest explicitly says otherwise.

## Official references

- https://docs.minecraftforge.net/en/latest/gettingstarted/modfiles/
- https://docs.neoforged.net/docs/1.21.1/gettingstarted/modfiles/
- https://maven.apache.org/pom.html#Dependency_Version_Requirement_Specification
- https://github.com/xoofx/Tomlyn/blob/2.10.1/site/docs/dom.md
- https://github.com/xoofx/Tomlyn/blob/2.10.1/site/docs/low-level.md
