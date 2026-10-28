# PTenoLauncher

**Створено та розробляється за допомогою ШІ (OpenAI Codex).**
**Created and developed with AI assistance (OpenAI Codex).** Requirements and development direction are set by the project owner.

An independent Minecraft Java Edition launcher for Windows 11 x64, built with C#, .NET 10 and Avalonia UI. Current version: **0.2.0 Development Preview**. This is work in progress, not a stable release.

## Downloads and build status

[Development prereleases](https://github.com/PanTenolN/PTenoLauncher/releases) · [Windows build and tests](https://github.com/PanTenolN/PTenoLauncher/actions) · [Build from source](docs/BUILDING.md)

The repository contains the launcher source, tests, build scripts and installer recipe. The Windows workflow is configured to publish Setup, Portable ZIP, Source ZIP and SHA256 sums as a uniquely tagged prerelease only after successful build, tests and package/startup verification. Check the individual workflow run and release assets; configuration alone is not proof of a successful remote build or upload.

## Current implementation

- Graphite desktop interface, five live themes and SQLite settings.
- Isolated profiles, archive/unarchive, confirmed deletion safeguards and backups/restore.
- Official Minecraft releases, snapshots and historical catalog; cached offline use.
- Vanilla/Fabric/Quilt installation preparation, managed Java and verified download caches.
- Modrinth integration, deterministic recursive dependencies independent of AI, optional components and a combined local/catalog installation cart.
- Safe static local JAR inspection, partial Fabric/Quilt/Forge/NeoForge metadata checks, integrity baselines and transaction recovery.
- Mods navigation hidden for Vanilla profiles; a selected-profile mods-folder button.
- Launch process/console and official authentication code with protected local account storage. Their presence does not establish successful gameplay.

The source checkpoint passed **320 automatic tests** with zero build errors/warnings. Synthetic fixtures are used extensively. Real installation/cache checks are documented separately in [TEST_REPORT.md](TEST_REPORT.md); these results do not establish full game compatibility or absolute security.

## Microsoft / Minecraft authentication

The launcher uses its owner's registered desktop public client, MSAL and the system browser. It does not collect Microsoft passwords, embed a client secret, borrow another launcher's application ID or bypass license checks. Windows account tokens are protected locally using DPAPI CurrentUser.

The owner completed browser OAuth, but a subsequent Minecraft Services request returned HTTP 403. The owner reports submitting the AppID review request on 2026-10-28; **approval remains pending**. The reason for the 403 is not confirmed. Full licensed sign-in, entitlement/profile retrieval, refresh and actual online/offline Minecraft gameplay have **not** been verified. See [MICROSOFT_AUTH_SETUP.md](MICROSOFT_AUTH_SETUP.md).

## Languages

**Ukrainian, English, Polish, German, Spanish, French, Brazilian Portuguese and Russian are selectable**, with 230 validated UI keys per language, live resource switching and SQLite persistence. Low-level diagnostic localization and linguistic/DPI review remain partial. See [localization status](docs/LOCALIZATION.md).

## AI disclosure

AI-assisted development is separate from optional AI functionality inside the launcher. The runtime AI gate defaults to off. An optional OpenAI assistant/selected-log analysis and modern mod-JSON translation workflow are implemented, with protected user-supplied credentials, explicit paid-transfer consent, a local editor, incremental updates and resource-pack export. Live API/model quality and game activation remain unverified. No local AI models are included. See [online AI usage and limits](docs/ONLINE_AI.md). Minecraft preparation, mods and deterministic dependencies do not depend on an AI provider.

## Known limits and next work

Forge/NeoForge loader installation, complete nested-mod compatibility, modpacks, server management, broader AI/mod-format coverage, plugins and updating remain incomplete. The current installer lifecycle and licensed game tests need separate verification. Binaries are unsigned; local NuGet vulnerability-audit connectivity was blocked, and the enabled remote audit must be assessed from its actual run.

All approved requirements are retained in [REQUIREMENTS_TRACEABILITY.md](REQUIREMENTS_TRACEABILITY.md), [DEVELOPMENT_STATUS.md](DEVELOPMENT_STATUS.md) and [docs/ROADMAP.md](docs/ROADMAP.md). Preview limitations are listed in [docs/PREVIEW_RELEASE.md](docs/PREVIEW_RELEASE.md).

Minecraft game files, user worlds, private keys and account tokens are not distributed in this repository. This is an unofficial community project, not an official Minecraft product and not approved by or associated with Mojang or Microsoft.
