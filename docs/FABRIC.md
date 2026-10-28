# Fabric Loader

Fabric is installed into an isolated profile using the existing verified Vanilla installation, official Fabric metadata and official Fabric Maven libraries. No external installer JAR is executed.

In a Fabric profile, open Home, click **Версії Fabric**, choose a compatible version and use **Встановити / відновити**. With no explicit choice, repair preserves the installed loader version; a new installation chooses the first stable version from the official catalog. The installed version is recorded in the profile's installation receipt. Vanilla profiles hide both Mods navigation and Fabric controls.

The installer composes the official inherited profile over the Mojang receipt, adds verified libraries/classpath/JVM arguments and retains the official Minecraft authentication arguments. Library hashes are taken from metadata or official SHA512 sidecars. Downloads/metadata/archives are bounded, Maven coordinates and HTTPS hosts are checked, and the loader main class must exist in its JAR.

Warm metadata and verified libraries support offline repair. Cold offline metadata fails explicitly. Profile usage/world locks prevent installation into an active profile. Ordinary failures restore the prior receipt; an abrupt interruption can require repair, with a loader mismatch blocking launch. Shared cache and world files are not removed.

Live verification on 2026-10-27: Minecraft 1.21.1 + Fabric 0.20.1, 3969 verified files, 79 classpath entries, Java requirement 21, offline repair and synthetic-save preservation passed. Native offline version-picker smoke passed.

**Actual Minecraft/Fabric gameplay has not been tested.** It still requires official licensed Microsoft authentication. Historical versions/other loaders and full installed-mod readiness checks remain incomplete. A further combined Fabric + Modrinth run was blocked by DNS for api.modrinth.com; do not mark it passed.

Opt-in durable test runner (requires a previously installed 1.21.1 Vanilla profile in that private root):

~~~powershell
python scripts/run-live-check.py --check fabric --root .local/live-integration
python scripts/run-live-check.py --check fabric-mods --root .local/live-integration
~~~

The runner writes streaming logs and .status.json completion records under artifacts/0.2.0.
Official sources: https://meta.fabricmc.net/ and https://maven.fabricmc.net/.
