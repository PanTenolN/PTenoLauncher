# Quilt mod metadata and compatibility checks

This extends the existing LocalModManager and installed-mod readiness path. No JAR code, language adapter, entrypoint or loader plugin is executed. These are deterministic checks of a fixed client-side mod set; they do not emulate Quilt's complete SAT solver or prove gameplay compatibility.

## Implemented scope

- Bounded schema-1 quilt.mod.json inspection: ID, Maven group, display name, version, client/dedicated-server environment and provided IDs with their own versions/groups.
- Native depends/breaks, required and optional dependencies, qualified group:mod identifiers, alternative dependency groups and conditional unless clauses. Optional dependencies may be absent, but a present incompatible version blocks installation.
- Quilt version expressions: bare semantic versions use a major compatibility ceiling; explicit equality/comparison, caret, tilde, trailing .x, nested any/all version groups and deprecated ANY arrays. Fabric continues to use its own grammar.
- Minecraft, Java and the pinned Quilt Loader version are checked from the profile/installation receipt. Server-only mods cannot satisfy client dependencies.
- Cart review checks existing plus selected Quilt mods before file copies. Definite missing/incompatible dependencies and conflicts block the existing transactional installer. Successful installs retain the existing hash inventory and recovery journal.
- Offline readiness reuses the same native compatibility checks alongside SHA-256/size verification. No network service or AI provider is used for static inspection or installed-set checks.
- Duplicate metadata keys, unsafe archive paths, missing declared nested JARs, expansion limits and bounded recursive Quilt archives are checked without extraction.

## Deliberate limits

Conditional nested Quilt modules are validated as bounded archives but are not assumed to be active or added as provided IDs. Their presence keeps readiness Unknown; dependencies they might satisfy may remain unresolved. Complete nested candidate selection, load_type and cross-version SAT resolution are future work.

Unsupported schema/constraint extensions, loader-plugin effects, unsupported mapping namespaces and ambiguous supplied versions remain Unknown. Strict JSON is currently required; JSON5-specific syntax is not supported. Partial support never silently invents a dependency/project slug or a trusted integrity baseline.

Fabric handling of dual-format JARs is preserved. Automatic Fabric-on-Quilt bridging, QSL/QFAPI selection and Sinytra are not implemented. This block does not automatically download an unidentified native Mod ID; exact Modrinth identities still use the existing catalog dependency resolver. Full English diagnostic localization remains pending.

## Verification

QuiltModMetadataTests contains 32 cases covering the grammar differences, dependency alternatives, optional/unless behavior, qualified IDs/provided versions, runtime requirements, server/client separation, unknown states, unsafe nested archives, duplicate metadata fields and an actual local transactional installation followed by read-only hash readiness checks. All worlds are synthetic and directories are isolated.

The opt-in native live check downloads one real OK Zoomer artifact through the existing public Modrinth client and SHA-512 downloader, then inspects it twice and reviews its unresolved native dependencies offline without installing or launching it:

~~~powershell
python scripts/run-live-check.py --check quilt-mod-inspection --root .local/quilt-mod-inspection-live
~~~

Current live result: **BLOCKED by unreachable api.modrinth.com (SocketException 10051)** before metadata/artifact download. This is not a successful real-artifact check. See artifacts/0.2.0/quilt-mod-inspection-live.log. Automatic tests use synthetic JARs; they are not production catalog data or game-launch evidence.

## Sources used for semantics

- https://github.com/QuiltMC/rfcs/blob/main/specification/0002-quilt.mod.json.md
- https://github.com/QuiltMC/quilt-loader/blob/0.30.1/src/main/java/org/quiltmc/loader/impl/metadata/qmj/V1ModMetadataReader.java
- https://github.com/QuiltMC/quilt-loader/blob/0.30.1/src/main/java/org/quiltmc/loader/impl/metadata/qmj/Patterns.java

These references describe the metadata contract; they do not verify the launcher's game compatibility. No official-source Java implementation was copied into the product.
