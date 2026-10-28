# Testing

Run scripts/build.ps1 for locked restore, Release build and automatic tests.
Current verified checkpoint: 113 passed, 0 failed, 0 skipped; see ../TEST_REPORT.md.

Use an isolated data root:

~~~powershell
./scripts/dotnet-local.ps1 run --project src/PTenoLauncher.Desktop -c Release -- --data-root .local/ui-test --smoke-test --capture-ui
~~~

Real Java integration without launching a game:

~~~powershell
./scripts/dotnet-local.ps1 run --project tests/PTenoLauncher.IntegrationSmoke -c Release -- .local/java-test --java
~~~

Full Microsoft/Vanilla tests are manual and require the owner's registered client and licensed account.
Installer lifecycle tests must use a disposable Windows environment or isolated .local install/data paths; never real user worlds. Normal uninstall must preserve data; cleanup requires explicit opt-in.

See MODRINTH.md for live API/download/offline tests. Native installer lifecycle: python scripts/test-installer.py --older-setup artifacts/PTenoLauncher-Setup-0.1.0-win-x64.exe (only after compiling that test payload); the script refuses existing registered installs. Five isolated scenarios passed; no real user worlds were used.

Fabric live checks: python scripts/run-live-check.py --check fabric --root .local/live-integration. This requires a previously installed private Vanilla profile and writes durable logs. See FABRIC.md.

Installed-mod block (160 tests): artifacts/0.2.0/offline-mods-build.log, offline-mods-tests.log and tests/offline-mods.trx. Real cache-only integration can be reproduced after preparing the private verified Vanilla/Fabric/Java and Modrinth fixture caches:

```powershell
python scripts/run-live-check.py --check mods-offline --root .local/live-integration --cached-mods-root .local/live-modrinth-1793091630115
```

This creates a new private profile and synthetic save sentinel, installs from the existing verified caches, counts HTTP calls using a deny-network handler and requires account.missing/Ready=false. It does not sign in or launch Minecraft. Fresh online integration is a separate test and currently fails on DNS; do not mark it passed based on the cached result.

Local-JAR combined-cart block: 175 tests, local-deps-build.log/tests.log and tests/local-deps.trx. Real cache-only scenario:

```powershell
python scripts/run-live-check.py --check local-deps-offline --root .local/live-integration --cached-mods-root .local/live-modrinth-1793091630115
```

It requires previously verified private cache fixtures, creates a fresh isolated profile and synthetic sentinel, selects a genuine local JAR, resolves its catalog dependencies and verifies full readiness with a deny-network handler. The expected final readiness issue is account.missing, not Ready. It is not a fresh online API or game-launch test.

Optional selection / mods-folder block: 190 tests; optional-build.log, optional-tests.log and tests/optional.trx. Real cold-cache metadata check:

```powershell
python scripts/run-live-check.py --check optional-offline --root .local/optional-selection-check --cached-mods-root .local/live-modrinth-1793091630115
```

It uses real previously verified Modrinth metadata and a deny-network handler, checks opt-in and recoverable offline blocking, and never installs optional files or launches Minecraft. The home checklist/result template covers the remaining manual tests and interactive Explorer opening.
