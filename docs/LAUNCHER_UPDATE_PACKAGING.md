# Launcher update packaging and trust

Created and developed with AI assistance (OpenAI Codex).

These tools prepare local files only. They do not publish a GitHub release, configure production secrets, insert a trusted key, or perform Windows Authenticode signing. Production update trust remains unconfigured in this source.

## Build identity

Use one protected monotonic release counter across stable and preview, plus a unique immutable build ID. Do not sort commit hashes or the display version. Compile the application and helper with identical `PTenoBuildSequence`, `PTenoBuildId`, `PTenoBuildChannel` and version properties. Source builds default to sequence 0 and are not automatically replaceable. The signing tool checks a durable exclusive sequence ledger; preserve that ledger between release jobs. A failed output after reserving a number consumes the number; gaps are safe.

Example for a future authorized release build:

```powershell
.\scripts\build.ps1 -Publish -Installer -ReleaseSequence 123 -BuildId "unique-reviewed-build-id" -Channel preview
```

The script publishes a self-contained Windows x64 application and a separate helper under `updater/`, then emits `launcher-installation.json`. Inventory is generated **after** payload documentation is copied. No user data belongs in the publish tree. This command has not been used to create/publish a new production release in the current session.

The local tool is `scripts/PTenoLauncher.UpdateTools/bin/Release/net10.0/PTenoLauncher.UpdateTools.dll`, run with .NET 10. It inspects PE metadata without loading/executing the input assembly, refuses sequence 0 and test-trust resources, hashes the complete managed file set, and requires the helper. Existing previews without managed inventory must be manually replaced through the normal reviewed installer/portable distribution; no automatic adoption is attempted.

## Commands

```text
inventory <clean-publish-directory>
package <publish-directory> <new-payload.zip> <official-GitHub-payload-url>
package <publish-directory> <new-payload.zip> <official-GitHub-payload-url> <setup.exe> <official-GitHub-setup-url> inno-single-process-v1
index <payload.zip.json> <changes.txt> <stable-or-preview> <unsigned-index.json> [previous-unsigned-index.json]
sign <unsigned-index.json> <external-private-pem> <key-id> <source-root> <sequence-ledger> <signed-envelope.json>
```

- `package` creates a new archive and a companion exact file/hash/build descriptor. Files must match the inventory. Portable and Installed descriptors are distinct. The same build can have both packages; `index` merges packages and retains the prior stable/preview entries. Inspect the index before signing. It is valid for seven days by default; a protected release process must refresh/publish signed catalogs as needed.
- Official package URLs must be HTTPS under `https://github.com/PanTenolN/PTenoLauncher/releases/download/`. The public feed path is `https://github.com/PanTenolN/PTenoLauncher/releases/latest/download/launcher-updates.json`. The feed includes both channels; stable users receive no preview without opt-in. No such production signed feed is verified/configured yet.
- Signatures cover the exact payload bytes using ECDSA P-256/SHA-256 and 64-byte IEEE P1363 format. Private PEM input must be outside the supplied source root. No private key is generated, copied into the repository, embedded, printed or included in the envelope. Establish a reviewed trusted public-key mapping in the application/helper separately. Adjacent SHA256SUMS are integrity records, not independent origin proof.
- The Installed setup must use the exact supported single-process Inno contract (`UseSetupLdr=no`), the existing AppId, per-user ownership, unchanged data schema and supported registration layout. It must ship the same managed inventory and signed payload bytes. Helper arguments disable automatic application closing/restarting and machine reboot; only exit 0 plus verified files/inventory/registration proceeds to startup health. No Portable fallback exists. Registry access/ownership failures remain blockers.
- Authenticode certificates and metadata signing are separate. Neither a production key nor an Authenticode certificate was provisioned here. Do not publish test builds or call these tools with a production signing key without the owner's separate instruction.

## Isolated verification

`tests/PTenoLauncher.UpdaterE2E` generates its signing key in memory, writes only a public PEM, copies source to unique test roots and compiles real `UpdaterTest` builds. `Directory.Build.props` rejects test trust/failure hooks in normal configurations. Production has no settings/feed/environment switch to turn on test trust. The packaging tool's rejection of test-trust binaries was checked separately.

The harness runs actual GUI/helper processes using the existing .NET 10 runtime, with a populated synthetic SQLite data root and no network/account/game. It verifies exact parent waiting, update/restart/health, failed startup rollback and safe live-timeout recovery. Test-build-only cooperative closure/failure hooks are absent from normal builds. Inert fixture .exe/.dll bytes used by unit tests are reported separately from these actual executions.

Manual recovery uses the verified helper already copied to the operation's `helper/` directory. Do not supply arbitrary target paths: the helper takes **no arguments**, derives its operation/data root from its own location, checks the binding/signatures/current files and refuses active processes/foreign data. Interrupted apply resumes by restoring the backup, not by guessing remaining replacement steps. It never kills a game/server or replaces files of a still running launcher. Unknown/user-modified bytes and ambiguous ownership require manual review; backups and journals remain available.
