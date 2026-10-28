# Build from source

Windows 11 x64 is the application target. Use the .NET SDK specified in `global.json`, Python 3.11+ for package verification, and Inno Setup 6 to compile the installer. No Microsoft password, token or client secret is needed to build.

```powershell
./scripts/bootstrap-sdk.ps1
./scripts/build.ps1
```

The script performs locked NuGet restore with auditing enabled, Release build and the xUnit v3 executable test runner. Test results are written to `artifacts/tests/tests.trx`. A network-blocked restore/audit is a failure to resolve, not a reason to disable the repository's audit setting.

To start the desktop application:

```powershell
./scripts/dotnet-local.ps1 run --project src/PTenoLauncher.Desktop -c Release
```

From a clean checkout/output directory, build Windows packages:

```powershell
./scripts/build.ps1 -Publish -Installer
python scripts/package-preview.py
python scripts/verify-packages.py --smoke
```

The output includes the self-contained application, Setup EXE, Portable ZIP, Source ZIP, SHA256 sums and package verification JSON. Existing publish/staging directories cause the build script to stop rather than mix payloads. Keep user data and previous artifacts private and separate; do not delete them merely to rerun a build.

The GitHub workflow runs these same steps on a fresh Windows runner. A successful build then publishes a uniquely tagged **prerelease**, with limitations from `docs/PREVIEW_RELEASE.md`. A manual workflow run builds only unless `publish_preview` is explicitly selected. GitHub supplies the publication job's scoped token; do not put a personal token in source. A workflow file's presence is not evidence that a remote run or release succeeded.

An isolated Portable smoke checks launcher startup only. Full installer installation/upgrade/uninstallation, licensed Microsoft sign-in, Minecraft gameplay and code signing require separate evidence. No Minecraft binaries or account credentials are bundled.
