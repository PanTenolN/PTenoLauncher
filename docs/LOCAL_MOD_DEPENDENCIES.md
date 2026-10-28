# Local JAR dependency planning and combined cart

Import and drag-and-drop inspect selected files locally. The explicit Resolve dependencies action sends SHA-512 hashes of selected/installed JARs to the official Modrinth version-file endpoint (https://docs.modrinth.com/api/operations/versionfromhash/); JAR content is never uploaded. No account, API key, AI provider or local AI model is required.

## Exact local identities

RemoteModInstaller.LocalFiles.cs keeps each input read-locked across static inspection and SHA-256/SHA-512 hashing. A match is bound to the exact published file hash/size and MC/loader-compatible version. The matching file may be a secondary file of the published version. An operation-scoped catalog wrapper seeds that exact version/file into the existing resolver; it does not overwrite canonical version metadata or replace the input with the primary file. Explicit conflicting pins and multiple local versions of a project block the plan.

The deterministic resolver includes transitive required dependencies, optional suggestions, installed versions, incompatibilities, cycles and bounded backtracking. Roots may combine several local JARs and explicit catalog selections. Known local roots are not downloaded again. Identical already installed published files are reused; automatic replacement of another installed file/version is prohibited.

## Confirmation and transaction

One UI cart shows local source summaries and catalog/dependency rows. It reports real download bytes excluding local/already installed files. Cart changes discard the previous plan and reset both confirmations. Unknown identities require explicit acceptance of the internal Unknown Source warning; this is not antivirus scanning or a system-antivirus bypass.

After full-cart confirmation, source hashes and the installed-set fingerprint are checked before downloads. Dependencies are downloaded to the verified cache. Source files and the installed set are rechecked, and every actual local/dependency JAR is reviewed together before one existing staged/journaled installation transaction. Committed recovery/integrity inventory behavior is unchanged. A corrupt dependency or real-JAR metadata conflict prevents any profile mod copies. Original source JARs and worlds are never modified.

## Offline and uncertainty

Warm verified metadata and file caches support the same workflow offline. A cold hash lookup does not guess a project slug from the Mod ID/name. An unknown local file with satisfied local metadata can still be explicitly installed without a network request. Missing/unsupported metadata and unresolved dependencies are displayed. Missing mandatory local dependencies block when no planned downloads can supply them.

Catalog metadata does not expose every internal Fabric Mod ID. When dependencies have not yet been downloaded, definite duplicate IDs/files, wrong loader and known Minecraft/runtime mismatches block immediately. Other local dependency findings are shown as preliminary; after the actual dependency JARs reach the cache, the complete static review must pass before profile mutation. Broader typed pre-download compatibility diagnostics and other providers/loaders and automatic discovery of unidentified JAR dependencies remain incomplete. Static compatibility is not proof of in-game behavior or safety.

Limits: 128 combined selected local inputs/catalog roots, at most 2048 installed JARs for fingerprinting, 512 MiB per local JAR, existing bounded metadata/archive inspection and resolver search budgets. Actual Microsoft authentication and licensed Minecraft gameplay remain separate unverified/manual requirements.

## Evidence

LocalDependencyTests covers exact secondary-file pinning, transitive dependencies, one install transaction, both confirmations, changed inputs/installed sets, unknown IDs without slug guesses, cold/warm offline behavior, corrupt dependencies, actual-JAR ID mismatch, multiple local/remote roots, duplicates, incompatible game/pins, profile locks and exact installed reuse. DesktopTests covers real XAML, plan invalidation and offline unknown-local installation. See TEST_REPORT.md for real-artifact integration results.

Optional checkbox behavior: OPTIONAL_DEPENDENCIES.md. The Mods page also opens the selected profile’s mods directory using Перейти до теки.
