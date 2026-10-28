# User guide — 0.2.0 Development Preview

Create profiles using the separate profile dialog. Choose a release or snapshot; enable historical versions separately. The catalog refreshes online when opening this dialog if its cache is missing or older than 15 minutes. Manual refresh is available. Offline mode uses the cached catalog. Refreshing does not change a saved profile's selected version.

Install/repair Vanilla, then prepare compatible Java with the managed Java action or select an existing x64 Java installation. Inspect Offline Readiness before playing.
Official Microsoft setup is documented in ../MICROSOFT_AUTH_SETUP.md. A full authenticated game launch remains unverified.

Settings include live themes and the global AI switch. AI is disabled by default; the launcher core and local mods do not depend on it. No online AI provider is implemented yet.
Local JAR files can be inspected and added to the cart. Fabric constraints and known installed/cart dependencies are checked; Modrinth has a recursive required-dependency cart with version selection and confirmation; arbitrary local-JAR dependency recovery remains partial. See MODRINTH.md. Mods navigation is hidden for Vanilla profiles.

Permanent profile deletion shows a preview and size, warns about worlds, and requires typing the exact profile name. Backups may be preserved. Shared game libraries and Java are not removed.

Portable mode stores data under portable-data. Installed mode stores data under LocalAppData/PTenoLauncher. Default uninstall preserves user data. Prior isolated installer lifecycle tests passed. The latest Setup compiles, but its lifecycle retest was blocked by registry permissions before installation. Back up important data before using this preview.

For Fabric: Home -> Версії Fabric -> choose a version -> Встановити / відновити. Repair preserves the installed loader version by default. See FABRIC.md. Actual gameplay remains unverified.

On the Mods page, «Перевірити локальні моди…» / “Review local mods…” lists current top-level JAR files, sizes, SHA-256 and entries missing from the previous integrity baseline. Review the list and explicitly confirm to save the baseline. Changed files require a fresh review. Missing entries are removed from the baseline only after confirmation; no mod or world file is deleted by this action. This is local change detection, not a safety/provenance guarantee or antivirus bypass.

Confirmed new installs record hashes automatically. Offline Readiness checks hashes, available Fabric metadata (including declared nested modules), client dependencies, required Minecraft/Java/Fabric versions, conflicts and incomplete transactions. Unknown/unsupported metadata and old untracked files prevent a confirmed Ready result. Optional dependencies do not block readiness. Vanilla ignores the mods folder and hides Mods navigation. Other loader metadata and actual licensed game launch remain unverified.

## Local JARs and catalog selections in one cart

Import or drop JARs in Mods; this reads files locally and does not send network requests. Click «Знайти залежності» / “Resolve dependencies” to identify exact files by SHA-512 and resolve known Modrinth required dependencies. You can add catalog versions to the same cart. Review local/catalog sources, version pins, reasons, sizes and warnings, then confirm the full list. Unidentified files require a separate internal-warning acknowledgment. Changing the cart resets both confirmations. No AI is needed.

A hash-known local root is used from the selected source file; only dependencies are downloaded. Before changing the profile, the launcher checks every actual JAR together. Unknown files are not matched to project slugs by guessing; offline cold caches may leave dependencies unresolved. See LOCAL_MOD_DEPENDENCIES.md for limits.

Optional components appear as unchecked choices when Modrinth metadata declares them. Select a checkbox, click Застосувати вибір / Apply selection, inspect the updated cart and confirm again. A conflict blocks installation and the selected choice can be unchecked. Clearing choices does not remove installed files. Перейти до теки / Open mods folder opens the current profile’s mods directory. See HOME_TEST_CHECKLIST.md for unverified manual scenarios.
