# Security and known limits

The launcher runs as the current Windows user (asInvoker). Inno defaults to per-user installation. No hidden telemetry, local AI model, antivirus bypass or password collection is included.

Implemented controls include DPAPI CurrentUser token storage, HTTPS host allowlists, file size/hash validation, atomic cache writes, bounded downloads/archive extraction, path traversal/ADS/device-name/link rejection, JAR metadata inspection without execution, redacted/bounded process logs, isolated profiles, usage locks and owned-data deletion checks. Temurin uses an explicit bounded redirect allowlist; other download policies remain restrictive.

Permanent profile deletion requires a preview and exact profile-name confirmation. Shared libraries/cache/Java are preserved. Deletion is ordinary filesystem deletion and cannot promise physical erasure on SSD.
Uninstaller cleanup is opt-in and checks an ownership marker, fixed owned subtrees, links and active profiles; unknown root files are preserved. Five prior Fabric-payload isolated installer lifecycle scenarios passed; the latest rebuilt Setup lifecycle test is blocked before installation by registry permissions; an external database reader now blocks cleanup before any worlds are removed.

Local-mod operations and profile deletion have durable recovery journals. Backups restore into a new profile. Tested operations use synthetic data.
These controls are not an absolute security guarantee: same-user malware, local filesystem races/TOCTOU, malicious Java mods and unsupported metadata remain risks. DPAPI does not protect against code already running as the same Windows user.

The internal Unknown Source warning is informational and is not antivirus scanning. Defender integration/threat feeds/plugin isolation/updater signing are not implemented. Do not disable Windows Defender.
App/Setup are unsigned development previews; a real certificate is an external requirement. Full authentication and Minecraft game launch have not been verified. Interactive installer language/shortcut checks remain pending.

Dependency audit command:

~~~powershell
dotnet list PTenoLauncher.sln package --vulnerable --include-transitive --no-restore --format json
~~~

Retained audit evidence is in artifacts/dependency-audit.json. Absence of known advisories does not prove safety.

Modrinth requests occur on explicit user actions. Installed JAR SHA512 hashes can be sent for identity lookup, disclosed in the UI. No JAR is uploaded. Downloads use official API/CDN HTTPS and SHA512 verification; nested Fabric metadata is bounded and inspected without execution. These checks do not prove a mod is safe.

Fabric uses only official metadata/Maven HTTPS sources, verified hashes or official SHA512 sidecars, bounded static JAR checks and a required loader main class. Receipts bind loader type/version to the profile so Vanilla cannot silently use Fabric. Profile locks guard installs, ordinary failures restore prior receipts, and interrupted preparation can require repair. Authentication checks remain mandatory.

Installed-mod integrity baselines are local change-detection records, not authenticated publisher attestations. New installation commits/recovery record SHA-256/size; older or manually changed files require a full preview and explicit confirmation after a fresh scan under profile/world locks. Neither baseline confirmation nor static readiness replaces antivirus scanning or proves game compatibility. See OFFLINE_MODS.md.

Explicit local-JAR resolution sends SHA512 hashes to Modrinth for exact identification. Matching published versions are pinned, including secondary files. Source files and installed fingerprints are rechecked before downloads/commit; final actual-JAR review precedes a single journaled profile mutation. Unknown IDs never become guessed remote project names. This does not establish antivirus safety; see LOCAL_MOD_DEPENDENCIES.md.
