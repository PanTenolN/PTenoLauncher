# Local client-content ZIP import — MODPACKS-02

Created with AI assistance (OpenAI Codex). This continues the existing .NET 10/Avalonia application. ZIP is a container; this adapter deliberately supports a client-content subset, not a universal modpack standard.

## Supported layouts and requirements

- Files inside `mods/`, `config/`, `resourcepacks/`, `shaderpacks/` at ZIP root, or those directories immediately inside **one wrapper directory**, including `.minecraft/`. At least one eligible payload file is required. Directory names are case-sensitive and canonical; aliases are rejected or excluded, never guessed.
- Multiple candidate roots require an explicit selection. Contents from other roots are shown as excluded and are never merged. More deeply nested layouts are unsupported.
- Minecraft and the loader/version are entered explicitly. They are never inferred from archive/JAR names or Mod IDs. Choose Vanilla with an empty loader version, or Fabric/Quilt/Forge/NeoForge with an exact version. Floating `latest`, ranges and blank required values are blocked. An entered version is a requirement, not proof that an official installer supports or has installed it.
- An exact root `modrinth.index.json` routes to the existing .mrpack adapter, even when the extension is `.zip`. Wrapped/noncanonical indexes are blocked. Recognized Prism/MultiMC/CurseForge/Packwiz markers require separate adapters. Manifest-only archives cannot pass as self-contained content.

## User workflow

Open **Modpacks → Choose ZIP pack**, enter the target requirements and new profile name, select a root if necessary, and recalculate preview. Inspect the category/file/size list, exclusions, static dependency findings and Unknown warnings. Confirm the contents and import. A change of archive, root, name, pins or target invalidates the previous consent. Recalculation does not download anything.

Only direct `mods/*.jar` files are eligible as mods. Embedded JARs remain inside their original parent JAR. Other JAR locations, known executable/script extensions, hidden paths, known account/token/credential/key/database/runtime/launcher metadata names are excluded. Worlds, screenshots, logs, launcher settings/accounts/tokens, updater state, game binaries/libraries/assets, Java and installation receipts outside the allowed categories are excluded. The preview lists every excluded payload path. This conservative name/category policy can exclude legitimate configs; inspect the list before consent. It is **not a secret-content scanner or antivirus**: arbitrary config contents can contain private information and must be reviewed by the owner. Resource/shader ZIPs remain opaque content; importing them does not establish their validity in-game.

All plain ZIP payload is local and works offline with **zero HTTP requests**, no AI or account. No JAR, installer, script, game or launch command is executed during analysis/import. Static known conflicts/missing dependencies block import; unsupported/ambiguous nested rules and unavailable Java/loader facts stay Unknown. Static findings are technical diagnostics; full localization of the underlying legacy inspector remains a separate task.

## Transaction and recovery

The adapter reuses `MrpackImporter`'s per-data-root lease, new profile GUID, profile lease, staging, durable `Preparing → Ready → Moved → Committed` journal, complete SHA-256 inventory verification and insert-only SQLite publication. It never updates an existing profile. Both preview and apply hash the original archive; apply recomputes preview and compares the exact choice fingerprint before staging. The source archive is checked again after copying. Actual staged JARs are inspected again before Ready. Original JAR bytes are retained. Optional persistent pins have local file identity and `Provider=null`.

The shared historical filename `mrpack-import.json` now records `SourceKind=local-zip`, archive filename/SHA-256, content root, excluded paths, actual file hashes/sizes and static findings. `zip-import-target.json` is an **internal record of the user's explicit selection**, bound by hash/size to that receipt; it is not a third-party manifest or a Minecraft installation receipt. Existing .mrpack receipts/indexes are unchanged. Loader adapters/readiness use the exact stored requirements and reject a different selection.

After an interruption, use the existing confirmed **Recover interrupted import** action. Preparing is marked Aborted and retained for diagnosis; retry creates another independent profile. Ready/Moved recovery verifies every staged/published file and completes SQLite publication offline without the original archive. Changed/missing/additional/foreign files or a conflicting SQLite row block recovery rather than overwrite data. A crash immediately after the SQLite transaction can expose only a complete verified profile; readiness reports the uncommitted journal until recovery finishes. Journals/staging are not silently deleted, and no rollback of an existing profile is needed.

## Bounds and limitations

Current ZIP limits reuse the .mrpack guards: 512 MiB archive/per-file; 1 GiB expanded; 8192 archive entries; 2048 selected files; path length 240 / 24 segments; bounded central directory before allocating ZIP entries; compression ratio guard; no ZIP64, encryption or multi-disk archives. Additional ZIP static-preview bound: **128 root JARs**, regardless of optional pin selection. Shared history is limited to 128 operation directories. Temporary preview JARs are removed individually from a fresh owned directory; no recursive cleanup follows arbitrary contents.

Traversal, absolute paths, ADS/device names, Unicode normalization aliases, case aliases, duplicate entries, directory/file collisions and archive links/reparse points are rejected, including excluded entries. Extraction only writes a newly created staging profile. Hashes establish byte integrity, not publisher authenticity. This does not claim protection against every same-user adversarial race, disk/power failure, or platform security issue.

Content import, component installation, readiness and authorized gameplay are separate. Existing auth/ownership gates, MODS-19, updater, onboarding and eight languages remain. Foreign-launcher imports (MODPACKS-03/04), license-aware export, friends comparison and automatic conversion are separate future blocks. Current evidence and exclusions: `ZIP_IMPORT_CHECKPOINT.md`.
