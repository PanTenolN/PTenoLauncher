# Installed-mod offline readiness

This is a read-only, bounded client-side Fabric check integrated into OfflineReadiness and GameLauncher. It never calls a catalog, AI service or antivirus API, and never executes JAR code. A successful static check is not proof of in-game compatibility or malware safety.

## Integrity and recovery

Each profile can own installed-mods.json (schema 1, exact profile ID, at most 2048 top-level mods/*.jar entries with SHA-256 and byte size). Paths, duplicate records and hashes are validated. New confirmed local/Modrinth installations persist the inventory after the durable Committed journal marker. Recovery verifies every journal target before changing anything, restores the committed inventory idempotently and removes the journal last. A Prepared rollback leaves every target intact if any target was changed by the user. Inventory corruption is reported and not overwritten silently.

Old/manual files have no authenticated historical baseline and are marked mods.integrity.unknown. Review local mods opens a Ukrainian/English modal with every current JAR path, size and SHA-256, plus missing entries that would leave the baseline. Explicit acceptance rescans under the profile/world lock and compares a fingerprint including current files, the old inventory and profile identity/version/loader. Changed/stale/foreign previews are refused. Only inventory/history metadata is written; JARs, worlds and shared libraries remain untouched. Accepting a baseline is a local user decision, not a publisher signature or antivirus result.

## Static dependency checks

Supported Fabric schema-1 root/declared nested JAR metadata supplies client IDs, provided IDs, versions, depends and breaks. Dedicated-server-only modules do not satisfy client dependencies. Known Minecraft/Java/Fabric versions come from the profile/matching installation receipt. Missing required IDs, definite range mismatches and matching breaks require preparation; duplicate root IDs and wrong loaders are reported. Optional dependencies do not block. Unsupported schema/ranges/formats and competing bundled versions stay Unknown.

Fabric predicates follow the official schema-1 specification: https://wiki.fabricmc.net/documentation:fabric_mod_json_spec (reviewed 2026-10-27). Numeric versions use absent zero components, build metadata is ignored, prereleases are ordered, an empty prerelease is earliest. Fabric caret advances the major component even for 0.x; tilde advances minor. Arrays are OR, space-separated terms AND. Current parser bounds: 1024-character predicates/versions, 16 numeric components and 32-bit numeric components. Out-of-scope ordered comparisons stay Unknown. Generic common/Maven predicates remain a separate parser for existing callers.

## Limits

At most 2048 top-level JARs and 8192 immediate directory entries are scanned. Nested filesystem directories are not recursively scanned and cause Unknown; declared JAR-in-JAR metadata uses existing depth/size/archive limits. Unsafe links/paths and malformed archives fail closed. No files are downloaded or auto-repaired by the readiness check. A readiness report is a snapshot; launch checks it again under the profile lease. Files under the same user's control are not an authenticated trust boundary.

Forge/NeoForge/Quilt metadata, arbitrary local-JAR catalog recovery, complete Fabric dependency-selection emulation, security scanning, actual licensed online/offline gameplay and all mod combinations remain incomplete/unverified. See TEST_REPORT.md for current evidence.
